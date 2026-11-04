// Tarayıcı (Chromium) uçtan uca yolculuğu: açılış animasyonu → kayıt → örnek proje → tuval/Mermaid → ⌘K arama →
// yeni proje → gereksinim çıkarımı → mimari taslak → kısmi kabul → değişiklik talebi → dışa aktarma → mobil → çıkış.
//   ARCVORA_WEB_URL=http://127.0.0.1:5173 node scripts/e2e/ui-journey.mjs
// Gerekir: çalışan API + web (scripts/dev.sh veya scripts/e2e.sh) ve Playwright (cd web && npm i -D playwright && npx playwright install chromium).
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import path from 'node:path';
import { check, finish, section, fatal } from './lib.mjs';

const require = createRequire(import.meta.url);
let chromium;
try {
  ({ chromium } = require('playwright'));
} catch {
  try {
    ({ chromium } = createRequire(path.resolve('web/package.json'))('playwright'));
  } catch {
    console.error('Playwright bulunamadı. Kurulum: cd web && npm i -D playwright && npx playwright install chromium');
    process.exit(2);
  }
}

const WEB = (process.env.ARCVORA_WEB_URL ?? 'http://127.0.0.1:5173').replace(/\/$/, '');
const SHOTS = process.env.E2E_SHOTS_DIR ?? path.resolve('e2e-screenshots');
const JOB_MS = Number(process.env.E2E_JOB_TIMEOUT_MS ?? 180_000); // gerçek yerel model yavaş olabilir
const run = Date.now();
mkdirSync(SHOTS, { recursive: true });

const browser = await chromium.launch();
const context = await browser.newContext({ viewport: { width: 1360, height: 860 }, acceptDownloads: true, locale: 'tr-TR' });
const page = await context.newPage();
const problems = [];
page.on('pageerror', (e) => problems.push(`pageerror: ${e.message}`));
page.on('console', (m) => {
  if (m.type() === 'error') problems.push(`console.error: ${m.text()}`);
});
page.on('requestfailed', (r) => problems.push(`istek başarısız: ${r.method()} ${r.url()} ${r.failure()?.errorText}`));

const shot = (name) => page.screenshot({ path: path.join(SHOTS, `${name}.png`) }).catch(() => {});
const nav = (label) => page.locator('.sb-item', { has: page.locator('.label', { hasText: new RegExp(`^${label}`) }) }).first();
// <option> öğeleri Playwright'a "görünür" sayılmaz; sürüm listesinde belirmesini bekler.
const waitVersion = (n, timeout = 15_000) => page
  .waitForFunction((v) => [...document.querySelectorAll('#ver option')].some((o) => o.textContent.startsWith(`v${v}`)), n, { timeout })
  .then(() => true, () => false);
const visible = async (locator, timeout = 15_000) => {
  try {
    await locator.first().waitFor({ state: 'visible', timeout });
    return true;
  } catch {
    return false;
  }
};

try {
  console.log(`ARCVORA arayüz uçtan uca testi → ${WEB}`);

  section('Açılış animasyonu');
  await page.goto(`${WEB}/?intro`);
  check('açılış animasyonu görünür', await visible(page.locator('.intro'), 5000));
  await shot('01-intro');
  check('animasyon ~2,5 sn sonra kendiliğinden kalkar', await page.locator('.intro').waitFor({ state: 'detached', timeout: 8000 }).then(() => true, () => false));
  check('altındaki giriş ekranı hazır', await visible(page.getByRole('heading', { name: /Hesap oluştur|Tekrar hoş geldin/ })));
  await page.goto(WEB); // aynı oturumda ikinci açılış: animasyon tekrar etmemeli
  await page.waitForTimeout(600);
  check('aynı oturumda yeniden açılışta animasyon tekrarlanmaz', (await page.locator('.intro').count()) === 0);
  await page.goto(`${WEB}/?intro`);
  await page.keyboard.press('Escape');
  check('tuşa basınca animasyon atlanır', await page.locator('.intro').waitFor({ state: 'detached', timeout: 3000 }).then(() => true, () => false));

  section('Kayıt ve oturum');
  await page.getByRole('tab', { name: 'Kayıt ol' }).click();
  await page.getByLabel('Ad').fill('UI Test');
  await page.getByLabel(/E-posta/i).fill(`ui-${run}@example.com`);
  await page.locator('#password').fill('Parola-12345');
  await page.getByRole('button', { name: 'Hesap oluştur' }).click();
  check('kayıttan sonra Projeler sayfası açılır', await visible(page.getByRole('heading', { name: 'Projeler' })));
  await shot('02-projeler');

  section('Örnek proje: tuval, Mermaid, arama');
  await page.getByRole('button', { name: 'Örnek projeyi incele' }).first().click();
  check('örnek proje çalışma alanı açılır', await visible(page.getByRole('heading', { name: /Masraf/ }), 30_000));
  await page.waitForTimeout(800);
  await shot('03-tuval');
  const nodeCount = await page.locator('svg.canvas g[data-node-id]').count();
  check('mimari tuvali bileşen düğümlerini çizdi', nodeCount >= 4, nodeCount);
  const logoCount = await page.locator('svg.tech-logo').count();
  check('teknoloji logoları görünür (düğümlerde ve yığın şeridinde)', logoCount >= 3, logoCount);
  await page.getByRole('tab', { name: /Bileşenler \(Mermaid\)/ }).click();
  check('Mermaid diyagramı çizilir (strict mod, SVG üretildi)', await visible(page.locator('.diagram svg'), 30_000));
  await shot('04-mermaid');
  await page.getByRole('tab', { name: 'Veri modeli' }).click();
  check('veri modeli (ER) Mermaid çizilir', await visible(page.locator('.diagram svg'), 30_000));

  await page.keyboard.press('Control+k');
  const search = page.getByRole('dialog', { name: 'Ara' }).getByRole('combobox');
  check('Ctrl+K arama paleti açılır', await visible(search, 5000));
  await search.fill('Kararlar');
  await shot('05-arama');
  await page.keyboard.press('Enter');
  check('arama sonucu bölüme götürür (Kararlar)', await visible(page.getByRole('region', { name: 'Kararlar' }), 8000));
  await nav('Mimari').click();

  section('Yeni proje: gereksinim → mimari → kısmi kabul');
  await page.goto(WEB);
  await visible(page.getByRole('heading', { name: 'Projeler' }));
  await page.getByRole('button', { name: 'İlk mimarini oluştur' }).first().click();
  await page.locator('#pname').fill('UI masraf projesi');
  await page.locator('#idea').fill('Çalışanlar masraf girer, yönetici onaylar. Fiş fotoğrafı yüklenir.');
  await page.getByRole('button', { name: 'Projeyi oluştur' }).click();
  check('proje oluşur ve çalışma alanı açılır', await visible(page.getByRole('heading', { name: 'UI masraf projesi' }), 20_000));

  await nav('Kaynaklar').click();
  await page.getByRole('button', { name: 'Gereksinimleri çıkar' }).click();
  await nav('Öneriler').click();
  const analysisItem = page.locator('button.item', { hasText: 'Gereksinim çıkarımı' }).first();
  check('çıkarım işi bitince bekleyen öneri listelenir', await visible(analysisItem, JOB_MS));
  await analysisItem.click();
  check('önerinin etki özeti ve kabul düğmesi görünür', await visible(page.getByRole('button', { name: 'Hepsini kabul et' }), 15_000));
  await shot('06-oneri');
  await page.getByRole('button', { name: 'Hepsini kabul et' }).click();
  check('kabulden sonra sürüm v2 olur', await waitVersion(2));

  await nav('Mimari').click();
  await page.getByRole('button', { name: /Mimari taslak oluştur/ }).click();
  await nav('Öneriler').click();
  const archItem = page.locator('button.item', { hasText: 'Mimari taslak' }).first();
  check('mimari işi bitince bekleyen öneri listelenir', await visible(archItem, JOB_MS));
  await archItem.click();
  check('değişiklik grupları onay kutularıyla listelenir', await visible(page.getByText('Kabul edilecek gruplar'), 15_000));
  const boxes = page.locator('input[type="checkbox"]');
  const groupCount = await boxes.count();
  check('en az iki değişiklik grubu var', groupCount >= 2, groupCount);
  await shot('07-gruplar');
  if (groupCount >= 2) {
    // Bağımlı grubu tek başına işaretlemeyi dene: kök grup olmadan kabul düğmesi hata vermeli veya bağımlılık seçilmeli
    await boxes.nth(groupCount - 1).uncheck().catch(() => {});
    const partialBtn = page.getByRole('button', { name: /Seçilenleri kabul et|Hepsini kabul et/ });
    await partialBtn.click();
    check('bir grup seçimi kaldırılınca "kısmi kabul" etiketli v3 oluşur', await waitVersion(3));
    const label = await page.locator('#ver option:checked').innerText();
    const m = /kısmi kabul: (\d+)\/(\d+)/.exec(label);
    check('sürüm özeti kısmi kabulü belirtir (seçilen grup sayısı < toplam)', !!m && Number(m[1]) < Number(m[2]), label);
  }
  await shot('08-v3');

  section('Değişiklik talebi ve etki analizi');
  await page.getByRole('button', { name: /Yeni talep ekle/ }).click();
  await page.locator('#chg').fill('Yönetici onayından sonra muhasebe sistemine bildirim gitsin.');
  await page.getByRole('button', { name: 'Etkiyi analiz et' }).click();
  await nav('Öneriler').click();
  const changeItem = page.locator('button.item', { hasText: 'muhasebe sistemine' }).filter({ hasText: 'Bekliyor' }).first();
  check('değişiklik önerisi hazır (bekliyor)', await visible(changeItem, JOB_MS));
  await changeItem.click();
  check('"Etki özeti" bölümü görünür', await visible(page.getByRole('heading', { name: 'Etki özeti' }), 15_000));
  await shot('09-etki');
  await page.getByRole('button', { name: 'Reddet' }).click();
  check('öneri reddedilir (liste "Reddedildi" gösterir)', await visible(page.getByText('Reddedildi').first(), 10_000));

  section('Doğrulama bulguları ve dışa aktarma');
  await nav('Doğrulama').click();
  check('Doğrulama bölümü açılır', await visible(page.getByRole('region', { name: 'Doğrulama' }), 8000));
  const [download] = await Promise.all([page.waitForEvent('download', { timeout: 30_000 }), page.getByRole('button', { name: /Markdown/ }).click()]);
  check('Markdown dışa aktarma dosya indirir (.md)', /\.md$/i.test(download.suggestedFilename()), download.suggestedFilename());

  section('Tema ve mobil');
  const toggle = page.getByRole('switch', { name: /Koyu tema/ });
  const bg = () => page.evaluate(() => getComputedStyle(document.body).backgroundColor + '|' + getComputedStyle(document.querySelector('.sidebar, aside, nav') ?? document.body).backgroundColor);
  const bgBefore = await bg();
  await toggle.click();
  check('koyu tema anahtarı açılır (aria-checked=true)', (await toggle.getAttribute('aria-checked')) === 'true');
  await page.waitForTimeout(400);
  check('koyu temada renkler gerçekten değişir', (await bg()) !== bgBefore, [bgBefore, await bg()]);
  await shot('10-koyu');
  await toggle.click(); // açık temaya geri dön
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(500);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  check('mobilde yatay kaydırma yok', overflow <= 1, overflow);
  // Taşan öğe üst katmanda kırpılırsa scrollWidth fark etmez: asıl öğelerin sağ kenarı ekran içinde mi?
  const outside = await page.evaluate(() => [...document.querySelectorAll('.ws-head select, .ws-head .btn, .ws-head h1')]
    .map((el) => ({ el: el.id || el.className || el.tagName, right: Math.round(el.getBoundingClientRect().right) }))
    .filter((x) => x.right > window.innerWidth + 1));
  check('mobilde üst bölümün öğeleri ekran dışına taşmaz', outside.length === 0, outside);
  await shot('11-mobil');
  await page.setViewportSize({ width: 1360, height: 860 });

  section('Çıkış');
  await page.getByRole('button', { name: /Çıkış/ }).first().click();
  check('çıkış giriş ekranına döner', await visible(page.getByRole('heading', { name: /Tekrar hoş geldin|Hesap oluştur/ }), 8000));

  section('Tarayıcı hataları');
  check('sayfa/konsol hatası ve başarısız istek yok', problems.length === 0, problems.slice(0, 6));
} catch (e) {
  await shot('HATA');
  console.log(`\n✗ Beklenmeyen hata: ${e.message}\n  Ekran görüntüsü: ${path.join(SHOTS, 'HATA.png')}`);
  if (problems.length) console.log('  Tarayıcı hataları:', problems.slice(0, 5));
  await browser.close();
  fatal('arayüz yolculuğu yarıda kesildi');
}

await browser.close();
finish();
