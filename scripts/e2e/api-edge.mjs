// Uç durumlar: kimlik, girdi doğrulama, güvenlik, sürümler, dışa aktarma, öneri durum geçişleri.
//   ARCVORA_API_URL=http://127.0.0.1:5217 node scripts/e2e/api-edge.mjs
import { API_URL, call, check, fatal, finish, section, waitHealth } from './lib.mjs';
import { head, newProject, newUser, withAcceptedRequirements } from './fixtures.mjs';

if (!(await waitHealth(60_000))) fatal('API açılmadı');
const raw = (method, path, { body, headers = {}, token } = {}) =>
  fetch(API_URL + path, { method, body, headers: { ...(token ? { authorization: `Bearer ${token}` } : {}), ...headers } });

section('Kayıt ve giriş uç durumları');
const email = `edge-${Date.now()}@example.com`;
const reg = (b) => call('POST', '/api/auth/register', { body: b });
const first = await reg({ email, password: 'Parola-12345', displayName: 'Edge' });
check('ilk kayıt 201', first.status === 201, first);
const dup = await reg({ email, password: 'Parola-12345', displayName: 'Edge' });
check('aynı e-posta ikinci kez → 409 email_taken', dup.status === 409 && dup.body?.code === 'email_taken', dup);
const dupCase = await reg({ email: email.toUpperCase(), password: 'Parola-12345' });
check('büyük/küçük harf farkı ayrı hesap açmaz → 409', dupCase.status === 409, dupCase.status);
const weak = await reg({ email: `w-${email}`, password: 'kisa' });
check('10 karakterden kısa parola → 422 weak_password', weak.status === 422 && weak.body?.code === 'weak_password', weak);
const badMail = await reg({ email: 'e-posta-degil', password: 'Parola-12345' });
check('geçersiz e-posta → 422 invalid_email', badMail.status === 422 && badMail.body?.code === 'invalid_email', badMail);
check('boş gövde → 422 (500 değil)', (await reg({})).status === 422);
const longName = await reg({ email: `n-${email}`, password: 'Parola-12345', displayName: 'x'.repeat(300) });
check('300 karakterlik ad → 422 invalid_name', longName.status === 422 && longName.body?.code === 'invalid_name', longName);
const upperLogin = await call('POST', '/api/auth/login', { body: { email: email.toUpperCase(), password: 'Parola-12345' } });
check('girişte e-posta büyük/küçük harfe duyarsız', upperLogin.status === 200, upperLogin.status);
const wrongPw = await call('POST', '/api/auth/login', { body: { email, password: 'yanlis-parola-1' } });
const noUser = await call('POST', '/api/auth/login', { body: { email: 'kimse@example.com', password: 'Parola-12345' } });
check('yanlış parola ve olmayan kullanıcı AYNI yanıtı verir (kullanıcı sızdırılmaz)',
  wrongPw.status === 401 && noUser.status === 401 && wrongPw.body?.code === noUser.body?.code && wrongPw.body?.title === noUser.body?.title, [wrongPw.body, noUser.body]);
check('null alanlarla giriş → 401 (500 değil)', (await call('POST', '/api/auth/login', { body: { email: null, password: null } })).status === 401);

section('Token güvenliği');
const goodToken = first.body.token;
check('geçerli token /me için 200', (await call('GET', '/api/me', { token: goodToken })).status === 200);
check('bozuk token → 401', (await call('GET', '/api/me', { token: 'abc.def.ghi' })).status === 401);
check('imzası değiştirilmiş token → 401', (await call('GET', '/api/me', { token: goodToken.slice(0, -3) + 'AAA' })).status === 401);
const [h, p, sig] = goodToken.split('.');
const noneAlg = `${Buffer.from('{"alg":"none","typ":"JWT"}').toString('base64url')}.${p}.`;
check('"alg":"none" token → 401', (await call('GET', '/api/me', { token: noneAlg })).status === 401);
const forgedPayload = Buffer.from(JSON.stringify({ ...JSON.parse(Buffer.from(p, 'base64url')), sub: '00000000-0000-0000-0000-000000000001' })).toString('base64url');
check('yükü değiştirilmiş (sub) token → 401', (await call('GET', '/api/me', { token: `${h}.${forgedPayload}.${sig}` })).status === 401);
const wrongScheme = await raw('GET', '/api/me', { headers: { authorization: `Basic ${goodToken}` } });
check('Bearer olmayan şema → 401', wrongScheme.status === 401, wrongScheme.status);

section('İstek biçimi');
const badJson = await raw('POST', '/api/auth/login', { body: '{bozuk json', headers: { 'content-type': 'application/json' } });
check('bozuk JSON → 400 bad_request', badJson.status === 400 && (await badJson.json()).code === 'bad_request');
const wrongType = await raw('POST', '/api/auth/login', { body: 'x', headers: { 'content-type': 'text/plain' } });
check('yanlış Content-Type → 415', wrongType.status === 415, wrongType.status);
const unknownRoute = await raw('GET', '/api/yok-boyle-bir-yol', { token: goodToken });
check('olmayan yol → 404', unknownRoute.status === 404, unknownRoute.status);
const badGuid = await raw('GET', '/api/projects/guid-degil', { token: goodToken });
check('geçersiz GUID yolu → 404 (500 değil)', badGuid.status === 404, badGuid.status);
const hdrs = (await raw('GET', '/health')).headers;
check('güvenlik başlıkları: nosniff, DENY, no-referrer',
  hdrs.get('x-content-type-options') === 'nosniff' && hdrs.get('x-frame-options') === 'DENY' && hdrs.get('referrer-policy') === 'no-referrer',
  [...hdrs.entries()]);

section('Proje girdi doğrulaması');
const user = await newUser('edge');
const profiles = (await call('GET', '/api/profiles')).body;
const goodProfile = profiles.find((p) => p.selectable).id;
const mk = (b) => call('POST', '/api/projects', { token: user.token, body: { workspaceId: user.workspaceId, techProfileId: goodProfile, ...b } });
for (const [label, name] of [['boş ad', ''], ['yalnız boşluktan oluşan ad', '   '], ['null ad', null], ['201 karakterlik ad', 'a'.repeat(201)]]) {
  const r = await mk({ name });
  check(`${label} → 422 invalid_name`, r.status === 422 && r.body?.code === 'invalid_name', r);
}
check('tam 200 karakterlik ad kabul edilir (sınır değeri)', (await mk({ name: 'a'.repeat(200) })).status === 201);
const longDesc = await mk({ name: 'd', description: 'a'.repeat(4001) });
check('4001 karakterlik açıklama → 422 invalid_description', longDesc.status === 422 && longDesc.body?.code === 'invalid_description', longDesc);
check('tam 4000 karakterlik açıklama kabul edilir', (await mk({ name: 'd', description: 'a'.repeat(4000) })).status === 201);
const hugeIdea = await mk({ name: 'big', ideaText: 'a. '.repeat(70_000) });
check('200 KB fikir metni → 422 source_too_long', hugeIdea.status === 422 && hugeIdea.body?.code === 'source_too_long', hugeIdea);
const notReady = profiles.find((p) => !p.selectable);
if (notReady) {
  const r = await mk({ name: 'x', techProfileId: notReady.id });
  check(`doğrulanmamış profil (${notReady.id}) seçilemez → 422 profile_not_available`, r.status === 422 && r.body?.code === 'profile_not_available', r);
}
check('olmayan profil → 422 unknown_profile', (await mk({ name: 'x', techProfileId: 'yok' })).body?.code === 'unknown_profile');
const foreign = await call('POST', '/api/projects', { token: user.token, body: { workspaceId: '11111111-1111-1111-1111-111111111111', name: 'x', techProfileId: goodProfile } });
check('olmayan/yabancı çalışma alanına proje → 404', foreign.status === 404, foreign);
const otherUser = await newUser('edge-other');
const intoOthers = await call('POST', '/api/projects', { token: user.token, body: { workspaceId: otherUser.workspaceId, name: 'sızma', techProfileId: goodProfile } });
check('başkasının çalışma alanına proje açılamaz → 404', intoOthers.status === 404, intoOthers.status);

section('Kaynak ve değişiklik talebi doğrulaması');
const pid = await newProject(user);
await withAcceptedRequirements(user, pid);
const base = await head(user, pid);
const src = (b) => call('POST', `/api/projects/${pid}/sources`, { token: user.token, body: b });
check('boş kaynak → 422 empty_source', (await src({ title: 'x', text: '  ', baseVersion: base })).body?.code === 'empty_source');
const bigSrc = await src({ title: 'x', text: 'a'.repeat(100_001), baseVersion: base });
check('100001 karakterlik kaynak → 422 source_too_long', bigSrc.status === 422 && bigSrc.body?.code === 'source_too_long', bigSrc);
check('eski temele dayalı kaynak ekleme → 409', (await src({ title: 'x', text: 'metin burada', baseVersion: base - 1 })).status === 409);
const odd = await src({ title: 'Toplantı', text: 'Yöneticiler raporu PDF indirebilsin.', format: 'pdf', baseVersion: base });
check('tanınmayan format sessizce "text"e çevrilir, kaynak eklenir', odd.status === 201, odd);
const stored = (await call('GET', `/api/projects/${pid}`, { token: user.token })).body.model.sources.at(-1);
check('saklanan format yalnız "text" veya "markdown"', ['text', 'markdown'].includes(stored.format), stored.format);
const chg = (requestText) => call('POST', `/api/projects/${pid}/change-requests`, { token: user.token, body: { requestText } });
check('boş değişiklik talebi → 422 empty_request', (await chg('')).body?.code === 'empty_request');
check('1 karakterlik talep → 422 empty_request', (await chg('a')).body?.code === 'empty_request');
check('gövdesiz talep → 422', (await call('POST', `/api/projects/${pid}/change-requests`, { token: user.token, body: {} })).status === 422);
const tooLong = await chg('a'.repeat(8001));
check('8001 karakterlik talep → 422 request_too_long', tooLong.status === 422 && tooLong.body?.code === 'request_too_long', tooLong);
check('tam 8000 karakterlik talep kabul edilir (202)', (await chg('b'.repeat(8000))).status === 202);

section('İş ön koşulları');
const emptyProject = await newProject(user, 'Boş proje');
const noReq = await call('POST', `/api/projects/${emptyProject}/architecture-proposals`, { token: user.token });
check('gereksinimsiz projede mimari işi → 422 no_requirements', noReq.status === 422 && noReq.body?.code === 'no_requirements', noReq);
const analyzedProject = await newProject(user, 'Analiz edilmiş');
await withAcceptedRequirements(user, analyzedProject);
const noSrc = await call('POST', `/api/projects/${analyzedProject}/analyses`, { token: user.token });
check('tüm kaynaklar analiz edilmişken analiz işi → 422 no_pending_sources', noSrc.status === 422 && noSrc.body?.code === 'no_pending_sources', noSrc);

finish();
