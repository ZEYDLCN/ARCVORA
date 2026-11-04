// Model zaman aşımına uğrarsa iş "failed" olur, son kabul edilmiş model korunur ve açıklayıcı hata görünür.
// Gerekir: Ai__Provider=local, Ai__TimeoutSeconds=2 ile çalışan API + MOCK_LLM_DELAY_MS=6000 ile çalışan sahte model.
//   ARCVORA_API_URL=http://127.0.0.1:5287 node scripts/e2e/local-timeout.mjs
import { call, check, fatal, finish, section, waitHealth, waitJob } from './lib.mjs';
import { head, newProject, newUser } from './fixtures.mjs';

if (!(await waitHealth(60_000))) fatal('API açılmadı');
const user = await newUser('timeout');
const pid = await newProject(user);
const v = await head(user, pid);

section('Zaman aşımı');
const started = await call('POST', `/api/projects/${pid}/analyses`, { token: user.token });
const job = await waitJob(user.token, started.body.id, 120_000);
console.log('  iş:', JSON.stringify({ status: job.status, errorCode: job.errorCode, attempts: job.attempts, maxAttempts: job.maxAttempts }));
check('iş "failed" ile biter (sonsuza kadar asılı kalmaz)', job.status === 'failed', job);
check('hata kodu zaman aşımını belirtir', /timeout|zaman/i.test(`${job.errorCode} ${job.error}`), [job.errorCode, job.error]);
check('hata açıklaması kullanıcıya gösterilebilir (boş değil)', typeof job.error === 'string' && job.error.length > 10, job.error);
check('başarısız iş öneri üretmedi', !job.proposalId, job.proposalId);
check('model sürümü değişmedi', (await head(user, pid)) === v);
const props = (await call('GET', `/api/projects/${pid}/proposals`, { token: user.token })).body;
check('bekleyen öneri yok', props.length === 0, props);

finish();
