import { useEffect, useState } from 'react';
import { api } from '../../api';
import { AppIcon } from '../../components/AppIcon';
import { go } from '../../components/AppsShell';
import { emptyDefinition } from '../../lib/flow';
import { flowTemplates } from '../../lib/templates';
import type { AppSummary } from '../../types';
import { errorText, when, type Ctx } from './common';

const statusLabel = { draft: 'Taslak', published: 'Yayında', archived: 'Arşivde' } as const;
const statusTone = { draft: 'warn', published: 'ok', archived: 'neutral' } as const;

export function DesignList({ workspace }: Ctx) {
  const ws = workspace.id;
  const [apps, setApps] = useState<AppSummary[] | null>(null);
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => { api.apps(ws).then(setApps).catch((e) => setError(errorText(e, 'Uygulamalar yüklenemedi.'))); }, [ws]);

  const create = async (body: Parameters<typeof api.createApp>[1]) => {
    setBusy(true);
    setError(null);
    try { go({ name: 'designer', id: (await api.createApp(ws, body)).summary.id }); }
    catch (e) { setError(errorText(e)); setBusy(false); }
  };

  return (
    <>
      <div className="page-head">
        <h1>Uygulama tasarla</h1>
        <p className="tagline">Şirketinizin akışlarını (izin, avans, masraf…) tanımlayın; yayınlayınca yetkili çalışanlar kullanabilir.</p>
      </div>
      <div className="page-body">
        {error && <div className="notice error" role="alert" style={{ marginBottom: 12 }}>{error}</div>}

        <div className="card" style={{ maxWidth: 980, marginBottom: 18 }}>
          <h3>Şablondan başla</h3>
          <div className="template-grid" style={{ marginTop: 8 }}>
            {flowTemplates.map((t) => (
              <button key={t.id} className="proj-card" disabled={busy} onClick={() => void create({ name: t.name, description: '', icon: t.icon, definition: t.definition })}>
                <span className="row"><AppIcon name={t.icon} /></span>
                <b>{t.name}</b>
                <span className="muted" style={{ fontSize: 13 }}>{t.description}</span>
              </button>
            ))}
          </div>
          <form className="inline-form" style={{ marginTop: 16 }} onSubmit={(e) => { e.preventDefault(); void create({ name, definition: emptyDefinition() }); }}>
            <div><label htmlFor="new-app">Boş uygulama</label><input id="new-app" type="text" required maxLength={200} value={name} onChange={(e) => setName(e.target.value)} placeholder="Uygulama adı" /></div>
            <button className="btn primary" disabled={busy || !name.trim()}>Oluştur</button>
          </form>
        </div>

        {apps === null && !error && <p className="muted">Yükleniyor…</p>}
        {apps?.length === 0 && <div className="empty-state"><b>Henüz uygulama yok</b><span>Yukarıdan bir şablon seçin veya boş bir uygulama oluşturun.</span></div>}
        <div className="proj-grid">
          {apps?.map((a) => (
            <button key={a.id} className="proj-card" onClick={() => go({ name: 'designer', id: a.id })}>
              <span className="row"><AppIcon name={a.icon} /><span className="spacer" /><span className={`badge ${statusTone[a.status]}`}>{statusLabel[a.status]}</span></span>
              <b>{a.name}</b>
              <span className="muted">{a.versionNumber > 0 ? `Yayın v${a.versionNumber}` : 'Hiç yayınlanmadı'} · {when(a.updatedAt)}</span>
              {a.hasDraftChanges && a.versionNumber > 0 && <span className="badge warn">Yayınlanmamış değişiklik var</span>}
            </button>
          ))}
        </div>
      </div>
    </>
  );
}
