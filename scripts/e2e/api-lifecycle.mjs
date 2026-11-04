// Yaşam döngüsü: düzenleme doğrulaması, geçersiz yama, açık soru, iş iptali, proje silme, denetim kaydı.
//   ARCVORA_API_URL=http://127.0.0.1:5217 node scripts/e2e/api-lifecycle.mjs
import { call, check, fatal, finish, section, waitHealth, waitJob } from './lib.mjs';
import { head, newProject, newUser, withAcceptedRequirements } from './fixtures.mjs';

if (!(await waitHealth(60_000))) fatal('API açılmadı');
const user = await newUser('life');
const pid = await newProject(user);
await withAcceptedRequirements(user, pid);
const getModel = async () => (await call('GET', `/api/projects/${pid}`, { token: user.token })).body.model;
const reqId = (await getModel()).requirements[0].id;

section('Gereksinim düzenleme doğrulaması');
const base = await head(user, pid);
const empty = await call('PATCH', `/api/projects/${pid}/requirements/${reqId}`, { token: user.token, body: { baseVersion: base, text: '   ' } });
check('boş metin → 422 invalid_text', empty.status === 422 && empty.body?.code === 'invalid_text', empty);
const tooLong = await call('PATCH', `/api/projects/${pid}/requirements/${reqId}`, { token: user.token, body: { baseVersion: base, text: 'a'.repeat(2001) } });
check('2000 karakterden uzun metin → 422', tooLong.status === 422, tooLong.status);
const unknown = await call('PATCH', `/api/projects/${pid}/requirements/REQ-999`, { token: user.token, body: { baseVersion: base, text: 'x' } });
check('olmayan gereksinim → 404', unknown.status === 404, unknown.status);
check('reddedilen düzenlemeler sürüm üretmedi', (await head(user, pid)) === base);
const edit = await call('PATCH', `/api/projects/${pid}/requirements/${reqId}`, { token: user.token, body: { baseVersion: base, text: 'Düzenlenmiş gereksinim metni.', priority: 'critical' } });
check('geçerli düzenleme 200 ve yeni sürüm (kind=edit)', edit.status === 200 && edit.body?.version?.number === base + 1 && edit.body.version.kind === 'edit', edit);
const edited = (await getModel()).requirements.find((r) => r.id === reqId);
check('metin ve öncelik güncellendi', edited.text === 'Düzenlenmiş gereksinim metni.' && edited.priority === 'critical', edited);
check('düzenleme kaynak bilgisini silmez: origin ve sourceRefs korunur', edited.origin === 'user_statement' && edited.sourceRefs.length > 0, edited);

finish();
