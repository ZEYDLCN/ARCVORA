// Uç durumlar: kimlik, girdi doğrulama, güvenlik, sürümler, dışa aktarma, öneri durum geçişleri.
//   ARCVORA_API_URL=http://127.0.0.1:5217 node scripts/e2e/api-edge.mjs
import { API_URL, call, check, fatal, finish, section, waitHealth } from './lib.mjs';
import { newUser } from './fixtures.mjs';

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

finish();
