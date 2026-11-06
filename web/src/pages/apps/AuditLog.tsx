import { useEffect, useState } from 'react';
import { api } from '../../api';
import type { AuditPage } from '../../types';
import { errorText, when } from './common';

const known = ['', 'user.created', 'user.password_reset', 'user.anonymized', 'member.updated', 'member.removed', 'group.created', 'app.published', 'app.grants_set', 'delegation.created',
  'delegation.revoked', 'accounting.settings_updated', 'email.settings', 'retention.settings', 'retention.purged', 'request.deleted', 'demo.seeded', 'demo.removed'];

/** Denetim kaydı: kim, ne zaman, ne yaptı (master admin). Ayrıntı satırı açılınca JSON olarak okunur. */
export function AuditLog({ ws }: { ws: string }) {
  const [form, setForm] = useState({ action: '', from: '', to: '' });
  const [applied, setApplied] = useState(form);
  const [page, setPage] = useState(1);
  const [nonce, setNonce] = useState(0);
  const [data, setData] = useState<AuditPage | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setData(null);
    api.audit(ws, { ...applied, page, pageSize: 50 }).then((d) => { setData(d); setError(null); }).catch((e) => setError(errorText(e, 'Denetim kaydı yüklenemedi.')));
  }, [ws, applied, page, nonce]);

  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  return (
    <>
      <form className="card" style={{ maxWidth: 900, marginBottom: 12 }} onSubmit={(e) => { e.preventDefault(); setPage(1); setApplied({ ...form }); setNonce((n) => n + 1); }}>
        <div className="mini-grid">
          <div><label htmlFor="au-act">İşlem</label>
            <select id="au-act" value={form.action} onChange={(e) => setForm({ ...form, action: e.target.value })}>{known.map((a) => <option key={a} value={a}>{a === '' ? 'Tümü' : a}</option>)}</select></div>
          <div><label htmlFor="au-from">Başlangıç</label><input id="au-from" type="date" value={form.from} onChange={(e) => setForm({ ...form, from: e.target.value })} /></div>
          <div><label htmlFor="au-to">Bitiş (dahil)</label><input id="au-to" type="date" value={form.to} onChange={(e) => setForm({ ...form, to: e.target.value })} /></div>
        </div>
        <p style={{ marginTop: 10 }}><button className="btn primary">Süz</button></p>
      </form>
      {error && <div className="notice error" role="alert">{error}</div>}
      {data === null && !error && <p className="muted">Yükleniyor…</p>}
      {data && <p className="muted">{data.total} kayıt{data.total > 0 ? ` · sayfa ${data.page}/${pages}` : ''}</p>}
      <div className="table-wrap"><table>
        <thead><tr><th>Zaman</th><th>Kişi</th><th>İşlem</th><th>Nesne</th><th>Ayrıntı</th></tr></thead>
        <tbody>
          {data?.items.map((r) => (
            <tr key={r.id}>
              <td>{when(r.at)}</td><td>{r.actorName ?? 'Sistem'}</td><td className="mono-small">{r.action}</td>
              <td className="mono-small">{r.objectType ? `${r.objectType}${r.objectId ? ` · ${r.objectId.slice(0, 8)}` : ''}` : '—'}</td>
              <td>{r.detail ? <details><summary>göster</summary><pre className="mono-small" style={{ whiteSpace: 'pre-wrap', margin: 0 }}>{JSON.stringify(r.detail, null, 1)}</pre></details> : '—'}</td>
            </tr>
          ))}
        </tbody>
      </table></div>
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
