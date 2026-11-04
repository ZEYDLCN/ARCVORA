import { useCallback, useEffect, useMemo, useState } from 'react';
import { api, ApiError } from '../api';
import { Badge } from './Badge';
import { FindingList } from './ObjectDetail';
import { changeLabel, levelInfo, typeOfId } from '../lib/model';
import { withDependencies, withoutDependents } from '../lib/groups';
import type { ImpactLevel, ProposalDetail } from '../types';

interface Props {
  projectId: string;
  proposalId: string;
  headNumber: number;
  canAccept: boolean;
  onClose: () => void;
  onSelect: (id: string) => void;
  onAccepted: (versionNumber: number) => void;
  onChanged: () => void;
  onRetryChange: (requestText: string) => void;
}

const levels: ImpactLevel[] = ['direct', 'dependent', 'possible', 'uncertain'];

export function ProposalReview({ projectId, proposalId, headNumber, canAccept, onClose, onSelect, onAccepted, onChanged, onRetryChange }: Props) {
  const [d, setD] = useState<ProposalDetail | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<{ code: string; message: string; lines: string[] } | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const detail = await api.proposal(projectId, proposalId);
      setD(detail);
      setSelected(new Set(detail.groups.map((g) => g.id)));
    } catch (e) {
      setLoadError(e instanceof ApiError ? e.message : 'Öneri yüklenemedi.');
    }
  }, [projectId, proposalId]);

  useEffect(() => { void load(); }, [load]);

  const deps = useMemo(() => Object.fromEntries((d?.groups ?? []).map((g) => [g.id, g.dependsOn])), [d]);

  if (loadError) return <div className="notice error" role="alert">{loadError}</div>;
  if (!d) return <p className="muted">Öneri yükleniyor…</p>;

  const s = d.summary;
  const pending = s.status === 'pending';
  const providerBadge = s.provider === 'sample'
    ? { label: 'Elle hazırlanmış örnek (AI değil)', glyph: '✎', tone: 'neutral' as const }
    : s.isAi
      ? { label: `Yapay zekâ: ${s.provider}`, glyph: '✦', tone: 'info' as const }
      : { label: 'Buluşsal kural (AI değil)', glyph: '⚙', tone: 'warn' as const };

  const toggle = (g: string, on: boolean) =>
    setSelected((cur) => (on ? withDependencies(deps, cur, g) : withoutDependents(deps, cur, g)));

  const act = async (fn: () => Promise<void>) => {
    setBusy(true);
    setProblem(null);
    try { await fn(); } catch (e) {
      if (e instanceof ApiError) setProblem({ code: e.code, message: e.message, lines: e.detailLines });
      else setProblem({ code: 'error', message: 'Beklenmeyen hata.', lines: [] });
    } finally { setBusy(false); }
  };

  const allSelected = selected.size === d.groups.length;
  const empty = d.patch.operations.length === 0;

  return (
    <div className="stack">
      <div className="row">
        <h2 style={{ margin: 0 }}>{s.title}</h2>
        <span className="spacer" />
        <button className="btn small close-detail" onClick={onClose} aria-label="Kapat">✕</button>
        <button className="btn small" onClick={onClose}>Listeye dön</button>
      </div>
      <div className="row">
        <Badge info={providerBadge} />
        <span className="badge neutral">temel v{s.baseNumber}</span>
        <span className={`badge ${s.status === 'accepted' ? 'ok' : s.status === 'pending' ? 'info' : 'warn'}`}>
          {({ pending: '◔ Bekliyor', accepted: '✓ Kabul edildi', rejected: '✕ Reddedildi', stale: '! Eski temel sürüm' })[s.status]}
        </span>
      </div>
      {s.requestText && <blockquote className="notice">“{s.requestText}”</blockquote>}
      {s.note && <div className="notice warn">{s.note}</div>}
      {s.isStale && (
        <div className="notice warn" role="alert">
          Model bu öneri hazırlandıktan sonra değişti (v{s.baseNumber} → v{headNumber}). Kabul edilemez; yeniden değerlendirilmeli.
          {s.requestText && <div><button className="btn small" onClick={() => onRetryChange(s.requestText!)}>Aynı talebi güncel sürümde yeniden değerlendir</button></div>}
        </div>
      )}

      <section>
        <h3>Etki özeti</h3>
        {d.impact.items.length === 0 ? <p className="muted">Etki bulunamadı.</p> : (
          <table>
            <thead><tr><th>Alan</th><th className="num">Doğrudan</th><th className="num">Bağımlı</th><th className="num">Olası</th><th className="num">Belirsiz</th></tr></thead>
            <tbody>{d.impact.areas.map((a) => (
              <tr key={a.area}><td>{a.area}</td><td className="num">{a.direct || '·'}</td><td className="num">{a.dependent || '·'}</td><td className="num">{a.possible || '·'}</td><td className="num">{a.uncertain || '·'}</td></tr>
            ))}</tbody>
          </table>
        )}
        <p className="muted">Graf üzerinde erişilebilir olmak, bir nesnenin kesinlikle değişmesi gerektiği anlamına gelmez.</p>
      </section>

      {d.impact.openQuestionIds.length > 0 && (
        <div className="notice warn">
          <b>Cevap bekleyen sorular:</b>{' '}
          {d.impact.openQuestionIds.map((q) => <button key={q} className="chip" onClick={() => onSelect(q)}>{q}</button>)}
          <div className="muted">Bu sorular cevaplanmadan ilgili davranış kesinleşmez; taslak geçici varsayımla ilerler ve bağımlı kararlar işaretlidir.</div>
        </div>
      )}

      {levels.map((lv) => {
        const items = d.impact.items.filter((i) => i.level === lv && i.type !== 'TraceLink' && !i.id.startsWith('TRC'));
        if (items.length === 0) return null;
        return (
          <details key={lv} open={lv !== 'direct' || items.length < 12}>
            <summary><Badge info={levelInfo[lv]} /> <b>{items.length}</b></summary>
            <ul className="list">
              {items.map((i) => (
                <li key={i.id} className="impact-item">
                  <button className="chip" onClick={() => onSelect(i.id)}>{i.id}</button>
                  <span className="muted">{typeOfId(i.id)}{changeLabel[i.change] ? ` · ${changeLabel[i.change]}` : ''}</span>
                  <div>{i.title}</div>
                  <div className="muted">{i.reason}</div>
                  {i.blockers.length > 0 && <span className="badge neutral">? Belirsiz: {i.blockers.join(', ')}</span>}
                </li>
              ))}
            </ul>
          </details>
        );
      })}

      {d.newFindings.length > 0 && (
        <details>
          <summary>Bu değişiklikle ortaya çıkan bulgular ({d.newFindings.length})</summary>
          <FindingList findings={d.newFindings} />
        </details>
      )}

      {!empty && (
        <section>
          <h3>Kabul edilecek gruplar</h3>
          {d.groups.map((g) => (
            <label key={g.id} style={{ display: 'flex', gap: 8, alignItems: 'flex-start', margin: '6px 0', color: 'var(--text)' }}>
              <input type="checkbox" checked={selected.has(g.id)} disabled={!pending || !canAccept || busy}
                onChange={(e) => toggle(g.id, e.target.checked)} style={{ width: 'auto', marginTop: 4 }} />
              <span>
                <b>{g.title}</b> <span className="muted">· {g.operationCount} işlem</span>
                {g.description && <span className="muted"> — {g.description}</span>}
                {g.dependsOn.length > 0 && <span className="muted"> · bağlı: {g.dependsOn.map((x) => d.groups.find((y) => y.id === x)?.title ?? x).join(', ')}</span>}
              </span>
            </label>
          ))}
        </section>
      )}
      {empty && <div className="notice">Bu öneri uygulanabilir bir model değişikliği içermiyor; yalnızca etki işaretleri sunar.</div>}

      {problem && (
        <div className="notice error" role="alert">
          {problem.message}
          {problem.lines.length > 0 && <ul>{problem.lines.map((l) => <li key={l}>{l}</li>)}</ul>}
          {problem.code === 'reassessment_required' && s.requestText && (
            <div><button className="btn small" onClick={() => onRetryChange(s.requestText!)}>Yeniden değerlendir</button></div>
          )}
        </div>
      )}

      {pending && (
        <div className="row">
          <button className="btn primary" disabled={!canAccept || busy || empty || selected.size === 0 || s.isStale}
            title={canAccept ? '' : 'Kabul yetkiniz yok'}
            onClick={() => act(async () => {
              const r = await api.accept(projectId, proposalId, allSelected ? null : [...selected], s.baseNumber);
              onAccepted(r.version.number);
            })}>
            {allSelected ? 'Hepsini kabul et' : `Seçilenleri kabul et (${selected.size}/${d.groups.length})`}
          </button>
          <button className="btn danger" disabled={!canAccept || busy}
            onClick={() => act(async () => { await api.reject(projectId, proposalId, ''); onChanged(); await load(); })}>
            Reddet
          </button>
        </div>
      )}
      {pending && !canAccept && <p className="muted">Bu öneriyi kabul etme yetkiniz yok; inceleyebilirsiniz.</p>}
    </div>
  );
}
