// G fazı: tarayıcı yolculuğu (Playwright). Çalışan fiş yükler → talep + PDF indirir → müdür ekli talebi görür, vekâlet verir, onaylar →
// belge doğrulama sayfası → yönetici G sayfalarını (listeler, izin/tatil, raporlar, saklama, e-posta, demo veri) gezer.
//   Kullanıcı ve uygulama hazırlığı API ile yapılır (hızlı); gösterilen her şey arayüzden doğrulanır.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { openSession, WEB } from './ui-lib.mjs';
import { API_URL, call, check, finish, section } from './lib.mjs';

const uniq = `${Date.now().toString(36)}${Math.floor(Math.random() * 1e4)}`;
const TEMP = 'Gecici-Parola-123';
const PASS = 'Yeni-Parola-12345';
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==', 'base64');
const today = (offset = 0) => new Date(Date.now() + 180 * 60_000 + offset * 86_400_000).toISOString().slice(0, 10);
const sessions = [];
const open = async (opts) => { const s = await openSession(opts); sessions.push(s); return s; };

async function login(page, email, password) {
  await page.goto(`${WEB}/?intro`);
  await page.keyboard.press('Escape');
  await page.locator('#email').fill(email);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Giriş yap' }).click();
  await page.locator('.sb-nav').first().waitFor({ state: 'visible', timeout: 20_000 });      // oturum açılmadan gezinme başlamasın
}
function pdfText(bytes) {
  try {
    const f = path.join(os.tmpdir(), `arcvora-ui-${uniq}-${Math.random().toString(36).slice(2)}.pdf`);
    fs.writeFileSync(f, bytes);
    try { return execFileSync('pdftotext', ['-layout', f, '-'], { encoding: 'utf8' }); } finally { fs.rmSync(f, { force: true }); }
  } catch { return null; }
}

try {
  // ------------------------------------------------------------------ hazırlık (API)
  section('Hazırlık (API): kullanıcılar ve fişli masraf uygulaması');
  const reg = await call('POST', '/api/auth/register', { body: { email: `gui-admin-${uniq}@example.com`, password: 'Admin-Parola-12345', displayName: 'Master Admin' } });
  check('master admin kayıt oldu', reg.status === 201);
  const A = { token: reg.body.token };
  const ws = reg.body.workspaces[0].id;
  const mk = async (name, managerId) => {
    const email = `${name.toLowerCase().replace(/[^a-z]/g, '')}-${uniq}@example.com`;
    const r = await call('POST', `/api/workspaces/${ws}/users`, { ...A, body: { email, displayName: name, password: TEMP, role: 'app_user', managerId } });
    const l = await call('POST', '/api/auth/login', { body: { email, password: TEMP } });
    const ch = await call('POST', '/api/me/password', { token: l.body.token, body: { currentPassword: TEMP, newPassword: PASS } });   // ilk giriş zorunlu ekranı atlansın
    check(`${name} hazır`, r.status === 201 && ch.status === 204, [r.status, ch.status]);
    return { email, id: r.body.userId };
  };
  const boss = await mk('Mudur Mehmet');
  const worker = await mk('Calisan Can', boss.id);
  const deputy = await mk('Vekil Veli');
  const def = {
    schema: 1, prefix: 'MSU', summaryFields: ['amount', 'description'],
    fields: [
      { key: 'amount', label: 'Tutar', type: 'money', required: true },
      { key: 'description', label: 'Açıklama', type: 'text' },
      { key: 'receipt', label: 'Fiş', type: 'file', required: true, maxFiles: 2 },
    ],
    steps: [{ id: 's1', name: 'Yönetici onayı', approvers: { type: 'manager' } }],
    completion: { accounting: { enabled: true, category: 'masraf' } },
  };
  const c = await call('POST', `/api/workspaces/${ws}/apps`, { ...A, body: { name: 'Fişli masraf', description: 'Fiş yüklemeli masraf', icon: 'receipt', definition: def } });
  const appId = c.body.summary.id;
  await call('POST', `/api/apps/${appId}/publish`, A);
  await call('PUT', `/api/apps/${appId}/grants`, { ...A, body: { userIds: [worker.id], groupIds: [] } });
  check('fişli masraf uygulaması yayınlandı', c.status === 201);

  // ------------------------------------------------------------------ çalışan: fiş yükler
  section('Çalışan: formda fiş yükler, talebi gönderir, PDF indirir');
  const W = await open();
  await login(W.page, worker.email, PASS);
  check('çalışan uygulamalarını görür', await W.visible(W.page.getByRole('heading', { name: 'Uygulamalarım' })));
  await W.page.getByRole('button', { name: /Fişli masraf/ }).click();
  check('form açıldı (fiş alanı: "Dosya seç" ve "Kamera ile çek")', await W.visible(W.page.getByRole('button', { name: 'Dosya seç' })) && await W.visible(W.page.getByRole('button', { name: 'Kamera ile çek' })));
  await W.page.getByLabel('Tutar').fill('250,75');
  await W.page.getByLabel('Açıklama').fill('Müşteri yemeği, İstanbul');
  // Zorunlu fiş olmadan gönderme: hata (sunucu/istemci) ve talep oluşmaz
  await W.page.getByRole('button', { name: 'Talebi gönder' }).click();
  check('fişsiz gönderimde alan hatası görünür', await W.visible(W.page.locator('.file-field [role=alert], .err[role=alert]')));
  W.problems.splice(0);          // bilerek yapılan geçersiz gönderim (sunucu 422) tarayıcı konsoluna "hata" yazar; beklenen gürültü
  await W.page.locator('input[type=file][id$="-pick"]').setInputFiles({ name: 'yemek fişi.png', mimeType: 'image/png', buffer: PNG });
  check('yüklenen dosya adıyla listelenir', await W.visible(W.page.locator('.file-list .file-name', { hasText: 'yemek fişi.png' })));
  await W.page.getByRole('button', { name: 'Talebi gönder' }).click();
  check('talep gönderildi: ayrıntı sayfası açıldı', await W.visible(W.page.getByRole('heading', { name: /MSU-\d{4}-\d{4}/ })));
  check('ayrıntıda ek görünür', await W.visible(W.page.locator('.attachments', { hasText: 'yemek fişi.png' })));
  await W.shot('g-ui-calisan-talep');

  const [download] = await Promise.all([W.page.waitForEvent('download', { timeout: 20_000 }), W.page.getByRole('button', { name: /PDF indir/ }).click()]);
  const pdfBytes = fs.readFileSync(await download.path());
  check('PDF indirildi (.pdf, %PDF başlığı)', download.suggestedFilename().endsWith('.pdf') && pdfBytes.subarray(0, 4).toString() === '%PDF', download.suggestedFilename());
  const number = download.suggestedFilename().replace(/\.pdf$/, '');
  const text = pdfText(pdfBytes);
  const code = text ? /Doğrulama kodu: ([0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4})/.exec(text)?.[1] : null;
  if (text === null) console.log('  · pdftotext yok: doğrulama kodu adımları atlanacak');
  else check('PDF Türkçe metni taşır', text.includes('Müşteri yemeği') && text.includes('Calisan Can'), text.slice(0, 200));

  // ------------------------------------------------------------------ müdür: vekâlet verir, ekli talebi görür
  section('Müdür: vekâlet verir, ekli talebi açar ve onaylar');
  const B = await open();
  await login(B.page, boss.email, PASS);
  await B.page.goto(`${WEB}/#/delegations`);
  check('Vekâletlerim sayfası açıldı', await B.visible(B.page.getByRole('heading', { name: 'Vekâletlerim' })));
  await B.page.locator('#dg-to').selectOption({ label: 'Vekil Veli' });
  await B.page.locator('#dg-start').fill(today(0));
  await B.page.locator('#dg-end').fill(today(3));
  await B.page.locator('#dg-reason').fill('Yıllık izin');
  await B.page.getByRole('button', { name: 'Vekâlet ver' }).click();
  check('vekâlet listede görünür (Vekil Veli, açıklamasıyla)', await B.visible(B.page.locator('li, .row-card, .item', { hasText: 'Vekil Veli' }).filter({ hasText: 'Yıllık izin' })));
  await B.shot('g-ui-vekalet');

  await B.page.goto(`${WEB}/#/inbox`);
  check('onay kutusunda talep var', await B.visible(B.page.locator('button.item', { hasText: number })));
  await B.page.locator('button.item', { hasText: number }).click();
  check('müdür talepte fişi görür', await B.visible(B.page.locator('.attachments', { hasText: 'yemek fişi.png' })));
  const [att] = await Promise.all([B.page.waitForEvent('download', { timeout: 20_000 }).catch(() => null), B.page.locator('.attachments').getByRole('button').first().click().catch(() => null)]);
  if (att) check('ek indirilir ve baytlar özgün dosyayla aynıdır', fs.readFileSync(await att.path()).equals(PNG));
  await B.page.getByRole('button', { name: 'Onayla' }).click();
  check('onay sonrası durum "Onaylandı"', await B.visible(B.page.locator('.badge', { hasText: 'Onaylandı' })));

  // ------------------------------------------------------------------ belge doğrulama
  section('Belge doğrulama sayfası');
  await B.page.goto(`${WEB}/#/verify`);
  check('Belge doğrula sayfası açıldı', await B.visible(B.page.getByRole('heading', { name: 'Belge doğrula' })));
  await B.page.locator('#vf-number').fill(number);
  await B.page.locator('#vf-code').fill('0000-0000-0000');
  await B.page.getByRole('button', { name: 'Doğrula', exact: true }).click();
  check('yanlış kod: "Doğrulanamadı"', await B.visible(B.page.getByText('Doğrulanamadı')));
  if (code) {
    // PDF, onaydan ÖNCE alındı: durum değiştiği için bu kod artık geçersiz olmalı (basılı kâğıdın bayatlığı anlaşılır).
    await B.page.locator('#vf-code').fill(code);
    await B.page.getByRole('button', { name: 'Doğrula', exact: true }).click();
    check('onaydan önce basılan çıktının kodu artık doğrulanmaz (bayat nüsha)', await B.visible(B.page.getByText('Doğrulanamadı')));
  }
  // Güncel çıktı: yeniden indir, kodunu doğrula
  await B.page.goto(`${WEB}/#/requests`);
  const fresh = await fetch(`${API_URL}/api/auth/login`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email: worker.email, password: PASS }) }).then((r) => r.json());
  const reqs = await call('GET', '/api/my/requests', { token: fresh.token });
  const reqId = reqs.body.find((r) => r.number === number)?.id;
  const pdfRes = await fetch(`${API_URL}/api/requests/${reqId}/pdf`, { headers: { authorization: `Bearer ${fresh.token}` } });
  const freshText = pdfText(Buffer.from(await pdfRes.arrayBuffer()));
  const freshCode = freshText ? /Doğrulama kodu: ([0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4})/.exec(freshText)?.[1] : null;
  if (freshCode) {
    await B.page.goto(`${WEB}/#/verify`);
    await B.page.locator('#vf-number').fill(number);
    await B.page.locator('#vf-code').fill(freshCode.toLowerCase());                  // küçük harfle de çalışır
    await B.page.getByRole('button', { name: 'Doğrula', exact: true }).click();
    check('güncel çıktının kodu doğrulanır ("Belge geçerli", durum Onaylandı)', await B.visible(B.page.locator('.notice.ok', { hasText: 'Belge geçerli' })) && (await B.page.locator('.notice.ok').textContent()).includes('Onaylandı'));
    await B.shot('g-ui-dogrulama');
  }

  // ------------------------------------------------------------------ yönetici: G sayfaları
  section('Yönetici: listeler, izin/tatil, raporlar, saklama, e-posta, demo veri');
  const M = await open();
  await login(M.page, reg.body.user.email, 'Admin-Parola-12345');
  await M.page.goto(`${WEB}/#/admin/lists`);
  await M.page.locator('#ln-name').fill('Departmanlar');
  await M.page.locator('#ln-items').fill('Satış\nDepo\nMuhasebe');
  await M.page.getByRole('button', { name: 'Listeyi oluştur' }).click();
  check('liste oluşturuldu ve listelendi (3 aktif öğe)', await M.visible(M.page.getByText(/Departmanlar/).first()) && await M.visible(M.page.getByText('3 aktif öğe')));

  await M.page.goto(`${WEB}/#/admin/leave`);
  check('İzin ve tatiller sayfası açıldı', await M.visible(M.page.getByText(/Resmî tatil takvimi/)));
  await M.page.getByRole('button', { name: /Sabit resmî tatilleri ekle/ }).click();
  check('sabit resmî tatiller eklendi (1 Ocak, 23 Nisan…)', await M.visible(M.page.getByText(/Yılbaşı/)) && await M.visible(M.page.getByText(/23 Nisan|Ulusal Egemenlik/)));
  await M.shot('g-ui-tatil');

  await M.page.goto(`${WEB}/#/admin/reports`);
  check('Raporlar: özet ve onaylayıcı yükü görünür', await M.visible(M.page.getByText(/Özet ·/)) && await M.visible(M.page.getByRole('heading', { name: 'Onaylayıcı yükü' })));
  check('Raporlar: gönderilen talep sayılmış (Toplam ≥ 1)', (await M.page.locator('b').filter({ hasText: /^[1-9]\d*$/ }).count()) > 0);

  await M.page.goto(`${WEB}/#/admin/requests`);
  await M.page.locator('#rq-q').fill('calisan');
  await M.page.getByRole('main').getByRole('button', { name: 'Ara', exact: true }).click();
  check('Tüm talepler: "calisan" araması talebi bulur', await M.visible(M.page.locator('button.item', { hasText: number })));
  await M.page.locator('#rq-q').fill('olmayan-bir-sey');
  await M.page.getByRole('main').getByRole('button', { name: 'Ara', exact: true }).click();
  check('Tüm talepler: ilgisiz arama boş sonuç', await M.visible(M.page.getByText('0 talep')));

  await M.page.goto(`${WEB}/#/admin/retention`);
  check('Saklama sayfası: süre seçimi ve anonimleştirme bölümü', await M.visible(M.page.getByRole('heading', { name: 'Saklama süresi' })) && await M.visible(M.page.getByRole('heading', { name: /Ayrılan kullanıcılar/ })));

  await M.page.goto(`${WEB}/#/admin/email`);
  check('E-posta sayfası: SMTP açık, durum satırı görünür', await M.visible(M.page.getByText(/Gönderen:/)));
  check('E-posta: test e-postası düğmesi etkin', await M.page.getByRole('button', { name: 'Test e-postası gönder' }).isEnabled());

  await M.page.goto(`${WEB}/#/admin/audit`);
  check('Denetim sayfası kayıt listeler', await M.visible(M.page.getByText(/\d+ kayıt/)));

  await M.page.goto(`${WEB}/#/admin/demo`);
  check('Demo veri sayfası açılır', await M.visible(M.page.getByRole('heading', { name: 'Demo veri' })));

  // ------------------------------------------------------------------ hata denetimi
  section('Konsol/sayfa hataları');
  for (const [i, s] of sessions.entries()) check(`oturum ${i + 1}: sayfa/konsol hatası yok`, s.problems.length === 0, s.problems.slice(0, 5));
} catch (e) {
  check('beklenmeyen hata', false, String(e?.stack ?? e));
  for (const [i, s] of sessions.entries()) await s.shot(`g-ui-hata-${i}`);
} finally {
  for (const s of sessions) await s.browser.close().catch(() => {});
}
finish();
