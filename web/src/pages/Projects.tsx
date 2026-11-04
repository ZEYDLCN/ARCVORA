import { useEffect, useState } from 'react';
import { api, ApiError } from '../api';
import type { ProfileInfo, ProjectSummary, WorkspaceInfo } from '../types';

interface Props {
  user: { displayName: string };
  workspaces: WorkspaceInfo[];
  onOpen: (id: string) => void;
  onLogout: () => void;
  theme: 'dark' | 'light';
  onTheme: () => void;
}

export function Projects({ user, workspaces, onOpen, onLogout, theme, onTheme }: Props) {
  const [projects, setProjects] = useState<ProjectSummary[] | null>(null);
  const [profiles, setProfiles] = useState<ProfileInfo[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [busy, setBusy] = useState(false);
  const [name, setName] = useState('');
  const [idea, setIdea] = useState('');
  const [profile, setProfile] = useState('aspnetcore');
  const ws = workspaces[0];

  useEffect(() => {
    api.projects().then(setProjects).catch((e) => setError(e instanceof ApiError ? e.message : 'Projeler yüklenemedi.'));
    api.profiles().then(setProfiles).catch(() => setProfiles([]));
  }, []);

  const guard = async (fn: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try { await fn(); } catch (e) { setError(e instanceof ApiError ? e.message : 'İşlem başarısız.'); } finally { setBusy(false); }
  };

  return (
    <main className="page">
      <div className="row">
        <div className="brand"><span className="mark">▲</span> ARCVORA</div>
        <span className="spacer" />
        <span className="muted">{user.displayName}</span>
        <button className="btn small" onClick={onTheme} aria-label="Temayı değiştir">{theme === 'dark' ? '☀ Açık' : '☾ Koyu'}</button>
        <button className="btn small" onClick={onLogout}>Çıkış</button>
      </div>

      <h1 style={{ marginTop: 24 }}>Fikrini, ekibinin geliştirebileceği bir mimariye dönüştür.</h1>
      <p className="tagline">Gereksinimlerini anlat. Eksik kararları keşfet. Mimari seçeneklerini değerlendir. Yeni bir özellik istediğinde, hangi parçaların değişeceğini aynı çalışma alanında gör.</p>
      <div className="row">
        <button className="btn primary" onClick={() => setCreating((c) => !c)}>İlk mimarini oluştur</button>
        <button className="btn" disabled={busy || !ws} onClick={() => guard(async () => onOpen((await api.createSample(ws.id)).summary.id))}>Örnek projeyi incele</button>
      </div>
      <p className="muted">Örnek proje elle hazırlanmış bir masraf yönetimi modelidir (yapay zekâ çıktısı değildir) ve iki aşamalı onay değişikliğini içerir.</p>

      {error && <div className="notice error" role="alert">{error}</div>}

      {creating && ws && (
        <form className="card" onSubmit={(e) => { e.preventDefault(); void guard(async () => {
          const d = await api.createProject({ workspaceId: ws.id, name, description: '', techProfileId: profile, ideaText: idea });
          onOpen(d.summary.id);
        }); }}>
          <h2>Yeni proje</h2>
          <label htmlFor="pname">Proje adı</label>
          <input id="pname" type="text" required maxLength={200} value={name} onChange={(e) => setName(e.target.value)} />
          <label htmlFor="idea">Fikrin, beklediğin özellikler, ekip koşulları <span className="muted">(düz metin veya Markdown; bilmediklerini yazmak zorunda değilsin)</span></label>
          <textarea id="idea" rows={8} value={idea} onChange={(e) => setIdea(e.target.value)}
            placeholder="Örn. Şirketlerin çalışan masraflarını yönettiği bir SaaS geliştireceğiz. Çalışanlar fiş yükleyip masraf girecek. Yönetici onaylayacak…" />
          <label htmlFor="profile">Teknoloji profili</label>
          <select id="profile" value={profile} onChange={(e) => setProfile(e.target.value)}>
            {profiles.map((p) => (
              <option key={p.id} value={p.id} disabled={!p.selectable}>{p.name}{p.selectable ? '' : ' — planlandı, henüz doğrulanmadı'}</option>
            ))}
            {profiles.length === 0 && <option value="aspnetcore">ASP.NET Core</option>}
          </select>
          <p><button className="btn primary" disabled={busy || !name.trim()}>Projeyi oluştur</button></p>
        </form>
      )}

      <h2 style={{ marginTop: 24 }}>Projeler</h2>
      {projects === null && !error && <p className="muted">Yükleniyor…</p>}
      {projects?.length === 0 && <p className="muted">Henüz proje yok.</p>}
      <ul className="project-list">
        {projects?.map((p) => (
          <li key={p.id}>
            <button className="project-item" onClick={() => onOpen(p.id)}>
              <span><b>{p.name}</b><br /><span className="muted">v{p.headNumber} · {new Date(p.updatedAt).toLocaleString('tr-TR')}</span></span>
              <span className="spacer" />
              {p.pendingProposals > 0 && <span className="badge info">◔ {p.pendingProposals} bekleyen öneri</span>}
              <span className="badge neutral">{p.role}</span>
            </button>
          </li>
        ))}
      </ul>
    </main>
  );
}
