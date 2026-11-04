// Tarayıcı testleri için ortak kurulum (Playwright çözümleme, sayfa/konsol hatası toplama, kısa yardımcılar).
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import path from 'node:path';

export function loadChromium() {
  const require = createRequire(import.meta.url);
  try {
    return require('playwright').chromium;
  } catch {
    try {
      return createRequire(path.resolve('web/package.json'))('playwright').chromium;
    } catch {
      console.error('Playwright bulunamadı. Kurulum: cd web && npm i --no-save playwright && npx playwright install chromium');
      process.exit(2);
    }
  }
}

export const WEB = (process.env.ARCVORA_WEB_URL ?? 'http://127.0.0.1:5173').replace(/\/$/, '');
export const SHOTS = process.env.E2E_SHOTS_DIR ?? path.resolve('e2e-screenshots');

/** Yeni tarayıcı oturumu: sayfa hatalarını `problems` içinde toplar. */
export async function openSession({ viewport = { width: 1360, height: 860 } } = {}) {
  mkdirSync(SHOTS, { recursive: true });
  const browser = await loadChromium().launch();
  const context = await browser.newContext({ viewport, acceptDownloads: true, locale: 'tr-TR' });
  const page = await context.newPage();
  const problems = [];
  page.on('pageerror', (e) => problems.push(`pageerror: ${e.message}`));
  page.on('console', (m) => {
    if (m.type() === 'error') problems.push(`console.error: ${m.text()}`);
  });
  const shot = (name) => page.screenshot({ path: path.join(SHOTS, `${name}.png`) }).catch(() => {});
  const visible = async (locator, timeout = 15_000) => locator.first().waitFor({ state: 'visible', timeout }).then(() => true, () => false);
  const gone = async (locator, timeout = 8_000) => locator.first().waitFor({ state: 'detached', timeout }).then(() => true, () => false);
  return { browser, context, page, problems, shot, visible, gone };
}

/** Giriş ekranından geçip kayıt olur; intro'yu atlar. */
export async function register(page, { email, password = 'Parola-12345', name = 'UI Edge' }) {
  await page.goto(`${WEB}/?intro`);
  await page.keyboard.press('Escape');
  await page.getByRole('tab', { name: 'Kayıt ol' }).click();
  await page.getByLabel('Ad').fill(name);
  await page.getByLabel(/E-posta/i).fill(email);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Hesap oluştur' }).click();
}
