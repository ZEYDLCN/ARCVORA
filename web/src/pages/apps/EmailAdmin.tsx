import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api';
import type { EmailOutboxRow, EmailStatus } from '../../types';
import { errorText, when } from './common';

const statusLabel: Record<EmailOutboxRow['status'], string> = { pending: 'Bekliyor', sent: 'Gönderildi', failed: 'Hata', skipped: 'Atlandı' };
const statusTone: Record<EmailOutboxRow['status'], string> = { pending: 'warn', sent: 'ok', failed: 'bad', skipped: 'neutral' };
const filters: { id: string; label: string }[] = [{ id: '', label: 'Tümü' }, { id: 'pending', label: 'Bekleyen' }, { id: 'failed', label: 'Hatalı' }, { id: 'sent', label: 'Gönderilen' }, { id: 'skipped', label: 'Atlanan' }];

/** E-posta bildirimleri: durum, çalışma alanı ayarları, test maili ve gönderim kuyruğu (hatalıları yeniden deneme). SMTP ayarları sunucu yapılandırmasındadır. */
export function EmailAdmin({ ws }: { ws: string }) {
  const [status, setStatus] = useState<EmailStatus | null>(null);
  const [rows, setRows] = useState<EmailOutboxRow[] | null>(null);
  const [filter, setFilter] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    api.emailStatus(ws).then(setStatus).catch((e) => setError(errorText(e, 'Durum okunamadı.')));
    api.emailOutbox(ws, filter || undefined).then(setRows).catch((e) => setError(errorText(e, 'Kuyruk okunamadı.')));
  }, [ws, filter]);
  useEffect(load, [load]);

  const run = async (fn: () => Promise<unknown>, ok?: string) => {
    setBusy(true);
    setError(null);
    setInfo(null);
    try { await fn(); if (ok) setInfo(ok); } catch (e) { setError(errorText(e)); } finally { setBusy(false); load(); }
  };

  return (
    <div style={{ display: 'grid', gap: 14, maxWidth: 900 }}>
      <div className="card">
        <h3>E-posta bildirimleri</h3>
        {error && <div className="notice error" role="alert">{error}</div>}
        {info && <div className="notice" role="status">{info}</div>}
        {!status && !error && <p className="muted">Yükleniyor…</p>}
        {status && !status.configured && (
          <div className="notice warn" role="status">
            E-posta sunucuda yapılandırılmamış; hiçbir bildirim gönderilmez. Sunucu ortamında <code>Email__Enabled=true</code>, <code>Email__Host</code>, <code>Email__From</code>
            (ve gerekirse <code>Email__User</code>/<code>Email__Password</code>) ayarlayın. Ayrıntı: dağıtım rehberi.
          </div>
        )}
        {status && (
          <>
            <p className="muted">Gönderen: {status.from ?? '—'} · Bekleyen: <b>{status.pending}</b> · Hatalı: <b>{status.failed}</b></p>
            <div className="check-row">
              <input id="em-on" type="checkbox" checked={status.emailEnabled} disabled={busy} onChange={(e) => void run(() => api.updateEmail(ws, { emailEnabled: e.target.checked }))} />
              <label htmlFor="em-on" style={{ margin: 0 }}>Bu çalışma alanı için e-posta bildirimleri açık</label>
            </div>
            <div className="check-row" style={{ marginTop: 6 }}>
              <input id="em-rem" type="checkbox" checked={status.remindersEnabled} disabled={busy} onChange={(e) => void run(() => api.updateEmail(ws, { remindersEnabled: e.target.checked }))} />
              <label htmlFor="em-rem" style={{ margin: 0 }}>Bekleyen onaylar için hatırlatma gönder</label>
            </div>
            <p style={{ marginTop: 12 }}>
              <button className="btn" disabled={busy || !status.configured} onClick={() => void run(async () => { const r = await api.sendTestEmail(ws); setInfo(`Test e-postası ${r.to} adresine gönderildi.`); })}>Test e-postası gönder</button>
            </p>
          </>
        )}
      </div>

      <div className="card">
        <div className="row" style={{ justifyContent: 'space-between' }}>
          <h3 style={{ margin: 0 }}>Gönderim kuyruğu</h3>
          <div className="seg" role="tablist">{filters.map((f) => <button key={f.id} role="tab" aria-selected={filter === f.id} onClick={() => setFilter(f.id)}>{f.label}</button>)}</div>
        </div>
        {rows === null && !error && <p className="muted">Yükleniyor…</p>}
        {rows?.length === 0 && <p className="muted">Kayıt yok.</p>}
        <div className="table-wrap">
          <table>
            <thead><tr><th>Alıcı</th><th>Konu</th><th>Durum</th><th>Zaman</th><th /></tr></thead>
            <tbody>
              {rows?.map((r) => (
                <tr key={r.id}>
                  <td className="mono-small">{r.to}</td>
                  <td>{r.subject}{r.lastError && <div className="muted">{r.lastError} ({r.attempts}. deneme)</div>}</td>
                  <td><span className={`badge ${statusTone[r.status]}`}>{statusLabel[r.status]}</span></td>
                  <td>{when(r.sentAt ?? r.createdAt)}</td>
                  <td>{(r.status === 'failed' || r.status === 'pending') && <button className="btn small" disabled={busy} onClick={() => void run(() => api.retryEmail(ws, r.id))}>Yeniden dene</button>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
