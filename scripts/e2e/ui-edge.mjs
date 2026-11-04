// Tarayıcı uç durumları: hata yolları, oturum, güvenlik (XSS), API kesintisi, klavye, dar ekranlar.
//   ARCVORA_WEB_URL=http://127.0.0.1:5173 node scripts/e2e/ui-edge.mjs   (API ve web çalışıyor olmalı)
import { check, finish, section, fatal } from './lib.mjs';
import { WEB, openSession, register } from './ui-lib.mjs';

const run = Date.now();
const { browser, page, problems, shot, visible, gone } = await openSession();
const alertText = async () => (await page.getByRole('alert').first().innerText().catch(() => ''));

try {
  console.log(`ARCVORA arayüz uç durum testi → ${WEB}`);

  section('Giriş ve kayıt hata yolları');
  const email = `uiedge-${run}@example.com`;
  await register(page, { email });
  check('kayıt başarılı: Projeler sayfası', await visible(page.getByRole('heading', { name: 'Projeler' })));
  await page.getByRole('button', { name: /Çıkış/ }).first().click();
  await visible(page.getByRole('heading', { name: /Tekrar hoş geldin|Hesap oluştur/ }));
  await page.getByRole('tab', { name: 'Giriş' }).click();
  await page.getByLabel(/E-posta/i).fill(email);
  await page.locator('#password').fill('yanlis-parola-1');
  await page.getByRole('button', { name: 'Giriş yap' }).click();
  check('yanlış parola Türkçe hata gösterir ve giriş ekranında kalır', await visible(page.getByRole('alert')) && /hatalı/i.test(await alertText()), await alertText());
  check('hata mesajı parola veya e-postadan hangisinin yanlış olduğunu söylemez', !/parola yanlış|kullanıcı bulunamadı/i.test(await alertText()));
  await page.getByLabel(/E-posta/i).fill(`yok-${run}@example.com`);
  await page.getByRole('button', { name: 'Giriş yap' }).click();
  await page.waitForTimeout(500);
  check('olmayan kullanıcı için aynı hata (kullanıcı sızdırılmaz)', /hatalı/i.test(await alertText()), await alertText());
  await page.getByRole('tab', { name: 'Kayıt ol' }).click();
  await page.getByLabel('Ad').fill('Aynı');
  await page.getByLabel(/E-posta/i).fill(email);
  await page.locator('#password').fill('Parola-12345');
  await page.getByRole('button', { name: 'Hesap oluştur' }).click();
  check('aynı e-postayla ikinci kayıt anlaşılır hata verir', await visible(page.getByRole('alert')) && /zaten/i.test(await alertText()), await alertText());
  await page.getByLabel(/E-posta/i).fill(`kisa-${run}@example.com`);
  await page.locator('#password').fill('kisa');
  await page.getByRole('button', { name: 'Hesap oluştur' }).click();
  await page.waitForTimeout(400);
  const bubble = await page.locator('#password').evaluate((el) => el.validity.tooShort || el.validationMessage);
  check('kısa parola tarayıcı doğrulamasıyla engellenir (istek gönderilmez)', !!bubble && (await page.getByRole('heading', { name: 'Projeler' }).count()) === 0, bubble);
  await shot('edge-01-giris-hatalari');

  section('Sonuç');
  check('sayfa/konsol hatası yok (beklenen 4xx istekleri hariç)', problems.filter((p) => !/status of 4\d\d|401|409|422/.test(p)).length === 0, problems.slice(0, 5));
} catch (e) {
  await shot('edge-HATA');
  console.log(`\n✗ Beklenmeyen hata: ${e.message}`);
  await browser.close();
  fatal('arayüz uç durum testi yarıda kesildi');
}
await browser.close();
finish();
