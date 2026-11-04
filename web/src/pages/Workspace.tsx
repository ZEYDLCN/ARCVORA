import { useCallback, useEffect, useMemo, useState } from 'react';
import { api, ApiError } from '../api';
import { ArchCanvas } from '../components/ArchCanvas';
import { Badge } from '../components/Badge';
import type { PaletteItem } from '../components/CommandPalette';
import {
  IconChecklist, IconDecision, IconDiagram, IconDownload, IconFile, IconFolder, IconHelp, IconInbox, IconPlus, IconShield,
} from '../components/icons';
import { MermaidView } from '../components/MermaidView';
import { FindingList, ObjectDetail } from '../components/ObjectDetail';
import { ProposalReview } from '../components/ProposalReview';
import { Shell, type NavItem } from '../components/Shell';
import { TechLogo } from '../components/TechLogo';
import {
  buildIndex, findingKindLabel, kindLabel, originInfo, priorityLabel, reviewInfo, textAlternative, typeOfId,
} from '../lib/model';
import type { LogoId } from '../lib/logos.generated';
import type { Finding, Job, Model, ProjectDetail, ProposalSummary, VersionInfo, View } from '../types';

type Section = 'arch' | 'req' | 'dec' | 'oq' | 'src' | 'val' | 'prop';
type ArchTab = 'canvas' | 'component' | 'er' | 'sequence';

const jobLabel: Record<string, string> = { analysis: 'Gereksinim çıkarımı', architecture: 'Mimari taslak', change: 'Değişiklik analizi' };
const stageLabel: Record<string, string> = {
  generate: 'Taslak üretiliyor', validate: 'Doğrulanıyor', repair: 'Hatalar düzeltiliyor', analyze: 'Etki analiz ediliyor',
};
const sectionTitle: Record<Section, string> = {
  arch: 'Mimari', req: 'Gereksinimler', dec: 'Kararlar', oq: 'Açık sorular ve varsayımlar', src: 'Kaynaklar', val: 'Doğrulama', prop: 'Değişiklik önerileri',
};
const navLabel: Record<Section, string> = { arch: 'Mimari', req: 'Gereksinimler', dec: 'Kararlar', oq: 'Açık sorular', src: 'Kaynaklar', val: 'Doğrulama', prop: 'Öneriler' };
const profileLogo: Record<string, LogoId> = { aspnetcore: 'dotnet', springboot: 'springboot' };

interface Notice { type: 'error' | 'ok'; text: string; lines?: string[] }

interface Props {
  projectId: string;
  user: { displayName: string };
  onBack: () => void;
  onLogout: () => void;
  dark: boolean;
  onTheme: () => void;
}

export function Workspace({ projectId, user, onBack, onLogout, dark, onTheme }: Props) {
  const [detail, setDetail] = useState<ProjectDetail | null>(null);
  const [versions, setVersions] = useState<VersionInfo[]>([]);
  const [proposals, setProposals] = useState<ProposalSummary[]>([]);
  const [jobs, setJobs] = useState<Job[]>([]);
  const [viewNo, setViewNo] = useState<number | null>(null);
  const [histModel, setHistModel] = useState<Model | null>(null);
  const [views, setViews] = useState<View[]>([]);
  const [findings, setFindings] = useState<Finding[]>([]);
  const [selected, setSelected] = useState<string | null>(null);
  const [openProposal, setOpenProposal] = useState<string | null>(null);
  const [section, setSection] = useState<Section>('arch');
  const [archTab, setArchTab] = useState<ArchTab>('canvas');
  const [flowId, setFlowId] = useState<string | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [sourceText, setSourceText] = useState('');
  const [changeText, setChangeText] = useState('');
  const [showChange, setShowChange] = useState(false);
  const [busy, setBusy] = useState(false);

  const report = useCallback((e: unknown) => {
    if (e instanceof ApiError) setNotice({ type: 'error', text: e.message, lines: e.detailLines });
    else setNotice({ type: 'error', text: 'Beklenmeyen bir hata oluştu.' });
  }, []);

  const loadAll = useCallback(async () => {
    try {
      const [d, v, p, j] = await Promise.all([api.project(projectId), api.versions(projectId), api.proposals(projectId), api.jobs(projectId)]);
      setDetail(d); setVersions(v); setProposals(p); setJobs(j);
      setLoadError(null);
    } catch (e) {
      setLoadError(e instanceof ApiError ? e.message : 'Proje yüklenemedi.');
    }
  }, [projectId]);

  useEffect(() => { void loadAll(); }, [loadAll]);

  const headNo = detail?.head.number ?? 0;
  const effectiveNo = viewNo ?? headNo;
  const readOnly = viewNo !== null && viewNo !== headNo;
  const model: Model | null = readOnly ? histModel : detail?.model ?? null;

  // Seçili sürümün görünümleri ve bulguları (hepsi aynı model sürümünden).
  useEffect(() => {
    if (!detail) return;
    let cancelled = false;
    (async () => {
      try {
        const [v, f] = await Promise.all([api.views(projectId, effectiveNo), api.findings(projectId, effectiveNo)]);
        if (cancelled) return;
        setViews(v.views); setFindings(f.findings);
        if (readOnly) setHistModel((await api.version(projectId, effectiveNo)).model);
      } catch (e) { if (!cancelled) report(e); }
    })();
    return () => { cancelled = true; };
  }, [detail, effectiveNo, readOnly, projectId, report]);

  // Çalışan işleri izle
  const active = jobs.filter((j) => j.status === 'queued' || j.status === 'running');
  useEffect(() => {
    if (active.length === 0) return;
    const t = setInterval(async () => {
      try {
        const fresh = await Promise.all(active.map((j) => api.job(j.id)));
        setJobs((cur) => cur.map((j) => fresh.find((f) => f.id === j.id) ?? j));
        if (fresh.some((f) => f.status !== 'queued' && f.status !== 'running')) void loadAll();
      } catch { /* sonraki turda yeniden dener */ }
    }, 1500);
    return () => clearInterval(t);
  }, [active.map((j) => j.id).join(','), loadAll]);   // eslint-disable-line react-hooks/exhaustive-deps

  const idx = useMemo(() => (model ? buildIndex(model) : null), [model]);
  const pendingProposals = proposals.filter((p) => p.status === 'pending');

  /** Bir nesneyi ilgili bölümde ve ayrıntı panelinde aç. */
  const goTo = useCallback((id: string) => {
    setOpenProposal(null);
    setSelected(id);
    const prefix = id.split('-')[0];
    if (prefix === 'REQ') setSection('req');
    else if (prefix === 'DEC') setSection('dec');
    else if (prefix === 'OQ' || prefix === 'ASM') setSection('oq');
    else if (prefix === 'SRC') setSection('src');
    else if (prefix === 'ENT') { setSection('arch'); setArchTab('er'); }
    else if (prefix === 'FLW') { setSection('arch'); setArchTab('sequence'); setFlowId(id); }
    else if (['CMP', 'REL', 'ACT', 'API'].includes(prefix)) { setSection('arch'); setArchTab((t) => (t === 'sequence' || t === 'er' ? 'canvas' : t)); }
  }, []);

  const searchItems = useMemo<PaletteItem[]>(() => {
    if (!model || !idx) return [];
    const sections: PaletteItem[] = (Object.keys(sectionTitle) as Section[]).map((s) => ({
      id: `sec-${s}`, label: sectionTitle[s], group: 'Bölümler', onSelect: () => { setSection(s); setSelected(null); setOpenProposal(null); },
    }));
    const objs = [...idx.values()].filter((o) => !['İzlenebilirlik'].includes(o.type)).map((o) => ({
      id: o.id, label: o.title, hint: `${o.id} · ${o.type}`, group: 'Nesneler', onSelect: () => goTo(o.id),
    }));
    const props: PaletteItem[] = proposals.map((p) => ({
      id: `prop-${p.id}`, label: p.title, hint: 'Öneri', group: 'Öneriler', onSelect: () => { setSection('prop'); setOpenProposal(p.id); setSelected(null); },
    }));
    return [...sections, ...props, ...objs];
  }, [model, idx, proposals, goTo]);

  const guarded = async (fn: () => Promise<void>) => {
    setBusy(true); setNotice(null);
    try { await fn(); } catch (e) { report(e); } finally { setBusy(false); }
  };

  const startJob = (fn: (key: string) => Promise<Job>) => guarded(async () => {
    const job = await fn(crypto.randomUUID());
    setJobs((cur) => [job, ...cur.filter((j) => j.id !== job.id)]);
  });

  const nav: NavItem[] = [
    { id: 'back', label: 'Tüm projeler', icon: <IconFolder />, onClick: onBack },
    ...([
      ['arch', <IconDiagram key="a" />, undefined],
      ['req', <IconChecklist key="r" />, model?.requirements.length],
      ['dec', <IconDecision key="d" />, model?.decisions.length],
      ['oq', <IconHelp key="q" />, model?.openQuestions.filter((q) => q.status !== 'answered').length],
      ['src', <IconFile key="s" />, model?.sources.length],
      ['val', <IconShield key="v" />, findings.filter((f) => f.severity !== 'info').length],
      ['prop', <IconInbox key="p" />, pendingProposals.length],
    ] as const).map(([s, icon, badge]) => ({
      id: s, label: navLabel[s], icon, badge,
      active: section === s, onClick: () => { setSection(s); setSelected(null); setOpenProposal(null); },
    })),
  ];

  if (loadError) return <div className="auth"><div className="auth-form"><div className="notice error" role="alert">{loadError}</div><p style={{ marginTop: 12 }}><button className="btn" onClick={onBack}>← Projeler</button></p></div></div>;
  if (!detail || !model || !idx) return <div className="auth"><div className="auth-form"><p className="muted">Çalışma alanı yükleniyor…</p></div></div>;

  const canEdit = detail.canEdit && !readOnly;
  const analyzed = new Set(model.requirements.flatMap((r) => r.sourceRefs.map((s) => s.sourceId)));
  const hasPendingSource = model.sources.some((s) => !analyzed.has(s.id));
  const hasReq = model.requirements.some((r) => r.reviewStatus !== 'rejected');
  const hasArch = model.components.length > 0;
  const drafting = pendingProposals.length > 0;
  const detailOpen = openProposal !== null || selected !== null;
  const currentHash = (versions.find((v) => v.number === effectiveNo)?.contentHash ?? detail.head.contentHash).slice(0, 8);

  const flowViews = views.filter((v) => v.kind === 'sequence');
  const currentFlow = flowViews.find((v) => v.flowId === flowId) ?? flowViews[0];
  const mermaidView: View | undefined = archTab === 'sequence' ? currentFlow : views.find((v) => v.kind === archTab);

  const exportAs = (format: 'markdown' | 'package') => guarded(async () => {
    const a = await api.exportProject(projectId, effectiveNo, format);
    await api.download(projectId, a);
    setNotice({ type: 'ok', text: `v${a.versionNumber} dışa aktarıldı (model özeti ${a.modelHash.slice(0, 12)}).` });
  });
  const updateReq = async (id: string, body: Record<string, unknown>) => {
    try { await api.updateRequirement(projectId, id, { ...body, baseVersion: headNo }); await loadAll(); } catch (e) { report(e); }
  };
  const answerQ = async (id: string, body: Record<string, unknown>) => {
    try { await api.answerQuestion(projectId, id, { ...body, baseVersion: headNo }); await loadAll(); } catch (e) { report(e); }
  };

  const objectCard = (id: string, title: string, meta: React.ReactNode) => (
    <li key={id}>
      <button className="item" aria-current={selected === id} onClick={() => goTo(id)}>
        <span className="id">{id}</span><span className="title">{title}</span><span className="meta">{meta}</span>
      </button>
    </li>
  );

  return (
    <Shell nav={nav} navTitle="Proje" user={user} dark={dark} onTheme={onTheme} onLogout={onLogout}
      searchItems={searchItems} searchPlaceholder="Gereksinim, karar, bileşen, akış ara…">
      <div className="ws-head">
        <div style={{ minWidth: 0, flex: 1 }}>
          <h1>{detail.summary.name}</h1>
          <div className="row">
            <label htmlFor="ver" className="muted" style={{ margin: 0 }}>Sürüm</label>
            <select id="ver" style={{ width: 'auto', maxWidth: 420, padding: '5px 12px' }} value={effectiveNo}
              onChange={(e) => { const n = Number(e.target.value); setViewNo(n === headNo ? null : n); setSelected(null); }}>
              {versions.map((v) => <option key={v.id} value={v.number}>v{v.number} · {v.summary}{v.number === headNo ? ' (güncel)' : ''}</option>)}
            </select>
            <span className="badge neutral" title="Teknoloji profili">
              {profileLogo[detail.model.project.techProfileId] && <TechLogo id={profileLogo[detail.model.project.techProfileId]} size={14} />}
              {detail.model.project.techProfileId === 'aspnetcore' ? 'ASP.NET Core' : detail.model.project.techProfileId}
            </span>
            {drafting
              ? <span className="badge info" title="Bekleyen öneriler kabul edilmeden model değişmez">◔ Taslak: {pendingProposals.length} bekleyen öneri</span>
              : <span className="badge ok">✓ Kabul edilmiş sürüm</span>}
            <span className="muted mono">özet {currentHash}</span>
          </div>
        </div>
        <div className="row">
          <button className="btn" disabled={!detail.canEdit || readOnly} onClick={() => setShowChange((s) => !s)}><IconPlus size={16} /> Yeni talep ekle</button>
          <button className="btn" disabled={busy} onClick={() => exportAs('markdown')}><IconDownload size={16} /> Markdown</button>
          <button className="btn" disabled={busy} onClick={() => exportAs('package')}><IconDownload size={16} /> Paket</button>
        </div>
      </div>

      <div className={`ws-body ${detailOpen ? '' : 'no-detail'}`}>
        <section className="content" aria-label={sectionTitle[section]}>
          {readOnly && (
            <div className="banner warn" role="status">
              <span>Eski sürümü (v{effectiveNo}) görüntülüyorsunuz — salt okunur.</span>
              <button className="btn small" onClick={() => setViewNo(null)}>Güncel sürüme dön</button>
              {detail.canAccept && <button className="btn small" disabled={busy} onClick={() => guarded(async () => {
                await api.revert(projectId, effectiveNo, headNo); setViewNo(null); await loadAll();
                setNotice({ type: 'ok', text: `v${effectiveNo} içeriği yeni sürüm olarak geri yüklendi; geçmiş korundu.` });
              })}>Bu sürüme geri dön (yeni sürüm olarak)</button>}
            </div>
          )}

          {showChange && canEdit && (
            <form className="card" style={{ marginBottom: 12 }} onSubmit={(e) => { e.preventDefault(); void startJob((k) => api.changeRequest(projectId, changeText, k)).then(() => { setChangeText(''); setShowChange(false); }); }}>
              <label htmlFor="chg" style={{ marginTop: 0 }}>Yeni talep — kabul edilmiş sürüm üzerinde etkisi analiz edilir; sürüm yalnızca siz kabul edince oluşur.</label>
              <textarea id="chg" value={changeText} onChange={(e) => setChangeText(e.target.value)} placeholder="Örn. Artık şirketler kendi iki aşamalı onay sürecini tanımlayabilsin." />
              <div className="row" style={{ marginTop: 10 }}>
                <button className="btn primary" disabled={busy || changeText.trim().length < 3}>Etkiyi analiz et</button>
                <button type="button" className="btn" onClick={() => setShowChange(false)}>Vazgeç</button>
              </div>
            </form>
          )}

          {jobs.slice(0, 5).filter((j) => j.status !== 'succeeded' || active.some((a) => a.id === j.id)).map((j) => (
            <div key={j.id} className={`notice ${j.status === 'failed' ? 'error' : ''}`} style={{ marginBottom: 10 }} role="status">
              <div className="row">
                <b>{jobLabel[j.kind] ?? j.kind}</b>
                {j.status === 'queued' && <span className="badge neutral">◔ Kuyrukta</span>}
                {j.status === 'running' && <span className="badge info">⟳ {stageLabel[j.stage ?? ''] ?? 'Çalışıyor'}</span>}
                {j.status === 'failed' && <span className="badge bad">✕ Başarısız{j.errorCode ? ` (${j.errorCode})` : ''}</span>}
                {j.status === 'cancelled' && <span className="badge neutral">✕ İptal edildi</span>}
                {j.attempts > 1 && <span className="muted">deneme {j.attempts}/{j.maxAttempts}</span>}
                <span className="spacer" />
                {(j.status === 'queued' || j.status === 'running') && (
                  <button className="btn small" disabled={j.cancelRequested} onClick={() => guarded(async () => { await api.cancelJob(j.id); await loadAll(); })}>
                    {j.cancelRequested ? 'İptal isteniyor…' : 'İptal et'}
                  </button>
                )}
              </div>
              {j.status === 'failed' && <div>{j.error} Son kabul edilmiş model korundu.</div>}
              {j.status === 'failed' && j.problems.length > 0 && <ul>{j.problems.slice(0, 5).map((p) => <li key={p}>{p}</li>)}</ul>}
            </div>
          ))}

          {notice && (
            <div className={`notice ${notice.type === 'error' ? 'error' : ''}`} role={notice.type === 'error' ? 'alert' : 'status'} style={{ marginBottom: 10 }}>
              {notice.text}
              {notice.lines && notice.lines.length > 0 && <ul>{notice.lines.map((l) => <li key={l}>{l}</li>)}</ul>}
              <button className="btn small" style={{ marginTop: 6 }} onClick={() => setNotice(null)}>Kapat</button>
            </div>
          )}

          {/* ------------------------------ Mimari ------------------------------ */}
          {section === 'arch' && (
            <>
              <div className="tabs">
                <div className="seg" role="tablist" aria-label="Mimari görünüm">
                  <button role="tab" aria-selected={archTab === 'canvas'} onClick={() => setArchTab('canvas')}>Tuval</button>
                  <button role="tab" aria-selected={archTab === 'component'} onClick={() => setArchTab('component')}>Bileşenler (Mermaid)</button>
                  <button role="tab" aria-selected={archTab === 'er'} onClick={() => setArchTab('er')}>Veri modeli</button>
                  <button role="tab" aria-selected={archTab === 'sequence'} onClick={() => setArchTab('sequence')}>Akışlar ({flowViews.length})</button>
                </div>
                {archTab === 'sequence' && flowViews.length > 0 && (
                  <select aria-label="Akış seç" style={{ width: 'auto' }} value={currentFlow?.flowId ?? ''} onChange={(e) => setFlowId(e.target.value)}>
                    {flowViews.map((f) => <option key={f.flowId} value={f.flowId}>{f.title.replace('Akış: ', '')}</option>)}
                  </select>
                )}
                <span className="spacer" />
                {canEdit && (
                  <button className="btn" disabled={busy || !hasReq} title={hasReq ? '' : 'Önce gereksinim gerekli'}
                    onClick={() => startJob((k) => api.proposeArchitecture(projectId, k))}>{hasArch ? 'Mimari taslağı yenile' : 'Mimari taslak oluştur'}</button>
                )}
              </div>

              {archTab === 'canvas' ? <ArchCanvas model={model} selected={selected} onSelect={goTo} /> : mermaidView ? (
                <>
                  <MermaidView source={mermaidView.mermaid} dark={dark} onSelect={archTab === 'sequence' ? undefined : goTo} />
                  <details className="alt">
                    <summary>Metinsel karşılık ve Mermaid kaynağı</summary>
                    <ul>{textAlternative(model, archTab, mermaidView.flowId).map((l) => <li key={l}>{l}</li>)}</ul>
                    <pre className="notice mono" style={{ overflow: 'auto' }}>{mermaidView.mermaid}</pre>
                    <p className="muted">Bu dosyada dışarıda yapılan düzenlemeler modele otomatik aktarılmaz; model yapılandırılmış düzenleyici veya öneriler üzerinden değişir.</p>
                  </details>
                </>
              ) : <div className="empty-state"><b>{archTab === 'sequence' ? 'Henüz akış yok' : 'Görünüm yükleniyor…'}</b></div>}
            </>
          )}

          {/* ---------------------------- Gereksinimler ---------------------------- */}
          {section === 'req' && (
            <ul className="list">
              {model.requirements.map((r) => objectCard(r.id, r.text, <>
                <Badge info={reviewInfo[r.reviewStatus]} /><Badge info={originInfo[r.origin]} />
                <span className="badge neutral">{kindLabel[r.kind]} · {priorityLabel[r.priority]}</span>
              </>))}
              {model.requirements.length === 0 && <div className="empty-state"><b>Henüz gereksinim yok</b><span>Kaynaklar bölümünden bir metin ekleyip “Gereksinimleri çıkar”ı çalıştırın.</span></div>}
            </ul>
          )}

          {/* ------------------------------ Kararlar ------------------------------ */}
          {section === 'dec' && (
            <ul className="list">
              {model.decisions.map((d) => objectCard(d.id, d.title, <><Badge info={reviewInfo[d.reviewStatus]} /><Badge info={originInfo[d.origin]} /></>))}
              {model.decisions.length === 0 && <div className="empty-state"><b>Henüz karar yok</b><span>Mimari taslak oluşturulunca gerekçeli kararlar burada listelenir.</span></div>}
            </ul>
          )}

          {/* ------------------------- Açık sorular, varsayımlar ------------------------- */}
          {section === 'oq' && (
            <>
              <ul className="list">
                {model.openQuestions.map((q) => objectCard(q.id, q.text, <>
                  <span className={`badge ${q.status === 'answered' ? 'ok' : q.status === 'deferred' ? 'warn' : 'info'}`}>
                    {q.status === 'answered' ? '✓ Cevaplandı' : q.status === 'deferred' ? '⏸ Ertelendi' : '? Açık'}</span>
                  <span className="badge neutral">Etki: {priorityLabel[q.impact]}</span>
                </>))}
                {model.openQuestions.length === 0 && <div className="empty-state"><b>Açık soru yok</b></div>}
              </ul>
              {model.assumptions.length > 0 && <h3 style={{ margin: '20px 0 8px' }}>Varsayımlar</h3>}
              <ul className="list">
                {model.assumptions.map((a) => objectCard(a.id, a.text, <span className="badge warn">? {a.status === 'unverified' ? 'Doğrulanmadı' : a.status === 'verified' ? 'Doğrulandı' : 'Geçersiz'}</span>))}
              </ul>
            </>
          )}

          {/* ------------------------------ Kaynaklar ------------------------------ */}
          {section === 'src' && (
            <div className="stack">
              <ul className="list">
                {model.sources.map((s) => objectCard(s.id, s.title || s.text.slice(0, 90),
                  analyzed.has(s.id) ? <span className="badge ok">✓ Analiz edildi</span> : <span className="badge warn">◔ Analiz bekliyor</span>))}
                {model.sources.length === 0 && <div className="empty-state"><b>Henüz kaynak yok</b></div>}
              </ul>
              {canEdit && (
                <div className="card">
                  <label htmlFor="src" style={{ marginTop: 0 }}>Yeni kaynak (toplantı notu, gereksinim paketi…)</label>
                  <textarea id="src" value={sourceText} onChange={(e) => setSourceText(e.target.value)} />
                  <div className="row" style={{ marginTop: 10 }}>
                    <button className="btn" disabled={busy || !sourceText.trim()} onClick={() => guarded(async () => {
                      await api.addSource(projectId, '', sourceText, headNo); setSourceText(''); await loadAll();
                    })}>Kaynağı ekle</button>
                    <button className="btn primary" disabled={busy || !hasPendingSource} title={hasPendingSource ? '' : 'Analiz edilecek yeni kaynak yok'}
                      onClick={() => startJob((k) => api.analyze(projectId, k))}>Gereksinimleri çıkar</button>
                  </div>
                </div>
              )}
            </div>
          )}

          {/* ------------------------------ Doğrulama ------------------------------ */}
          {section === 'val' && (
            <div className="stack">
              <p className="muted">Şema ve referans bulguları deterministik kontrollerdir; “inceleme önerisi” kural tabanlı bir tavsiyedir ve kanıt değildir.</p>
              {(['schema', 'reference', 'consistency', 'traceability', 'profile', 'review_suggestion'] as const).map((k) => {
                const list = findings.filter((f) => f.kind === k);
                return list.length === 0 ? null : (
                  <details key={k} className="card" open={k === 'schema' || k === 'reference' || k === 'review_suggestion'}>
                    <summary><b>{findingKindLabel[k]}</b> ({list.length})</summary>
                    <FindingList findings={list} />
                  </details>
                );
              })}
              {findings.length === 0 && <div className="empty-state"><b>Bulgu yok</b></div>}
            </div>
          )}

          {/* -------------------------------- Öneriler -------------------------------- */}
          {section === 'prop' && (
            <>
              <ul className="list">
                {proposals.map((p) => (
                  <li key={p.id}><button className="item" aria-current={openProposal === p.id} onClick={() => { setOpenProposal(p.id); setSelected(null); }}>
                    <span className="title">{p.title}</span>
                    <span className="meta">
                      <span className={`badge ${p.status === 'accepted' ? 'ok' : p.status === 'pending' ? 'info' : p.status === 'rejected' ? 'bad' : 'warn'}`}>
                        {({ pending: '◔ Bekliyor', accepted: '✓ Kabul edildi', rejected: '✕ Reddedildi', stale: '! Eski temel' })[p.status]}</span>
                      <span className="badge neutral">temel v{p.baseNumber}</span>
                      <span className="badge neutral">{p.provider === 'sample' ? 'örnek (AI değil)' : p.isAi ? `AI: ${p.provider}` : 'buluşsal (AI değil)'}</span>
                    </span>
                  </button></li>
                ))}
              </ul>
              {proposals.length === 0 && <div className="empty-state"><b>Henüz öneri yok</b><span>“Yeni talep ekle” ile mevcut tasarım üzerinde etki analizi başlatın.</span></div>}
            </>
          )}
        </section>

        {detailOpen && (
          <aside className="detail open" aria-label="Ayrıntı ve değişiklik incelemesi">
            {openProposal ? (
              <ProposalReview projectId={projectId} proposalId={openProposal} headNumber={headNo} canAccept={detail.canAccept && !readOnly}
                onClose={() => setOpenProposal(null)} onSelect={goTo} onChanged={() => void loadAll()}
                onAccepted={async (n) => { setOpenProposal(null); setViewNo(null); await loadAll(); setNotice({ type: 'ok', text: `Yeni sürüm oluşturuldu: v${n}. Önceki sürümler erişilebilir kalır.` }); }}
                onRetryChange={(t) => { setOpenProposal(null); void startJob((k) => api.changeRequest(projectId, t, k)); }} />
            ) : selected ? (
              <>
                <div className="row" style={{ marginBottom: 8 }}>
                  <span className="muted">{typeOfId(selected)}</span><span className="spacer" />
                  <button className="btn small" onClick={() => setSelected(null)} aria-label="Ayrıntıyı kapat">Kapat ✕</button>
                </div>
                <ObjectDetail model={model} id={selected} findings={findings} canEdit={canEdit} onSelect={goTo}
                  onUpdateRequirement={updateReq} onAnswerQuestion={answerQ} />
              </>
            ) : null}
          </aside>
        )}
      </div>
    </Shell>
  );
}
