// Yavaş yerel model sırasında iş iptali: iptal edilen iş öneri üretmez, model değişmez.
// Gecikmeli sahte modele (MOCK_LLM_DELAY_MS=4000) bağlı, Ai__Provider=local ile çalışan bir API gerekir.
//   ARCVORA_API_URL=http://127.0.0.1:5218 node scripts/e2e/local-cancel.mjs
import { call, check, fatal, finish, section, sleep, waitHealth, waitJob } from './lib.mjs';
import { head, newProject, newUser, withAcceptedRequirements } from './fixtures.mjs';

if (!(await waitHealth(60_000))) fatal('API açılmadı');
const user = await newUser('cancel');
const pid = await newProject(user);
const v = await withAcceptedRequirements(user, pid);

section('Çalışan işi iptal etmek');
const started = await call('POST', `/api/projects/${pid}/architecture-proposals`, { token: user.token });
check('iş kuyruğa alındı (202)', started.status === 202, started);
await sleep(1200); // worker işi alıp modele istek göndersin
const mid = (await call('GET', `/api/jobs/${started.body.id}`, { token: user.token })).body;
check('iş şu an yürüyor veya kuyrukta', ['queued', 'running'].includes(mid.status), mid.status);
const cancel = await call('POST', `/api/jobs/${started.body.id}/cancel`, { token: user.token });
check('iptal isteği 200', cancel.status === 200, cancel);
const done = await waitJob(user.token, started.body.id, 60_000);
check('iş "cancelled" ile biter', done.status === 'cancelled', done);
check('iptal edilen iş öneri üretmedi', !done.proposalId, done.proposalId);
const pending = (await call('GET', `/api/projects/${pid}/proposals?status=pending`, { token: user.token })).body;
check('bekleyen mimari önerisi yok', !pending.some((p) => p.kind === 'architecture'), pending.map((p) => p.kind));
check('model sürümü değişmedi', (await head(user, pid)) === v, await head(user, pid));

section('İptal sonrası yeni iş çalışır');
const again = await waitJob(user.token, (await call('POST', `/api/projects/${pid}/architecture-proposals`, { token: user.token })).body.id, 90_000);
check('iptalden sonra yeni mimari işi başarılı olur', again.status === 'succeeded', again);

finish();
