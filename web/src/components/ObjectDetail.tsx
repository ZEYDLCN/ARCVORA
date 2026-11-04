import { useState } from 'react';
import { Badge } from './Badge';
import {
  buildIndex, findingKindLabel, kindLabel, originInfo, priorityLabel, relationsOf, reviewInfo, severityInfo, typeOfId,
} from '../lib/model';
import type { Assumption, Component, Decision, Endpoint, Entity, Finding, Flow, Model, OpenQuestion, Requirement, Source } from '../types';

interface Props {
  model: Model;
  id: string;
  findings: Finding[];
  canEdit: boolean;
  onSelect: (id: string) => void;
  onUpdateRequirement: (id: string, body: Record<string, unknown>) => Promise<void>;
  onAnswerQuestion: (id: string, body: Record<string, unknown>) => Promise<void>;
}

export function ObjectDetail({ model, id, findings, canEdit, onSelect, onUpdateRequirement, onAnswerQuestion }: Props) {
  const idx = buildIndex(model);
  const found = idx.get(id);
  if (!found) return <p className="muted">Bu nesne seçili sürümde yok.</p>;

  const obj = found.obj as Record<string, unknown>;
  const reviewable = 'reviewStatus' in obj && 'origin' in obj;
  const relations = relationsOf(model, id);
  const own = findings.filter((f) => f.objectId === id);

  return (
    <div className="stack">
      <div>
        <span className="id">{found.id} · {typeOfId(id)}</span>
        <h2>{found.title}</h2>
        {reviewable && (
          <div className="row">
            <Badge info={originInfo[obj.origin as keyof typeof originInfo]} title="Bilginin kaynağı: kullanıcı, sistem önerisi veya varsayım" />
            <Badge info={reviewInfo[obj.reviewStatus as keyof typeof reviewInfo]} title="İnsan inceleme durumu" />
          </div>
        )}
      </div>

      {id.startsWith('REQ') && <RequirementBody r={obj as unknown as Requirement} model={model} canEdit={canEdit} onUpdate={onUpdateRequirement} onSelect={onSelect} />}
      {id.startsWith('DEC') && <DecisionBody d={obj as unknown as Decision} />}
      {id.startsWith('CMP') && <ComponentBody c={obj as unknown as Component} />}
      {id.startsWith('ENT') && <EntityBody e={obj as unknown as Entity} />}
      {id.startsWith('FLW') && <FlowBody f={obj as unknown as Flow} />}
      {id.startsWith('API') && <EndpointBody e={obj as unknown as Endpoint} />}
      {id.startsWith('ASM') && <AssumptionBody a={obj as unknown as Assumption} />}
      {id.startsWith('SRC') && <SourceBody s={obj as unknown as Source} />}
      {id.startsWith('OQ') && <QuestionBody q={obj as unknown as OpenQuestion} canEdit={canEdit} onAnswer={onAnswerQuestion} />}

      {relations.length > 0 && (
        <section>
          <h3>İlişkiler</h3>
          {relations.map((g) => (
            <div key={g.label}>
              <div className="muted">{g.label}</div>
              {g.ids.map((rid) => (
                <button key={rid} className="chip" onClick={() => onSelect(rid)} title={idx.get(rid)?.title}>
                  {rid} {(idx.get(rid)?.title ?? '').slice(0, 28)}
                </button>
              ))}
            </div>
          ))}
        </section>
      )}

      {own.length > 0 && (
        <section>
          <h3>Bu nesneye ait bulgular</h3>
          <FindingList findings={own} />
        </section>
      )}
    </div>
  );
}

export function FindingList({ findings }: { findings: Finding[] }) {
  return (
    <ul className="list">
      {findings.map((f, i) => (
        <li key={`${f.ruleId}-${f.objectId}-${i}`} className="impact-item">
          <div className="row">
            <Badge info={severityInfo[f.severity]} />
            <span className={`badge ${f.kind === 'review_suggestion' ? 'info' : 'neutral'}`}>{findingKindLabel[f.kind]}</span>
            <span className="id">{f.ruleId}{f.objectId ? ` · ${f.objectId}` : ''}</span>
          </div>
          <div>{f.message}</div>
          {f.basis && <div className="muted">{f.basis}</div>}
        </li>
      ))}
    </ul>
  );
}

function Field({ label, children }: { label: string; children?: React.ReactNode }) {
  if (children === undefined || children === null || children === '') return null;
  return (<><dt>{label}</dt><dd>{children}</dd></>);
}

function RequirementBody({ r, model, canEdit, onUpdate, onSelect }: {
  r: Requirement; model: Model; canEdit: boolean; onUpdate: Props['onUpdateRequirement']; onSelect: (id: string) => void;
}) {
  const [text, setText] = useState(r.text);
  const [criterion, setCriterion] = useState('');
  const [busy, setBusy] = useState(false);
  const run = async (body: Record<string, unknown>) => { setBusy(true); try { await onUpdate(r.id, body); } finally { setBusy(false); } };
  const sources = new Map(model.sources.map((s) => [s.id, s]));

  return (
    <>
      <dl className="kv">
        <Field label="Tür">{kindLabel[r.kind]}</Field>
        <Field label="Öncelik">{priorityLabel[r.priority]}</Field>
      </dl>
      {r.sourceRefs.map((s) => (
        <blockquote key={`${s.sourceId}-${s.excerpt}`} className="notice">
          <span className="muted">Kaynak cümle · </span>
          <button className="chip" onClick={() => onSelect(s.sourceId)}>{s.sourceId} {sources.get(s.sourceId)?.title}</button>
          <div>“{s.excerpt ?? '—'}”</div>
        </blockquote>
      ))}
      <section>
        <h3>Kabul ölçütleri</h3>
        {r.acceptanceCriteria.length === 0 && <p className="muted">Tanımlı değil. Bağlantı bulunması yeterlilik kanıtı değildir; ölçüt eksikliği açık bir boşluktur.</p>}
        <ul>{r.acceptanceCriteria.map((c) => <li key={c.id}><span className="id">{c.id}</span> {c.text}</li>)}</ul>
      </section>
      {canEdit && (
        <section className="stack">
          <h3>Düzenle</h3>
          <textarea value={text} onChange={(e) => setText(e.target.value)} aria-label="Gereksinim metni" />
          <div className="row">
            <button className="btn small" disabled={busy || text.trim() === r.text || !text.trim()} onClick={() => run({ text })}>Metni kaydet</button>
            {r.reviewStatus !== 'confirmed' && <button className="btn small" disabled={busy} onClick={() => run({ reviewStatus: 'confirmed' })}>✓ Onayla</button>}
            {r.reviewStatus !== 'deferred' && <button className="btn small" disabled={busy} onClick={() => run({ reviewStatus: 'deferred' })}>⏸ Ertele</button>}
            {r.reviewStatus !== 'rejected' && <button className="btn small danger" disabled={busy} onClick={() => run({ reviewStatus: 'rejected' })}>✕ Reddet</button>}
          </div>
          <div className="row">
            <input type="text" placeholder="Yeni kabul ölçütü" value={criterion} onChange={(e) => setCriterion(e.target.value)} aria-label="Yeni kabul ölçütü" />
            <button className="btn small" disabled={busy || !criterion.trim()}
              onClick={async () => { await run({ acceptanceCriteria: [...r.acceptanceCriteria.map((c) => ({ id: c.id, text: c.text })), { text: criterion }] }); setCriterion(''); }}>
              Ekle
            </button>
          </div>
        </section>
      )}
    </>
  );
}

function DecisionBody({ d }: { d: Decision }) {
  return (
    <>
      <dl className="kv">
        <Field label="Seçim">{d.choice}</Field>
        <Field label="Gerekçe">{d.rationale}</Field>
        <Field label="Sonuçlar">{d.consequences}</Field>
      </dl>
      {d.alternatives.length > 0 && (
        <section>
          <h3>Alternatifler</h3>
          <ul>{d.alternatives.map((a) => <li key={a.name}><b>{a.name}</b>{a.tradeoff ? ` — ${a.tradeoff}` : ''}</li>)}</ul>
        </section>
      )}
    </>
  );
}

function ComponentBody({ c }: { c: Component }) {
  return <dl className="kv"><Field label="Tür">{c.kind}</Field><Field label="Sorumluluk">{c.responsibility}</Field><Field label="Teknoloji">{c.technology}</Field></dl>;
}

function EntityBody({ e }: { e: Entity }) {
  return (
    <>
      <table>
        <thead><tr><th>Alan</th><th>Tür</th><th /></tr></thead>
        <tbody>{e.fields.map((f) => <tr key={f.name}><td>{f.name}</td><td className="mono">{f.type}</td><td>{f.isKey ? 'PK' : f.isNullable ? 'boş olabilir' : ''}</td></tr>)}</tbody>
      </table>
      {e.constraints.length > 0 && <p className="muted">Kısıtlar: {e.constraints.join('; ')}</p>}
    </>
  );
}

function FlowBody({ f }: { f: Flow }) {
  return (
    <>
      <p>{f.description}</p>
      <ol>{f.steps.map((s, i) => <li key={i}>{s.from} → {s.to}: {s.message}{s.condition ? ` [${s.condition}]` : ''}</li>)}</ol>
    </>
  );
}

function EndpointBody({ e }: { e: Endpoint }) {
  return <dl className="kv"><Field label="Özet">{e.summary}</Field><Field label="Yetkilendirme">{e.authorization}</Field></dl>;
}

function AssumptionBody({ a }: { a: Assumption }) {
  const label = { unverified: 'Doğrulanmadı', verified: 'Doğrulandı', invalidated: 'Geçersiz' }[a.status];
  return <dl className="kv"><Field label="Gerekçe">{a.rationale}</Field><Field label="Doğrulama">{label}</Field></dl>;
}

function SourceBody({ s }: { s: Source }) {
  return <pre className="notice" style={{ whiteSpace: 'pre-wrap' }}>{s.text}</pre>;
}

function QuestionBody({ q, canEdit, onAnswer }: { q: OpenQuestion; canEdit: boolean; onAnswer: Props['onAnswerQuestion'] }) {
  const [answer, setAnswer] = useState(q.answer ?? '');
  const [busy, setBusy] = useState(false);
  const run = async (body: Record<string, unknown>) => { setBusy(true); try { await onAnswer(q.id, body); } finally { setBusy(false); } };
  const status = { open: 'Açık', answered: 'Cevaplandı', deferred: 'Ertelendi' }[q.status];
  return (
    <>
      <dl className="kv"><Field label="Durum">{status}</Field><Field label="Etki">{priorityLabel[q.impact]}</Field><Field label="Cevap">{q.answer}</Field></dl>
      {canEdit && (
        <div className="stack">
          <textarea value={answer} onChange={(e) => setAnswer(e.target.value)} placeholder="Cevap" aria-label="Cevap" />
          <div className="row">
            <button className="btn small" disabled={busy || !answer.trim()} onClick={() => run({ answer, status: 'answered' })}>Cevapla</button>
            {q.status !== 'deferred' && <button className="btn small" disabled={busy} onClick={() => run({ status: 'deferred' })}>⏸ Ertele</button>}
          </div>
          <p className="muted">Ertelenen karar ve ona bağlı tasarım varsayımları görünür kalır.</p>
        </div>
      )}
    </>
  );
}
