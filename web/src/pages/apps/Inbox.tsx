import { useEffect, useState } from 'react';
import { api } from '../../api';
import { go } from '../../components/AppsShell';
import { RequestCard } from '../../components/RequestCard';
import { IconInbox, IconUser } from '../../components/icons';
import { EmptyState } from '../../components/ui';
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
        {tasks?.length === 0 && <EmptyState icon={<IconInbox size={30} />} title={done ? 'Henüz karar vermediniz' : 'Bekleyen göreviniz yok'}>{done ? '' : 'Yeni bir talep size atandığında burada görünür.'}</EmptyState>}
        <ul className="list">
          {tasks?.map((t) => (
            <li key={t.id}>
              <RequestCard icon={t.icon} number={t.requestNumber} appName={t.appName} person={t.requesterName} isDemo={t.isDemo}
                summary={`${t.appName} · ${t.summary || '—'}`} parts={t.summaryParts ? [{ key: '_app', label: 'Uygulama', kind: 'text', text: t.appName }, ...t.summaryParts] : undefined}
                badge={<TaskBadge status={t.status} />}
                chips={<>
                  <span className="chip-soft"><IconUser size={14} />{t.stepName}</span>
                  {t.onBehalfOfName && <span className="badge info" title="Bu görev başka bir kişiye atanmış; vekâlet gereği siz de karar verebilirsiniz">{t.onBehalfOfName} adına (vekâleten)</span>}
                  {t.fallback && <span className="badge warn" title={`Size atanma nedeni: ${fallbackLabel(t.fallback)} master admin’e yönlendirildi`}>Yönlendirilmiş görev</span>}
                </>}
                time={when(t.decidedAt ?? t.createdAt)} note={t.comment} onOpen={() => go({ name: 'request', id: t.requestId })} />
            </li>
          ))}
        </ul>
      </div>
    </>
  );
}
