// README için ekran görüntüleri üretir (örnek proje üzerinden). Arayüz değişince yeniden çalıştırın:
//   scripts/dev.sh   (başka terminalde)   →   ARCVORA_API_URL=http://localhost:5217 ARCVORA_WEB_URL=http://localhost:5173 node scripts/docs-screenshots.mjs
// Gerekir: Playwright (cd web && npm i --no-save playwright && npx playwright install chromium)
import path from 'node:path';
import { call, waitJob } from './e2e/lib.mjs';
import { openSession, WEB } from './e2e/ui-lib.mjs';

const OUT = process.env.DOCS_ASSETS_DIR ?? path.resolve('docs/assets');
process.env.E2E_SHOTS_DIR = OUT;

const email = `docs-${Date.now()}@example.com`;
const reg = await call('POST', '/api/auth/register', { body: { email, password: 'Parola-12345', displayName: 'Demo' } });
const token = reg.body.token;
const sample = await call('POST', '/api/samples/expense-management', { token, body: { workspaceId: reg.body.workspaces[0].id } });
const pid = sample.body.summary.id;
// İnceleme ekranı için bekleyen bir değişiklik önerisi
const job = await waitJob(token, (await call('POST', `/api/projects/${pid}/change-requests`, {
  token, body: { requestText: 'Yönetici onayından sonra muhasebe sistemine bildirim gitsin.' },
})).body.id);
if (job.status !== 'succeeded') throw new Error(`öneri üretilemedi: ${JSON.stringify(job)}`);

const { browser, context, page, visible } = await openSession({ viewport: { width: 1360, height: 860 } });
const shoot = (name, opts = {}) => page.screenshot({ path: path.join(OUT, name), type: 'jpeg', quality: 86, ...opts });
await context.addInitScript((t) => { localStorage.setItem('arcvora.token', t); sessionStorage.setItem('arcvora.intro', '1'); }, token);
await page.goto(WEB);
await page.locator('button.proj-card', { hasText: 'Masraf' }).first().click();
await visible(page.locator('svg.canvas g[data-node-id]'), 30_000);
await page.getByRole('button', { name: 'Sığdır' }).click(); // tüm mimari görünsün
await page.waitForTimeout(900);
await shoot('screen-canvas.jpg');

const theme = page.getByRole('switch', { name: /Koyu tema/ });
await theme.click();
await page.waitForTimeout(500);
await shoot('screen-canvas-dark.jpg');
await theme.click();

await page.getByRole('tab', { name: /Bileşenler \(Mermaid\)/ }).click();
await visible(page.locator('.diagram svg'), 30_000);
await page.waitForTimeout(600);
await shoot('screen-mermaid.jpg');

await page.locator('.sb-item', { has: page.locator('.label', { hasText: /^Öneriler/ }) }).first().click();
const item = page.locator('button.item', { hasText: 'muhasebe sistemine' }).first();
await visible(item);
await item.click();
await visible(page.getByRole('heading', { name: 'Etki özeti' }), 15_000);
await page.waitForTimeout(500);
await shoot('screen-review.jpg');

await page.setViewportSize({ width: 390, height: 844 });
await page.getByRole('button', { name: /Menüyü aç/ }).first().click().catch(() => {});
await page.goto(WEB);
await page.locator('button.proj-card', { hasText: 'Masraf' }).first().click();
await visible(page.locator('svg.canvas g[data-node-id]'), 30_000);
await page.waitForTimeout(700);
await shoot('screen-mobile.jpg');

await browser.close();
console.log('Ekran görüntüleri →', OUT);
