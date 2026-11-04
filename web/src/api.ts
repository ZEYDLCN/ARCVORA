import type {
  Artifact, AuthResult, Job, ProfileInfo, ProjectDetail, ProjectSummary, ProposalDetail, ProposalSummary, View, Finding,
  Model, VersionInfo, WorkspaceInfo,
} from './types';

const TOKEN_KEY = 'arcvora.token';

// localStorage kapalı/engelli olabilir (özel pencere vb.); uygulama bunsuz da çalışır.
export function getToken(): string | null {
  try { return localStorage.getItem(TOKEN_KEY); } catch { return null; }
}
export function setToken(token: string | null): void {
  try { if (token) localStorage.setItem(TOKEN_KEY, token); else localStorage.removeItem(TOKEN_KEY); } catch { /* yoksay */ }
}

export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string, public details?: unknown) {
    super(message);
  }
  /** Sunucunun ayrıntılarını (ör. geçersiz seçim nedenleri) okunur metne çevirir. */
  get detailLines(): string[] {
    const d = this.details;
    if (Array.isArray(d)) {
      return d.map((x) => (typeof x === 'string' ? x : typeof x === 'object' && x && 'message' in x ? String((x as { message: unknown }).message) : JSON.stringify(x)));
    }
    return [];
  }
}

let memoryToken: string | null = null;

async function request<T>(method: string, path: string, body?: unknown, extra?: Record<string, string>): Promise<T> {
  const token = memoryToken ?? getToken();
  const res = await fetch(`/api${path}`, {
    method,
    headers: {
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...extra,
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  let data: unknown = null;
  try { data = text ? JSON.parse(text) : null; } catch { /* gövde JSON değil */ }
  if (!res.ok) {
    const p = (data ?? {}) as { code?: string; title?: string; details?: unknown };
    if (res.status === 401 && token) window.dispatchEvent(new Event('arcvora:unauthorized'));
    throw new ApiError(res.status, p.code ?? 'error', p.title ?? `İstek başarısız (${res.status}).`, p.details);
  }
  return data as T;
}

export function setSessionToken(token: string | null): void { memoryToken = token; }

export const api = {
  register: (email: string, password: string, displayName: string) =>
    request<AuthResult>('POST', '/auth/register', { email, password, displayName }),
  login: (email: string, password: string) => request<AuthResult>('POST', '/auth/login', { email, password }),
  me: () => request<{ user: AuthResult['user']; workspaces: WorkspaceInfo[] }>('GET', '/me'),
  profiles: () => request<ProfileInfo[]>('GET', '/profiles'),

  projects: () => request<ProjectSummary[]>('GET', '/projects'),
  createProject: (b: { workspaceId: string; name: string; description: string; techProfileId: string; ideaText: string }) =>
    request<ProjectDetail>('POST', '/projects', b),
  createSample: (workspaceId: string) => request<ProjectDetail>('POST', '/samples/expense-management', { workspaceId }),
  project: (id: string) => request<ProjectDetail>('GET', `/projects/${id}`),
  deleteProject: (id: string) => request<void>('DELETE', `/projects/${id}`),

  versions: (id: string) => request<VersionInfo[]>('GET', `/projects/${id}/versions`),
  version: (id: string, n: number) => request<{ info: VersionInfo; model: Model }>('GET', `/projects/${id}/versions/${n}`),
  views: (id: string, n: number) => request<{ versionNumber: number; modelHash: string; views: View[] }>('GET', `/projects/${id}/versions/${n}/views`),
  findings: (id: string, n: number) => request<{ versionNumber: number; findings: Finding[] }>('GET', `/projects/${id}/versions/${n}/findings`),
  revert: (id: string, n: number, baseVersion: number) =>
    request<{ version: VersionInfo }>('POST', `/projects/${id}/versions/${n}/revert`, { baseVersion }),

  addSource: (id: string, title: string, text: string, baseVersion: number) =>
    request<{ version: VersionInfo }>('POST', `/projects/${id}/sources`, { title, text, baseVersion }),
  updateRequirement: (id: string, rid: string, body: Record<string, unknown>) =>
    request<{ version: VersionInfo }>('PATCH', `/projects/${id}/requirements/${rid}`, body),
  answerQuestion: (id: string, qid: string, body: Record<string, unknown>) =>
    request<{ version: VersionInfo }>('PATCH', `/projects/${id}/open-questions/${qid}`, body),

  analyze: (id: string, key: string) => request<Job>('POST', `/projects/${id}/analyses`, {}, { 'Idempotency-Key': key }),
  proposeArchitecture: (id: string, key: string) =>
    request<Job>('POST', `/projects/${id}/architecture-proposals`, {}, { 'Idempotency-Key': key }),
  changeRequest: (id: string, requestText: string, key: string) =>
    request<Job>('POST', `/projects/${id}/change-requests`, { requestText }, { 'Idempotency-Key': key }),
  jobs: (id: string) => request<Job[]>('GET', `/projects/${id}/jobs`),
  job: (jobId: string) => request<Job>('GET', `/jobs/${jobId}`),
  cancelJob: (jobId: string) => request<Job>('POST', `/jobs/${jobId}/cancel`, {}),

  proposals: (id: string) => request<ProposalSummary[]>('GET', `/projects/${id}/proposals`),
  proposal: (id: string, pid: string) => request<ProposalDetail>('GET', `/projects/${id}/proposals/${pid}`),
  accept: (id: string, pid: string, groupIds: string[] | null, baseVersion: number) =>
    request<{ version: VersionInfo; alreadyAccepted: boolean }>('POST', `/projects/${id}/proposals/${pid}/accept`, { groupIds, baseVersion }),
  reject: (id: string, pid: string, note: string) => request<ProposalSummary>('POST', `/projects/${id}/proposals/${pid}/reject`, { note }),

  exportProject: (id: string, versionNumber: number, format: 'markdown' | 'package') =>
    request<Artifact>('POST', `/projects/${id}/exports`, { versionNumber, format }),

  /** Kimlik doğrulamalı indirme (bağlantıya token eklenemediği için fetch + blob). */
  async download(id: string, artifact: Artifact): Promise<void> {
    const token = memoryToken ?? getToken();
    const res = await fetch(`/api/projects/${id}/exports/${artifact.id}/download`, { headers: token ? { Authorization: `Bearer ${token}` } : {} });
    if (!res.ok) throw new ApiError(res.status, 'download_failed', 'Dosya indirilemedi.');
    const url = URL.createObjectURL(await res.blob());
    const a = document.createElement('a');
    a.href = url;
    a.download = artifact.fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  },
};
