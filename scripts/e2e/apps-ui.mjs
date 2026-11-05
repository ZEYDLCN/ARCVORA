// Şirket içi akış uygulamaları: tarayıcı yolculuğu (Playwright).
//   master admin kullanıcı açar ve şablondan uygulama tasarlayıp yayınlar → çalışan (app_user) geçici parolasını değiştirir, YALNIZCA Uygulamalar
//   menüsünü görür, mimari/tasarım sayfalarına giremez, izin talebi açar → yönetici onay kutusunda onaylar → çalışan sonucu görür; mobil taşma.
import { openSession, register, WEB } from './ui-lib.mjs';
import { check, finish, section } from './lib.mjs';

const uniq = `${Date.now().toString(36)}${Math.floor(Math.random() * 1e4)}`;
const problemsAll = [];
const workerTempOverride = { value: null };      // yönetici çalışanın parolasını sıfırladığında yeni geçici parola burada tutulur
const sessions = [];
const open = async (opts) => { const s = await openSession(opts); sessions.push(s); return s; };

async function login(page, email, password) {
  await page.goto(`${WEB}/?intro`);
  await page.keyboard.press('Escape');
  await page.locator('#email').fill(email);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Giriş yap' }).click();
}

/** İlk giriş: geçici parola → zorunlu parola belirleme ekranı. */
async function setFirstPassword(s, temp, next) {
  const ok = await s.visible(s.page.getByRole('heading', { name: 'Parolanızı belirleyin' }));
  check('ilk girişte zorunlu parola ekranı açılır', ok);
  await s.page.locator('#pw-cur').fill(temp);
  await s.page.locator('#pw-new').fill(next);
  await s.page.locator('#pw-again').fill(next);
  await s.page.getByRole('button', { name: 'Parolayı kaydet' }).click();
}

const navLabels = (page) => page.locator('.sb-nav .sb-item .label').allTextContents();
const noOverflow = (page) => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1);

try {
  // ============================================================ master admin
  section('Master admin: kullanıcı açar, uygulamayı şablondan tasarlayıp yayınlar');
  const adminS = await open();
  const { page: A } = adminS;
  await register(A, { email: `admin-${uniq}@example.com`, name: 'Master Admin' });
  check('master admin giriş yaptı: mimari ve yönetim menüsü görünür', await adminS.visible(A.getByRole('heading', { name: 'Projeler' })));
  const adminNav = await navLabels(A);
  check('admin menüsü: Uygulamalarım, Projeler, Uygulama tasarla, Kullanıcılar ve yetkiler', ['Uygulamalarım', 'Projeler', 'Uygulama tasarla', 'Kullanıcılar ve yetkiler'].every((l) => adminNav.includes(l)), adminNav);

  await A.goto(`${WEB}/#/admin/users`);
  const addUser = async (name, mail, managerLabel) => {
    await A.locator('#u-name').fill(name);
    await A.locator('#u-mail').fill(mail);
    const temp = await A.inputValue('#u-pass');
    if (managerLabel) await A.locator('#u-mgr').selectOption({ label: managerLabel });
    await A.getByRole('button', { name: 'Kullanıcıyı oluştur' }).click();
    // Önceki kullanıcının bildirimi hâlâ görünebilir: bu kullanıcının geçici parolasını içeren bildirimi bekle.
    const ok = await adminS.visible(A.locator('.notice[role="status"]', { hasText: temp }));
    check(`${name} oluşturuldu, geçici parola gösterildi`, ok);
    return temp;
  };
  check('kullanıcı yönetimi sayfası açıldı', await adminS.visible(A.getByRole('heading', { name: 'Yönetim' })));
  const bossMail = `yonetici-${uniq}@example.com`, workerMail = `calisan-${uniq}@example.com`;
  const bossTemp = await addUser('Yonetici Kisi', bossMail);
  const workerTemp = await addUser('Calisan Kisi', workerMail, 'Yonetici Kisi');
  // Tablo, kullanıcı oluşturulduktan sonra eşzamansız yenilenir: satırların gelmesini bekle (admin + 2 kullanıcı).
  check('kullanıcı tablosunda ikisi de görünür', await adminS.visible(A.locator('table tbody tr').nth(2)) && (await A.locator('table tbody tr', { hasText: 'Calisan Kisi' }).count()) === 1);
  check('geçici parola rozeti görünür', (await A.locator('.badge.warn', { hasText: 'geçici parola' }).count()) === 2);
  check('Parola sıfırla yalnızca admin\'in açtığı hesaplarda görünür (kendi hesabında yok)', (await A.getByRole('button', { name: 'Parola sıfırla' }).count()) === 2
    && (await A.locator('table tbody tr', { hasText: 'Master Admin' }).getByRole('button', { name: 'Parola sıfırla' }).count()) === 0);
  A.once('dialog', (d) => d.accept());
  await A.locator('table tbody tr', { hasText: 'Calisan Kisi' }).getByRole('button', { name: 'Parola sıfırla' }).click();
  check('parola sıfırlandı: yeni geçici parola gösterilir', await adminS.visible(A.locator('.notice[role="status"]', { hasText: 'Parola sıfırlandı' })));
  const resetTemp = (await A.locator('.notice[role="status"] .mono').innerText()).split(' · ')[1];
  check('üretilen geçici parola en az 10 karakter', resetTemp.length >= 10, resetTemp);
  workerTempOverride.value = resetTemp;

  await A.goto(`${WEB}/#/design`);
  check('tasarım sayfası: 3 şablon', await adminS.visible(A.getByRole('heading', { name: 'Uygulama tasarla' })) && (await A.locator('.template-grid .proj-card').count()) === 3);
  await A.getByRole('button', { name: /İzin talebi/ }).click();
  check('tasarlayıcı açıldı', await adminS.visible(A.getByRole('heading', { name: 'İzin talebi', level: 1 })));
  check('şablon yayınlanabilir: sorun yok', await adminS.visible(A.getByText('✓ Yayınlanabilir')));
  check('önizleme formu şablon alanlarını gösterir', (await A.locator('.card', { hasText: 'Önizleme' }).locator('label', { hasText: 'İzin türü' }).count()) === 1);
  await A.locator('.card', { hasText: 'Önizleme' }).getByLabel('Başlangıç tarihi').fill('2026-11-10');
  await A.locator('.card', { hasText: 'Önizleme' }).getByLabel('Bitiş tarihi').fill('2026-11-14');
  check('önizlemede gün sayısı canlı hesaplanır (5)', (await A.locator('.computed').first().innerText()) === '5');

  // Anlık doğrulama: öneki boşaltınca yayın kapanır ve sorun görünür.
  await A.locator('#d-prefix').fill('');
  check('anlık doğrulama: öneki boş bırakınca Yayınla kapanır', await A.getByRole('button', { name: 'Yayınla', exact: true }).isDisabled());
  check('anlık doğrulama: sorun listesi önek hatasını söyler', await adminS.visible(A.locator('.issues', { hasText: 'Önek' })));
  await A.locator('#d-prefix').fill('IZN');
  check('düzeltince yeniden yayınlanabilir', await adminS.visible(A.getByText('✓ Yayınlanabilir')));

  await A.getByRole('button', { name: 'Yayınla', exact: true }).click();
  check('yayınlandı (v1)', await adminS.visible(A.locator('.notice[role="status"]', { hasText: 'Yayınlandı: v1' })));
  await A.getByRole('tab', { name: 'Yetkiler' }).click();
  await A.getByRole('group', { name: 'Yetkili kişiler' }).locator('label', { hasText: 'Calisan Kisi' }).locator('input').check();
  await A.getByRole('group', { name: 'Yetkili kişiler' }).locator('label', { hasText: 'Yonetici Kisi' }).locator('input').check();
  await A.getByRole('button', { name: 'Yetkileri kaydet' }).click();
  check('yetkiler kaydedildi', await adminS.visible(A.locator('.notice[role="status"]', { hasText: 'Yetkiler kaydedildi' })));
  await adminS.shot('apps-designer');

  // ============================================================ çalışan (app_user)
  section('Çalışan: geçici parolasını değiştirir, yalnızca Uygulamalar menüsünü görür, izin talebi açar');
  const workerS = await open();
  const { page: W } = workerS;
  // Eski geçici parola artık geçersiz; sıfırlamayla verilen yeni geçici parola kullanılır.
  const oldTry = await W.evaluate(async ([email, password]) => (await fetch('/api/auth/login', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email, password }) })).status, [workerMail, workerTemp]);
  check('sıfırlanmadan önceki geçici parola artık geçersiz', oldTry === 401, oldTry);
  await login(W, workerMail, workerTempOverride.value);
  await setFirstPassword(workerS, workerTempOverride.value, 'Calisan-Yeni-Parola-1');
  check('çalışan Uygulamalarım sayfasına yönlendirildi', await workerS.visible(W.getByRole('heading', { name: 'Uygulamalarım' })));
  const wNav = await navLabels(W);
  check('çalışan menüsü yalnızca Uygulamalar ve Hesap', wNav.join('|') === 'Uygulamalarım|Taleplerim|Onay kutusu|Parolamı değiştir', wNav.join('|'));
  check('mimari/yönetim menüsü yok', !wNav.some((l) => ['Projeler', 'Uygulama tasarla', 'Kullanıcılar ve yetkiler', 'Muhasebe'].includes(l)));
  check('uygulama kartı görünür', await workerS.visible(W.locator('.proj-card', { hasText: 'İzin talebi' })));

  for (const hash of ['#/projects', '#/design', '#/admin/users', '#/accounting']) {
    await W.goto(`${WEB}/${hash}`);
    check(`çalışan ${hash} sayfasına giremez`, await workerS.visible(W.getByText('Bu sayfaya erişiminiz yok')));
  }
  const apiBlocked = await W.evaluate(async () => {
    const token = localStorage.getItem('arcvora.token');
    const list = await fetch('/api/projects', { headers: { authorization: `Bearer ${token}` } });
    const created = await fetch('/api/workspaces/00000000-0000-0000-0000-000000000000/apps', { headers: { authorization: `Bearer ${token}` } });
    return [list.status, (await list.json()).length, created.status];
  });
  check('sunucu da reddeder: proje listesi boş, uygulama listesi 404', apiBlocked[0] === 200 && apiBlocked[1] === 0 && apiBlocked[2] === 404, apiBlocked);

  await W.goto(`${WEB}/#/apps`);
  await W.locator('.proj-card', { hasText: 'İzin talebi' }).click();
  check('izin formu açıldı', await workerS.visible(W.getByRole('heading', { name: 'İzin talebi', level: 1 })));
  await W.getByRole('button', { name: 'Talebi gönder' }).click();
  check('boş formda alan hataları görünür', await workerS.visible(W.locator('.err', { hasText: 'zorunlu' })) && (await W.locator('.err').count()) >= 3);
  await W.getByLabel('İzin türü').selectOption({ label: 'Yıllık izin' });
  await W.getByLabel('Başlangıç tarihi').fill('2026-11-10');
  await W.getByLabel('Bitiş tarihi').fill('2026-11-14');
  await W.getByLabel('Açıklama').fill('<b>aile ziyareti</b>');
  check('gün sayısı canlı hesaplandı (5)', (await W.locator('.computed').first().innerText()) === '5');
  check('onay akışı önceden gösterilir', (await W.locator('.step-strip').first().innerText()).includes('Yönetici onayı'));
  await W.getByRole('button', { name: 'Talebi gönder' }).click();
  check('talep açıldı: numara IZN-yıl-0001', await workerS.visible(W.getByRole('heading', { level: 1, name: /^IZN-\d{4}-0001$/ })));
  check('durum İncelemede, bekleyen adım Yönetici onayı', (await W.locator('.page-head').innerText()).includes('İncelemede') && (await W.locator('.step-strip .step.current').innerText()).includes('Yönetici onayı'));
  check('kullanıcı metni HTML olarak çalışmadı', (await W.locator('b', { hasText: 'aile ziyareti' }).count()) === 0 && (await W.locator('.kv-table').innerText()).includes('<b>aile ziyareti</b>'));
  check('talep eden iptal edebilir', await W.getByRole('button', { name: 'Talebi iptal et' }).isVisible());
  await workerS.shot('apps-request-worker');

  // ============================================================ yönetici onaylar
  section('Yönetici: geçici parolasını değiştirir, onay kutusunda talebi onaylar');
  const bossS = await open();
  const { page: B } = bossS;
  await login(B, bossMail, bossTemp);
  await setFirstPassword(bossS, bossTemp, 'Yonetici-Yeni-Parola-1');
  check('yönetici uygulamalarına yönlendirildi', await bossS.visible(B.getByRole('heading', { name: 'Uygulamalarım' })));
  const badge = B.locator('.sb-item', { hasText: 'Onay kutusu' }).locator('.count');
  check('onay kutusu rozeti 1 bekleyen görevi gösterir', await bossS.visible(badge) && (await badge.innerText()) === '1');
  await B.getByRole('button', { name: /Onay kutusu/ }).click();
  check('bekleyen görev listelenir (çalışan + izin tarihleri)', await bossS.visible(B.locator('.list .item', { hasText: 'Calisan Kisi' })));
  await B.locator('.list .item', { hasText: 'Calisan Kisi' }).click();
  check('onay kutusu görünür', await bossS.visible(B.locator('.decision-box')));
  check('gerekçesiz Reddet kapalı', await B.getByRole('button', { name: 'Reddet' }).isDisabled());
  await B.locator('#decision-comment').fill('İyi tatiller');
  await B.getByRole('button', { name: 'Onayla' }).click();
  check('onaylandı olarak güncellendi', await bossS.visible(B.locator('.page-head', { hasText: 'Onaylandı' })));
  check('zaman çizelgesi onayı ve yorumu anlatır', (await B.locator('.timeline').innerText()).includes('Yonetici Kisi “Yönetici onayı” adımını onayladı: İyi tatiller'));
  check('karar sonrası rozet kalktı', await bossS.gone(badge));
  await bossS.shot('apps-request-approved');

  // ============================================================ çalışan sonucu görür
  section('Çalışan: sonucu Taleplerim sayfasında görür');
  await W.goto(`${WEB}/#/requests`);
  check('Taleplerim: onaylanmış talep görünür', await workerS.visible(W.locator('.list .item', { hasText: 'Onaylandı' })));
  await W.getByRole('tab', { name: 'İncelemede' }).click();
  check('filtre: İncelemede boş', await workerS.visible(W.getByText('Bu filtreye uyan talep bulunmuyor.')));

  // ============================================================ mobil taşma
  section('Mobil görünüm (390 px): sayfa yatay taşmaz');
  await W.setViewportSize({ width: 390, height: 800 });
  for (const hash of ['#/apps', '#/requests', '#/inbox']) {
    await W.goto(`${WEB}/${hash}`);
    await W.waitForTimeout(500);
    check(`çalışan ${hash}: taşma yok`, await noOverflow(W));
  }
  await A.setViewportSize({ width: 390, height: 800 });
  for (const hash of ['#/design', '#/admin/users', '#/admin/groups']) {
    await A.goto(`${WEB}/${hash}`);
    await A.waitForTimeout(600);
    check(`admin ${hash}: taşma yok`, await noOverflow(A));
  }
  await A.goBack();
  await A.goto(`${WEB}/#/design`);
  await A.locator('.proj-card', { hasText: 'İzin talebi' }).first().click();
  await adminS.visible(A.getByRole('heading', { level: 1, name: 'İzin talebi' }));
  await A.waitForTimeout(600);
  check('admin tasarlayıcı: taşma yok (tek sütuna iner)', await noOverflow(A));
  await adminS.shot('apps-designer-mobile');

  for (const s of sessions) problemsAll.push(...s.problems);
  // Bilerek yapılan iki istek konsola "Failed to load resource" yazar: çalışanın yetkisiz uygulama listesi (404) ve boş formun gönderimi (422).
  const status = (n) => problemsAll.filter((p) => new RegExp(`status of ${n}\\b`).test(p)).length;
  const unexpected = problemsAll.filter((p) => !/Failed to load resource: the server responded with a status of (404|422)/.test(p));
  check('beklenmeyen sayfa/konsol hatası yok', unexpected.length === 0, unexpected.slice(0, 5));
  check('bilerek yapılan reddedilen istekler dışında 404/422 yok', status(404) <= 1 && status(422) <= 1, { 404: status(404), 422: status(422) });
} finally {
  for (const s of sessions) await s.browser.close().catch(() => {});
}
finish();
