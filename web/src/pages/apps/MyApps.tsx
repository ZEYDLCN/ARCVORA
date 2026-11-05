import { useEffect, useState } from 'react';
import { api } from '../../api';
import { go } from '../../components/AppsShell';
import { AppIcon } from '../../components/AppIcon';
import type { MyApp } from '../../types';
import { errorText } from './common';

export function MyApps() {
  const [apps, setApps] = useState<MyApp[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { api.myApps().then(setApps).catch((e) => setError(errorText(e, 'Uygulamalar yüklenemedi.'))); }, []);

  return (
    <>
      <div className="page-head">
        <h1>Uygulamalarım</h1>
        <p className="tagline">Yetkili olduğunuz uygulamalardan yeni talep oluşturun.</p>
      </div>
      <div className="page-body">
        {error && <div className="notice error" role="alert">{error}</div>}
        {apps === null && !error && <p className="muted">Yükleniyor…</p>}
        {apps?.length === 0 && (
          <div className="empty-state"><b>Henüz size atanmış bir uygulama yok</b><span>Bir uygulamayı kullanmak için master admin’den yetki isteyin.</span></div>
        )}
        <div className="proj-grid">
          {apps?.map((a) => (
            <button key={a.id} className="proj-card" onClick={() => go({ name: 'app', id: a.id })}>
              <span className="row"><AppIcon name={a.icon} /><span className="spacer" /><span className="badge neutral">v{a.versionNumber}</span></span>
              <b>{a.name}</b>
              <span className="muted">{a.description || 'Talep oluşturmak için açın.'}</span>
            </button>
          ))}
        </div>
      </div>
    </>
  );
}
