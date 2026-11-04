// Tarayıcı uç durumları: hata yolları, oturum, güvenlik (XSS), API kesintisi, klavye, dar ekranlar.
//   ARCVORA_WEB_URL=http://127.0.0.1:5173 node scripts/e2e/ui-edge.mjs   (API ve web çalışıyor olmalı)
import { call, check, finish, section, fatal, waitJob } from './lib.mjs';
import { newProject, newUser, withAcceptedRequirements } from './fixtures.mjs';
import { WEB, openSession, register } from './ui-lib.mjs';

const run = Date.now();
const { browser, context, page, problems, shot, visible, gone } = await openSession();
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

  section('Oturum: yenileme, çıkış, bozuk token');
  const email2 = `uisess-${run}@example.com`;
  await page.goto(`${WEB}/?intro`);
  await page.keyboard.press('Escape');
  await register(page, { email: email2 });
  await visible(page.getByRole('heading', { name: 'Projeler' }));
  await page.reload();
  check('sayfa yenilenince oturum korunur (giriş ekranına dönmez)', await visible(page.getByRole('heading', { name: 'Projeler' })));
  check('yenilemede giriş formu görünmez', (await page.getByRole('tab', { name: 'Kayıt ol' }).count()) === 0);
  const storedKeys = await page.evaluate(() => Object.keys(localStorage).concat(Object.keys(sessionStorage)));
  const secretLike = await page.evaluate(() => [...Object.values(localStorage), ...Object.values(sessionStorage)].some((v) => /Parola-12345/.test(v)));
  check('parola hiçbir tarayıcı deposunda saklanmaz', !secretLike, storedKeys);
  // Token'ı bozarak oturum düşmesini sına
  const tokenKey = await page.evaluate(() => Object.keys(localStorage).find((k) => /token|auth|session/i.test(k) && !/intro/i.test(k)));
  check('token bir tarayıcı deposunda bulunur (oturum anahtarı)', !!tokenKey, storedKeys);
  if (tokenKey) {
    await page.evaluate((k) => localStorage.setItem(k, localStorage.getItem(k).slice(0, -4) + 'AAAA'), tokenKey);
    await page.reload();
    check('bozulmuş token: kullanıcı giriş ekranına yönlendirilir (boş/hatalı sayfa değil)', await visible(page.getByRole('heading', { name: /Tekrar hoş geldin|Hesap oluştur/ }), 15_000));
    check('bozuk token temizlenir (yeniden yenilemede döngü yok)', !(await page.evaluate((k) => localStorage.getItem(k), tokenKey)) || (await page.getByRole('tab', { name: 'Kayıt ol' }).count()) > 0);
  }
  await page.getByRole('tab', { name: 'Giriş' }).click();
  await page.getByLabel(/E-posta/i).fill(email2);
  await page.locator('#password').fill('Parola-12345');
  await page.getByRole('button', { name: 'Giriş yap' }).click();
  check('doğru parolayla yeniden giriş yapılır', await visible(page.getByRole('heading', { name: 'Projeler' })));
  await page.getByRole('button', { name: /Çıkış/ }).first().click();
  await visible(page.getByRole('heading', { name: /Tekrar hoş geldin|Hesap oluştur/ }));
  await page.reload();
  check('çıkıştan sonra yenileme oturumu geri getirmez', await visible(page.getByRole('heading', { name: /Tekrar hoş geldin|Hesap oluştur/ })) && (await page.getByRole('heading', { name: 'Projeler' }).count()) === 0);
  await page.goBack().catch(() => {});
  check('çıkıştan sonra "geri" tuşu korumalı veriyi göstermez', (await page.getByRole('heading', { name: 'Projeler' }).count()) === 0);

  section('XSS: düşmanca adlar arayüzde çalıştırılmaz');
  const dialogs = [];
  page.on('dialog', (d) => { dialogs.push(d.message()); d.dismiss().catch(() => {}); });
  const xssUser = await newUser('xss');
  const evilName = '<img src=x onerror="window.__xss=1"> <script>window.__xss=2</script>';
  const evilPid = await newProject(xssUser, evilName);
  await withAcceptedRequirements(xssUser, evilPid);
  const evilJob = await waitJob(xssUser.token, (await call('POST', `/api/projects/${evilPid}/architecture-proposals`, { token: xssUser.token })).body.id);
  await call('POST', `/api/projects/${evilPid}/proposals/${evilJob.proposalId}/accept`, { token: xssUser.token, body: {} });
  const evilModel = (await call('GET', `/api/projects/${evilPid}`, { token: xssUser.token })).body.model;
  const evilComp = evilModel.components[0];
  const evilCompName = 'C"]; <svg onload="window.__xss=3"> %%{init:{"securityLevel":"loose"}}%%';
  await call('POST', `/api/projects/${evilPid}/patches`, {
    token: xssUser.token,
    body: { baseVersion: (await call('GET', `/api/projects/${evilPid}`, { token: xssUser.token })).body.summary.headNumber, summary: '<b>özet</b>',
      patch: { operations: [{ op: 'upsert', type: 'component', id: evilComp.id, value: { ...evilComp, name: evilCompName, responsibility: '<img src=x onerror="window.__xss=4">' } }] } },
  });
  await context.addInitScript((t) => localStorage.setItem('arcvora.token', t), xssUser.token);
  await page.goto(WEB);
  const card = page.locator('button.proj-card', { hasText: 'img src=x' });
  check('proje listesinde düşmanca ad metin olarak görünür (etiket çalışmaz)', await visible(card) && (await card.first().innerText()).includes('<img src=x'), await card.first().innerText().catch(() => ''));
  check('proje listesinde enjekte edilmiş <img>/<script> öğesi oluşmadı', (await page.locator('.proj-card img[src="x"], .proj-card script').count()) === 0);
  await card.first().click();
  await visible(page.locator('svg.canvas g[data-node-id]'), 30_000);
  await page.waitForTimeout(600);
  check('çalışma alanı başlığında ad metin olarak görünür', (await page.locator('h1').first().innerText()).includes('<img src=x'));
  check('tuval düğümünde düşmanca bileşen adı metin olarak görünür, <svg> enjekte edilmedi',
    (await page.locator('svg.canvas').innerHTML()).includes('&lt;svg onload') && (await page.locator('svg.canvas svg[onload]').count()) === 0);
  await page.getByRole('tab', { name: /Bileşenler \(Mermaid\)/ }).click();
  await page.waitForTimeout(1500);
  check('Mermaid görünümü düşmanca adla bile çizilir veya güvenli hata gösterir (çökmez)', (await page.locator('.diagram svg').count()) > 0 || (await page.getByRole('alert').count()) > 0);
  check('Mermaid çıktısında çalışan onload/onerror niteliği yok', (await page.locator('.diagram [onload], .diagram [onerror]').count()) === 0);
  await page.keyboard.press('Control+k');
  await page.getByRole('dialog', { name: 'Ara' }).getByRole('combobox').fill('svg');
  await page.waitForTimeout(400);
  check('arama paletinde düşmanca metin etiket olarak yorumlanmaz', (await page.locator('.palette img, .palette svg[onload], .palette script').count()) === 0);
  await page.keyboard.press('Escape');
  const xssFlag = await page.evaluate(() => window.__xss);
  check('window.__xss tanımsız: hiçbir enjekte edilmiş betik çalışmadı', xssFlag === undefined, xssFlag);
  check('hiçbir alert/confirm/prompt iletişim kutusu açılmadı', dialogs.length === 0, dialogs);
  await shot('edge-02-xss');

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
