import { useCallback, useEffect, useMemo, useState } from 'react';
import { api, ApiError } from '../api';
import { Badge } from '../components/Badge';
import { MermaidView } from '../components/MermaidView';
import { FindingList, ObjectDetail } from '../components/ObjectDetail';
import { ProposalReview } from '../components/ProposalReview';
import { buildIndex, findingKindLabel, kindLabel, originInfo, priorityLabel, reviewInfo, textAlternative } from '../lib/model';
import type { Finding, Job, Model, ProjectDetail, ProposalSummary, VersionInfo, View } from '../types';

type LeftTab = 'req' | 'dec' | 'oq' | 'src' | 'val';
type CenterTab = 'component' | 'er' | 'sequence';

const jobLabel: Record<string, string> = { analysis: 'Gereksinim çıkarımı', architecture: 'Mimari taslak', change: 'Değişiklik analizi' };
const stageLabel: Record<string, string> = {
  generate: 'Taslak üretiliyor', validate: 'Doğrulanıyor', repair: 'Hatalar düzeltiliyor', analyze: 'Etki analiz ediliyor',
};

interface Notice { type: 'error' | 'ok'; text: string; lines?: string[] }

export function Workspace({ projectId, onBack, dark, onTheme }: { projectId: string; onBack: () => void; dark: boolean; onTheme: () => void }) {
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
  const [leftTab, setLeftTab] = useState<LeftTab>('req');
  const [centerTab, setCenterTab] = useState<CenterTab>('component');
  const [flowId, setFlowId] = useState<string | null>(null);
  const [mobile, setMobile] = useState<'req' | 'diagram' | 'dec'>('diagram');
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
  const selectObj = useCallback((id: string) => { setSelected(id); setOpenProposal(null); }, []);

  const flowViews = views.filter((v) => v.kind === 'sequence');
  const currentFlow = flowViews.find((v) => v.flowId === flowId) ?? flowViews[0];
  const currentView: View | undefined = centerTab === 'sequence' ? currentFlow : views.find((v) => v.kind === centerTab);

  const guarded = async (fn: () => Promise<void>) => {
    setBusy(true); setNotice(null);
    try { await fn(); } catch (e) { report(e); } finally { setBusy(false); }
  };

  const startJob = (fn: (key: string) => Promise<Job>) => guarded(async () => {
    const job = await fn(crypto.randomUUID());
    setJobs((cur) => [job, ...cur.filter((j) => j.id !== job.id)]);
  });

  if (loadError) return <div className="page"><div className="notice error" role="alert">{loadError}</div><p><button className="btn" onClick={onBack}>← Projeler</button></p></div>;
  if (!detail || !model || !idx) return <div className="page"><p className="muted">Çalışma alanı yükleniyor…</p></div>;

  const canEdit = detail.canEdit && !readOnly;
  const analyzed = new Set(model.requirements.flatMap((r) => r.sourceRefs.map((s) => s.sourceId)));
  const hasPendingSource = model.sources.some((s) => !analyzed.has(s.id));
  const hasReq = model.requirements.some((r) => r.reviewStatus !== 'rejected');
  const hasArch = model.components.length > 0;

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

  const drafting = pendingProposals.length > 0;
  const detailOpen = selected !== null || openProposal !== null;

  return (
    <div className="ws" data-mobile={mobile}>
      <header className="topbar">
        <button className="btn small" onClick={onBack} aria-label="Projelere dön">←</button>
        <b>{detail.summary.name}</b>
        <label htmlFor="ver" className="muted" style={{ margin: 0 }}>Sürüm</label>
        <select id="ver" value={effectiveNo} onChange={(e) => { const n = Number(e.target.value); setViewNo(n === headNo ? null : n); setSelected(null); }}>
          {versions.map((v) => <option key={v.id} value={v.number}>v{v.number} · {v.summary}{v.number === headNo ? ' (güncel)' : ''}</option>)}
        </select>
        <span className="badge neutral" title="Teknoloji profili">⚙ {detail.model.project.techProfileId === 'aspnetcore' ? 'ASP.NET Core' : detail.model.project.techProfileId}</span>
        {drafting
          ? <span className="badge info" title="Bekleyen öneriler kabul edilmeden model değişmez">◔ Taslak: {pendingProposals.length} bekleyen öneri</span>
          : <span className="badge ok">✓ Kabul edilmiş sürüm</span>}
        <span className="spacer" />
        <button className="btn small" disabled={!detail.canEdit || readOnly} onClick={() => setShowChange((s) => !s)}>＋ Yeni talep ekle</button>
        <button className="btn small" disabled={busy} onClick={() => exportAs('markdown')}>⇩ Markdown</button>
        <button className="btn small" disabled={busy} onClick={() => exportAs('package')}>⇩ Paket (zip)</button>
        <button className="btn small" onClick={onTheme} aria-label="Temayı değiştir">{dark ? '☀' : '☾'}</button>
      </header>

      <nav className="mobile-tabs" aria-label="Bölümler">
        {([['req', 'Gereksinimler'], ['diagram', 'Diyagram'], ['dec', 'Kararlar']] as const).map(([k, l]) => (
          <button key={k} className="tab" aria-selected={mobile === k} onClick={() => { setMobile(k); if (k === 'req') setLeftTab('req'); if (k === 'dec') setLeftTab('dec'); }}>{l}</button>
        ))}
      </nav>

      <aside className="panel left" aria-label="Proje kapsamı">
        <div className="tabs" role="tablist">
          {([['req', `Gereksinimler (${model.requirements.length})`], ['dec', `Kararlar (${model.decisions.length})`],
            ['oq', `Açık sorular (${model.openQuestions.filter((q) => q.status !== 'answered').length})`], ['src', 'Kaynaklar'],
            ['val', `Doğrulama (${findings.length})`]] as const).map(([k, l]) => (
            <button key={k} role="tab" className="tab" aria-selected={leftTab === k} onClick={() => setLeftTab(k)}>{l}</button>
          ))}
        </div>
        <div className="inner">
          {leftTab === 'req' && (
            <ul className="list">
              {model.requirements.map((r) => (
                <li key={r.id}><button className="item" aria-current={selected === r.id} onClick={() => selectObj(r.id)}>
                  <span className="id">{r.id}</span><span className="title">{r.text}</span>
                  <span className="meta"><Badge info={reviewInfo[r.reviewStatus]} /><Badge info={originInfo[r.origin]} />
                    <span className="badge neutral">{kindLabel[r.kind]} · {priorityLabel[r.priority]}</span></span>
                </button></li>
              ))}
              {model.requirements.length === 0 && <li className="muted">Henüz gereksinim yok. Bir kaynak ekleyip “Gereksinimleri çıkar”ı çalıştırın.</li>}
            </ul>
          )}
          {leftTab === 'dec' && (
            <ul className="list">
              {model.decisions.map((d) => (
                <li key={d.id}><button className="item" aria-current={selected === d.id} onClick={() => selectObj(d.id)}>
                  <span className="id">{d.id}</span><span className="title">{d.title}</span>
                  <span className="meta"><Badge info={reviewInfo[d.reviewStatus]} /><Badge info={originInfo[d.origin]} /></span>
                </button></li>
              ))}
              {model.decisions.length === 0 && <li className="muted">Henüz karar yok.</li>}
            </ul>
          )}
          {leftTab === 'oq' && (
            <ul className="list">
              {model.openQuestions.map((q) => (
                <li key={q.id}><button className="item" aria-current={selected === q.id} onClick={() => selectObj(q.id)}>
                  <span className="id">{q.id}</span><span className="title">{q.text}</span>
                  <span className="meta"><span className={`badge ${q.status === 'answered' ? 'ok' : q.status === 'deferred' ? 'warn' : 'info'}`}>
                    {q.status === 'answered' ? '✓ Cevaplandı' : q.status === 'deferred' ? '⏸ Ertelendi' : '? Açık'}</span>
                    <span className="badge neutral">Etki: {priorityLabel[q.impact]}</span></span>
                </button></li>
              ))}
              {model.openQuestions.length === 0 && <li className="muted">Açık soru yok.</li>}
              {model.assumptions.length > 0 && <li><h3 style={{ marginTop: 12 }}>Varsayımlar</h3></li>}
              {model.assumptions.map((a) => (
                <li key={a.id}><button className="item" aria-current={selected === a.id} onClick={() => selectObj(a.id)}>
                  <span className="id">{a.id}</span><span className="title">{a.text}</span>
                  <span className="meta"><span className="badge warn">? {a.status === 'unverified' ? 'Doğrulanmadı' : a.status === 'verified' ? 'Doğrulandı' : 'Geçersiz'}</span></span>
                </button></li>
              ))}
            </ul>
          )}
          {leftTab === 'src' && (
            <div className="stack">
              <ul className="list">
                {model.sources.map((s) => (
                  <li key={s.id}><button className="item" aria-current={selected === s.id} onClick={() => selectObj(s.id)}>
                    <span className="id">{s.id}</span><span className="title">{s.title || s.text.slice(0, 80)}</span>
                    <span className="meta">{analyzed.has(s.id) ? <span className="badge ok">✓ Analiz edildi</span> : <span className="badge warn">◔ Analiz bekliyor</span>}</span>
                  </button></li>
                ))}
              </ul>
              {canEdit && (
                <div className="card">
                  <label htmlFor="src">Yeni kaynak (toplantı notu, gereksinim paketi…)</label>
                  <textarea id="src" value={sourceText} onChange={(e) => setSourceText(e.target.value)} />
                  <div className="row" style={{ marginTop: 8 }}>
                    <button className="btn small" disabled={busy || !sourceText.trim()} onClick={() => guarded(async () => {
                      await api.addSource(projectId, '', sourceText, headNo); setSourceText(''); await loadAll();
                    })}>Kaynağı ekle</button>
                  </div>
                </div>
              )}
            </div>
          )}
          {leftTab === 'val' && (
            <div className="stack">
              <p className="muted">Şema/referans bulguları deterministik kontrollerdir; “inceleme önerisi” kural tabanlı bir tavsiyedir ve kanıt değildir.</p>
              {(['schema', 'reference', 'consistency', 'traceability', 'profile', 'review_suggestion'] as const).map((k) => {
                const list = findings.filter((f) => f.kind === k);
                return list.length === 0 ? null : (
                  <details key={k} open={k === 'schema' || k === 'reference'}>
                    <summary><b>{findingKindLabel[k]}</b> ({list.length})</summary>
                    <FindingList findings={list} />
                  </details>
                );
              })}
              {findings.length === 0 && <p>Bulgu yok.</p>}
            </div>
          )}
        </div>
        {canEdit && (
          <div className="inner" style={{ borderTop: '1px solid var(--line)' }}>
            <div className="row">
              <button className="btn small" disabled={busy || !hasPendingSource} title={hasPendingSource ? '' : 'Analiz edilecek yeni kaynak yok'}
                onClick={() => startJob((k) => api.analyze(projectId, k))}>Gereksinimleri çıkar</button>
              <button className="btn small" disabled={busy || !hasReq} title={hasReq ? '' : 'Önce gereksinim gerekli'}
                onClick={() => startJob((k) => api.proposeArchitecture(projectId, k))}>{hasArch ? 'Mimari taslağı yenile' : 'Mimari taslak oluştur'}</button>
            </div>
          </div>
        )}
      </aside>

      <main className="center">
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
            <label htmlFor="chg">Yeni talep — kabul edilmiş sürüm üzerinde etkisi analiz edilir; sürüm yalnızca siz kabul edince oluşur.</label>
            <textarea id="chg" value={changeText} onChange={(e) => setChangeText(e.target.value)} placeholder="Örn. Artık şirketler kendi iki aşamalı onay sürecini tanımlayabilsin." />
            <div className="row" style={{ marginTop: 8 }}>
              <button className="btn primary" disabled={busy || changeText.trim().length < 3}>Etkiyi analiz et</button>
              <button type="button" className="btn" onClick={() => setShowChange(false)}>Vazgeç</button>
            </div>
          </form>
        )}

        {jobs.slice(0, 5).filter((j) => j.status !== 'succeeded' || active.some((a) => a.id === j.id)).map((j) => (
          <div key={j.id} className={`notice ${j.status === 'failed' ? 'error' : ''}`} style={{ marginBottom: 8 }} role="status">
            <div className="row">
              <b>{jobLabel[j.kind] ?? j.kind}</b>
              {j.status === 'queued' && <span className="badge neutral">◔ Kuyrukta</span>}
              {j.status === 'running' && <span className="badge info">⟳ {stageLabel[j.stage ?? ''] ?? 'Çalışıyor'}</span>}
              {j.status === 'failed' && <span className="badge bad">✕ Başarısız{j.errorCode ? ` (${j.errorCode})` : ''}</span>}
              {j.status === 'cancelled' && <span className="badge neutral">✕ İptal edildi</span>}
              {j.attempts > 1 && <span className="muted">deneme {j.attempts}/{j.maxAttempts}</span>}
              <span className="spacer" />
              {(j.status === 'queued' || j.status === 'running') && (
                <button className="btn small" disabled={j.cancelRequested} onClick={() => guarded(async () => { setJobs((c) => c.map((x) => x.id === j.id ? { ...x, ...{} } : x)); await api.cancelJob(j.id); await loadAll(); })}>
                  {j.cancelRequested ? 'İptal isteniyor…' : 'İptal et'}
                </button>
              )}
            </div>
            {j.status === 'failed' && <div>{j.error} Son kabul edilmiş model korundu.</div>}
            {j.status === 'failed' && j.problems.length > 0 && <ul>{j.problems.slice(0, 5).map((p) => <li key={p}>{p}</li>)}</ul>}
          </div>
        ))}

        {notice && (
          <div className={`notice ${notice.type === 'error' ? 'error' : ''}`} role={notice.type === 'error' ? 'alert' : 'status'} style={{ marginBottom: 8 }}>
            {notice.text}
            {notice.lines && notice.lines.length > 0 && <ul>{notice.lines.map((l) => <li key={l}>{l}</li>)}</ul>}
            <button className="btn small" style={{ marginLeft: 8 }} onClick={() => setNotice(null)}>Kapat</button>
          </div>
        )}

        <div className="tabs" role="tablist" style={{ marginBottom: 10 }}>
          <button role="tab" className="tab" aria-selected={centerTab === 'component'} onClick={() => setCenterTab('component')}>Bileşenler</button>
          <button role="tab" className="tab" aria-selected={centerTab === 'er'} onClick={() => setCenterTab('er')}>Veri modeli</button>
          <button role="tab" className="tab" aria-selected={centerTab === 'sequence'} onClick={() => setCenterTab('sequence')}>Akışlar ({flowViews.length})</button>
          {centerTab === 'sequence' && flowViews.length > 0 && (
            <select aria-label="Akış seç" style={{ width: 'auto', marginLeft: 8 }} value={currentFlow?.flowId ?? ''} onChange={(e) => setFlowId(e.target.value)}>
              {flowViews.map((f) => <option key={f.flowId} value={f.flowId}>{f.title.replace('Akış: ', '')}</option>)}
            </select>
          )}
          <span className="spacer" />
          <span className="muted" style={{ alignSelf: 'center' }}>v{effectiveNo} · özet {(versions.find((v) => v.number === effectiveNo)?.contentHash ?? detail.head.contentHash).slice(0, 8)}</span>
        </div>

        {currentView ? (
          <>
            <MermaidView source={currentView.mermaid} dark={dark} onSelect={centerTab === 'sequence' ? undefined : selectObj} />
            <details className="alt">
              <summary>Metinsel karşılık ve Mermaid kaynağı</summary>
              <ul>{textAlternative(model, centerTab, currentView.flowId).map((l) => <li key={l}>{l}</li>)}</ul>
              <pre className="notice mono" style={{ overflow: 'auto' }}>{currentView.mermaid}</pre>
              <p className="muted">Bu dosyada dışarıda yapılan düzenlemeler modele otomatik aktarılmaz; model yapılandırılmış düzenleyici veya öneriler üzerinden değişir.</p>
            </details>
          </>
        ) : <p className="muted">{centerTab === 'sequence' ? 'Henüz akış yok.' : 'Görünüm yükleniyor…'}</p>}
      </main>

      <aside className={`panel right ${detailOpen ? 'open' : ''}`} aria-label="Ayrıntı ve değişiklik incelemesi">
        <div className="inner">
          {openProposal ? (
            <ProposalReview projectId={projectId} proposalId={openProposal} headNumber={headNo} canAccept={detail.canAccept && !readOnly}
              onClose={() => setOpenProposal(null)} onSelect={selectObj} onChanged={() => void loadAll()}
              onAccepted={async (n) => { setOpenProposal(null); setViewNo(null); await loadAll(); setNotice({ type: 'ok', text: `Yeni sürüm oluşturuldu: v${n}. Önceki sürümler erişilebilir kalır.` }); }}
              onRetryChange={(t) => { setOpenProposal(null); void startJob((k) => api.changeRequest(projectId, t, k)); }} />
          ) : selected ? (
            <>
              <button className="btn small close-detail" onClick={() => setSelected(null)} aria-label="Kapat" style={{ marginBottom: 8 }}>✕ Kapat</button>
              <div className="row" style={{ marginBottom: 8 }}><button className="btn small" onClick={() => setSelected(null)}>← Öneriler</button></div>
              <ObjectDetail model={model} id={selected} findings={findings} canEdit={canEdit} onSelect={selectObj}
                onUpdateRequirement={updateReq} onAnswerQuestion={answerQ} />
            </>
          ) : (
            <div className="stack">
              <h2>Değişiklik önerileri</h2>
              {proposals.length === 0 && <p className="muted">Henüz öneri yok. “Yeni talep ekle” ile mevcut tasarım üzerinde etki analizi başlatın; bir bileşene, karara veya gereksinime tıklayarak gerekçesini ve bağlantılarını görün.</p>}
              <ul className="list">
                {proposals.map((p) => (
                  <li key={p.id}><button className="item" onClick={() => { setOpenProposal(p.id); setSelected(null); }}>
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
            </div>
          )}
        </div>
      </aside>
    </div>
  );
}
