import { ApiError } from '../../api';
import type { WorkspaceInfo } from '../../types';

export interface Ctx { user: { id: string; displayName: string }; workspace: WorkspaceInfo }

export const errorText = (e: unknown, fallback = 'İşlem başarısız.') => (e instanceof ApiError ? e.message : fallback);
export const when = (iso?: string) => (iso ? new Date(iso).toLocaleString('tr-TR', { dateStyle: 'short', timeStyle: 'short' }) : '—');

export function newKey(): string {
  try { return crypto.randomUUID(); } catch { return `${Date.now()}-${Math.random().toString(36).slice(2)}`; }
}
