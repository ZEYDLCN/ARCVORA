// Proxy arkasında (Render, Fly, Koyeb, nginx…) gerçek istemci IP'si X-Forwarded-For'dan okunmalı; aksi halde tüm kullanıcılar
// proxy'nin tek IP'sini paylaşır ve kimlik hız sınırı HERKES için ortak dolar.
// API'nin ForwardedHeaders__Enabled=true ve RateLimit__AuthPerMinute=3 ile başlatılmış olması gerekir.
//   ARCVORA_API_URL=http://127.0.0.1:5217 node scripts/e2e/api-forwarded.mjs
import { API_URL, check, fatal, finish, section, waitHealth } from './lib.mjs';

if (!(await waitHealth(60_000))) fatal('API açılmadı');
const login = (ip, password = 'yanlis-parola-1') => fetch(`${API_URL}/api/auth/login`, {
  method: 'POST',
  headers: { 'content-type': 'application/json', ...(ip ? { 'x-forwarded-for': ip } : {}) },
  body: JSON.stringify({ email: 'kimse@example.com', password }),
});

section('Proxy arkasında istemci başına hız sınırı');
const a = [];
for (let i = 0; i < 4; i++) a.push((await login('198.51.100.10')).status);
check('istemci A: ilk 3 istek 401, 4.sü 429 (kendi kovası)', a.slice(0, 3).every((c) => c === 401) && a[3] === 429, a);
const b = await login('198.51.100.20');
check('istemci B (başka IP) A\'nın yüzünden engellenmez → 401', b.status === 401, b.status);
// Zincirde en SAĞDAki adres, bizim proxy'mizin eklediğidir (güvenilir); soldakiler istemci tarafından sahtelenebilir.
const freshRight = await login('198.51.100.10, 198.51.100.40');
check('zincirde sağdaki adres (proxy\'nin eklediği) kovayı belirler: soldaki dolu A adresi sayılmaz → 401', freshRight.status === 401, freshRight.status);
const dodge = await login('203.0.113.99, 198.51.100.10');
check('istemci sola sahte adres ekleyerek sınırdan kaçamaz (sağdaki gerçek adres A, kovası dolu) → 429', dodge.status === 429, dodge.status);
check('istemci A hâlâ engelli (kovası dolu)', (await login('198.51.100.10')).status === 429);

finish();
