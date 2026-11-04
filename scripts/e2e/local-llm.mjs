// `Ai__Provider=local` yolunun uçtan uca denetimi: ARCVORA, sahte yerel model sunucusuna (mock-llm.mjs) doğru biçimde konuşuyor mu?
//   ARCVORA_API_URL=http://127.0.0.1:5218 MOCK_URL=http://127.0.0.1:11599 MOCK_MODEL=qwen-mock node scripts/e2e/local-llm.mjs
//   MOCK_LLM_FAULT=repair ile başlatılmış sunucuya karşı EXPECT_REPAIR=1 verin: ilk mimari yanıtı bozuk gelir, ARCVORA onarım turu açmalıdır.
// GERÇEK sunucuya karşı (API Ai__Provider=local ile gerçek Ollama'ya bağlıyken): MOCK_URL vermeyin/erişilemez bırakın ve modeli verin:
//   ARCVORA_API_URL=http://localhost:5217 LOCAL_MODEL=<etiket> E2E_JOB_TIMEOUT_MS=1800000 node scripts/e2e/local-llm.mjs
//   Bu modda istek günlüğü (/__requests) olmadığı için "sunucuya giden istekler" ve onarım denetimleri atlanır; sağlayıcı etiketi,
//   token kaydı ve taslağın doğrulamadan geçip öneri olarak listelenmesi sınanır.
import { call, check, fatal, finish, section, waitHealth, waitJob } from './lib.mjs';

const MOCK = (process.env.MOCK_URL ?? 'http://127.0.0.1:11599').replace(/\/$/, '');
const MODEL = process.env.LOCAL_MODEL ?? process.env.MOCK_MODEL ?? 'qwen-mock';
const JOB_MS = Number(process.env.E2E_JOB_TIMEOUT_MS ?? 180_000);
const EXPECT_REPAIR = process.env.EXPECT_REPAIR === '1';
const run = Date.now();

if (!(await waitHealth(60_000))) fatal('API açılmadı');
// Sahte sunucuda istek günlüğü var; gerçek sunucuda yok → günlük denetimleri atlanır.
const fetchLog = async () => {
  try {
    const r = await fetch(`${MOCK}/__requests`, { signal: AbortSignal.timeout(3000) });
    return r.ok ? await r.json() : null;
  } catch {
    return null;
  }
};
const initialLog = await fetchLog();
const before = initialLog?.length ?? 0;

section('Yerel model üzerinden gereksinim çıkarımı ve mimari taslak');
const reg = await call('POST', '/api/auth/register', { body: { email: `local-${run}@example.com`, password: 'Parola-12345', displayName: 'Yerel' } });
if (reg.status !== 201) fatal('kayıt başarısız', reg);
const token = reg.body.token;
const prof = (await call('GET', '/api/profiles')).body.find((p) => p.selectable);
const p = await call('POST', '/api/projects', {
  token, body: { workspaceId: reg.body.workspaces[0].id, name: 'Yerel model denemesi', techProfileId: prof.id, ideaText: 'Çalışanlar masraf girer, yönetici onaylar. Fiş fotoğrafı yüklenir.' },
});
const pid = p.body.summary.id;

const a = await waitJob(token, (await call('POST', `/api/projects/${pid}/analyses`, { token })).body.id, JOB_MS);
check('çıkarım işi başarılı', a.status === 'succeeded', a);
check('çıkarım, yerel model token kullanımını kaydetti (bütçe uygulanır)', a.tokensUsed > 0, a.tokensUsed);
await call('POST', `/api/projects/${pid}/proposals/${a.proposalId}/accept`, { token, body: {} });

const m = await waitJob(token, (await call('POST', `/api/projects/${pid}/architecture-proposals`, { token })).body.id, JOB_MS);
check('mimari işi başarılı', m.status === 'succeeded', m);
const list = (await call('GET', `/api/projects/${pid}/proposals`, { token })).body;
const arch = list.find((x) => x.kind === 'architecture');
check('öneri "local:<model>" sağlayıcısıyla etiketli ve yapay zekâ sayılıyor', arch?.provider === `local:${MODEL}` && arch.isAi === true, arch);
check('buluşsal üretici notu yok (AI taslağı olarak gösterilir)', !/anahtar sözcük/.test(arch?.note ?? ''), arch?.note);

section('Model sunucusuna giden istekler');
const fullLog = await fetchLog();
if (fullLog === null) {
  console.log('  (istek günlüğü yok: gerçek bir sunucu; istek biçimi ve onarım denetimleri atlandı)');
  finish();
}
const log = fullLog.slice(before);
check('çıkarım ve mimari istekleri sunucuya ulaştı', log.some((r) => r.task === 'extract') && log.some((r) => r.task === 'architecture'), log.map((r) => r.task));
check('model adı yapılandırmadakiyle aynı', log.every((r) => r.model === MODEL), log.map((r) => r.model));
check('stream=false (yanıt tek parça)', log.every((r) => r.stream === false), log.map((r) => r.stream));
check('response_format=json_schema ve strict=true (şema kısıtlı çıktı)', log.every((r) => r.responseFormat === 'json_schema' && r.strict === true && r.schemaType === 'object'), log[0]);
check('çıktı sınırı yerel üst sınırı (8192) aşmıyor', log.every((r) => r.maxTokens > 0 && r.maxTokens <= 8192), log.map((r) => r.maxTokens));
check('istek Content-Length ile gidiyor (chunked değil; llama.cpp/Ollama uyumu)', log.every((r) => r.hasContentLength), log[0]);
check('anahtarsız yerel sunucuya Authorization başlığı gönderilmiyor', log.every((r) => r.authorization === 'yok'), log[0]);

section('Onarım turu');
if (EXPECT_REPAIR) {
  check('sunucu bozuk taslaktan sonra onarım isteği aldı', log.some((r) => r.task === 'repair'), log.map((r) => r.task));
  check('öneri onarım hakkı kullandığını belirtiyor (repairsUsed=1)', arch?.repairsUsed === 1, arch);
  const det = await call('GET', `/api/projects/${pid}/proposals/${arch.id}`, { token });
  check('onarım sonrası taslak doğrulamadan geçti: olmayan bileşene bağlantı yok', !JSON.stringify(det.body.patch).includes('CMP-999'), 'CMP-999 hâlâ var');
} else {
  check('geçerli taslakta onarım turu açılmadı', !log.some((r) => r.task === 'repair') && arch?.repairsUsed === 0, [log.map((r) => r.task), arch?.repairsUsed]);
}

finish();
