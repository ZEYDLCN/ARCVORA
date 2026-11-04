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

finish();
