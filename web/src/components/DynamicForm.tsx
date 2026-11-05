import { useId } from 'react';
import { formatValue, withComputed, type FlowDefinition, type FlowField, type FormValues } from '../lib/flow';
import type { DirectoryEntry } from '../types';

interface Props {
  def: FlowDefinition;
  values: FormValues;
  onChange: (key: string, value: unknown) => void;
  errors?: Record<string, string[]>;
  people?: DirectoryEntry[];
  disabled?: boolean;
}

/** Akış tanımından form çizer. Hesaplanan alanlar yalnızca gösterilir; kesin doğrulama sunucudadır. */
export function DynamicForm({ def, values, onChange, errors = {}, people = [], disabled }: Props) {
  const uid = useId();
  const shown = withComputed(def, values);
  const names = Object.fromEntries(people.map((p) => [p.id, p.displayName]));

  return (
    <div className="form-grid">
      {def.fields.map((f) => {
        const id = `${uid}-${f.key}`;
        const errs = errors[f.key] ?? [];
        const err = errs.length > 0;
        const common = { id, disabled, 'aria-invalid': err || undefined, 'aria-describedby': err ? `${id}-err` : undefined } as const;
        const str = typeof values[f.key] === 'string' ? (values[f.key] as string) : '';
        const label = (
          <label htmlFor={f.type === 'multi_select' ? undefined : id}>{f.label}{f.required && !f.computed && <span aria-hidden="true"> *</span>}</label>
        );

        let control: React.ReactNode;
        if (f.computed) {
          control = <div className="computed" id={id}>{shown[f.key] === undefined ? '—' : formatValue(f, shown[f.key], names)}</div>;
        } else switch (f.type) {
          case 'long_text':
            control = <textarea {...common} rows={4} maxLength={f.maxLength ?? 4000} value={str} onChange={(e) => onChange(f.key, e.target.value)} />;
            break;
          case 'number': case 'money':
            control = (
              <input {...common} type="text" inputMode="decimal" autoComplete="off" value={typeof values[f.key] === 'number' ? String(values[f.key]) : str}
                placeholder={f.type === 'money' ? `0,00 ${f.currency ?? 'TRY'}` : undefined} onChange={(e) => onChange(f.key, e.target.value)} />
            );
            break;
          case 'date':
            control = <input {...common} type="date" value={str} min={f.notBefore && typeof values[f.notBefore] === 'string' ? (values[f.notBefore] as string) : undefined}
              onChange={(e) => onChange(f.key, e.target.value)} />;
            break;
          case 'select':
            control = (
              <select {...common} value={str} onChange={(e) => onChange(f.key, e.target.value)}>
                <option value="">Seçin…</option>
                {(f.options ?? []).map((o) => <option key={o} value={o}>{o}</option>)}
              </select>
            );
            break;
          case 'multi_select': {
            const picked = Array.isArray(values[f.key]) ? (values[f.key] as string[]) : [];
            control = (
              <div className="multi" role="group" aria-label={f.label} id={id}>
                {(f.options ?? []).map((o) => (
                  <label key={o}>
                    <input type="checkbox" disabled={disabled} checked={picked.includes(o)}
                      onChange={(e) => onChange(f.key, e.target.checked ? [...picked, o] : picked.filter((x) => x !== o))} />
                    {o}
                  </label>
                ))}
              </div>
            );
            break;
          }
          case 'checkbox':
            return (
              <div className="field wide" key={f.key}>
                <div className="check-row">
                  <input {...common} type="checkbox" checked={values[f.key] === true} onChange={(e) => onChange(f.key, e.target.checked)} />
                  <label htmlFor={id} style={{ margin: 0 }}>{f.label}{f.required && <span aria-hidden="true"> *</span>}</label>
                </div>
                {f.helpText && <div className="help">{f.helpText}</div>}
                {err && <div className="err" id={`${id}-err`} role="alert">{errs.join(' ')}</div>}
              </div>
            );
          case 'user':
            control = (
              <select {...common} value={str} onChange={(e) => onChange(f.key, e.target.value)}>
                <option value="">Seçin…</option>
                {people.map((p) => <option key={p.id} value={p.id}>{p.displayName}</option>)}
              </select>
            );
            break;
          default:
            control = <input {...common} type="text" maxLength={f.maxLength ?? 200} value={str} onChange={(e) => onChange(f.key, e.target.value)} />;
        }

        return (
          <div className={`field${f.type === 'long_text' || f.type === 'multi_select' ? ' wide' : ''}`} key={f.key}>
            {label}
            {control}
            {f.helpText && <div className="help">{f.helpText}</div>}
            {err && <div className="err" id={`${id}-err`} role="alert">{errs.join(' ')}</div>}
          </div>
        );
      })}
    </div>
  );
}

export type { FlowField };
