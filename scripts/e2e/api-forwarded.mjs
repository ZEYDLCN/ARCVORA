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
const chain = await login('198.51.100.30, 10.0.0.1');
check('zincirli başlıkta ilk (gerçek istemci) adres kullanılır → 401', chain.status === 401, chain.status);
check('istemci A hâlâ engelli (kovası dolu)', (await login('198.51.100.10')).status === 429);

finish();
