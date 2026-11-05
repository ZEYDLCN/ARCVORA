import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api';
import { go } from '../../components/AppsShell';
import { RequestBadge, TaskBadge } from '../../components/StatusBadge';
import { eventText, fallbackLabel, stepStates } from '../../lib/requestText';
import { formatValue } from '../../lib/flow';
import { AttachmentList } from '../../components/AttachmentList';
import type { RequestDetail as Detail } from '../../types';
import { errorText, when } from './common';

export function RequestDetail({ id }: { id: string }) {
  const [d, setD] = useState<Detail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [comment, setComment] = useState('');
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => { api.requestDetail(id).then((r) => { setD(r); setError(null); }).catch((e) => setError(errorText(e, 'Talep yüklenemedi.'))); }, [id]);
  useEffect(() => { setD(null); load(); }, [load]);

  const act = async (fn: () => Promise<Detail>) => {
    setBusy(true);
    setError(null);
    try { setD(await fn()); setComment(''); window.dispatchEvent(new Event('arcvora:tasks-changed')); }
    catch (e) { setError(errorText(e)); load(); }
    finally { setBusy(false); }
  };

  if (error && !d) return <div className="page-body"><div className="notice error" role="alert">{error}</div></div>;
  if (!d) return <div className="page-body"><p className="muted">Yükleniyor…</p></div>;

  const s = d.summary;
  const states = stepStates(d.definition.steps, d.tasks, s.status);
  const taskId = d.myPendingTaskId;

  return (
    <>
      <div className="page-head">
        <p><button className="btn small" onClick={() => history.length > 1 ? history.back() : go({ name: 'requests' })}>← Geri</button></p>
        <h1>{s.number}</h1>
        <div className="status-row"><span className="muted">{s.appName}</span><RequestBadge status={s.status} /><span className="muted">· {s.requesterName} · {when(s.createdAt)}</span></div>
      </div>
      <div className="page-body">
        {error && <div className="notice error" role="alert" style={{ marginBottom: 12 }}>{error}</div>}

        <div className="card" style={{ maxWidth: 860 }}>
          <h3>Onay akışı</h3>
          <div className="step-strip">
            {d.definition.steps.map((st, i) => (
              <span key={st.id} style={{ display: 'contents' }}>
                {i > 0 && <span className="arrow" aria-hidden="true">→</span>}
                <span className={`step ${states[i] === 'upcoming' || states[i] === 'skipped' ? '' : states[i]}`}>
                  {states[i] === 'done' ? '✓ ' : states[i] === 'rejected' ? '✕ ' : states[i] === 'skipped' ? '⤼ ' : ''}{st.name}{states[i] === 'skipped' ? ' (atlandı)' : ''}
                </span>
              </span>
            ))}
          </div>

          <h3>Talep bilgileri</h3>
          <dl className="kv-table">
            {d.definition.fields.map((f) => (
              <div key={f.key} style={{ display: 'contents' }}>
                <dt>{f.label}</dt><dd>{formatValue(f, d.data[f.key], d.people)}</dd>
              </div>
            ))}
          </dl>

          {(d.attachments ?? []).length > 0 && (
            <>
              <h3>Ekler</h3>
              <AttachmentList attachments={d.attachments} labels={Object.fromEntries(d.definition.fields.map((f) => [f.key, f.label]))} />
            </>
          )}

          {taskId && s.status === 'in_review' && (
            <div className="decision-box">
              <b>Bu talep sizin onayınızı bekliyor</b>
              {d.tasks.find((t) => t.id === taskId)?.fallback && (
                <span className="muted">Size atanma nedeni: {fallbackLabel(d.tasks.find((t) => t.id === taskId)?.fallback)} master admin’e yönlendirildi.</span>
              )}
              <label htmlFor="decision-comment" style={{ margin: 0 }}>Yorum <span className="muted">(reddetmek için zorunlu)</span></label>
              <textarea id="decision-comment" value={comment} maxLength={1000} onChange={(e) => setComment(e.target.value)} />
              <div className="row">
                <button className="btn primary" disabled={busy} onClick={() => void act(() => api.approveTask(taskId, comment))}>Onayla</button>
                <button className="btn danger" disabled={busy || comment.trim() === ''} onClick={() => void act(() => api.rejectTask(taskId, comment))}
                  title={comment.trim() === '' ? 'Reddetmek için gerekçe yazın' : undefined}>Reddet</button>
              </div>
            </div>
          )}

          <p style={{ marginTop: 14 }}>
            <button className="btn" disabled={busy} onClick={() => { setBusy(true); void api.downloadRequestPdf(id, `${s.number}.pdf`).catch((e) => setError(errorText(e, 'PDF oluşturulamadı.'))).finally(() => setBusy(false)); }}>
              PDF indir / yazdır
            </button>
          </p>

          {d.canCancel && (
            <p style={{ marginTop: 14 }}>
              <button className="btn danger" disabled={busy} onClick={() => { if (window.confirm('Bu talebi iptal etmek istediğinize emin misiniz?')) void act(() => api.cancelRequest(id)); }}>Talebi iptal et</button>
            </p>
          )}
        </div>

        <div className="card" style={{ maxWidth: 860, marginTop: 14 }}>
          <h3>Görevler</h3>
          {d.tasks.length === 0 && <p className="muted">Onay gerektirmedi.</p>}
          <ul className="list" style={{ marginTop: 6 }}>
            {d.tasks.map((t) => (
              <li key={t.id} className="row" style={{ gap: 10 }}>
                <span style={{ flex: 1 }}>{t.stepName} · <b>{d.people[t.assigneeId] ?? '—'}</b>{t.comment ? <span className="muted"> — {t.comment}</span> : null}</span>
                <TaskBadge status={t.status} />
              </li>
            ))}
          </ul>
          <h3 style={{ marginTop: 18 }}>Zaman çizelgesi</h3>
          <ol className="timeline">
            {d.events.map((e) => (
              <li key={e.id} className={e.kind === 'rejected' || e.kind === 'accounting_failed' ? 'bad' : e.kind === 'approved' || e.kind === 'completed' ? 'ok' : e.kind === 'step_skipped' ? 'skip' : ''}>
                {eventText(e, d.people)}
                <time dateTime={e.at}>{when(e.at)}</time>
              </li>
            ))}
          </ol>
        </div>
      </div>
    </>
  );
}
