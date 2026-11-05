import { useEffect, useState } from 'react';
import { api } from '../../api';
import { go } from '../../components/AppsShell';
import { AppIcon } from '../../components/AppIcon';
import { TaskBadge } from '../../components/StatusBadge';
import { fallbackLabel } from '../../lib/requestText';
import type { MyTask } from '../../types';
import { errorText, when } from './common';

export function Inbox() {
  const [done, setDone] = useState(false);
  const [tasks, setTasks] = useState<MyTask[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { setTasks(null); api.myTasks(done).then(setTasks).catch((e) => setError(errorText(e, 'Görevler yüklenemedi.'))); }, [done]);

  return (
    <>
      <div className="page-head"><h1>Onay kutusu</h1><p className="tagline">Onayınızı bekleyen talepler ve verdiğiniz kararlar.</p></div>
      <div className="page-body">
        <div className="tabs"><div className="seg" role="tablist">
          <button role="tab" aria-selected={!done} onClick={() => setDone(false)}>Bekleyenler</button>
          <button role="tab" aria-selected={done} onClick={() => setDone(true)}>Geçmiş</button>
        </div></div>
        {error && <div className="notice error" role="alert">{error}</div>}
        {tasks === null && !error && <p className="muted">Yükleniyor…</p>}
        {tasks?.length === 0 && <div className="empty-state"><b>{done ? 'Henüz karar vermediniz' : 'Bekleyen göreviniz yok'}</b><span>{done ? '' : 'Yeni bir talep size atandığında burada görünür.'}</span></div>}
        <ul className="list">
          {tasks?.map((t) => (
            <li key={t.id}>
              <button className="item" onClick={() => go({ name: 'request', id: t.requestId })}>
                <span className="row">
                  <AppIcon name={t.icon} />
                  <span style={{ flex: 1, minWidth: 0 }}>
                    <span className="title">{t.requestNumber} · {t.appName}</span>
                    <span className="muted" style={{ display: 'block' }}>{t.requesterName} · {t.summary || '—'}</span>
                  </span>
                  <TaskBadge status={t.status} />
                </span>
                <span className="meta">
                  <span className="badge neutral">{t.stepName}</span>
                  {t.fallback && <span className="badge warn" title={`Size atanma nedeni: ${fallbackLabel(t.fallback)} master admin’e yönlendirildi`}>Yönlendirilmiş görev</span>}
                  <span className="muted">{when(t.decidedAt ?? t.createdAt)}</span>
                  {t.comment && <span className="muted">“{t.comment}”</span>}
                </span>
              </button>
            </li>
          ))}
        </ul>
      </div>
    </>
  );
}
