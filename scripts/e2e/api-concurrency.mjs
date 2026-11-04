// Eşzamanlılık: aynı kabulün paralel tekrarı tek sürüm üretmeli; aynı temele dayalı iki düzenleme çakışmayı bildirmeli.
//   ARCVORA_API_URL=http://127.0.0.1:5217 node scripts/e2e/api-concurrency.mjs
import { call, check, fatal, finish, section, waitHealth, waitJob } from './lib.mjs';
import { head, newProject, newUser, withAcceptedRequirements } from './fixtures.mjs';

if (!(await waitHealth(60_000))) fatal('API açılmadı');
const user = await newUser('conc');
const pid = await newProject(user);
const v = await withAcceptedRequirements(user, pid);

section('Aynı öneriyi aynı anda kabul etmek');
const job = await waitJob(user.token, (await call('POST', `/api/projects/${pid}/architecture-proposals`, { token: user.token })).body.id);
if (job.status !== 'succeeded') fatal('mimari taslak üretilemedi', job);
const N = 6;
const results = await Promise.all(Array.from({ length: N }, () =>
  call('POST', `/api/projects/${pid}/proposals/${job.proposalId}/accept`, { token: user.token, body: {} })));
console.log('  durum kodları:', results.map((r) => r.status).join(' '));
check('hiçbir istek 5xx vermedi', results.every((r) => r.status < 500), results.map((r) => r.status));
check('en az bir istek başarılı (200)', results.some((r) => r.status === 200));
check(`başarılı yanıtların hepsi AYNI sürümü döndürür (v${v + 1})`,
  results.filter((r) => r.status === 200).every((r) => r.body.version.number === v + 1), results.map((r) => r.body?.version?.number));
check(`proje tek sürüm ilerledi (v${v + 1}); ${N} paralel kabul ${N} sürüm üretmedi`, (await head(user, pid)) === v + 1, await head(user, pid));
const versions = (await call('GET', `/api/projects/${pid}/versions`, { token: user.token })).body;
check('sürüm numaraları boşluksuz ve tekrarsız', versions.map((x) => x.number).sort((a, b) => a - b).join() === Array.from({ length: v + 1 }, (_, i) => i + 1).join(), versions.map((x) => x.number));

section('Aynı temele dayalı iki düzenleme');
const model = (await call('GET', `/api/projects/${pid}`, { token: user.token })).body.model;
const reqId = model.requirements[0].id;
const base = await head(user, pid);
const edits = await Promise.all([
  call('PATCH', `/api/projects/${pid}/requirements/${reqId}`, { token: user.token, body: { baseVersion: base, text: 'Birinci düzenleme: masraf girişi.' } }),
  call('PATCH', `/api/projects/${pid}/requirements/${reqId}`, { token: user.token, body: { baseVersion: base, text: 'İkinci düzenleme: masraf girişi.' } }),
]);
console.log('  durum kodları:', edits.map((r) => r.status).join(' '), edits.map((r) => r.body?.code ?? '').join(' '));
check('hiçbir düzenleme 5xx vermedi', edits.every((r) => r.status < 500), edits.map((r) => r.status));
check('tam olarak biri başarılı, diğeri çakışma bildirir (200 + 409)', edits.filter((r) => r.status === 200).length === 1 && edits.filter((r) => r.status === 409).length === 1, edits.map((r) => [r.status, r.body?.code]));
check('çakışma sessizce ezmedi: yalnız bir yeni sürüm oluştu', (await head(user, pid)) === base + 1, await head(user, pid));
const stale = await call('PATCH', `/api/projects/${pid}/requirements/${reqId}`, { token: user.token, body: { baseVersion: base, text: 'Geç kalan düzenleme.' } });
check('eski sürüme dayalı düzenleme → 409', stale.status === 409, stale);
check('çakışma yanıtı bir hata kodu içerir', typeof stale.body?.code === 'string' && stale.body.code.length > 0, stale.body);

finish();
