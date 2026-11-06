import { useEffect, useState } from 'react';
import { api } from '../../api';
import { go } from '../../components/AppsShell';
import { RequestCard } from '../../components/RequestCard';
import { IconFile, IconUser } from '../../components/icons';
import { EmptyState } from '../../components/ui';
import { RequestBadge } from '../../components/StatusBadge';
import { requestStatusLabel } from '../../lib/requestText';
import type { RequestSummary, RequestStatus } from '../../types';
import { errorText, when } from './common';

const filters: ('all' | RequestStatus)[] = ['all', 'in_review', 'approved', 'rejected', 'cancelled'];

/** Talep listesi: kullanıcının kendi talepleri veya (admin için) çalışma alanındaki tüm talepler. */
export function RequestList({ workspaceId, all = false }: { workspaceId: string; all?: boolean }) {
  const [status, setStatus] = useState<'all' | RequestStatus>('all');
  const [items, setItems] = useState<RequestSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setItems(null);
    const s = status === 'all' ? undefined : status;
    (all ? api.allRequests(workspaceId, s) : api.myRequests(s)).then(setItems).catch((e) => setError(errorText(e, 'Talepler yüklenemedi.')));
  }, [status, all, workspaceId]);

  return (
    <>
      <div className="tabs" role="tablist" aria-label="Durum">
        <div className="seg">
          {filters.map((f) => (
            <button key={f} role="tab" aria-selected={status === f} onClick={() => setStatus(f)}>{f === 'all' ? 'Tümü' : requestStatusLabel[f]}</button>
          ))}
        </div>
      </div>
      {error && <div className="notice error" role="alert">{error}</div>}
      {items === null && !error && <p className="muted">Yükleniyor…</p>}
      {items?.length === 0 && <EmptyState icon={<IconFile size={30} />} title="Talep yok">Bu filtreye uyan talep bulunmuyor.</EmptyState>}
      <ul className="list">
        {items?.map((r) => (
          <li key={r.id}>
            <RequestCard icon={r.icon} number={r.number} appName={r.appName} person={all ? r.requesterName : undefined} isDemo={r.isDemo}
              summary={`${r.appName} · ${r.summary || '—'}`} parts={r.summaryParts ? [{ key: '_app', label: 'Uygulama', kind: 'text', text: r.appName }, ...r.summaryParts] : undefined}
              badge={<RequestBadge status={r.status} />}
              chips={r.currentStepName ? <span className="chip-soft"><IconUser size={14} />Bekleyen: {r.currentStepName}</span> : undefined}
              time={when(r.createdAt)} onOpen={() => go({ name: 'request', id: r.id })} />
          </li>
        ))}
      </ul>
    </>
  );
}

export function MyRequests({ workspace }: { workspace: { id: string } }) {
  return (
    <>
      <div className="page-head"><h1>Taleplerim</h1><p className="tagline">Oluşturduğunuz taleplerin durumu ve onay geçmişi.</p></div>
      <div className="page-body"><RequestList workspaceId={workspace.id} /></div>
    </>
  );
}
