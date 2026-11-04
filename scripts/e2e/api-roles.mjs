// Rol ve yetki uçtan uca denetimi: okuyucu / düzenleyici / kabul yetkisi / yönetici / üye olmayan.
//   ARCVORA_API_URL=http://127.0.0.1:5217 node scripts/e2e/api-roles.mjs
import { call, check, fatal, finish, section, waitHealth } from './lib.mjs';
import { head, newProject, newUser, withAcceptedRequirements } from './fixtures.mjs';

if (!(await waitHealth(60_000))) fatal('API açılmadı');

const admin = await newUser('admin');
const reader = await newUser('reader');
const editor = await newUser('editor');
const outsider = await newUser('outsider');
const pid = await newProject(admin);
await withAcceptedRequirements(admin, pid);
const add = (email, role, canAccept) => call('POST', `/api/workspaces/${admin.workspaceId}/members`, { token: admin.token, body: { email, role, canAccept } });

section('Üye yönetimi');
check('yönetici okuyucu ekler', (await add(reader.email, 'reader')).status === 200);
check('yönetici düzenleyici ekler (kabul yetkisiz)', (await add(editor.email, 'editor', false)).status === 200);
const badRole = await add(reader.email, 'sahip');
check('geçersiz rol → 422 invalid_role', badRole.status === 422 && badRole.body?.code === 'invalid_role', badRole);
const ghost = await add('yok-boyle-biri@example.com', 'reader');
check('kayıtsız e-posta → 422 user_not_found', ghost.status === 422 && ghost.body?.code === 'user_not_found', ghost);
const members = await call('GET', `/api/workspaces/${admin.workspaceId}/members`, { token: admin.token });
check('üye listesi 3 kişi (yönetici, okuyucu, düzenleyici)', members.status === 200 && members.body.length === 3, members.body);
const demote = await add(admin.email, 'editor');
check('son yönetici düşürülemez → 409 last_admin', demote.status === 409 && demote.body?.code === 'last_admin', demote);
const readerAdds = await call('POST', `/api/workspaces/${admin.workspaceId}/members`, { token: reader.token, body: { email: outsider.email, role: 'reader' } });
check('okuyucu üye ekleyemez → 403', readerAdds.status === 403, readerAdds.status);

section('Okuyucu: yalnız okur');
check('okuyucu projeyi görür (200)', (await call('GET', `/api/projects/${pid}`, { token: reader.token })).status === 200);
const rSource = await call('POST', `/api/projects/${pid}/sources`, { token: reader.token, body: { title: 'x', text: 'Yeni gereksinim metni burada.' } });
check('okuyucu kaynak ekleyemez → 403', rSource.status === 403, rSource.status);
const rJob = await call('POST', `/api/projects/${pid}/architecture-proposals`, { token: reader.token });
check('okuyucu mimari işi başlatamaz → 403', rJob.status === 403, rJob.status);

section('Üye olmayan');
check('üye olmayan projeyi göremez → 404', (await call('GET', `/api/projects/${pid}`, { token: outsider.token })).status === 404);
check('üye olmayan görünümlere erişemez → 404', (await call('GET', `/api/projects/${pid}/versions/1/views`, { token: outsider.token })).status === 404);

section('Düzenleyici: düzenler ama kabul yetkisi ayrı');
const eJob = await call('POST', `/api/projects/${pid}/architecture-proposals`, { token: editor.token });
check('düzenleyici mimari işi başlatabilir (202)', eJob.status === 202, eJob);
const props = await call('GET', `/api/projects/${pid}/proposals?status=pending`, { token: editor.token });
check('düzenleyici önerileri listeleyebilir', props.status === 200, props.status);

section('Kabul yetkisi ayrı bir izindir');
const pending = (await call('GET', `/api/projects/${pid}/proposals?status=pending`, { token: admin.token })).body;
let arch = pending.find((p) => p.kind === 'architecture');
if (!arch) {
  const { waitJob } = await import('./lib.mjs');
  const job = await waitJob(editor.token, eJob.body.id);
  arch = { id: job.proposalId };
}
const noAccept = await call('POST', `/api/projects/${pid}/proposals/${arch.id}/accept`, { token: editor.token, body: {} });
check('kabul yetkisiz düzenleyici öneriyi kabul edemez → 403', noAccept.status === 403, noAccept);
const noReject = await call('POST', `/api/projects/${pid}/proposals/${arch.id}/reject`, { token: editor.token, body: {} });
check('kabul yetkisiz düzenleyici öneriyi reddedemez → 403', noReject.status === 403, noReject);
const asReader = await call('POST', `/api/projects/${pid}/proposals/${arch.id}/accept`, { token: reader.token, body: {} });
check('okuyucu kabul edemez → 403', asReader.status === 403, asReader.status);
check('reddedilen denemeler sürüm üretmedi (v2)', (await head(admin, pid)) === 2);
check('yönetici düzenleyiciye kabul yetkisi verir', (await add(editor.email, 'editor', true)).status === 200);
const yesAccept = await call('POST', `/api/projects/${pid}/proposals/${arch.id}/accept`, { token: editor.token, body: {} });
check('yetki verilince düzenleyici kabul edebilir → v3', yesAccept.status === 200 && yesAccept.body?.version?.number === 3, yesAccept);

finish();
