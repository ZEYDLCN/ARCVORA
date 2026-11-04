// API uçtan uca yolculuğu: kayıt → proje → gereksinim çıkarımı → mimari taslak → kısmi kabul → değişiklik talebi
// → görünümler → dışa aktarma → yetki sınırları. Çalışan bir API ve PostgreSQL gerekir.
//   ARCVORA_API_URL=http://127.0.0.1:5217 EXPECT_PROVIDER=heuristic|local node scripts/e2e/api-journey.mjs
import { API_URL, call, check, fatal, finish, section, waitHealth, waitJob } from './lib.mjs';

const EXPECT = process.env.EXPECT_PROVIDER ?? 'heuristic'; // heuristic | local
const IDEA = 'Çalışanlar masraf girer, yönetici onaylar. Fiş fotoğrafı yüklenir.';
const run = Date.now();

console.log(`ARCVORA API uçtan uca testi → ${API_URL} (beklenen sağlayıcı: ${EXPECT})`);

section('Sağlık');
if (!(await waitHealth(60_000))) fatal('API açılmadı', API_URL);
check('GET /health 200', true);

section('Kimlik ve yetki');
const email = `e2e-${run}@example.com`;
const reg = await call('POST', '/api/auth/register', { body: { email, password: 'Parola-12345', displayName: 'E2E Kullanıcı' } });
check('kayıt 201 + token', reg.status === 201 && !!reg.body?.token, reg);
if (reg.status !== 201) fatal('kayıt olmadan devam edilemez', reg);
const token = reg.body.token;
const workspaceId = reg.body.workspaces?.[0]?.id;
check('kayıtla birlikte çalışma alanı oluştu', !!workspaceId);

const noAuth = await call('GET', '/api/projects');
check('tokensız /projects → 401', noAuth.status === 401, noAuth.status);
const badLogin = await call('POST', '/api/auth/login', { body: { email, password: 'yanlis-parola' } });
check('yanlış parola → 401', badLogin.status === 401, badLogin.status);
const login = await call('POST', '/api/auth/login', { body: { email, password: 'Parola-12345' } });
check('doğru parola ile giriş 200', login.status === 200 && !!login.body?.token, login.status);

section('Proje');
const profiles = await call('GET', '/api/profiles');
check('teknoloji profilleri listelenir', profiles.status === 200 && profiles.body.length > 0, profiles.status);
const profileId = (profiles.body.find((p) => p.selectable) ?? profiles.body[0]).id;
const created = await call('POST', '/api/projects', {
  token, body: { workspaceId, name: 'Masraf yönetimi', description: 'E2E', techProfileId: profileId, ideaText: IDEA },
});
check('proje 201 ve v1 var', created.status === 201 && created.body?.head?.number === 1, created);
if (created.status !== 201) fatal('proje oluşturulamadı', created);
const pid = created.body.summary.id;
const unknownProfile = await call('POST', '/api/projects', { token, body: { workspaceId, name: 'x', techProfileId: 'yok' } });
check('bilinmeyen profil → 422', unknownProfile.status === 422, unknownProfile.status);

section('Gereksinim çıkarımı (iş kuyruğu)');
const j1 = await call('POST', `/api/projects/${pid}/analyses`, { token, headers: { 'Idempotency-Key': `a-${run}` } });
check('analiz işi 202', j1.status === 202, j1);
const j1again = await call('POST', `/api/projects/${pid}/analyses`, { token, headers: { 'Idempotency-Key': `a-${run}` } });
check('aynı Idempotency-Key aynı işi döndürür', j1again.body?.id === j1.body?.id, [j1.body?.id, j1again.body?.id]);
const job1 = await waitJob(token, j1.body.id);
check('analiz işi başarıyla bitti', job1.status === 'succeeded', job1);
if (job1.status !== 'succeeded') fatal('analiz başarısız', job1);

const props1 = await call('GET', `/api/projects/${pid}/proposals?status=pending`, { token });
const prop1 = props1.body?.[0];
check('bekleyen bir öneri oluştu', props1.body?.length === 1, props1.body);
check(`sağlayıcı beklenen türde (${EXPECT})`, EXPECT === 'local' ? String(prop1?.provider).startsWith('local:') && prop1.isAi === true : prop1?.provider === 'heuristic' && prop1.isAi === false, prop1);
check('öneri henüz sürüm değil (taslak): başlangıç sürümü hâlâ 1', (await call('GET', `/api/projects/${pid}`, { token })).body?.summary?.headNumber === 1);

const acc1 = await call('POST', `/api/projects/${pid}/proposals/${prop1.id}/accept`, { token, body: {} });
check('analiz önerisi kabul → v2', acc1.status === 200 && acc1.body?.version?.number === 2, acc1);
const acc1again = await call('POST', `/api/projects/${pid}/proposals/${prop1.id}/accept`, { token, body: {} });
check('aynı öneriyi tekrar kabul yeni sürüm üretmez (idempotent)', acc1again.status === 200 && acc1again.body?.version?.number === 2, acc1again);

const afterReq = await call('GET', `/api/projects/${pid}`, { token });
const reqs = afterReq.body?.model?.requirements ?? [];
check('gereksinimler modele girdi', reqs.length > 0, reqs.length);
check('her gereksinimin kaynağı var (origin ≠ görünmez)', reqs.every((r) => r.origin && r.sourceRefs?.length > 0), reqs.map((r) => [r.id, r.origin, r.sourceRefs?.length]));

section('Mimari taslak ve kısmi kabul');
const j2 = await call('POST', `/api/projects/${pid}/architecture-proposals`, { token, headers: { 'Idempotency-Key': `m-${run}` } });
check('mimari işi 202', j2.status === 202, j2);
const job2 = await waitJob(token, j2.body.id);
check('mimari işi başarıyla bitti', job2.status === 'succeeded', job2);
if (job2.status !== 'succeeded') fatal('mimari taslak üretilemedi', job2);
const det2 = await call('GET', `/api/projects/${pid}/proposals/${job2.proposalId}`, { token });
const groups = det2.body?.groups ?? [];
check('değişiklik grupları var', groups.length > 0, det2.body);
check('etki analizi kalemleri var', (det2.body?.impact?.items?.length ?? 0) > 0, det2.body?.impact);

const root = groups.find((g) => !g.dependsOn?.length);
const dependent = groups.find((g) => g.dependsOn?.includes(root?.id));
check('bağımlılığı olan ve olmayan gruplar var', !!root && !!dependent, groups);
const alone = await call('POST', `/api/projects/${pid}/proposals/${job2.proposalId}/accept`, { token, body: { groupIds: [dependent.id] } });
check('bağımlı grup kökü olmadan tek başına kabul edilemez (422 invalid_selection)',
  alone.status === 422 && alone.body?.code === 'invalid_selection' && alone.body.details?.length > 0, alone);
check('reddedilen kabul sürüm üretmedi', (await call('GET', `/api/projects/${pid}`, { token })).body?.summary?.headNumber === 2);
const partial = await call('POST', `/api/projects/${pid}/proposals/${job2.proposalId}/accept`, { token, body: { groupIds: [root.id] } });
check('yalnız kök grup kabul → v3 ("kısmi kabul" etiketli)', partial.status === 200 && partial.body?.version?.number === 3 && /kısmi/.test(partial.body.version.summary), partial);
const afterArch = await call('GET', `/api/projects/${pid}`, { token });
const head = afterArch.body?.summary?.headNumber;
check('model sürümü 3', head === 3, head);
const comps = afterArch.body?.model?.components ?? [];
check('yalnız seçilen grubun bileşenleri modele girdi', comps.length > 0, comps.length);
check('kabul edilen nesneler "confirmed" olur ama kaynak bilgisi kaybolmaz: origin system_proposal kalır (kullanıcı ifadesi sayılmaz)',
  comps.every((c) => c.reviewStatus === 'confirmed' && c.origin === 'system_proposal'), comps.map((c) => [c.id, c.origin, c.reviewStatus]));

section('Görünümler ve bulgular');
const views = await call('GET', `/api/projects/${pid}/versions/${head}/views`, { token });
check('görünümler 200', views.status === 200 && views.body.views?.length > 0, views.status);
check('Mermaid metinleri dolu ve yönerge içermiyor', views.body.views?.every((v) => v.mermaid?.trim() && !v.mermaid.includes('%%{')), views.body.views?.map((v) => v.kind));
const findings = await call('GET', `/api/projects/${pid}/versions/${head}/findings`, { token });
check('bulgular (doğrulama) 200', findings.status === 200, findings.status);

section('Değişiklik talebi ve sürümler');
const crText = 'Yönetici onayından sonra muhasebe sistemine bildirim gitsin.';
const j3 = await call('POST', `/api/projects/${pid}/change-requests`, { token, body: { requestText: crText }, headers: { 'Idempotency-Key': `c-${run}` } });
check('değişiklik işi 202', j3.status === 202, j3);
const job3 = await waitJob(token, j3.body.id);
check('değişiklik işi bitti', job3.status === 'succeeded', job3);
if (job3.status !== 'succeeded') fatal('değişiklik önerisi üretilemedi', job3);
const det3 = await call('GET', `/api/projects/${pid}/proposals/${job3.proposalId}`, { token });
check('değişiklik önerisi etki kalemleri içerir', (det3.body?.impact?.items?.length ?? 0) > 0, det3.body?.impact);
const accCr = await call('POST', `/api/projects/${pid}/proposals/${job3.proposalId}/accept`, { token, body: {} });
check('değişiklik önerisi kabul → v4', accCr.status === 200 && accCr.body?.version?.number === 4, accCr);

// Bayat öneri: v4'e dayalı bir öneri üretilir, sonra geçmiş bir sürüme dönülür → kabul sessizce uygulanmamalı.
const j4 = await call('POST', `/api/projects/${pid}/change-requests`, { token, body: { requestText: 'Fiş fotoğrafları 5 yıl saklansın.' }, headers: { 'Idempotency-Key': `c2-${run}` } });
const job4 = await waitJob(token, j4.body.id);
check('ikinci değişiklik işi bitti', job4.status === 'succeeded', job4);
const revert = await call('POST', `/api/projects/${pid}/versions/2/revert`, { token, body: { baseVersion: 4 } });
check("v2'ye dönüş YENİ sürüm (v5) üretir", revert.status === 200 && (revert.body?.version?.number ?? revert.body?.number) === 5, revert);
const versions = await call('GET', `/api/projects/${pid}/versions`, { token });
check('geçmiş silinmedi: v1..v5 hepsi durur', versions.body?.map((v) => v.number).join() === '5,4,3,2,1', versions.body?.map((v) => v.number));
const stale = await call('POST', `/api/projects/${pid}/proposals/${job4.proposalId}/accept`, { token, body: {} });
check('eski sürüme dayalı öneri kabulü 409 reassessment_required', stale.status === 409 && stale.body?.code === 'reassessment_required', stale);
const rej = await call('POST', `/api/projects/${pid}/proposals/${job4.proposalId}/reject`, { token, body: { note: 'e2e: bayat' } });
check('bayat öneri reddedilebilir', rej.status === 200, rej);
check('ret sürüm üretmedi (head v5)', (await call('GET', `/api/projects/${pid}`, { token })).body.summary.headNumber === 5);

section('Dışa aktarma');
const exp = await call('POST', `/api/projects/${pid}/exports`, { token, body: { format: 'markdown' } });
check('dışa aktarma 201', exp.status === 201, exp);
if (exp.status === 201) {
  check('çıktı sürüm/profil/hash ile kaydedildi', !!exp.body.modelHash && !!exp.body.profileVersion && !!exp.body.generatorVersion, exp.body);
  const dl = await call('GET', `/api/projects/${pid}/exports/${exp.body.id}/download`, { token, raw: true });
  check('indirme 200 ve Markdown içeriği dolu', dl.status === 200 && dl.body.length > 500, dl.body.slice(0, 200));
  check('indirilen dosya model adını ve bileşenleri içerir', dl.body.includes('Masraf yönetimi'), dl.body.slice(0, 200));
}

section('Çalışma alanı sınırları');
const other = await call('POST', '/api/auth/register', { body: { email: `other-${run}@example.com`, password: 'Parola-12345', displayName: 'Başka' } });
const otherToken = other.body.token;
const foreign = await call('GET', `/api/projects/${pid}`, { token: otherToken });
check('başka çalışma alanından proje → 404 (varlığı sızdırılmaz)', foreign.status === 404, foreign.status);
const foreignJob = await call('GET', `/api/jobs/${j1.body.id}`, { token: otherToken });
check('başka kullanıcının işi → 404', foreignJob.status === 404, foreignJob.status);
const foreignAccept = await call('POST', `/api/projects/${pid}/proposals/${job2.proposalId}/accept`, { token: otherToken, body: {} });
check('başkasının önerisini kabul → 404', foreignAccept.status === 404, foreignAccept.status);

section('Örnek proje');
const sample = await call('POST', '/api/samples/expense-management', { token, body: { workspaceId } });
check('örnek proje 201', sample.status === 201, sample.status);
check('örnek proje birçok bileşen ve karar içerir', (sample.body?.model?.components?.length ?? 0) >= 4 && (sample.body?.model?.decisions?.length ?? 0) >= 2, sample.body?.model && Object.keys(sample.body.model));

finish();
