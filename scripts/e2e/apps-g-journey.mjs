// G fazı özellikleri: API düzeyinde uçtan uca yolculuk (gerçek HTTP + gerçek SMTP alıcısı).
//   ekler (fiş yükleme, tür/yetki), yazdırılabilir PDF + doğrulama, vekâlet + e-posta, Türkçe arama, rapor/CSV, listeler, izin bakiyesi,
//   saklama (önizleme), KVKK dışa aktarım. Önkoşul: API'nin Email__* ayarları smtp-sink.mjs'e bakmalı (scripts/e2e.sh bunu yapar).
//   ARCVORA_API_URL, SMTP_SINK_URL (varsayılan http://127.0.0.1:2526)
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { API_URL, call, check, finish, section, sleep } from './lib.mjs';

const SINK = (process.env.SMTP_SINK_URL ?? 'http://127.0.0.1:2526').replace(/\/$/, '');
const uniq = `${Date.now().toString(36)}${Math.floor(Math.random() * 1e4)}`;
const TEMP = 'Gecici-Parola-123';
const jsonOf = (r) => JSON.stringify(r && typeof r === 'object' && 'status' in r && 'body' in r ? r.body : r).slice(0, 400);
const today = (offset = 0) => new Date(Date.now() + 180 * 60_000 + offset * 86_400_000).toISOString().slice(0, 10);   // UTC+3 yerel gün

/** Geçerli en küçük PNG (1×1) — içerik sihirli baytlarla tanınır. */
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==', 'base64');
const EXE = Buffer.from('MZ' + '\x90\x00\x03\x00'.repeat(8) + 'This program cannot be run in DOS mode.', 'latin1');

async function upload(token, appId, bytes, fileName) {
  const res = await fetch(`${API_URL}/api/apps/${appId}/uploads?fileName=${encodeURIComponent(fileName)}`, {
    method: 'POST', headers: { authorization: `Bearer ${token}`, 'content-type': 'application/octet-stream' }, body: bytes,
  });
  const text = await res.text();
  return { status: res.status, body: text ? JSON.parse(text) : null };
}
async function download(token, url) {
  const res = await fetch(API_URL + url, { headers: { authorization: `Bearer ${token}` } });
  return { status: res.status, bytes: Buffer.from(await res.arrayBuffer()), headers: res.headers };
}
const inbox = async () => (await fetch(`${SINK}/messages`)).json();
async function waitMail(pred, ms = 20_000) {
  const t0 = Date.now();
  while (Date.now() - t0 < ms) {
    const hit = (await inbox()).find(pred);
    if (hit) return hit;
    await sleep(400);
  }
  return null;
}
function pdfText(bytes) {
  try {
    const f = path.join(os.tmpdir(), `arcvora-e2e-${uniq}-${Math.random().toString(36).slice(2)}.pdf`);
    fs.writeFileSync(f, bytes);
    try { return execFileSync('pdftotext', ['-layout', f, '-'], { encoding: 'utf8' }); } finally { fs.rmSync(f, { force: true }); }
  } catch { return null; }
}

try {
  // ------------------------------------------------------------------ hazırlık
  section('Hazırlık: kullanıcılar, fişli masraf uygulaması, bakiye bağlı izin uygulaması');
  const reg = await call('POST', '/api/auth/register', { body: { email: `g-admin-${uniq}@example.com`, password: 'Admin-Parola-12345', displayName: 'Master Admin' } });
  check('master admin kayıt oldu', reg.status === 201, jsonOf(reg));
  const A = { token: reg.body.token };
  const ws = reg.body.workspaces[0].id;
  const wsUrl = (p) => `/api/workspaces/${ws}${p}`;

  const mk = async (name, managerId) => {
    const email = `${name.toLowerCase().replace(/[^a-z]/g, '')}-${uniq}@example.com`;
    const r = await call('POST', wsUrl('/users'), { ...A, body: { email, displayName: name, password: TEMP, role: 'app_user', managerId } });
    check(`${name} açıldı`, r.status === 201, jsonOf(r));
    const l = await call('POST', '/api/auth/login', { body: { email, password: TEMP } });
    return { email, id: r.body.userId, token: l.body.token };
  };
  const boss = await mk('Müdür Mehmet');
  const worker = await mk('Işıl Öztürk', boss.id);                 // Türkçe harfli ad (arama sınaması için)
  const deputy = await mk('Vekil Veli');
  const outsider = await mk('Başka Birim');

  const expenseDef = {
    schema: 1, prefix: 'MSF', summaryFields: ['amount', 'description'],
    fields: [
      { key: 'amount', label: 'Tutar', type: 'money', required: true },
      { key: 'description', label: 'Açıklama', type: 'text' },
      { key: 'receipt', label: 'Fiş', type: 'file', required: true, maxFiles: 2 },
    ],
    steps: [{ id: 's1', name: 'Yönetici onayı', approvers: { type: 'manager' } }],
    completion: { accounting: { enabled: true, category: 'masraf' } },
  };
  const mkApp = async (name, def, userIds) => {
    const c = await call('POST', wsUrl('/apps'), { ...A, body: { name, description: name, icon: 'receipt', definition: def } });
    check(`${name} taslağı oluşturuldu`, c.status === 201, jsonOf(c));
    const id = c.body.summary.id;
    const p = await call('POST', `/api/apps/${id}/publish`, A);
    check(`${name} yayınlandı`, p.status === 200, jsonOf(p));
    const g = await call('PUT', `/api/apps/${id}/grants`, { ...A, body: { userIds, groupIds: [] } });
    check(`${name} yetkisi verildi`, g.status === 200, jsonOf(g));
    return id;
  };
  const expenseApp = await mkApp('Fişli masraf', expenseDef, [worker.id, deputy.id]);

  // ------------------------------------------------------------------ ekler
  section('Ekler: fiş yükleme, tür denetimi, yetkili erişim');
  const bad = await upload(worker.token, expenseApp, EXE, 'fis.png');
  check('yürütülebilir dosya .png adıyla bile reddedilir (içerik sihirli baytlardan tanınır)', bad.status === 422 || bad.status === 415 || bad.status === 400, bad);
  const empty = await upload(worker.token, expenseApp, Buffer.alloc(0), 'bos.png');
  check('boş dosya reddedilir', empty.status === 422 || empty.status === 400, empty);
  const up = await upload(worker.token, expenseApp, PNG, '../../etc/passwd<fis>.png');
  check('geçerli fiş yüklendi (201)', up.status === 201, up);
  check('dosya adı güvenli hâle getirildi (yol/özel karakter yok)', up.body && !/[\\/<>]/.test(up.body.fileName), up.body?.fileName);
  check('içerik türü içerikten belirlendi (image/png)', up.body?.contentType === 'image/png', up.body);
  const outsiderGet = await download(outsider.token, `/api/attachments/${up.body.id}`);
  check('hazırlanmış dosya başkasına 404 (varlığı sızmaz)', outsiderGet.status === 404, outsiderGet.status);
  const ownGet = await download(worker.token, `/api/attachments/${up.body.id}`);
  check('yükleyen kendi hazırlanmış dosyasını indirir ve baytlar aynıdır', ownGet.status === 200 && ownGet.bytes.equals(PNG), ownGet.status);
  check('indirme önbelleksiz ve nosniff', /no-store/.test(ownGet.headers.get('cache-control') ?? '') && ownGet.headers.get('x-content-type-options') === 'nosniff', [...ownGet.headers.entries()]);

  const noFile = await call('POST', `/api/apps/${expenseApp}/requests`, { token: worker.token, body: { data: { amount: 120.5, description: 'Müşteri yemeği' } } });
  check('zorunlu fiş yoksa talep 422', noFile.status === 422, jsonOf(noFile));

  // Vekâlet: talepten ÖNCE başlasın ki vekil de e-posta alsın.
  section('Vekâlet: müdür vekil bırakır; görev ve e-posta vekile de gider');
  const del = await call('POST', wsUrl('/delegations'), { token: boss.token, body: { toUserId: deputy.id, startsOn: today(), endsOn: today(5), reason: 'Yıllık izin' } });
  check('vekâlet verildi', del.status === 201, jsonOf(del));
  const granted = await waitMail((m) => m.to.includes(deputy.email) && /vekil olarak/.test(m.subject));
  check('vekile "vekil olarak belirledi" e-postası gitti', !!granted, (await inbox()).map((m) => m.subject));

  section('Talep, ek bağlama ve e-postalar');
  const sub = await call('POST', `/api/apps/${expenseApp}/requests`, { token: worker.token, body: { data: { amount: 120.5, description: 'Şirket gideri: Işık Gıda, İstanbul', receipt: [up.body.id] } } });
  check('fişli talep gönderildi (201)', sub.status === 201, jsonOf(sub));
  const reqId = sub.body.summary.id;
  const number = sub.body.summary.number;
  const detail = await call('GET', `/api/requests/${reqId}`, { token: worker.token });
  check('talep ayrıntısında ek listelenir', Array.isArray(detail.body.attachments) && detail.body.attachments.length === 1, jsonOf(detail));
  const reuse = await call('POST', `/api/apps/${expenseApp}/requests`, { token: worker.token, body: { data: { amount: 5, receipt: [up.body.id] } } });
  check('aynı dosya ikinci talebe eklenemez', reuse.status >= 400 && reuse.status < 500, jsonOf(reuse));
  const bossGet = await download(boss.token, `/api/attachments/${up.body.id}`);
  check('onaylayıcı bağlı dosyayı indirir', bossGet.status === 200 && bossGet.bytes.equals(PNG), bossGet.status);
  const strangerGet = await download(outsider.token, `/api/attachments/${up.body.id}`);
  check('ilgisiz kullanıcı bağlı dosyaya 404 alır', strangerGet.status === 404, strangerGet.status);

  const toBoss = await waitMail((m) => m.to.includes(boss.email) && /Onayınız bekleniyor/.test(m.subject));
  check('müdüre "Onayınız bekleniyor" e-postası gitti (Türkçe başlık doğru çözüldü)', !!toBoss && toBoss.subject.includes(number), toBoss?.subject);
  check('e-posta metni talep eden adını ve adımı içerir', !!toBoss && toBoss.text.includes('Işıl Öztürk') && toBoss.text.includes('Yönetici onayı'), toBoss?.text?.slice(0, 200));
  const toDeputy = await waitMail((m) => m.to.includes(deputy.email) && /Vekâleten onayınız bekleniyor/.test(m.subject));
  check('aktif vekile "Vekâleten onayınız bekleniyor" e-postası gitti', !!toDeputy && toDeputy.text.includes('Müdür Mehmet'), toDeputy?.subject);
  const all = await inbox();
  check('hiçbir e-postada parola/geçici parola yok', all.every((m) => !m.text.includes(TEMP) && !m.html.includes(TEMP)));
  check('talep edene (kendi talebi için) "onayınız bekleniyor" maili gitmedi', !all.some((m) => m.to.includes(worker.email) && /bekleniyor/.test(m.subject)));

  // ------------------------------------------------------------------ vekil karar verir
  section('Vekil adına karar verir; sonuç e-postası ve kayıtlar');
  const deputyTasks = await call('GET', '/api/my/tasks', { token: deputy.token });
  const dt = deputyTasks.body.find((t) => t.requestId === reqId);
  check('vekil görevi "vekâleten" işaretli görür', !!dt && dt.onBehalfOfName === 'Müdür Mehmet', jsonOf(deputyTasks));
  const approve = await call('POST', `/api/tasks/${dt.id}/approve`, { token: deputy.token, body: { comment: 'Müdür izinde.' } });
  check('vekil onayladı', approve.status === 200 && approve.body.summary.status === 'approved', jsonOf(approve));
  const ok = await waitMail((m) => m.to.includes(worker.email) && /onaylandı/.test(m.subject));
  check('çalışana "Talebiniz onaylandı" e-postası gitti', !!ok, (await inbox()).map((m) => m.subject));

  // ------------------------------------------------------------------ PDF + doğrulama
  section('Yazdırılabilir PDF ve belge doğrulama');
  const pdf = await download(worker.token, `/api/requests/${reqId}/pdf`);
  check('PDF indirildi (application/pdf, %PDF başlığı)', pdf.status === 200 && pdf.bytes.subarray(0, 4).toString() === '%PDF' && /application\/pdf/.test(pdf.headers.get('content-type') ?? ''), pdf.status);
  const strangerPdf = await download(outsider.token, `/api/requests/${reqId}/pdf`);
  check('ilgisiz kullanıcı PDF alamaz (404)', strangerPdf.status === 404, strangerPdf.status);
  const text = pdfText(pdf.bytes);
  if (text === null) console.log('  · pdftotext yok: PDF metin denetimleri atlandı');
  else {
    check('PDF Türkçe metni doğru taşır (ad, açıklama, vekil adı)', text.includes('Işıl Öztürk') && text.includes('Şirket gideri') && text.includes('Müdür Mehmet'), text.slice(0, 300));
    const code = /Doğrulama kodu: ([0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4})/.exec(text)?.[1];
    check('PDF doğrulama kodu içerir', !!code, text.slice(-300));
    if (code) {
      const v = await call('GET', wsUrl(`/documents/verify?number=${encodeURIComponent(number)}&code=${encodeURIComponent(code)}`), { token: boss.token });
      check('doğru numara + kod → geçerli ve durum "onaylandı"', v.status === 200 && v.body.valid === true && v.body.status === 'approved', jsonOf(v));
      const w = await call('GET', wsUrl(`/documents/verify?number=${encodeURIComponent(number)}&code=${encodeURIComponent('0000-0000-0000')}`), { token: boss.token });
      check('yanlış kod → geçersiz (ayrıntı verilmez)', w.status === 200 && w.body.valid === false && !w.body.number, jsonOf(w));
    }
  }

  // ------------------------------------------------------------------ arama, rapor, CSV
  section('Arama (Türkçe harf katlama), rapor ve CSV');
  const search = async (q) => (await call('GET', wsUrl(`/requests/search?q=${encodeURIComponent(q)}`), A)).body;
  for (const q of ['isil', 'ISIL', 'ışıl', 'ozturk', 'sirket', 'ŞİRKET', 'istanbul', 'isik gida', number.toLowerCase()])
    check(`"${q}" araması talebi bulur`, (await search(q)).total === 1, (await search(q)).total);
  check('ilgisiz arama sonuç vermez', (await search('xyz')).total === 0);
  const report = await call('GET', wsUrl('/reports/summary'), A);
  check('rapor özeti toplam ve durum verir', report.status === 200 && report.body.total >= 1 && report.body.byStatus.approved >= 1, jsonOf(report));
  const csv = await download(A.token, wsUrl('/reports/requests.csv'));
  const csvText = csv.bytes.toString('utf8');
  check('talepler CSV (UTF-8, ; ayırıcı, numara içerir)', csv.status === 200 && csvText.includes(number) && csvText.includes(';'), csv.status);
  check('CSV Türkçe karakterleri bozmaz', csvText.includes('Işıl Öztürk'), csvText.slice(0, 200));
  const entries = await call('GET', wsUrl('/accounting/entries'), A);
  check('onaylanan fişli masraf muhasebe kuyruğuna düştü', entries.status === 200 && entries.body.length >= 1, jsonOf(entries));

  // ------------------------------------------------------------------ listeler + izin bakiyesi
  section('Dinamik liste + izin bakiyesi (rezerve/iade)');
  const list = await call('POST', wsUrl('/lists'), { ...A, body: { name: 'Departmanlar', items: ['Satış', 'Depo', 'Muhasebe'] } });
  check('liste oluşturuldu', list.status === 201, jsonOf(list));
  const leaveDef = {
    schema: 1, prefix: 'IZG', summaryFields: ['start', 'end', 'dept'],
    fields: [
      { key: 'start', label: 'Başlangıç', type: 'date', required: true },
      { key: 'end', label: 'Bitiş', type: 'date', required: true, notBefore: 'start' },
      { key: 'days', label: 'Gün', type: 'number', computed: { op: 'work_days', from: 'start', to: 'end' } },
      { key: 'dept', label: 'Birim', type: 'select', required: true, listId: list.body.id },
    ],
    steps: [{ id: 's1', name: 'Yönetici onayı', approvers: { type: 'manager' } }],
    completion: { leave: { enabled: true, daysField: 'days', startField: 'start' } },
  };
  const leaveApp = await mkApp('Bakiyeli izin', leaveDef, [worker.id]);
  const grant = await call('POST', wsUrl('/leave/entries'), { ...A, body: { userId: worker.id, year: 2027, kind: 'entitlement', days: 5, note: 'Kıdem' } });
  check('izin hakkı yazıldı', grant.status === 200 || grant.status === 201, jsonOf(grant));
  const askLeave = (start, end, dept = 'Satış') => call('POST', `/api/apps/${leaveApp}/requests`, { token: worker.token, body: { data: { start, end, dept } } });
  const wrongDept = await askLeave('2027-03-01', '2027-03-02', 'Uydurma birim');
  check('listede olmayan değer 422', wrongDept.status === 422, jsonOf(wrongDept));
  const tooLong = await askLeave('2027-03-01', '2027-03-12');          // 10 iş günü > 5
  check('bakiyeyi aşan izin reddedilir (422, sayılarla)', tooLong.status === 422, jsonOf(tooLong));
  const leave = await askLeave('2027-03-01', '2027-03-03');            // 3 iş günü (Pzt–Çar)
  check('bakiye içinde izin kabul edildi', leave.status === 201, jsonOf(leave));
  const bal = async () => (await call('GET', wsUrl('/leave/me?year=2027'), { token: worker.token })).body;
  check('gönderilince 3 gün rezerve edildi (kalan 2)', (await bal()).used === 3 && (await bal()).available === 2, jsonOf(await bal()));
  const cancel = await call('POST', `/api/requests/${leave.body.summary.id}/cancel`, { token: worker.token });
  check('iptal edildi', cancel.status === 200, jsonOf(cancel));
  check('iptalde gün iade edildi (kalan 5)', (await bal()).used === 0 && (await bal()).available === 5, jsonOf(await bal()));

  // ------------------------------------------------------------------ saklama + KVKK
  section('Saklama (önizleme) ve kişisel veri dışa aktarma');
  const noPolicy = await call('POST', wsUrl('/retention/preview'), A);
  check('saklama süresi tanımlı değilken önizleme açıklayıcı hata verir', noPolicy.status >= 400 && noPolicy.status < 500, jsonOf(noPolicy));
  await call('PUT', wsUrl('/retention'), { ...A, body: { closedRequestMonths: 12, autoPurge: false } });
  const preview = await call('POST', wsUrl('/retention/preview'), A);
  check('önizleme: yeni sonuçlanmış talepler silinecek listede değil', preview.status === 200 && preview.body.eligible === 0, jsonOf(preview));
  const exp = await call('GET', wsUrl(`/members/${worker.id}/data-export`), { token: worker.token });
  check('kişi kendi verisini dışa aktarır (talepler, dosyalar, izin)', exp.status === 200 && exp.body.requests.length >= 1 && exp.body.files.length === 1 && Array.isArray(exp.body.leave), jsonOf(exp));
  check('dışa aktarımda parola özeti yok', !/pbkdf2|passwordHash/i.test(JSON.stringify(exp.body)));
  const other = await call('GET', wsUrl(`/members/${boss.id}/data-export`), { token: worker.token });
  check('başkasının verisi 403', other.status === 403, other.status);
  const audit = await call('GET', wsUrl('/audit?pageSize=50'), A);
  check('denetim kaydı işlemleri listeler', audit.status === 200 && audit.body.total > 5, jsonOf(audit));
} catch (e) {
  check('beklenmeyen hata', false, String(e?.stack ?? e));
}
finish();
