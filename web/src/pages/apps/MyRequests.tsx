import { useEffect, useState } from 'react';
import { api } from '../../api';
import { go } from '../../components/AppsShell';
import { AppIcon } from '../../components/AppIcon';
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
      {items?.length === 0 && <div className="empty-state"><b>Talep yok</b><span>Bu filtreye uyan talep bulunmuyor.</span></div>}
      <ul className="list">
        {items?.map((r) => (
          <li key={r.id}>
            <button className="item" onClick={() => go({ name: 'request', id: r.id })}>
              <span className="row">
                <AppIcon name={r.icon} />
                <span style={{ flex: 1, minWidth: 0 }}>
                  <span className="title">{r.number} · {r.appName}</span>
                  <span className="muted" style={{ display: 'block' }}>{r.summary || '—'}</span>
                </span>
                <RequestBadge status={r.status} />
              </span>
              <span className="meta">
                {all && <span className="badge neutral">{r.requesterName}</span>}
                <span className="muted">{when(r.createdAt)}</span>
                {r.currentStepName && <span className="badge info">Bekleyen: {r.currentStepName}</span>}
              </span>
            </button>
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
