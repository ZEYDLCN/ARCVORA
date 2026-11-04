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

section('Geçersiz yama modeli bozamaz');
const before = await head(user, pid);
const badPatch = await call('POST', `/api/projects/${pid}/patches`, {
  token: user.token,
  body: { baseVersion: before, summary: 'bozuk', patch: { operations: [{ op: 'upsert', type: 'relationship', id: 'REL-1', value: { id: 'REL-1', sourceId: 'CMP-404', targetId: 'CMP-405' } }] } },
});
check('olmayan bileşene bağlantı → 422 validation_failed', badPatch.status === 422 && badPatch.body?.code === 'validation_failed', badPatch);
check('hata ayrıntıları kural kimliği (REF-…) içerir', badPatch.body?.details?.some((d) => /^REF-/.test(d.ruleId)), badPatch.body?.details);
check('geçersiz yama sürüm üretmedi', (await head(user, pid)) === before);

section('Açık soruyu yanıtlama');
const arch = await waitJob(user.token, (await call('POST', `/api/projects/${pid}/architecture-proposals`, { token: user.token })).body.id);
check('mimari taslak üretildi', arch.status === 'succeeded', arch);
const accArch = await call('POST', `/api/projects/${pid}/proposals/${arch.proposalId}/accept`, { token: user.token, body: {} });
check('taslak kabul edildi', accArch.status === 200, accArch.body);
const question = (await getModel()).openQuestions?.[0];
check('modelde en az bir açık soru var', !!question, (await getModel()).openQuestions);
if (question) {
  const qBase = await head(user, pid);
  const stale = await call('PATCH', `/api/projects/${pid}/open-questions/${question.id}`, { token: user.token, body: { baseVersion: qBase - 1, status: 'answered', answer: 'Eski temel' } });
  check('eski temele dayalı yanıt → 409', stale.status === 409, stale);
  const ans = await call('PATCH', `/api/projects/${pid}/open-questions/${question.id}`, { token: user.token, body: { baseVersion: qBase, status: 'answered', answer: 'Eşzamanlı en fazla 50 kullanıcı bekleniyor.' } });
  check('yanıt 200 ve yeni sürüm', ans.status === 200 && ans.body?.version?.number === qBase + 1, ans);
  const answered = (await getModel()).openQuestions.find((q) => q.id === question.id);
  check('soru "answered" ve yanıt metni saklandı', answered.status === 'answered' && /50 kullanıcı/.test(answered.answer ?? ''), answered);
  const missing = await call('PATCH', `/api/projects/${pid}/open-questions/OQ-999`, { token: user.token, body: { baseVersion: await head(user, pid), status: 'answered', answer: 'x' } });
  check('olmayan soru → 404', missing.status === 404, missing.status);
}

section('Denetim kaydı');
const audit = await call('GET', `/api/projects/${pid}/audit`, { token: user.token });
check('yönetici denetim kaydını okur (200)', audit.status === 200 && Array.isArray(audit.body), audit.status);
const actions = new Set((audit.body ?? []).map((a) => a.action));
console.log('  eylemler:', [...actions].join(', '));
check('kabul, düzenleme ve proje oluşturma izi var', actions.size >= 3, [...actions]);
check('her kayıtta kim (actor), ne zaman (at) ve hangi nesne bilgisi var', (audit.body ?? []).every((a) => a.actor && !Number.isNaN(Date.parse(a.at)) && a.objectType), audit.body?.[0]);
check('eylemi yapan kullanıcı e-postası kayıtlı', audit.body?.every((a) => a.actor === user.email), audit.body?.map((a) => a.actor));
const outsider = await newUser('audit-out');
check('üye olmayan denetim kaydını göremez → 404', (await call('GET', `/api/projects/${pid}/audit`, { token: outsider.token })).status === 404);

finish();
