// Kimlik uçlarında hız sınırı (kaba kuvvet koruması). API'nin RateLimit__AuthPerMinute=3 ile başlatılmış olması gerekir.
//   RateLimit__AuthPerMinute=3 ... ARCVORA_API_URL=http://127.0.0.1:5217 node scripts/e2e/api-ratelimit.mjs
import { API_URL, call, check, fatal, finish, section, waitHealth } from './lib.mjs';
import { newUser } from './fixtures.mjs';

if (!(await waitHealth(60_000))) fatal('API açılmadı');
const LIMIT = 3;

section('Kimlik uçlarında dakikalık sınır');
const user = await newUser('rl'); // kayıt = 1. istek
const bad = () => fetch(`${API_URL}/api/auth/login`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email: user.email, password: 'yanlis-parola-1' }) });
const codes = [];
for (let i = 0; i < LIMIT - 1; i++) codes.push((await bad()).status);
check(`sınıra kadar (${LIMIT}) istekler normal işlenir (401, 429 değil)`, codes.every((c) => c === 401), codes);
const blocked = await bad();
check('sınır aşılınca → 429', blocked.status === 429, blocked.status);
const retry = Number(blocked.headers.get('retry-after'));
check('Retry-After başlığı 1–60 sn arası bir sayı', Number.isInteger(retry) && retry >= 1 && retry <= 60, blocked.headers.get('retry-after'));
let body = null;
try { body = await blocked.json(); } catch { /* boş gövde */ }
check('429 yanıtı açıklayıcı JSON içerir (code=rate_limited, title dolu)', body?.code === 'rate_limited' && typeof body.title === 'string' && body.title.length > 10, body);
check('429 yanıtı application/problem+json', /application\/problem\+json/.test(blocked.headers.get('content-type') ?? ''), blocked.headers.get('content-type'));
const reg = await call('POST', '/api/auth/register', { body: { email: `rl2-${Date.now()}@example.com`, password: 'Parola-12345' } });
check('sınır kayıt ucunu da kapsar (aynı pencere)', reg.status === 429, reg.status);

section('Sınır yalnız kimlik uçlarını etkiler');
check('kimlik gerektirmeyen uç (/profiles) etkilenmez', (await call('GET', '/api/profiles')).status === 200);
check('oturum açık kullanıcının işlemleri etkilenmez (/me, /projects)',
  (await call('GET', '/api/me', { token: user.token })).status === 200 && (await call('GET', '/api/projects', { token: user.token })).status === 200);
check('/health etkilenmez', (await fetch(`${API_URL}/health`)).status === 200);

section('X-Forwarded-For varsayılan olarak güvenilmez (sınırı aşmak için kullanılamaz)');
const spoof = await fetch(`${API_URL}/api/auth/login`, {
  method: 'POST',
  headers: { 'content-type': 'application/json', 'x-forwarded-for': '203.0.113.77' },
  body: JSON.stringify({ email: user.email, password: 'yanlis-parola-1' }),
});
check('sınır dolmuşken sahte X-Forwarded-For başlığı kovası değiştirmez → hâlâ 429', spoof.status === 429, spoof.status);

finish();
