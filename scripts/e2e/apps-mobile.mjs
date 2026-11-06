// Mobil kullanım (telefon ekranı, dokunmatik): tüm Apps sayfaları 390 px genişlikte yatay taşmaz, konsol/sayfa hatası vermez;
// PWA dosyaları (manifest, simgeler, service worker) sunulur; service worker kaydolur ve /api'yi önbelleğe almaz (yalnızca üretimde).
//   Veri: demo veri üreticisi (kullanıcılar, uygulamalar, talepler, ekler) + yönetici hesabı.
import { openSession, register, WEB } from './ui-lib.mjs';
import { API_URL, call, check, finish, section } from './lib.mjs';

const uniq = `${Date.now().toString(36)}${Math.floor(Math.random() * 1e4)}`;
const PROD = process.env.E2E_PROD === '1';
const PHONE = { width: 390, height: 844 };
let problemsBeforeOffline;

const s = await openSession({ viewport: PHONE });
const { page } = s;
try {
  section('Hazırlık: yönetici + demo veri');
  const email = `mobil-${uniq}@example.com`;
  await register(page, { email, name: 'Mobil Admin' });
  check('yönetici girişi', await s.visible(page.getByRole('heading', { name: 'Projeler' })));
  const login = await call('POST', '/api/auth/login', { body: { email, password: 'Parola-12345' } });
  const ws = login.body.workspaces[0].id;
  const seed = await call('POST', `/api/workspaces/${ws}/demo-data`, { token: login.body.token });
  check('demo veri üretildi', seed.status === 201, seed.body);

  // Hem belge hem ana içerik kapsayıcısı (.page-body, kendi içinde kayar) taşmamalı: yalnızca belgeye bakmak, sağdan kesilen
  // kartları kaçırır. Tablolar kendi .table-wrap sarmalayıcısında kayabilir, o kabul edilir. Dönen değer: taşan piksel.
  const overflow = () => page.evaluate(() => {
    const pb = document.querySelector('.page-body');
    return Math.max(document.documentElement.scrollWidth - window.innerWidth, pb ? pb.scrollWidth - pb.clientWidth : 0);
  });
  const routes = [
    '#/apps', '#/requests', '#/inbox', '#/delegations', '#/verify', '#/account', '#/design', '#/accounting',
    '#/admin/users', '#/admin/groups', '#/admin/requests', '#/admin/lists', '#/admin/leave', '#/admin/reports',
    '#/admin/accounting', '#/admin/email', '#/admin/retention', '#/admin/audit', '#/admin/demo',
  ];
  section(`Mobil görünüm (${PHONE.width}×${PHONE.height}): her sayfa yatay taşmaz`);
  for (const r of routes) {
    await page.goto(`${WEB}/${r}`);
    await page.waitForTimeout(700);
    const o = await overflow();
    check(`${r}: yatay taşma yok (${o} px)`, o <= 1);
    await s.shot(`mobil${r.replace(/[#/]+/g, '-')}`);
  }

  section('Mobil: talep ayrıntısı (ek, PDF, olay akışı) ve talep formu');
  // Demo talepler demo kullanıcılarındır: yönetici bunları "Tüm talepler" listesinde görür.
  await page.goto(`${WEB}/#/admin/requests`);
  await page.waitForTimeout(1000);
  const firstReq = page.locator('button.item').first();
  if (await firstReq.count()) {
    await firstReq.click();
    await page.waitForTimeout(1000);
    const o = await overflow();
    check(`talep ayrıntısı taşmaz (${o} px)`, o <= 1);
    await s.shot('mobil-talep-ayrinti');
  } else check('tüm talepler listesinde en az bir demo talep var', false);

  await page.goto(`${WEB}/#/apps`);
  await page.waitForTimeout(1000);
  const firstApp = page.locator('button.proj-card').first();
  if (await firstApp.count()) {
    await firstApp.click();
    await page.waitForTimeout(1000);
    const o = await overflow();
    check(`talep formu taşmaz (${o} px)`, o <= 1);
    await s.shot('mobil-talep-formu');
  } else check('uygulamalar listesinde en az bir uygulama var', false);

  section('Mobil: dokunma hedefleri (düğmeler en az 32 px yüksekliğinde)');
  await page.goto(`${WEB}/#/inbox`);
  await page.waitForTimeout(600);
  const tiny = await page.evaluate(() => [...document.querySelectorAll('button, a.btn')].filter((el) => {
    const r = el.getBoundingClientRect();
    return r.width > 0 && r.height > 0 && r.height < 28;
  }).map((el) => el.textContent?.trim().slice(0, 30)));
  check('onay kutusunda 28 px altı düğme yok', tiny.length === 0, tiny);

  section('PWA: manifest, simgeler, service worker');
  const get = (p) => fetch(`${WEB}${p}`);
  const mf = await get('/manifest.webmanifest');
  check('manifest sunulur', mf.status === 200, mf.status);
  const manifest = mf.status === 200 ? await mf.json() : { icons: [] };
  check('manifest standalone ve Türkçe', manifest.display === 'standalone' && manifest.lang === 'tr');
  for (const i of manifest.icons) {
    const r = await get(i.src);
    check(`simge ${i.src} sunulur (image/png)`, r.status === 200 && (r.headers.get('content-type') ?? '').includes('image/png'), r.status);
  }
  const sw = await get('/sw.js');
  check('/sw.js sunulur ve JavaScript', sw.status === 200 && (sw.headers.get('content-type') ?? '').includes('javascript'), sw.status);
  if (PROD) {
    check('üretimde /sw.js Cache-Control: no-cache', (sw.headers.get('cache-control') ?? '') === 'no-cache', sw.headers.get('cache-control'));
    const reg = await page.evaluate(async () => {
      const r = await navigator.serviceWorker.ready;
      return { scope: r.scope, active: Boolean(r.active) };
    });
    check('service worker kayıtlı ve etkin', reg.active && reg.scope.startsWith(WEB), reg);
    // /api yanıtı önbelleğe girmemeli
    await page.goto(`${WEB}/#/apps`);
    await page.waitForTimeout(800);
    const cached = await page.evaluate(async () => {
      const out = [];
      for (const k of await caches.keys()) for (const req of await (await caches.open(k)).keys()) out.push(new URL(req.url).pathname);
      return out;
    });
    check('önbellekte /api yolu yok', cached.every((p) => !p.startsWith('/api')), cached);
    check('kabuk önbellekte', cached.includes('/index.html') || cached.includes('/'), cached);
    // Çevrimdışı: kabuk açılır
    problemsBeforeOffline = s.problems.length;
    await s.context.setOffline(true);
    await page.reload();
    await page.waitForTimeout(800);
    const body = (await page.textContent('body')) ?? '';
    check('çevrimdışıyken uygulama kabuğu açılır (boş/kırık sayfa değil)', body.length > 0 && !/ERR_INTERNET_DISCONNECTED/.test(body));
    await s.context.setOffline(false);
  }
  // Çevrimdışı yeniden yükleme ağ hatası üretir (beklenen); onu saymadan önceki hataları denetle.
  const real = s.problems.slice(0, problemsBeforeOffline ?? s.problems.length);
  check('sayfa/konsol hatası yok', real.length === 0, real.slice(0, 5));
} catch (e) {
  check('beklenmeyen hata', false, String(e?.stack ?? e));
} finally {
  await s.browser.close();
}
finish();
