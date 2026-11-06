import type {
  AccountingEntry, AccountingSettings, AppDetail, AttachmentInfo, Delegation, DemoSeed, DemoStatus, AppForm, AppGrants, AppSummary, Artifact, AuthResult, DirectoryEntry, FlowDefinition,
  GroupInfo, Job, Member, MyApp, MyTask, ProfileInfo, ProjectDetail, ProjectSummary, ProposalDetail, ProposalSummary, RequestDetail,
  RequestSummary, VerifyResult, View, Finding, Model, VersionInfo, WorkspaceInfo,
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
  me: () => request<{ user: AuthResult['user']; workspaces: WorkspaceInfo[]; mustChangePassword?: boolean }>('GET', '/me'),
  changePassword: (currentPassword: string, newPassword: string) => request<void>('POST', '/me/password', { currentPassword, newPassword }),
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

  // ---- akış uygulamaları: kullanım
  myApps: () => request<MyApp[]>('GET', '/my/apps'),
  appForm: (id: string) => request<AppForm>('GET', `/apps/${id}/form`),
  submit: (id: string, data: Record<string, unknown>, key: string) =>
    request<RequestDetail>('POST', `/apps/${id}/requests`, { data }, { 'Idempotency-Key': key }),
  myRequests: (status?: string) => request<RequestSummary[]>('GET', `/my/requests${status ? `?status=${status}` : ''}`),
  allRequests: (ws: string, status?: string) => request<RequestSummary[]>('GET', `/workspaces/${ws}/requests${status ? `?status=${status}` : ''}`),
  requestDetail: (id: string) => request<RequestDetail>('GET', `/requests/${id}`),
  cancelRequest: (id: string) => request<RequestDetail>('POST', `/requests/${id}/cancel`, {}),
  myTasks: (done = false) => request<MyTask[]>('GET', `/my/tasks${done ? '?done=true' : ''}`),
  approveTask: (taskId: string, comment: string) => request<RequestDetail>('POST', `/tasks/${taskId}/approve`, { comment }),
  rejectTask: (taskId: string, comment: string) => request<RequestDetail>('POST', `/tasks/${taskId}/reject`, { comment }),
  directory: (ws: string) => request<DirectoryEntry[]>('GET', `/workspaces/${ws}/directory`),

  // ---- akış uygulamaları: tasarım (master admin)
  apps: (ws: string) => request<AppSummary[]>('GET', `/workspaces/${ws}/apps`),
  createApp: (ws: string, body: { name: string; description?: string; icon?: string; definition?: FlowDefinition }) =>
    request<AppDetail>('POST', `/workspaces/${ws}/apps`, body),
  appDetail: (id: string) => request<AppDetail>('GET', `/apps/${id}`),
  updateApp: (id: string, body: { baseRevision: number; name?: string; description?: string; icon?: string; definition?: FlowDefinition }) =>
    request<AppDetail>('PUT', `/apps/${id}`, body),
  publishApp: (id: string) => request<AppDetail>('POST', `/apps/${id}/publish`, {}),
  archiveApp: (id: string, archived: boolean) => request<AppSummary>('POST', `/apps/${id}/${archived ? 'archive' : 'restore'}`, {}),
  deleteApp: (id: string) => request<void>('DELETE', `/apps/${id}`),
  appGrants: (id: string) => request<AppGrants>('GET', `/apps/${id}/grants`),
  setAppGrants: (id: string, g: AppGrants) => request<AppGrants>('PUT', `/apps/${id}/grants`, g),

  // ---- yönetim: kullanıcı ve grup
  members: (ws: string) => request<Member[]>('GET', `/workspaces/${ws}/members`),
  createUser: (ws: string, b: { email: string; displayName: string; password: string; role: string; managerId?: string }) =>
    request<Member>('POST', `/workspaces/${ws}/users`, b),
  updateMember: (ws: string, userId: string, b: Record<string, unknown>) => request<Member>('PATCH', `/workspaces/${ws}/members/${userId}`, b),
  removeMember: (ws: string, userId: string) => request<void>('DELETE', `/workspaces/${ws}/members/${userId}`),
  delegations: (ws: string, scope: 'mine' | 'all' = 'mine') => request<Delegation[]>('GET', `/workspaces/${ws}/delegations?scope=${scope}`),
  createDelegation: (ws: string, body: { toUserId: string; startsOn: string; endsOn: string; reason?: string; fromUserId?: string }) =>
    request<Delegation>('POST', `/workspaces/${ws}/delegations`, body),
  revokeDelegation: (ws: string, id: string) => request<Delegation>('DELETE', `/workspaces/${ws}/delegations/${id}`),
  reassignTask: (taskId: string, toUserId: string) => request<RequestDetail>('POST', `/tasks/${taskId}/reassign`, { toUserId }),
  demoStatus: (ws: string) => request<DemoStatus>('GET', `/workspaces/${ws}/demo-data`),
  seedDemo: (ws: string) => request<DemoSeed>('POST', `/workspaces/${ws}/demo-data`, {}),
  removeDemo: (ws: string) => request<void>('DELETE', `/workspaces/${ws}/demo-data`),
  resetPassword: (ws: string, userId: string, password: string) => request<Member>('POST', `/workspaces/${ws}/members/${userId}/reset-password`, { password }),
  groups: (ws: string) => request<GroupInfo[]>('GET', `/workspaces/${ws}/groups`),
  createGroup: (ws: string, name: string) => request<GroupInfo>('POST', `/workspaces/${ws}/groups`, { name }),
  renameGroup: (ws: string, id: string, name: string) => request<GroupInfo>('PATCH', `/workspaces/${ws}/groups/${id}`, { name }),
  setGroupMembers: (ws: string, id: string, userIds: string[]) => request<GroupInfo>('PUT', `/workspaces/${ws}/groups/${id}/members`, { userIds }),
  deleteGroup: (ws: string, id: string) => request<void>('DELETE', `/workspaces/${ws}/groups/${id}`),

  // ---- muhasebe
  accountingSettings: (ws: string) => request<AccountingSettings>('GET', `/workspaces/${ws}/accounting/settings`),
  updateAccountingSettings: (ws: string, b: Record<string, unknown>) => request<AccountingSettings>('PUT', `/workspaces/${ws}/accounting/settings`, b),
  accountingEntries: (ws: string, status?: string) => request<AccountingEntry[]>('GET', `/workspaces/${ws}/accounting/entries${status ? `?status=${status}` : ''}`),
  processEntry: (ws: string, id: string, note: string) => request<AccountingEntry>('POST', `/workspaces/${ws}/accounting/entries/${id}/process`, { note }),
  retryEntry: (ws: string, id: string) => request<AccountingEntry>('POST', `/workspaces/${ws}/accounting/entries/${id}/retry`, {}),
  /** CSV indirme (kimlik doğrulamalı). */
  async downloadAccountingCsv(ws: string, status?: string): Promise<void> {
    const token = memoryToken ?? getToken();
    const res = await fetch(`/api/workspaces/${ws}/accounting/export.csv${status ? `?status=${status}` : ''}`, { headers: token ? { Authorization: `Bearer ${token}` } : {} });
    if (!res.ok) throw new ApiError(res.status, 'download_failed', 'CSV indirilemedi.');
    const url = URL.createObjectURL(await res.blob());
    const a = document.createElement('a');
    a.href = url;
    a.download = /filename="?([^";]+)"?/.exec(res.headers.get('content-disposition') ?? '')?.[1] ?? 'muhasebe.csv';
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  },

  /** Dosyayı ham gövdeyle yükler (talebe bağlanmadan önce "hazırlanır"). Tür/boyut kararı sunucudadır. */
  async uploadFile(appId: string, file: File): Promise<AttachmentInfo> {
    const token = memoryToken ?? getToken();
    const res = await fetch(`/api/apps/${appId}/uploads?fileName=${encodeURIComponent(file.name)}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/octet-stream', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
      body: file,
    });
    const text = await res.text();
    let data: unknown = null;
    try { data = text ? JSON.parse(text) : null; } catch { /* gövde JSON değil */ }
    if (!res.ok) {
      const p = (data ?? {}) as { code?: string; title?: string; details?: unknown };
      if (res.status === 401 && token) window.dispatchEvent(new Event('arcvora:unauthorized'));
      throw new ApiError(res.status, p.code ?? 'error', p.title ?? `Dosya yüklenemedi (${res.status}).`, p.details);
    }
    return data as AttachmentInfo;
  },
  verifyDocument: (ws: string, number: string, code: string) =>
    request<VerifyResult>('GET', `/workspaces/${ws}/documents/verify?number=${encodeURIComponent(number)}&code=${encodeURIComponent(code)}`),
  /** Talebin yazdırılabilir PDF'i (kimlik doğrulamalı indirme). */
  async downloadRequestPdf(id: string, fallbackName: string): Promise<void> {
    const token = memoryToken ?? getToken();
    const res = await fetch(`/api/requests/${id}/pdf`, { headers: token ? { Authorization: `Bearer ${token}` } : {} });
    if (!res.ok) throw new ApiError(res.status, 'download_failed', 'PDF oluşturulamadı.');
    const url = URL.createObjectURL(await res.blob());
    const a = document.createElement('a');
    a.href = url;
    a.download = /filename="?([^";]+)"?/.exec(res.headers.get('content-disposition') ?? '')?.[1] ?? fallbackName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
  },
  deleteAttachment: (id: string) => request<void>('DELETE', `/attachments/${id}`),
  /** Ek dosyanın baytları (kimlik doğrulamalı). Önizleme ve kaydetme bunu kullanır. */
  async attachmentBlob(id: string): Promise<Blob> {
    const token = memoryToken ?? getToken();
    const res = await fetch(`/api/attachments/${id}`, { headers: token ? { Authorization: `Bearer ${token}` } : {} });
    if (!res.ok) throw new ApiError(res.status, 'download_failed', 'Dosya indirilemedi.');
    return res.blob();
  },

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
