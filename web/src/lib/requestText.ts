import type { RequestEventInfo, RequestStatus, TaskStatus } from '../types';

export const requestStatusLabel: Record<RequestStatus, string> = {
  in_review: 'İncelemede', approved: 'Onaylandı', rejected: 'Reddedildi', cancelled: 'İptal edildi',
};
export const taskStatusLabel: Record<TaskStatus, string> = { pending: 'Bekliyor', approved: 'Onayladı', rejected: 'Reddetti', skipped: 'Gerekmedi' };

export type Tone = 'ok' | 'warn' | 'bad' | 'info' | 'neutral';

export function requestTone(s: RequestStatus): Tone {
  return s === 'approved' ? 'ok' : s === 'rejected' ? 'bad' : s === 'cancelled' ? 'neutral' : 'info';
}
export function taskTone(s: TaskStatus): Tone {
  return s === 'approved' ? 'ok' : s === 'rejected' ? 'bad' : s === 'skipped' ? 'neutral' : 'warn';
}

export const accountingStatusLabel = { pending: 'Beklemede', sent: 'Gönderildi', processed: 'İşlendi', failed: 'Hata' } as const;
export function accountingTone(s: keyof typeof accountingStatusLabel): Tone {
  return s === 'processed' ? 'ok' : s === 'sent' ? 'info' : s === 'failed' ? 'bad' : 'warn';
}

const fallbackText: Record<string, string> = {
  manager_missing: 'talep edenin yöneticisi tanımlı olmadığı için',
  group_empty: 'grup boş olduğu için',
  user_missing: 'seçilen kişi bulunamadığı için',
  admin_missing: 'master admin bulunamadığı için',
  only_approver_is_requester: 'tek onaylayıcı talep edenin kendisi olduğu için',
  no_approver: 'onaylayıcı bulunamadığı için',
  assignee_removed: 'önceki onaylayıcı çalışma alanından çıkarıldığı için',
};

export function fallbackLabel(reason: string | undefined): string | null {
  return reason ? (fallbackText[reason] ?? reason) : null;
}

const str = (v: unknown): string => (typeof v === 'string' ? v : '');

/** Zaman çizelgesi satırı: kimin ne yaptığını Türkçe bir cümleyle anlatır. */
export function eventText(e: RequestEventInfo, people: Record<string, string>): string {
  const who = e.actorId ? (people[e.actorId] ?? 'Bir kullanıcı') : 'Sistem';
  const d = e.detail ?? {};
  const step = str(d.stepName);
  switch (e.kind) {
    case 'submitted': return `${who} talebi oluşturdu.`;
    case 'step_started': {
      const names = Array.isArray(d.assignees) ? d.assignees.map((id) => people[String(id)] ?? 'Bilinmeyen').join(', ') : '';
      const fb = fallbackLabel(typeof d.fallback === 'string' ? d.fallback : undefined);
      return `“${step}” adımı başladı. Onaylayacak: ${names}${fb ? ` (${fb} master admin’e yönlendirildi)` : ''}.`;
    }
    case 'step_skipped': return `“${step}” adımı koşul sağlanmadığı için atlandı.`;
    case 'approved': return `${who} “${step}” adımını onayladı${str(d.comment) ? `: ${str(d.comment)}` : '.'}`;
    case 'rejected': return `${who} “${step}” adımında reddetti${str(d.comment) ? `: ${str(d.comment)}` : '.'}`;
    case 'cancelled': return `${who} talebi iptal etti.`;
    case 'approver_removed': {
      const removed = typeof d.removedId === 'string' ? (people[d.removedId] ?? 'Bir onaylayıcı') : 'Bir onaylayıcı';
      const handed = Array.isArray(d.replacements) && d.replacements.length > 0;
      return `${removed} çalışma alanından çıkarıldığı için “${step}” adımı ${handed ? 'master admin’e yönlendirildi' : 'kalan onaylayıcılara bırakıldı'}.`;
    }
    case 'completed': return 'Talep onaylandı.';
    case 'accounting_queued': return 'Muhasebe kuyruğuna eklendi.';
    case 'accounting_sent': return 'Muhasebe sistemine gönderildi.';
    case 'accounting_failed': return 'Muhasebe sistemine gönderilemedi; muhasebe elle işleyebilir.';
    case 'accounting_processed': return `${who} muhasebede işledi${str(d.note) ? `: ${str(d.note)}` : '.'}`;
    default: return e.kind;
  }
}

export type StepState = 'done' | 'current' | 'rejected' | 'skipped' | 'upcoming';

/**
 * Akış şeridi için adım durumları: görevi olan adım görevlerine göre (bekliyor → sırada, onay → tamam, ret → reddedildi),
 * hiç görevi olmayan adım talep sonuçlandıysa atlandı, sonuçlanmadıysa sırada bekleyen (gelecek) adımdır.
 */
export function stepStates(steps: readonly { id: string }[], tasks: readonly { stepId: string; status: TaskStatus }[], status: RequestStatus): StepState[] {
  return steps.map((s) => {
    const mine = tasks.filter((t) => t.stepId === s.id);
    if (mine.some((t) => t.status === 'rejected')) return 'rejected';
    if (mine.some((t) => t.status === 'pending')) return 'current';
    if (mine.some((t) => t.status === 'approved')) return 'done';
    return status === 'in_review' ? 'upcoming' : 'skipped';
  });
}
