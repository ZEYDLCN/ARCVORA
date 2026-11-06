import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api';
import { go } from '../../components/AppsShell';
import { AppIcon } from '../../components/AppIcon';
import { RequestBadge } from '../../components/StatusBadge';
import { requestStatusLabel } from '../../lib/requestText';
import type { AppSummary, SearchPage } from '../../types';
import { errorText, when } from './common';

const statuses = ['', 'in_review', 'approved', 'rejected', 'cancelled'] as const;

/** Tüm talepler: metin/durum/uygulama/tarih ile arama, sayfalama ve CSV dışa aktarım (master admin). */
export function AdminRequests({ ws }: { ws: string }) {
  const [form, setForm] = useState({ q: '', status: '', appId: '', from: '', to: '' });
  const [applied, setApplied] = useState(form);
  const [page, setPage] = useState(1);
  const [nonce, setNonce] = useState(0);        // aynı ölçütle "Ara"ya basmak da listeyi yeniler
  const [data, setData] = useState<SearchPage | null>(null);
  const [apps, setApps] = useState<AppSummary[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => { api.apps(ws).then(setApps).catch(() => setApps([])); }, [ws]);
  const load = useCallback(() => {
    setData(null);
    api.searchRequests(ws, { ...applied, page, pageSize: 25 }).then((d) => { setData(d); setError(null); }).catch((e) => setError(errorText(e, 'Talepler yüklenemedi.')));
  }, [ws, applied, page, nonce]);
  useEffect(load, [load]);

  const submit = (e: React.FormEvent) => { e.preventDefault(); setPage(1); setApplied({ ...form }); setNonce((n) => n + 1); };
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <>
      <form className="card" style={{ maxWidth: 900, marginBottom: 12 }} onSubmit={submit}>
        <div className="mini-grid">
          <div><label htmlFor="rq-q">Ara</label><input id="rq-q" type="search" maxLength={100} placeholder="Numara, özet veya kişi" value={form.q} onChange={(e) => setForm({ ...form, q: e.target.value })} /></div>
          <div><label htmlFor="rq-st">Durum</label>
            <select id="rq-st" value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value })}>
              {statuses.map((s) => <option key={s} value={s}>{s === '' ? 'Tümü' : requestStatusLabel[s]}</option>)}
            </select></div>
          <div><label htmlFor="rq-app">Uygulama</label>
            <select id="rq-app" value={form.appId} onChange={(e) => setForm({ ...form, appId: e.target.value })}>
              <option value="">Tümü</option>
              {apps.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
            </select></div>
          <div><label htmlFor="rq-from">Başlangıç</label><input id="rq-from" type="date" value={form.from} onChange={(e) => setForm({ ...form, from: e.target.value })} /></div>
          <div><label htmlFor="rq-to">Bitiş (dahil)</label><input id="rq-to" type="date" value={form.to} onChange={(e) => setForm({ ...form, to: e.target.value })} /></div>
        </div>
        <p className="row" style={{ marginTop: 10 }}>
          <button className="btn primary">Ara</button>
          <button type="button" className="btn" disabled={busy} onClick={() => { setBusy(true); void api.downloadFile(`/workspaces/${ws}/reports/requests.csv${qsOf(applied)}`, 'talepler.csv').catch((e) => setError(errorText(e))).finally(() => setBusy(false)); }}>CSV indir</button>
        </p>
      </form>
      {error && <div className="notice error" role="alert">{error}</div>}
      {data === null && !error && <p className="muted">Yükleniyor…</p>}
      {data && <p className="muted">{data.total} talep{data.total > 0 ? ` · sayfa ${data.page}/${pages}` : ''}</p>}
      {data?.items.length === 0 && <div className="empty-state"><b>Talep yok</b><span>Bu aramaya uyan talep bulunmuyor.</span></div>}
      <ul className="list">
        {data?.items.map((r) => (
          <li key={r.id}>
            <button className="item" onClick={() => go({ name: 'request', id: r.id })}>
              <span className="row">
                <AppIcon name={r.icon} />
                <span style={{ flex: 1, minWidth: 0 }}><span className="title">{r.number} · {r.appName}</span><span className="muted" style={{ display: 'block' }}>{r.summary || '—'}</span></span>
                <RequestBadge status={r.status} />
              </span>
              <span className="meta"><span className="badge neutral">{r.requesterName}</span><span className="muted">{when(r.createdAt)}</span>{r.currentStepName && <span className="badge info">Bekleyen: {r.currentStepName}</span>}</span>
            </button>
          </li>
        ))}
      </ul>
      {data && pages > 1 && (
        <p className="row" style={{ justifyContent: 'center', gap: 12 }}>
          <button className="btn small" disabled={page <= 1} onClick={() => setPage(page - 1)}>← Önceki</button>
          <span className="muted">{page} / {pages}</span>
          <button className="btn small" disabled={page >= pages} onClick={() => setPage(page + 1)}>Sonraki →</button>
        </p>
      )}
    </>
  );
}

function qsOf(f: { q: string; status: string; appId: string; from: string; to: string }): string {
  const e = Object.entries(f).filter(([, v]) => v !== '');
  return e.length === 0 ? '' : '?' + e.map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`).join('&');
}
