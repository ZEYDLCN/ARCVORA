import { Fragment, useCallback, useEffect, useState } from 'react';
import { IconPackage } from '../../components/icons';
import { EmptyState } from '../../components/ui';
import { api } from '../../api';
import { go } from '../../components/AppsShell';
import { AccountingBadge } from '../../components/StatusBadge';
import { accountingStatusLabel } from '../../lib/requestText';
import type { AccountingEntry } from '../../types';
import { errorText, when, type Ctx } from './common';

const filters = ['all', 'pending', 'sent', 'processed', 'failed'] as const;

type PayloadField = { label: string; value: unknown; valueLabel?: string; currency?: string };
const payloadRequester = (e: AccountingEntry) => String((e.payload.requester as { name?: string } | undefined)?.name ?? '—');
const payloadSummary = (e: AccountingEntry) => String(e.payload.summary ?? '');
const payloadFields = (e: AccountingEntry) => (Array.isArray(e.payload.fields) ? (e.payload.fields as PayloadField[]) : []);
const fieldText = (f: PayloadField) => (f.valueLabel ?? (typeof f.value === 'boolean' ? (f.value ? 'Evet' : 'Hayır') : Array.isArray(f.value) ? f.value.join(', ') : `${f.value}${f.currency ? ` ${f.currency}` : ''}`));

export function Accounting({ workspace }: Ctx) {
  const ws = workspace.id;
  const [status, setStatus] = useState<(typeof filters)[number]>('all');
  const [entries, setEntries] = useState<AccountingEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [open, setOpen] = useState<string | null>(null);

  const load = useCallback(() => {
    api.accountingEntries(ws, status === 'all' ? undefined : status).then((r) => { setEntries(r); setError(null); }).catch((e) => setError(errorText(e, 'Kayıtlar yüklenemedi.')));
  }, [ws, status]);
  useEffect(() => { setEntries(null); load(); }, [load]);

  const run = async (id: string, fn: () => Promise<unknown>) => {
    setBusy(id);
    try { await fn(); load(); } catch (e) { setError(errorText(e)); } finally { setBusy(null); }
  };

  return (
    <>
      <div className="page-head">
        <h1>Muhasebe</h1>
        <p className="tagline">Onaylanan talepler buraya düşer. Webhook tanımlıysa otomatik gönderilir; değilse elle işleyin veya CSV indirin.</p>
        <div className="row" style={{ marginTop: 10 }}>
          <button className="btn" onClick={() => void api.downloadAccountingCsv(ws, status === 'all' ? undefined : status).catch((e) => setError(errorText(e)))}>CSV indir</button>
          {workspace.role === 'admin' && <button className="btn" onClick={() => go({ name: 'admin', sub: 'accounting' })}>Muhasebe ayarları</button>}
        </div>
      </div>
      <div className="page-body">
        <div className="tabs"><div className="seg" role="tablist">
          {filters.map((f) => <button key={f} role="tab" aria-selected={status === f} onClick={() => setStatus(f)}>{f === 'all' ? 'Tümü' : accountingStatusLabel[f]}</button>)}
        </div></div>
        {error && <div className="notice error" role="alert" style={{ marginBottom: 10 }}>{error}</div>}
        {entries === null && !error && <p className="muted">Yükleniyor…</p>}
        {entries?.length === 0 && <EmptyState icon={<IconPackage size={30} />} title="Kayıt yok">Muhasebeye aktarılacak onaylı talep bulunmuyor.</EmptyState>}
        {entries && entries.length > 0 && (
          <div className="card table-wrap">
            <table>
              <thead><tr><th>Numara</th><th>Kategori</th><th>Talep eden</th><th>Özet</th><th>Durum</th><th>Tarih</th><th>Eylem</th></tr></thead>
              <tbody>
                {entries.map((e) => (
                  <Fragment key={e.id}>
                    <tr>
                      <td><button className="btn small" onClick={() => go({ name: 'request', id: e.requestId })}>{e.number}</button></td>
                      <td>{e.category}</td>
                      <td>{payloadRequester(e)}</td>
                      <td>{payloadSummary(e)}</td>
                      <td>
                        <AccountingBadge status={e.status} />
                        {e.lastError && <div className="muted" title={e.lastError}>{e.attempts}. deneme: {e.lastError}</div>}
                        {e.status === 'processed' && <div className="muted">{e.processedByName}{e.note ? ` — ${e.note}` : ''}</div>}
                      </td>
                      <td>{when(e.createdAt)}</td>
                      <td>
                        <div className="actions">
                          <button className="btn small" onClick={() => setOpen(open === e.id ? null : e.id)} aria-expanded={open === e.id}>Ayrıntı</button>
                          {e.status !== 'processed' && (
                            <button className="btn small primary" disabled={busy === e.id} onClick={() => {
                              const note = window.prompt('İşlem notu (isteğe bağlı):', '');
                              if (note !== null) void run(e.id, () => api.processEntry(ws, e.id, note));
                            }}>İşlendi</button>
                          )}
                          {e.status === 'failed' && <button className="btn small" disabled={busy === e.id} onClick={() => void run(e.id, () => api.retryEntry(ws, e.id))}>Yeniden dene</button>}
                        </div>
                      </td>
                    </tr>
                    {open === e.id && (
                      <tr><td colSpan={7}>
                        <dl className="kv-table">
                          {payloadFields(e).map((f) => <div key={f.label} style={{ display: 'contents' }}><dt>{f.label}</dt><dd>{fieldText(f)}</dd></div>)}
                        </dl>
                      </td></tr>
                    )}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </>
  );
}
