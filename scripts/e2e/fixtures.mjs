// Uçtan uca betikler için ortak kurulum: kullanıcı, proje, kabul edilmiş gereksinimler.
import { call, waitJob } from './lib.mjs';

export const IDEA = 'Çalışanlar masraf girer, yönetici onaylar. Fiş fotoğrafı yüklenir.';
const stamp = Date.now();
let counter = 0;

export async function newUser(label = 'u') {
  const email = `${label}-${stamp}-${counter++}@example.com`;
  const r = await call('POST', '/api/auth/register', { body: { email, password: 'Parola-12345', displayName: `Test ${label}` } });
  if (r.status !== 201) throw new Error(`kayıt başarısız (${r.status}): ${JSON.stringify(r.body)}`);
  return { email, token: r.body.token, workspaceId: r.body.workspaces[0].id, id: r.body.user.id };
}

export async function newProject(user, name = 'Masraf yönetimi') {
  const profiles = (await call('GET', '/api/profiles')).body;
  const r = await call('POST', '/api/projects', {
    token: user.token,
    body: { workspaceId: user.workspaceId, name, techProfileId: profiles.find((p) => p.selectable).id, ideaText: IDEA },
  });
  if (r.status !== 201) throw new Error(`proje oluşturulamadı: ${JSON.stringify(r.body)}`);
  return r.body.summary.id;
}

/** Gereksinim çıkarımını çalıştırır ve kabul eder; kabul edilen sürüm numarasını döndürür. */
export async function withAcceptedRequirements(user, projectId) {
  const j = await call('POST', `/api/projects/${projectId}/analyses`, { token: user.token });
  const job = await waitJob(user.token, j.body.id);
  if (job.status !== 'succeeded') throw new Error(`analiz başarısız: ${JSON.stringify(job)}`);
  const acc = await call('POST', `/api/projects/${projectId}/proposals/${job.proposalId}/accept`, { token: user.token, body: {} });
  if (acc.status !== 200) throw new Error(`kabul başarısız: ${JSON.stringify(acc.body)}`);
  return acc.body.version.number;
}

export const head = async (user, projectId) => (await call('GET', `/api/projects/${projectId}`, { token: user.token })).body.summary.headNumber;
