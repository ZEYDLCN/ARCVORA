// Uç durumlar: kimlik, girdi doğrulama, güvenlik, sürümler, dışa aktarma, öneri durum geçişleri.
//   ARCVORA_API_URL=http://127.0.0.1:5217 node scripts/e2e/api-edge.mjs
import { API_URL, call, check, fatal, finish, section, waitHealth, waitJob } from './lib.mjs';
import { readZip } from './zip.mjs';
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

section('Düşmanca metinler: Mermaid görünümleri');
const sec = await newUser('sec');
const spid = await newProject(sec, 'Güvenlik');
await withAcceptedRequirements(sec, spid);
const archJob = await waitJob(sec.token, (await call('POST', `/api/projects/${spid}/architecture-proposals`, { token: sec.token })).body.id);
await call('POST', `/api/projects/${spid}/proposals/${archJob.proposalId}/accept`, { token: sec.token, body: {} });
const comp = (await call('GET', `/api/projects/${spid}`, { token: sec.token })).body.model.components[0];
const evil = 'Web"] %%{init:{"securityLevel":"loose"}}%% <script>alert(1)</script> -->|x| Z[<img src=x onerror=alert(1)>]';
const beforeNo = await head(sec, spid);
const beforeViews = (await call('GET', `/api/projects/${spid}/versions/${beforeNo}/views`, { token: sec.token })).body.views;
const patched = await call('POST', `/api/projects/${spid}/patches`, {
  token: sec.token,
  body: { baseVersion: beforeNo, summary: 'ad', patch: { operations: [{ op: 'upsert', type: 'component', id: comp.id,
    value: { ...comp, name: evil, responsibility: '```\n# kırma\n```', technology: '<b>x</b>' } }] } },
});
check('düşmanca ad modele ham haliyle yazılır (veri bütünlüğü; kaçışlama çıktıda yapılır)', patched.status === 200, patched);
const afterViews = (await call('GET', `/api/projects/${spid}/versions/${beforeNo + 1}/views`, { token: sec.token })).body.views;
check('hiçbir Mermaid görünümünde "%%{" yönergesi kalmaz', afterViews.every((v) => !v.mermaid.includes('%%{')), afterViews.map((v) => v.kind));
check('hiçbir Mermaid görünümünde ham <script> / <img> etiketi kalmaz', afterViews.every((v) => !/<script|<img/i.test(v.mermaid)));
const lines = (kind, views) => views.find((v) => v.kind === kind).mermaid.split('\n').length;
check('ad satır sonu/ok enjekte edemez: diyagramın satır sayısı değişmez (bileşen görünümü)', lines('component', afterViews) === lines('component', beforeViews), [lines('component', beforeViews), lines('component', afterViews)]);
check('ad yeni düğüm/kenar üretmez: "-->|x|" kaçışlanmış', !afterViews.find((v) => v.kind === 'component').mermaid.includes('-->|x|'));

section('Düşmanca metinler: Markdown dışa aktarma ve dosya adı');
const exp = await call('POST', `/api/projects/${spid}/exports`, { token: sec.token, body: { format: 'markdown' } });
const dl = await call('GET', `/api/projects/${spid}/exports/${exp.body.id}/download`, { token: sec.token, raw: true });
check('indirilen Markdown ham <script> içermez (HTML kaçışlanır)', dl.status === 200 && !dl.body.includes('<script') && dl.body.includes('&lt;script&gt;'));
const row = dl.body.split('\n').find((l) => l.startsWith(`| \`${comp.id}\` |`));
check('bileşen satırı tablo olarak bozulmadan tek satırda kalır (| kaçışlı, satır sonu yok)', !!row && row.trimEnd().endsWith('|') && row.includes('\\|x\\|'), row);
const sneaky = await newProject(sec, '../../etc/passwd"\r\nSet-Cookie: a=b; x.md');
await withAcceptedRequirements(sec, sneaky);
const exp2 = await call('POST', `/api/projects/${sneaky}/exports`, { token: sec.token, body: { format: 'markdown' } });
const dl2 = await call('GET', `/api/projects/${sneaky}/exports/${exp2.body.id}/download`, { token: sec.token, raw: true });
const disp = dl2.headers.get('content-disposition') ?? '';
check('dosya adı yol/başlık enjeksiyonu içermez (/, \\, .., CR/LF yok)', dl2.status === 200 && !/[\/\\\r\n]|\.\./.test(disp.replace(/^attachment; /, '')), disp);
check('yanıtta enjekte edilmiş Set-Cookie başlığı yok', dl2.headers.get('set-cookie') === null);
check('Markdown yanıtı text/markdown ve nosniff ile gelir', /text\/markdown/.test(dl.headers.get('content-type') ?? '') && dl.headers.get('x-content-type-options') === 'nosniff', [...dl.headers.entries()]);

section('Sürüm okuma uç durumları');
const vu = await newUser('ver');
const vpid = await newProject(vu);
await withAcceptedRequirements(vu, vpid);
const vget = (path) => call('GET', `/api/projects/${vpid}${path}`, { token: vu.token });
check('olmayan sürüm (v99) → 404', (await vget('/versions/99')).status === 404);
for (const bad of ['0', 'abc', '-1']) check(`geçersiz sürüm "${bad}" → 400 (500 değil)`, (await vget(`/versions/${bad}`)).status === 400);
const vlist = (await vget('/versions')).body;
const byGuid = await vget(`/versions/${vlist[0].id}`);
check('sürüm GUID ile de okunur ve numarası eşleşir', byGuid.status === 200 && byGuid.body.info.number === vlist[0].number, byGuid.body?.info);
check('olmayan sürümün görünümleri/bulguları → 404', (await vget('/versions/99/views')).status === 404 && (await vget('/versions/99/findings')).status === 404);
const v1 = (await vget('/versions/1')).body;
const v2 = (await vget('/versions/2')).body;
check('eski sürüm kendi içeriğini korur (v1 gereksinimsiz, v2 gereksinimli)', v1.model.requirements.length === 0 && v2.model.requirements.length > 0, [v1.model.requirements.length, v2.model.requirements.length]);

section('Geri alma uç durumları');
const hv = await head(vu, vpid);
const toHead = await call('POST', `/api/projects/${vpid}/versions/${hv}/revert`, { token: vu.token, body: { baseVersion: hv } });
check('güncel sürüme dönüş → 422 already_head', toHead.status === 422 && toHead.body?.code === 'already_head', toHead);
check('olmayan sürüme dönüş → 404', (await call('POST', `/api/projects/${vpid}/versions/99/revert`, { token: vu.token, body: { baseVersion: hv } })).status === 404);
check('eski temelle geri alma → 409 version_conflict', (await call('POST', `/api/projects/${vpid}/versions/1/revert`, { token: vu.token, body: { baseVersion: hv - 1 } })).body?.code === 'version_conflict');
const rev = await call('POST', `/api/projects/${vpid}/versions/1/revert`, { token: vu.token, body: { baseVersion: hv } });
check('v1\'e dönüş yeni sürüm üretir (kind=revert)', rev.status === 200 && rev.body.version.number === hv + 1 && rev.body.version.kind === 'revert', rev);
check('dönülen sürümün içeriği v1 ile aynı özet (hash) taşır', rev.body.version.contentHash === (await vget('/versions/1')).body.info.contentHash, rev.body?.version);
check('geçmiş silinmedi: v2 hâlâ okunabilir', (await vget('/versions/2')).status === 200);
const revRev = await call('POST', `/api/projects/${vpid}/versions/2/revert`, { token: vu.token, body: { baseVersion: hv + 1 } });
check('geri almayı geri alma da yeni sürümdür (v' + (hv + 2) + ')', revRev.status === 200 && revRev.body.version.number === hv + 2, revRev);
check('gereksinimler geri geldi', (await vget('')).body.model.requirements.length > 0);

section('Dışa aktarma biçimleri');
const expPost = (body) => call('POST', `/api/projects/${vpid}/exports`, { token: vu.token, body });
const badFmt = await expPost({ format: 'docx' });
check('desteklenmeyen biçim → 422 unknown_format', badFmt.status === 422 && badFmt.body?.code === 'unknown_format', badFmt);
check('olmayan sürümün çıktısı → 404', (await expPost({ format: 'markdown', versionNumber: 99 })).status === 404);
const e1 = await expPost({ format: 'markdown', versionNumber: 2 });
const e1b = await expPost({ format: 'markdown', versionNumber: 2 });
check('aynı sürüm+biçim için çıktı tekrar üretilmez (aynı kimlik ve özet)', e1.body.id === e1b.body.id && e1.body.modelHash === e1b.body.modelHash, [e1.body.id, e1b.body.id]);
const old = await call('GET', `/api/projects/${vpid}/exports/${e1.body.id}/download`, { token: vu.token, raw: true });
check('eski sürümün Markdown çıktısı o sürümün gereksinimlerini içerir', old.status === 200 && /REQ-001/.test(old.body), old.body.slice(0, 120));
const pack = await expPost({ format: 'package', versionNumber: 2 });
check('paket çıktısı 201 ve model özeti kaydı var', pack.status === 201 && !!pack.body.modelHash, pack);
const zipRes = await fetch(`${API_URL}/api/projects/${vpid}/exports/${pack.body.id}/download`, { headers: { authorization: `Bearer ${vu.token}` } });
check('paket application/zip olarak iner', zipRes.status === 200 && /application\/zip/.test(zipRes.headers.get('content-type') ?? ''), zipRes.headers.get('content-type'));
const zip = readZip(Buffer.from(await zipRes.arrayBuffer()));
const expected = ['PROJECT_OVERVIEW.md', 'REQUIREMENTS.md', 'ARCHITECTURE.md', 'DECISIONS.md', 'OPEN_QUESTIONS.md', 'CHANGELOG.md', 'MANIFEST.json'];
check('zip beklenen 7 dosyayı içerir', expected.every((n) => zip[n]) && Object.keys(zip).length === expected.length, Object.keys(zip));
const manifest = JSON.parse(zip['MANIFEST.json'].toString('utf8'));
check('MANIFEST model sürümü ve özeti çıktı kaydıyla aynı', manifest.modelVersion === 2 && manifest.modelHash === pack.body.modelHash, manifest);
check('MANIFEST üretici ve profil sürümünü belirtir', !!manifest.generator && !!manifest.profile?.profileVersion, manifest);
check('MANIFEST "üretime hazır" demez, dürüstlük notu taşır', /üretime hazır olduğu doğrulandı.*anlamına gelmez/.test(manifest.notice ?? ''), manifest.notice);
check('olmayan çıktı indirme → 404', (await call('GET', `/api/projects/${vpid}/exports/11111111-1111-1111-1111-111111111111/download`, { token: vu.token })).status === 404);
const stranger = await newUser('ver-out');
check('başkasının çıktısını indirme → 404', (await call('GET', `/api/projects/${vpid}/exports/${e1.body.id}/download`, { token: stranger.token })).status === 404);
check('çıktı listesi dışa aktarılanları gösterir', (await call('GET', `/api/projects/${vpid}/exports`, { token: vu.token })).body.length >= 2);

section('Öneri durum geçişleri');
const pu = await newUser('prop');
const ppid = await newProject(pu);
await withAcceptedRequirements(pu, ppid);
const pcall = (method, path, body, headers) => call(method, `/api/projects/${ppid}${path}`, { token: pu.token, body, headers });
const genArch = async () => (await waitJob(pu.token, (await pcall('POST', '/architecture-proposals')).body.id)).proposalId;
const A = await genArch();
check('olmayan öneri → 404', (await pcall('GET', '/proposals/11111111-1111-1111-1111-111111111111')).status === 404);
const badGroup = await pcall('POST', `/proposals/${A}/accept`, { groupIds: ['grp-yok'] });
check('bilinmeyen grup → 422 invalid_selection', badGroup.status === 422 && badGroup.body?.code === 'invalid_selection', badGroup);
const noGroup = await pcall('POST', `/proposals/${A}/accept`, { groupIds: [] });
check('boş grup listesi → 422 invalid_selection', noGroup.status === 422 && noGroup.body?.code === 'invalid_selection', noGroup);
check('reddedilen kabul denemeleri öneriyi bozmadı (hâlâ pending)', (await pcall('GET', '/proposals?status=pending')).body.some((x) => x.id === A));
check('öneri reddedilir', (await pcall('POST', `/proposals/${A}/reject`, { note: 'olmaz' })).body?.status === 'rejected');
check('aynı öneriyi tekrar reddetmek zararsız (idempotent 200)', (await pcall('POST', `/proposals/${A}/reject`, {})).status === 200);
const accRej = await pcall('POST', `/proposals/${A}/accept`, {});
check('reddedilmiş öneri kabul edilemez → 409 proposal_rejected', accRej.status === 409 && accRej.body?.code === 'proposal_rejected', accRej);
check('durum filtresi: rejected yalnız reddedilenleri listeler', (await pcall('GET', '/proposals?status=rejected')).body.every((x) => x.status === 'rejected'));
const badFilter = await pcall('GET', '/proposals?status=zzz');
check('sayısal durum filtresi ("1") de reddedilir', (await pcall('GET', '/proposals?status=1')).body?.code === 'invalid_status');
check('durum filtresi büyük/küçük harfe duyarsız (PENDING geçerli)', (await pcall('GET', '/proposals?status=PENDING')).status === 200);
check('geçersiz durum filtresi → 422 invalid_status (sessizce yok sayılmaz)', badFilter.status === 422 && badFilter.body?.code === 'invalid_status', badFilter);
const B = await genArch();
const accB = await pcall('POST', `/proposals/${B}/accept`, {});
check('yeni öneri kabul edilir', accB.status === 200, accB);
const rejAcc = await pcall('POST', `/proposals/${B}/reject`, {});
check('kabul edilmiş öneri reddedilemez → 409 proposal_accepted', rejAcc.status === 409 && rejAcc.body?.code === 'proposal_accepted', rejAcc);

section('Mimariyi yeniden üretmek bileşenleri çoğaltmaz (regresyon)');
const ru = await newUser('regen');
const rpid = await newProject(ru);
await withAcceptedRequirements(ru, rpid);
const rcall = (method, path, body, headers) => call(method, `/api/projects/${rpid}${path}`, { token: ru.token, body, headers });
const regen = async () => waitJob(ru.token, (await rcall('POST', '/architecture-proposals')).body.id);
const g1 = await regen();
await rcall('POST', `/proposals/${g1.proposalId}/accept`, {});
const model1 = (await rcall('GET', '')).body.model;
const g2 = await regen();
check('ikinci mimari işi hata vermeden biter', g2.status === 'succeeded', g2);
const d2 = (await rcall('GET', `/proposals/${g2.proposalId}`)).body;
check('mevcut mimari varken yeni öneri etki kalemi içermez (yeniden önermez)', (d2.impact?.items?.length ?? 0) === 0, d2.impact?.items?.length);
const acc2 = await rcall('POST', `/proposals/${g2.proposalId}/accept`, {});
check('boş öneriyi kabul → 422 empty_patch (sürüm üretmez)', acc2.status === 422 && acc2.body?.code === 'empty_patch', acc2);
const model2 = (await rcall('GET', '')).body.model;
check('bileşen, karar ve ilişki sayıları değişmedi',
  model2.components.length === model1.components.length && model2.decisions.length === model1.decisions.length && model2.relationships.length === model1.relationships.length,
  [model1.components.length, model2.components.length]);
const names = model2.components.map((c) => c.name);
check('aynı adlı birden fazla bileşen yok', new Set(names).size === names.length, names);

section('Idempotency-Key ve iş yaşam döngüsü');
const iu = await newUser('idem');
const ipid = await newProject(iu);
await withAcceptedRequirements(iu, ipid);
const icall = (method, path, body, headers) => call(method, `/api/projects/${ipid}${path}`, { token: iu.token, body, headers });
const k1 = await icall('POST', '/change-requests', { requestText: 'Aynı anahtar' }, { 'Idempotency-Key': 'ayni-1' });
const k2 = await icall('POST', '/change-requests', { requestText: 'Farklı metin, aynı anahtar' }, { 'Idempotency-Key': 'ayni-1' });
check('aynı anahtar aynı işi döndürür (ikinci yanıt 200, yeni iş yok)', k1.status === 202 && k2.status === 200 && k1.body.id === k2.body.id, [k1.status, k2.status]);
const k3 = await icall('POST', '/change-requests', { requestText: 'Aynı anahtar' }, { 'Idempotency-Key': 'baska' });
check('farklı anahtar yeni iş üretir', k3.body.id !== k1.body.id);
const k4 = await icall('POST', '/change-requests', { requestText: 'Anahtarsız' });
const k5 = await icall('POST', '/change-requests', { requestText: 'Anahtarsız' });
check('anahtarsız istekler her seferinde yeni iş üretir', k4.body.id !== k5.body.id);
const longKey = await icall('POST', '/change-requests', { requestText: 'uzun anahtar' }, { 'Idempotency-Key': 'k'.repeat(101) });
check('101 karakterlik anahtar → 400', longKey.status === 400, longKey);
const ipid2 = await newProject(iu, 'Diğer');
await withAcceptedRequirements(iu, ipid2);
const sameKeyOther = await call('POST', `/api/projects/${ipid2}/architecture-proposals`, { token: iu.token, headers: { 'Idempotency-Key': 'ayni-1' } });
check('aynı anahtar başka projede başka işi üretir (anahtar proje kapsamlı)', sameKeyOther.status === 202 && sameKeyOther.body.projectId === ipid2, sameKeyOther);
const finished = await waitJob(iu.token, k1.body.id);
check('iş tamamlanır', finished.status === 'succeeded', finished);
const cancelDone = await call('POST', `/api/jobs/${k1.body.id}/cancel`, { token: iu.token });
check('biten işi iptal etmek zararsız: durum succeeded kalır', cancelDone.status === 200 && cancelDone.body.status === 'succeeded', cancelDone);
check('olmayan iş → 404', (await call('GET', '/api/jobs/11111111-1111-1111-1111-111111111111', { token: iu.token })).status === 404);
const stranger2 = await newUser('idem-out');
check('başkasının işini görmek → 404', (await call('GET', `/api/jobs/${k1.body.id}`, { token: stranger2.token })).status === 404);
check('başkasının işini iptal etmek → 404', (await call('POST', `/api/jobs/${k1.body.id}/cancel`, { token: stranger2.token })).status === 404);
const jobList = await icall('GET', '/jobs');
check('iş listesi projenin işlerini içerir', jobList.status === 200 && jobList.body.some((j) => j.id === k1.body.id), jobList.body?.length);

section('Yama bütünlüğü: modeli bozan değişiklikler reddedilir');
const iu2 = await newUser('integrity');
const ipid3 = await newProject(iu2);
await withAcceptedRequirements(iu2, ipid3);
const archI = await waitJob(iu2.token, (await call('POST', `/api/projects/${ipid3}/architecture-proposals`, { token: iu2.token })).body.id);
await call('POST', `/api/projects/${ipid3}/proposals/${archI.proposalId}/accept`, { token: iu2.token, body: {} });
const modelI = (await call('GET', `/api/projects/${ipid3}`, { token: iu2.token })).body.model;
const headBefore = await head(iu2, ipid3);
const patchI = async (operations) => call('POST', `/api/projects/${ipid3}/patches`, { token: iu2.token, body: { baseVersion: await head(iu2, ipid3), summary: 't', patch: { operations } } });
const apiComp = modelI.components.find((c) => modelI.relationships.some((r) => r.sourceId === c.id || r.targetId === c.id));
const rm = await patchI([{ op: 'remove', type: 'component', id: apiComp.id }]);
check('ilişkisi olan bileşeni silmek → 422 validation_failed', rm.status === 422 && rm.body?.code === 'validation_failed', rm);
check('sarkan referans kural kimliğiyle bildirilir (REF-…)', rm.body?.details?.some((d) => /^REF-/.test(d.ruleId)), rm.body?.details);
const reqRm = await patchI([{ op: 'remove', type: 'requirement', id: modelI.requirements[0].id }]);
check('izlenebilirlik bağlantısı olan gereksinimi silmek → 422', reqRm.status === 422 && reqRm.body?.code === 'validation_failed', reqRm);
const unknownRm = await patchI([{ op: 'remove', type: 'component', id: 'CMP-999' }]);
check('olmayan nesneyi silmek → 422 patch_invalid', unknownRm.status === 422 && unknownRm.body?.code === 'patch_invalid', unknownRm);
check('boş işlem listesi → 422 empty_patch', (await patchI([])).body?.code === 'empty_patch');
const collide = await patchI([{ op: 'upsert', type: 'actor', id: modelI.components[0].id, value: { id: modelI.components[0].id, name: 'çakışma' } }]);
check('başka türde nesneyle kimlik çakışması → 422 (SCH-001)', collide.status === 422 && collide.body?.details?.some((d) => d.ruleId === 'SCH-001'), collide);
const mismatch = await patchI([{ op: 'upsert', type: 'component', id: 'CMP-050', value: { id: 'CMP-051', name: 'a' } }]);
check('işlem kimliği ile nesne kimliği uyuşmuyor → 422 patch_invalid', mismatch.status === 422 && mismatch.body?.code === 'patch_invalid', mismatch);
check('bilinmeyen nesne türü → 400', (await patchI([{ op: 'upsert', type: 'foo', id: 'X-1', value: {} }])).status === 400);
const srcRm = await patchI([{ op: 'remove', type: 'source', id: 'SRC-001' }]);
check('kaynak silinemez → 422 source_removal', srcRm.status === 422 && srcRm.body?.code === 'source_removal', srcRm);
check('reddedilen yamaların hiçbiri sürüm üretmedi', (await head(iu2, ipid3)) === headBefore, await head(iu2, ipid3));

section('Yama: geçici kimlikler gerçek kimliğe çevrilir');
const temp = await patchI([{ op: 'upsert', type: 'component', id: 'x y', value: { id: 'x y', name: 'Boşluklu kimlikli' } }]);
check('biçimi bozuk kimlikli yeni nesne kabul edilir', temp.status === 200, temp);
const modelT = (await call('GET', `/api/projects/${ipid3}`, { token: iu2.token })).body.model;
const added = modelT.components.find((c) => c.name === 'Boşluklu kimlikli');
check('nesne ham kimlikle değil, sistemin atadığı CMP-NNN kimliğiyle saklanır', /^CMP-\d{3,}$/.test(added?.id ?? ''), added);
const own = await patchI([{ op: 'upsert', type: 'component', id: 'CMP-777', value: { id: 'CMP-777', name: 'Kendi kimliği' } }]);
check('biçimi geçerli yeni kimlik olduğu gibi korunur', own.status === 200 && (await call('GET', `/api/projects/${ipid3}`, { token: iu2.token })).body.model.components.some((c) => c.id === 'CMP-777'));
const dupName = await patchI([{ op: 'upsert', type: 'relationship', id: 'REL-900', value: { id: 'REL-900', sourceId: added.id, targetId: added.id } }]);
check('kendine bağlantı reddedilmez ama doğrulama uyarısı üretir (CON-001)', dupName.status === 200 && (await call('GET', `/api/projects/${ipid3}/versions/${await head(iu2, ipid3)}/findings`, { token: iu2.token })).body.findings.some((f) => f.ruleId === 'CON-001'));

finish();
