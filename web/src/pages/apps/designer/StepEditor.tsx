import { allowedOps, approverLabels, opLabels, type ApproverType, type ConditionOp, type FlowCondition, type FlowField, type FlowStep } from '../../../lib/flow';
import type { DirectoryEntry, GroupInfo } from '../../../types';

interface Props {
  step: FlowStep;
  index: number;
  total: number;
  fields: FlowField[];
  people: DirectoryEntry[];
  groups: GroupInfo[];
  errors: string[];
  onChange: (patch: Partial<FlowStep>) => void;
  onMove: (delta: -1 | 1) => void;
  onRemove: () => void;
}

export function StepEditor({ step, index, total, fields, people, groups, errors, onChange, onMove, onRemove }: Props) {
  const id = `s${index}`;
  const conds = step.conditions ?? [];
  const setCond = (j: number, patch: Partial<FlowCondition>) => onChange({ conditions: conds.map((c, k) => (k === j ? { ...c, ...patch } : c)) });
  const usable = fields.filter((f) => allowedOps(f.type).length > 0);

  return (
    <div className="row-card" data-testid={`step-${step.id}`}>
      <div className="row-head">
        <b>{index + 1}. {step.name || 'Adsız adım'}</b>
        <button className="icon-btn" onClick={() => onMove(-1)} disabled={index === 0} aria-label="Yukarı taşı">↑</button>
        <button className="icon-btn" onClick={() => onMove(1)} disabled={index === total - 1} aria-label="Aşağı taşı">↓</button>
        <button className="btn small danger" onClick={onRemove}>Sil</button>
      </div>
      <div className="mini-grid">
        <div><label htmlFor={`${id}-n`}>Adım adı</label><input id={`${id}-n`} type="text" maxLength={100} value={step.name} onChange={(e) => onChange({ name: e.target.value })} /></div>
        <div>
          <label htmlFor={`${id}-a`}>Kim onaylar</label>
          <select id={`${id}-a`} value={step.approvers.type} onChange={(e) => onChange({ approvers: { type: e.target.value as ApproverType } })}>
            {(Object.keys(approverLabels) as ApproverType[]).map((t) => <option key={t} value={t}>{approverLabels[t]}</option>)}
          </select>
        </div>
        {step.approvers.type === 'user' && (
          <div><label htmlFor={`${id}-u`}>Kişi</label>
            <select id={`${id}-u`} value={step.approvers.id ?? ''} onChange={(e) => onChange({ approvers: { type: 'user', id: e.target.value || undefined } })}>
              <option value="">Seçin…</option>{people.map((p) => <option key={p.id} value={p.id}>{p.displayName}</option>)}
            </select></div>
        )}
        {step.approvers.type === 'group' && (
          <>
            <div><label htmlFor={`${id}-g`}>Grup</label>
              <select id={`${id}-g`} value={step.approvers.id ?? ''} onChange={(e) => onChange({ approvers: { type: 'group', id: e.target.value || undefined } })}>
                <option value="">Seçin…</option>{groups.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
              </select></div>
            <div><label htmlFor={`${id}-m`}>Gruptan</label>
              <select id={`${id}-m`} value={step.mode ?? 'any'} onChange={(e) => onChange({ mode: e.target.value as 'any' | 'all' })}>
                <option value="any">Biri onaylasa yeter</option><option value="all">Hepsi onaylamalı</option>
              </select></div>
          </>
        )}
      </div>

      <div>
        <b style={{ fontSize: 13 }}>Koşul</b> <span className="muted" style={{ fontSize: 12 }}>(boşsa her talepte çalışır; doluysa hepsi sağlanınca)</span>
        {conds.map((c, j) => {
          const f = fields.find((x) => x.key === c.field);
          return (
            <div className="mini-grid" key={j} style={{ marginTop: 6, alignItems: 'end' }}>
              <div><label htmlFor={`${id}-cf${j}`}>Alan</label>
                <select id={`${id}-cf${j}`} value={c.field} onChange={(e) => { const nf = fields.find((x) => x.key === e.target.value); setCond(j, { field: e.target.value, op: nf ? allowedOps(nf.type)[0] : 'eq', value: undefined }); }}>
                  {usable.map((x) => <option key={x.key} value={x.key}>{x.label}</option>)}
                  {!f && <option value={c.field}>{c.field} (yok)</option>}
                </select></div>
              <div><label htmlFor={`${id}-co${j}`}>İşleç</label>
                <select id={`${id}-co${j}`} value={c.op} onChange={(e) => setCond(j, { op: e.target.value as ConditionOp, value: undefined })}>
                  {(f ? allowedOps(f.type) : (Object.keys(opLabels) as ConditionOp[])).map((o) => <option key={o} value={o}>{opLabels[o]}</option>)}
                </select></div>
              <div><label htmlFor={`${id}-cv${j}`}>Değer</label><ConditionValue id={`${id}-cv${j}`} cond={c} field={f} people={people} onChange={(v) => setCond(j, { value: v })} /></div>
              <button className="btn small danger" onClick={() => onChange({ conditions: conds.filter((_, k) => k !== j) })}>Koşulu sil</button>
            </div>
          );
        })}
        <p><button className="btn small" disabled={usable.length === 0} onClick={() => onChange({ conditions: [...conds, { field: usable[0].key, op: allowedOps(usable[0].type)[0] }] })}>Koşul ekle</button></p>
      </div>
      {errors.length > 0 && <ul className="issues" role="alert" style={{ color: 'var(--red)' }}>{errors.map((m) => <li key={m}>{m}</li>)}</ul>}
    </div>
  );
}

/** Alan türüne uygun değer girişi. "içinde" işleci liste ister: seçenekli alanlarda onay kutuları, diğerlerinde virgülle ayrılmış metin. */
function ConditionValue({ id, cond, field, people, onChange }: { id: string; cond: FlowCondition; field?: FlowField; people: DirectoryEntry[]; onChange: (v: unknown) => void }) {
  const t = field?.type;
  const isIn = cond.op === 'in';
  const options = field?.options ?? [];

  if (isIn && options.length > 0) {
    const picked = Array.isArray(cond.value) ? (cond.value as string[]) : [];
    return (
      <div className="multi" id={id} role="group" aria-label="Değerler">
        {options.map((o) => <label key={o}><input type="checkbox" checked={picked.includes(o)} onChange={(e) => onChange(e.target.checked ? [...picked, o] : picked.filter((x) => x !== o))} />{o}</label>)}
      </div>
    );
  }
  if (isIn) {
    const numeric = t === 'number' || t === 'money';
    const text = Array.isArray(cond.value) ? cond.value.join(', ') : '';
    return <input id={id} type="text" value={text} placeholder="değer1, değer2" onChange={(e) => {
      const parts = e.target.value.split(',').map((s) => s.trim()).filter(Boolean);
      onChange(numeric ? parts.map(Number).filter(Number.isFinite) : parts);
    }} />;
  }
  if (t === 'number' || t === 'money') return <input id={id} type="number" step="any" value={typeof cond.value === 'number' ? cond.value : ''} onChange={(e) => onChange(e.target.value === '' ? undefined : Number(e.target.value))} />;
  if (t === 'date') return <input id={id} type="date" value={typeof cond.value === 'string' ? cond.value : ''} onChange={(e) => onChange(e.target.value || undefined)} />;
  if (t === 'checkbox') return (
    <select id={id} value={cond.value === true ? 'true' : cond.value === false ? 'false' : ''} onChange={(e) => onChange(e.target.value === '' ? undefined : e.target.value === 'true')}>
      <option value="">Seçin…</option><option value="true">Evet</option><option value="false">Hayır</option>
    </select>
  );
  if (t === 'select') return (
    <select id={id} value={typeof cond.value === 'string' ? cond.value : ''} onChange={(e) => onChange(e.target.value || undefined)}>
      <option value="">Seçin…</option>{options.map((o) => <option key={o} value={o}>{o}</option>)}
    </select>
  );
  if (t === 'user') return (
    <select id={id} value={typeof cond.value === 'string' ? cond.value : ''} onChange={(e) => onChange(e.target.value || undefined)}>
      <option value="">Seçin…</option>{people.map((p) => <option key={p.id} value={p.id}>{p.displayName}</option>)}
    </select>
  );
  return <input id={id} type="text" value={typeof cond.value === 'string' ? cond.value : ''} onChange={(e) => onChange(e.target.value || undefined)} />;
}
