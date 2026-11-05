// Şirket içi akış uygulamaları: API düzeyinde uçtan uca yolculuk.
//   master admin kullanıcı/grup açar → şablondan uygulama → yayın → yetki → talep → onay/ret/iptal → muhasebe webhook'u (gerçek HTTP alıcı,
//   HMAC doğrulamalı) → CSV → rol yalıtımı (app kullanıcısı mimari projeleri göremez).
import crypto from 'node:crypto';
import fs from 'node:fs';
import http from 'node:http';
import { API_URL, call, check, finish, section, sleep } from './lib.mjs';

const templates = JSON.parse(fs.readFileSync(new URL('../../web/src/lib/templates.json', import.meta.url), 'utf8'));
const tpl = (id) => templates.find((t) => t.id === id);
const uniq = `${Date.now().toString(36)}${Math.floor(Math.random() * 1e4)}`;
const TEMP = 'Gecici-Parola-123';
const SECRET = 'e2e-webhook-secret-0123456789';

const login = async (email, password) => {
  const r = await call('POST', '/api/auth/login', { body: { email, password } });
  return { ...r.body, status: r.status };
};
const jsonOf = (r) => JSON.stringify(r.body).slice(0, 400);

// ---------------------------------------------------------------- webhook alıcısı (gerçek HTTP)
const received = [];
const server = http.createServer((req, res) => {
  let body = '';
  req.on('data', (c) => (body += c));
  req.on('end', () => { received.push({ headers: req.headers, body, url: req.url }); res.writeHead(200).end('ok'); });
});
await new Promise((r) => server.listen(0, '127.0.0.1', r));
const hookUrl = `http://127.0.0.1:${server.address().port}/hook`;

async function waitFor(fn, ms = 25_000) {
  const t0 = Date.now();
  while (Date.now() - t0 < ms) { if (await fn()) return true; await sleep(300); }
  return false;
}

try {
  section('Master admin kullanıcıları ve grubu açar');
  const reg = await call('POST', '/api/auth/register', { body: { email: `admin-${uniq}@example.com`, password: 'Admin-Parola-12345', displayName: 'Master Admin' } });
  check('master admin kayıt oldu', reg.status === 201, jsonOf(reg));
  const admin = { token: reg.body.token, id: reg.body.user.id, ws: reg.body.workspaces[0].id };
  const A = { token: admin.token };
  const wsUrl = (p) => `/api/workspaces/${admin.ws}${p}`;

  const mk = async (name, role, managerId) => {
    const email = `${name.toLowerCase()}-${uniq}@example.com`;      // sunucu e-postayı küçük harfe çevirir
    const r = await call('POST', wsUrl('/users'), { ...A, body: { email, displayName: name, password: TEMP, role, managerId } });
    check(`${name} (${role}) açıldı`, r.status === 201 && r.body.mustChangePassword === true, jsonOf(r));
    const l = await login(email, TEMP);
    return { email, id: r.body.userId, token: l.token, mustChange: l.mustChangePassword, role: l.workspaces?.[0]?.role };
  };
  const boss = await mk('Yonetici', 'app_user');
  const worker = await mk('Calisan', 'app_user', boss.id);
  const accountant = await mk('Muhasebeci', 'app_user');
  const stranger = await mk('Yabanci', 'app_user');
  const editor = await mk('Mimar', 'editor');
  check('yeni kullanıcı giriş yaptığında geçici parola bayrağı görünür', worker.mustChange === true);
  check('app kullanıcısının rolü app_user', worker.role === 'app_user' && editor.role === 'editor');

  const group = await call('POST', wsUrl('/groups'), { ...A, body: { name: 'Muhasebe' } });
  check('muhasebe grubu oluşturuldu', group.status === 201, jsonOf(group));
  const setMembers = await call('PUT', wsUrl(`/groups/${group.body.id}/members`), { ...A, body: { userIds: [accountant.id] } });
  check('gruba üye eklendi', setMembers.status === 200 && setMembers.body.memberIds.length === 1, jsonOf(setMembers));

  section('Parola değiştirme');
  const bad = await call('POST', '/api/me/password', { token: worker.token, body: { currentPassword: 'yanlis-parola-123', newPassword: 'Yeni-Parola-12345' } });
  check('yanlış mevcut parola 422 wrong_password', bad.status === 422 && bad.body.code === 'wrong_password', jsonOf(bad));
  const ch = await call('POST', '/api/me/password', { token: worker.token, body: { currentPassword: TEMP, newPassword: 'Yeni-Parola-12345' } });
  check('parola değişti (204)', ch.status === 204, jsonOf(ch));
  check('eski parola artık çalışmaz', (await login(worker.email, TEMP)).status === 401);
  const relog = await login(worker.email, 'Yeni-Parola-12345');
  check('yeni parolayla giriş; geçici parola bayrağı kalktı', relog.status === 200 && relog.mustChangePassword !== true);
  worker.token = relog.token;

  section('Parola sıfırlama: yalnızca master admin\'in açtığı hesaplar');
  const reset = await call('POST', wsUrl(`/members/${worker.id}/reset-password`), { ...A, body: { password: 'Sifirlanan-Parola-1' } });
  check('admin çalışanın parolasını sıfırladı', reset.status === 200 && reset.body.mustChangePassword === true, jsonOf(reset));
  check('kullanıcının kendi parolası artık geçersiz', (await login(worker.email, 'Yeni-Parola-12345')).status === 401);
  const afterReset = await login(worker.email, 'Sifirlanan-Parola-1');
  check('sıfırlanan geçici parolayla giriş; yeniden değiştirmeye çağrılır', afterReset.status === 200 && afterReset.mustChangePassword === true);
  worker.token = afterReset.token;
  check('admin kendi hesabını sıfırlayamaz (not_managed)', (await call('POST', wsUrl(`/members/${admin.id}/reset-password`), { ...A, body: { password: 'Baska-Parola-12345' } })).body?.code === 'not_managed');
  const indep = await call('POST', '/api/auth/register', { body: { email: `bagimsiz-${uniq}@example.com`, password: 'Bagimsiz-Parola-123', displayName: 'Bağımsız' } });
  await call('POST', wsUrl('/members'), { ...A, body: { email: `bagimsiz-${uniq}@example.com`, role: 'reader' } });
  const hijack = await call('POST', wsUrl(`/members/${indep.body.user.id}/reset-password`), { ...A, body: { password: 'Ele-Gecirilen-123' } });
  check('kendi kaydıyla açılmış (başka şirkete de üye olabilecek) hesap sıfırlanamaz', hijack.status === 409 && hijack.body.code === 'not_managed', jsonOf(hijack));
  check('o hesabın parolası değişmedi', (await login(`bagimsiz-${uniq}@example.com`, 'Bagimsiz-Parola-123')).status === 200);
  check('çalışan sıfırlama yapamaz (403)', (await call('POST', wsUrl(`/members/${boss.id}/reset-password`), { token: stranger.token, body: { password: 'Yetkisiz-Parola-1' } })).status === 403);
  // sonraki bölümler çalışanın parolasını değiştirmiş gibi devam eder
  const pw = await call('POST', '/api/me/password', { token: worker.token, body: { currentPassword: 'Sifirlanan-Parola-1', newPassword: 'Yeni-Parola-12345' } });
  check('çalışan sıfırlanan parolasını kendi parolasıyla değiştirdi', pw.status === 204);

  section('Rol yalıtımı: app kullanıcısı mimari projeleri göremez');
  const sample = await call('POST', '/api/samples/expense-management', { ...A, body: { workspaceId: admin.ws } });
  check('admin örnek proje oluşturdu', sample.status === 201, jsonOf(sample));
  const pid = sample.body.summary.id;
  for (const [name, u] of [['çalışan', worker], ['yabancı', stranger], ['yönetici', boss]]) {
    const list = await call('GET', '/api/projects', { token: u.token });
    check(`${name}: proje listesi boş`, list.status === 200 && list.body.length === 0, jsonOf(list));
    for (const p of [`/api/projects/${pid}`, `/api/projects/${pid}/versions`, `/api/projects/${pid}/audit`, `/api/projects/${pid}/proposals`])
      check(`${name}: ${p.split('/').slice(3).join('/') || 'proje'} 404`, (await call('GET', p, { token: u.token })).status === 404);
    check(`${name}: proje oluşturamaz (403)`, (await call('POST', '/api/projects', { token: u.token, body: { workspaceId: admin.ws, name: 'x', description: '', techProfileId: 'aspnetcore', ideaText: '' } })).status === 403);
  }
  check('düzenleyici (mimari ekibi) projeleri görür', (await call('GET', '/api/projects', { token: editor.token })).body.length === 1);
  check('düzenleyici uygulama tasarlayamaz (403)', (await call('POST', wsUrl('/apps'), { token: editor.token, body: { name: 'x' } })).status === 403);
  check('çalışan kullanıcı oluşturamaz (403)', (await call('POST', wsUrl('/users'), { token: worker.token, body: { email: 'x@example.com', displayName: 'x', password: TEMP, role: 'admin' } })).status === 403);

  section('Şablonlardan uygulama tasarımı ve yayın');
  const apps = {};
  for (const id of ['leave', 'advance', 'expense']) {
    const t = tpl(id);
    const c = await call('POST', wsUrl('/apps'), { ...A, body: { name: t.name, icon: t.icon, definition: t.definition } });
    check(`${t.name}: taslak oluştu, sorun yok`, c.status === 201 && c.body.issues.length === 0, jsonOf(c));
    const p = await call('POST', `/api/apps/${c.body.summary.id}/publish`, A);
    check(`${t.name}: yayınlandı (v1)`, p.status === 200 && p.body.summary.versionNumber === 1 && p.body.summary.status === 'published', jsonOf(p));
    check(`${t.name}: tekrar yayın 409 nothing_to_publish`, (await call('POST', `/api/apps/${c.body.summary.id}/publish`, A)).body?.code === 'nothing_to_publish');
    apps[id] = c.body.summary.id;
  }
  for (const id of ['leave', 'advance', 'expense']) {
    const g = await call('PUT', `/api/apps/${apps[id]}/grants`, { ...A, body: { userIds: [worker.id, boss.id], groupIds: [] } });
    check(`yetki verildi: ${id}`, g.status === 200 && g.body.userIds.length === 2, jsonOf(g));
  }
  check('çalışan 3 uygulamayı görür', (await call('GET', '/api/my/apps', { token: worker.token })).body.length === 3);
  check('yabancı hiç uygulama görmez', (await call('GET', '/api/my/apps', { token: stranger.token })).body.length === 0);
  check('yabancı form açamaz (403 no_app_access)', (await call('GET', `/api/apps/${apps.leave}/form`, { token: stranger.token })).body?.code === 'no_app_access');
  check('yabancı talep açamaz (403)', (await call('POST', `/api/apps/${apps.leave}/requests`, { token: stranger.token, body: { data: { kind: 'Rapor', start: '2026-11-10', end: '2026-11-11' } } })).status === 403);

  section('İzin: kısa izin yalnız yönetici onayıyla, uzun izin ayrıca master admin onayıyla');
  const submit = (u, app, data, key) => call('POST', `/api/apps/${apps[app]}/requests`, { token: u.token, body: { data }, headers: key ? { 'idempotency-key': key } : {} });
  const short = await submit(worker, 'leave', { kind: 'Yıllık izin', start: '2026-11-10', end: '2026-11-14', note: '<b>tatil</b>' });
  check('talep açıldı, numara IZN-yıl-0001', short.status === 201 && /^IZN-\d{4}-0001$/.test(short.body.summary.number), jsonOf(short));
  check('gün sayısı sunucuda hesaplandı (5)', short.body.data.days === 5);
  check('özet okunur', short.body.summary.summary === '10.11.2026 · 14.11.2026 · Yıllık izin', short.body.summary.summary);
  check('HTML metni olduğu gibi saklandı (kaçışı arayüz yapar)', short.body.data.note === '<b>tatil</b>');
  check('yönetici onayı bekliyor', short.body.tasks[0].assigneeId === boss.id && short.body.summary.status === 'in_review');
  const bossTasks = await call('GET', '/api/my/tasks', { token: boss.token });
  check('yöneticinin onay kutusunda 1 görev', bossTasks.status === 200 && bossTasks.body.length === 1, jsonOf(bossTasks));
  check('görevi atanan dışındakiler karar veremez (404)', (await call('POST', `/api/tasks/${bossTasks.body[0].id}/approve`, { token: worker.token, body: {} })).status === 404);
  const ap1 = await call('POST', `/api/tasks/${bossTasks.body[0].id}/approve`, { token: boss.token, body: { comment: 'iyi tatiller' } });
  check('yönetici onayladı → talep onaylandı', ap1.status === 200 && ap1.body.summary.status === 'approved', jsonOf(ap1));
  check('zaman çizelgesi: submitted, step_started, approved, step_skipped, completed', ap1.body.events.map((e) => e.kind).join() === 'submitted,step_started,approved,step_skipped,completed', ap1.body.events.map((e) => e.kind).join());
  check('onaylanmış izin iptal edilemez (409)', (await call('POST', `/api/requests/${short.body.summary.id}/cancel`, { token: worker.token })).status === 409);

  const long = await submit(worker, 'leave', { kind: 'Mazeret izni', start: '2026-12-01', end: '2026-12-12' });
  const longId = long.body.summary.id;
  const t2 = await call('GET', '/api/my/tasks', { token: boss.token });
  const ap2 = await call('POST', `/api/tasks/${t2.body[0].id}/approve`, { token: boss.token, body: {} });
  check('12 günlük izin yönetici sonrası master admin adımına geçti', ap2.body.summary.status === 'in_review' && ap2.body.summary.currentStepName.startsWith('Master admin'), jsonOf(ap2));
  const at = await call('GET', '/api/my/tasks', { token: admin.token });
  const ap3 = await call('POST', `/api/tasks/${at.body[0].id}/approve`, { token: admin.token, body: { comment: 'tamam' } });
  check('master admin onayladı → onaylandı', ap3.body.summary.status === 'approved', jsonOf(ap3));
  check('izin muhasebeye gitmez', (await call('GET', wsUrl('/accounting/entries'), A)).body.length === 0);

  const rej = await submit(worker, 'leave', { kind: 'Ücretsiz izin', start: '2026-12-20', end: '2026-12-22' });
  const rt = (await call('GET', '/api/my/tasks', { token: boss.token })).body[0];
  check('gerekçesiz red 422 comment_required', (await call('POST', `/api/tasks/${rt.id}/reject`, { token: boss.token, body: {} })).body?.code === 'comment_required');
  const rr = await call('POST', `/api/tasks/${rt.id}/reject`, { token: boss.token, body: { comment: 'Yoğun dönem' } });
  check('gerekçeli red → reddedildi', rr.status === 200 && rr.body.summary.status === 'rejected', jsonOf(rr));
  check('reddedilmiş talepte ikinci karar 409', (await call('POST', `/api/tasks/${rt.id}/approve`, { token: boss.token, body: {} })).status === 409);

  const toCancel = await submit(worker, 'leave', { kind: 'Rapor', start: '2027-01-05', end: '2027-01-06' });
  const cancelled = await call('POST', `/api/requests/${toCancel.body.summary.id}/cancel`, { token: worker.token });
  check('talep eden iptal edebilir', cancelled.status === 200 && cancelled.body.summary.status === 'cancelled', jsonOf(cancelled));
  check('iptal sonrası yöneticide bekleyen görev kalmadı', (await call('GET', '/api/my/tasks', { token: boss.token })).body.length === 0);

  section('Doğrulama ve görünürlük');
  const invalid = await submit(worker, 'leave', { kind: 'Hediye', start: '2026-11-14', end: '2026-11-10', hack: 1 });
  const codes = (invalid.body?.details ?? []).map((d) => d.code);
  check('geçersiz form 422 ve tüm bulgular birlikte', invalid.status === 422 && ['invalid_option', 'not_before', 'unknown_field'].every((c) => codes.includes(c)), jsonOf(invalid));
  check('yabancı talebi açamaz (404)', (await call('GET', `/api/requests/${longId}`, { token: stranger.token })).status === 404);
  check('talep eden, onaylayan ve admin talebi açabilir', (await Promise.all([worker, boss, admin].map((u) => call('GET', `/api/requests/${longId}`, { token: u.token })))).every((r) => r.status === 200));
  const mine = await call('GET', '/api/my/requests', { token: worker.token });
  check('taleplerim: 4 talep (onaylı, onaylı, reddedilmiş, iptal)', mine.body.length === 4, mine.body.map?.((r) => r.status).join());
  check('durum filtresi', (await call('GET', '/api/my/requests?status=approved', { token: worker.token })).body.length === 2);

  section('Onaylayıcı çalışma alanından çıkarılırsa bekleyen görevi master admin\'e devredilir');
  const boss2 = await mk('Yonetici2', 'app_user');
  const worker2 = await mk('Calisan2', 'app_user', boss2.id);
  await call('PUT', `/api/apps/${apps.leave}/grants`, { ...A, body: { userIds: [worker.id, boss.id, worker2.id], groupIds: [] } });
  const orphan = await call('POST', `/api/apps/${apps.leave}/requests`, { token: worker2.token, body: { data: { kind: 'Rapor', start: '2027-03-01', end: '2027-03-02' } } });
  check('talep açıldı, yeni yönetici bekliyor', orphan.status === 201 && orphan.body.tasks[0].assigneeId === boss2.id, jsonOf(orphan));
  check('yönetici çalışma alanından çıkarıldı (204)', (await call('DELETE', wsUrl(`/members/${boss2.id}`), A)).status === 204);
  const handed = await call('GET', `/api/requests/${orphan.body.summary.id}`, A);
  const pendingTask = handed.body.tasks.find((t) => t.status === 'pending');
  check('bekleyen görev master admin\'e devredildi ve nedeni yazıldı', pendingTask?.assigneeId === admin.id && pendingTask?.fallback === 'assignee_removed', jsonOf(handed));
  check('zaman çizelgesinde approver_removed olayı', handed.body.events.some((e) => e.kind === 'approver_removed'));
  const doneOrphan = await call('POST', `/api/tasks/${pendingTask.id}/approve`, { ...A, body: {} });
  check('master admin onaylayınca talep tamamlandı', doneOrphan.body?.summary?.status === 'approved', jsonOf(doneOrphan));

  section('Aynı Idempotency-Key ile eşzamanlı gönderim tek talep üretir');
  const burst = await Promise.all(Array.from({ length: 6 }, () => submit(worker, 'leave', { kind: 'Rapor', start: '2027-02-01', end: '2027-02-02' }, `burst-${uniq}`)));
  check('hepsi 200/201 döndü', burst.every((r) => r.status === 200 || r.status === 201), burst.map((r) => r.status).join());
  check('tek bir talep oluştu', new Set(burst.map((r) => r.body.summary.id)).size === 1);

  section('Muhasebe: onaylanan talep kuyruğa düşer ve webhook ile imzalı teslim edilir');
  const set = await call('PUT', wsUrl('/accounting/settings'), { ...A, body: { webhookUrl: hookUrl, webhookSecret: SECRET, accountingGroupId: group.body.id } });
  check('ayarlar kaydedildi, gizli anahtar geri dönmedi', set.status === 200 && set.body.hasSecret === true && !JSON.stringify(set.body).includes(SECRET), jsonOf(set));
  check('muhasebe üyesi dışındakiler kuyruğu göremez (403)', (await call('GET', wsUrl('/accounting/entries'), { token: worker.token })).status === 403);
  const adv = await submit(worker, 'advance', { amount: 8000, reason: 'Kira' });
  const adv2 = await submit(worker, 'advance', { amount: 12000.5, reason: 'Taşınma' });
  const exp = await submit(worker, 'expense', { expense_date: '2026-11-03', amount: 120.5, category: 'Yemek', description: '=1+1 öğle yemeği', receipt_no: 'F-77' });
  const approveFirst = async (u, requestId) => {
    const tasks = (await call('GET', '/api/my/tasks', { token: u.token })).body.filter((t) => t.requestId === requestId);
    return call('POST', `/api/tasks/${tasks[0].id}/approve`, { token: u.token, body: {} });
  };
  check('8.000 TRY avans yalnızca yönetici onayıyla bitti', (await approveFirst(boss, adv.body.summary.id)).body.summary.status === 'approved');
  check('12.000 TRY avans yöneticiden sonra master admin bekliyor', (await approveFirst(boss, adv2.body.summary.id)).body.summary.status === 'in_review');
  check('master admin onayladı', (await approveFirst(admin, adv2.body.summary.id)).body.summary.status === 'approved');
  check('masraf onaylandı', (await approveFirst(boss, exp.body.summary.id)).body.summary.status === 'approved');

  const entries = await call('GET', wsUrl('/accounting/entries'), { token: accountant.token });
  check('muhasebe üyesi kuyruğu görür: 3 kayıt', entries.status === 200 && entries.body.length === 3, jsonOf(entries));
  check('kategoriler avans, avans, masraf', entries.body.map((e) => e.category).sort().join() === 'avans,avans,masraf');
  const delivered = await waitFor(async () => received.length >= 3);
  check('webhook alıcısı 3 teslim aldı', delivered, `alınan: ${received.length}`);
  for (const r of received) {
    const sig = 'sha256=' + crypto.createHmac('sha256', SECRET).update(r.body).digest('hex');
    check(`imza doğru (${JSON.parse(r.body).number})`, r.headers['x-arcvora-signature'] === sig);
    check(`Idempotency-Key kayıt kimliği (${JSON.parse(r.body).number})`, r.headers['idempotency-key'] === JSON.parse(r.body).entryId);
  }
  const sentAll = await waitFor(async () => (await call('GET', wsUrl('/accounting/entries'), A)).body.every((e) => e.status === 'sent'));
  check('kayıtların durumu gönderildi', sentAll);
  const expPayload = JSON.parse(received.find((r) => JSON.parse(r.body).category === 'masraf').body);
  check('yük: talep eden, tutar ve onay bilgisi', expPayload.requester.email === worker.email && expPayload.fields.some((f) => f.key === 'amount' && f.value === 120.5 && f.currency === 'TRY') && expPayload.approvals[0].by.name === 'Yonetici', JSON.stringify({ requester: expPayload.requester, approvals: expPayload.approvals, workerEmail: worker.email }));
  check('muhasebe üyesi, kaydı olan talebi açabilir; kaydı olmayanı açamaz', (await call('GET', `/api/requests/${exp.body.summary.id}`, { token: accountant.token })).status === 200 && (await call('GET', `/api/requests/${longId}`, { token: accountant.token })).status === 404);
  const entryId = entries.body[0].id;
  const proc = await call('POST', wsUrl(`/accounting/entries/${entryId}/process`), { token: accountant.token, body: { note: 'Logo\'ya girildi' } });
  check('muhasebeci kaydı işledi olarak işaretledi', proc.status === 200 && proc.body.status === 'processed' && proc.body.note === "Logo'ya girildi", jsonOf(proc));
  check('ikinci işaretleme 409', (await call('POST', wsUrl(`/accounting/entries/${entryId}/process`), { token: accountant.token, body: {} })).status === 409);

  section('CSV dışa aktarma');
  const csv = await call('GET', wsUrl('/accounting/export.csv'), { token: accountant.token, raw: true });
  // fetch().text() BOM'u atar; bayt düzeyinde ayrıca doğrulanır.
  const raw = new Uint8Array(await (await fetch(API_URL + wsUrl('/accounting/export.csv'), { headers: { authorization: `Bearer ${accountant.token}` } })).arrayBuffer());
  check('CSV indirildi (text/csv; UTF-8 BOM ile)', csv.status === 200 && csv.headers.get('content-type').startsWith('text/csv') && raw[0] === 0xef && raw[1] === 0xbb && raw[2] === 0xbf, `${csv.status} ${csv.headers.get('content-type')} ${[...raw.slice(0, 4)]}`);
  check('başlık satırı ve 3 kayıt', csv.body.trim().split('\r\n').length === 4, csv.body.slice(0, 300));
  check('formül ile başlayan kullanıcı metni hücre başında değil (enjeksiyon yok)', !/(^|;)"?=1\+1/m.test(csv.body), csv.body.slice(0, 500));
  check('yetkisiz CSV indiremez (403)', (await call('GET', wsUrl('/accounting/export.csv'), { token: worker.token })).status === 403);
  check('webhook ayarını yalnızca master admin değiştirir (403)', (await call('PUT', wsUrl('/accounting/settings'), { token: accountant.token, body: { webhookUrl: 'https://x.example/h' } })).status === 403);

  section('Çalışma alanı yalıtımı');
  const outsiderReg = await call('POST', '/api/auth/register', { body: { email: `outsider-${uniq}@example.com`, password: 'Outsider-Parola-123', displayName: 'Dışarıdan' } });
  const out = { token: outsiderReg.body.token };
  for (const [what, r] of [
    ['uygulama', await call('GET', `/api/apps/${apps.leave}`, out)],
    ['talep', await call('GET', `/api/requests/${longId}`, out)],
    ['talep açma', await call('POST', `/api/apps/${apps.leave}/requests`, { ...out, body: { data: {} } })],
    ['muhasebe kuyruğu', await call('GET', wsUrl('/accounting/entries'), out)],
    ['dizin', await call('GET', wsUrl('/directory'), out)],
    ['kullanıcı oluşturma', await call('POST', wsUrl('/users'), { ...out, body: { email: 'z@example.com', displayName: 'z', password: TEMP, role: 'app_user' } })],
  ]) check(`başka çalışma alanı: ${what} 404`, r.status === 404, jsonOf(r));
} finally {
  server.close();
}
finish();
