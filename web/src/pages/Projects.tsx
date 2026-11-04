import { useEffect, useMemo, useState } from 'react';
import { api, ApiError } from '../api';
import { IconFolder, IconPlus, IconSparkle } from '../components/icons';
import { Shell, type NavItem } from '../components/Shell';
import { TechLogo } from '../components/TechLogo';
import type { PaletteItem } from '../components/CommandPalette';
import type { LogoId } from '../lib/logos.generated';
import type { ProfileInfo, ProjectSummary, WorkspaceInfo } from '../types';

interface Props {
  user: { displayName: string };
  workspaces: WorkspaceInfo[];
  onOpen: (id: string) => void;
  onLogout: () => void;
  dark: boolean;
  onTheme: () => void;
}

const profileLogo: Record<string, LogoId> = { aspnetcore: 'dotnet', springboot: 'springboot' };

export function Projects({ user, workspaces, onOpen, onLogout, dark, onTheme }: Props) {
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
  const createSample = () => guard(async () => onOpen((await api.createSample(ws.id)).summary.id));

  const nav: NavItem[] = [
    { id: 'projects', label: 'Projeler', icon: <IconFolder />, active: !creating, badge: projects?.length, onClick: () => setCreating(false) },
    { id: 'new', label: 'İlk mimarini oluştur', icon: <IconPlus />, active: creating, onClick: () => setCreating(true) },
    { id: 'sample', label: 'Örnek projeyi incele', icon: <IconSparkle />, onClick: () => { if (ws && !busy) void createSample(); } },
  ];

  const search = useMemo<PaletteItem[]>(() => [
    { id: 'a-new', label: 'İlk mimarini oluştur', group: 'Eylemler', onSelect: () => setCreating(true) },
    { id: 'a-sample', label: 'Örnek projeyi incele', hint: 'Masraf yönetimi', group: 'Eylemler', onSelect: () => { void createSample(); } },
    ...(projects ?? []).map((p) => ({ id: p.id, label: p.name, hint: `v${p.headNumber}`, group: 'Projeler', onSelect: () => onOpen(p.id) })),
  // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [projects]);

  return (
    <Shell nav={nav} navTitle="Çalışma alanı" user={user} dark={dark} onTheme={onTheme} onLogout={onLogout}
      searchItems={search} searchPlaceholder="Proje veya eylem ara…">
      <div className="page-head">
        <h1>{creating ? 'Yeni proje' : 'Projeler'}</h1>
        <p className="tagline">Fikrini anlat, kararlarını netleştir, mimariyi birlikte geliştir.</p>
        <div className="row" style={{ marginTop: 10 }}>
          <button className="btn primary" onClick={() => setCreating((c) => !c)}>{creating ? 'Listeye dön' : 'İlk mimarini oluştur'}</button>
          <button className="btn" disabled={busy || !ws} onClick={() => void createSample()}>Örnek projeyi incele</button>
        </div>
        <p className="muted" style={{ marginTop: 8 }}>Örnek proje elle hazırlanmış bir masraf yönetimi modelidir (yapay zekâ çıktısı değildir) ve iki aşamalı onay değişikliğini içerir.</p>
      </div>

      <div className="page-body">
        {error && <div className="notice error" role="alert" style={{ marginBottom: 12 }}>{error}</div>}

        {creating && ws && (
          <form className="card" style={{ maxWidth: 760 }} onSubmit={(e) => { e.preventDefault(); void guard(async () => {
            const d = await api.createProject({ workspaceId: ws.id, name, description: '', techProfileId: profile, ideaText: idea });
            onOpen(d.summary.id);
          }); }}>
            <label htmlFor="pname" style={{ marginTop: 0 }}>Proje adı</label>
            <input id="pname" type="text" required maxLength={200} value={name} onChange={(e) => setName(e.target.value)} />
            <label htmlFor="idea">Fikrin, beklediğin özellikler, ekip koşulları <span className="muted">(düz metin veya Markdown; bilmediklerini yazmak zorunda değilsin)</span></label>
            <textarea id="idea" rows={8} value={idea} onChange={(e) => setIdea(e.target.value)}
              placeholder="Örn. Şirketlerin çalışan masraflarını yönettiği bir SaaS geliştireceğiz. Çalışanlar fiş yükleyip masraf girecek. Yönetici onaylayacak…" />
            <label id="profile-label">Teknoloji profili</label>
            <div className="profile-cards" role="radiogroup" aria-labelledby="profile-label">
              {(profiles.length ? profiles : [{ id: 'aspnetcore', name: 'ASP.NET Core', selectable: true } as ProfileInfo]).map((p) => (
                <button key={p.id} type="button" role="radio" aria-checked={profile === p.id} className="profile-card" disabled={!p.selectable}
                  onClick={() => setProfile(p.id)}>
                  <span className="pc-logo">{profileLogo[p.id] ? <TechLogo id={profileLogo[p.id]} size={26} surfaceDark={false} /> : null}</span>
                  <span><b>{p.name}</b><br /><span className="muted">{p.selectable ? 'Doğrulanmış profil' : 'Planlandı · henüz doğrulanmadı'}</span></span>
                </button>
              ))}
            </div>
            <p style={{ marginTop: 16 }}><button className="btn primary" disabled={busy || !name.trim()}>Projeyi oluştur</button></p>
          </form>
        )}

        {!creating && (
          <>
            {projects === null && !error && <p className="muted">Yükleniyor…</p>}
            {projects?.length === 0 && <div className="empty-state"><b>Henüz proje yok</b><span>“İlk mimarini oluştur” ile başlayın veya örnek projeyi inceleyin.</span></div>}
            <div className="proj-grid">
              {projects?.map((p) => (
                <button key={p.id} className="proj-card" onClick={() => onOpen(p.id)}>
                  <span className="row">
                    {profileLogo[p.techProfileId] && <TechLogo id={profileLogo[p.techProfileId]} size={22} />}
                    <span className="spacer" />
                    <span className="badge neutral">{p.role}</span>
                  </span>
                  <b>{p.name}</b>
                  <span className="muted">v{p.headNumber} · {new Date(p.updatedAt).toLocaleString('tr-TR')}</span>
                  <span className="row" style={{ marginTop: 'auto' }}>
                    {p.pendingProposals > 0 ? <span className="badge info">◔ {p.pendingProposals} bekleyen öneri</span> : <span className="badge ok">✓ Kabul edilmiş sürüm</span>}
                  </span>
                </button>
              ))}
            </div>
          </>
        )}
      </div>
    </Shell>
  );
}
