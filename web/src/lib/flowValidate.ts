import { allowedOps, type FlowDefinition, type FlowCondition, type FlowField } from './flow';

export interface FlowIssue { code: string; path: string; message: string }

export const LIMITS = { fields: 60, steps: 10, summary: 3, options: 50, maxText: 4000, maxFiles: 10 } as const;

const FIELD_KEY = /^[a-z][a-z0-9_]{0,39}$/;
const STEP_ID = /^[a-z][a-z0-9_]{0,19}$/;
const PREFIX = /^[A-Z0-9]{1,10}$/;
const isoDate = (s: unknown) => typeof s === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(s) && !Number.isNaN(Date.parse(s));

/**
 * Sunucudaki FlowValidator'ın birebir istemci karşılığı: tasarlayıcıda anlık geri bildirim içindir. Kesin karar yayınlama sırasında
 * sunucudadır (ayrıca onaylayıcı kişi/grubun var olduğunu yalnızca sunucu bilir).
 */
export function validateFlow(def: FlowDefinition): FlowIssue[] {
  const issues: FlowIssue[] = [];
  const add = (code: string, path: string, message: string) => issues.push({ code, path, message });

  if (def.schema !== 1) add('unsupported_schema', 'schema', 'Desteklenmeyen şema sürümü.');
  if (!PREFIX.test(def.prefix ?? '')) add('invalid_prefix', 'prefix', 'Önek 1–10 büyük harf veya rakam olmalı (ör. IZN).');

  if (def.fields.length === 0) add('fields_required', 'fields', 'En az bir alan gerekli.');
  if (def.fields.length > LIMITS.fields) add('too_many_fields', 'fields', `En fazla ${LIMITS.fields} alan tanımlanabilir.`);

  const byKey = new Map<string, FlowField>();
  def.fields.forEach((f, i) => {
    const p = `fields[${i}]`;
    if (!FIELD_KEY.test(f.key ?? '')) add('invalid_key', `${p}.key`, 'Anahtar küçük harfle başlamalı; yalnızca a-z, 0-9 ve _ içermeli (en fazla 40).');
    else if (byKey.has(f.key)) add('duplicate_key', `${p}.key`, `'${f.key}' anahtarı birden fazla alanda kullanılmış.`);
    else byKey.set(f.key, f);
    if (!f.label || f.label.trim() === '' || f.label.length > 100) add('invalid_name', `${p}.label`, 'Etiket 1–100 karakter olmalı.');
  });

  def.fields.forEach((f, i) => {
    const p = `fields[${i}]`;
    if (f.type === 'select' || f.type === 'multi_select') {
      const opts = f.options ?? [];
      if (opts.length === 0 || opts.some((o) => o.trim() === '')) add('options_required', `${p}.options`, 'Seçim alanı için boş olmayan seçenekler gerekli.');
      else {
        if (opts.length > LIMITS.options) add('too_many_options', `${p}.options`, `En fazla ${LIMITS.options} seçenek tanımlanabilir.`);
        if (new Set(opts.map((o) => o.trim())).size !== opts.length) add('duplicate_option', `${p}.options`, 'Seçenekler benzersiz olmalı.');
        if (opts.some((o) => o.length > 100)) add('invalid_name', `${p}.options`, 'Seçenek en fazla 100 karakter olabilir.');
      }
    }
    if ((f.type === 'number' || f.type === 'money') && f.min !== undefined && f.max !== undefined && f.min > f.max)
      add('invalid_range', `${p}.min`, 'En küçük değer en büyük değerden büyük olamaz.');
    if (f.maxFiles !== undefined && (f.type !== 'file' || f.maxFiles < 1 || f.maxFiles > LIMITS.maxFiles))
      add('invalid_range', `${p}.maxFiles`, `Dosya sayısı yalnızca ek alanında ve 1–${LIMITS.maxFiles} arasında verilebilir.`);
    if ((f.type === 'text' || f.type === 'long_text') && f.maxLength !== undefined && (f.maxLength < 1 || f.maxLength > LIMITS.maxText))
      add('invalid_range', `${p}.maxLength`, `En fazla uzunluk 1–${LIMITS.maxText} olmalı.`);
    if (f.notBefore !== undefined && f.notBefore !== '') {
      const other = byKey.get(f.notBefore);
      if (f.type !== 'date' || f.notBefore === f.key || !other || other.type !== 'date')
        add('invalid_reference', `${p}.notBefore`, "'Önce olamaz' yalnızca başka bir tarih alanına bağlanabilir.");
    }
    if (f.computed) {
      const from = byKey.get(f.computed.from), to = byKey.get(f.computed.to);
      if (f.type !== 'number' || from?.type !== 'date' || to?.type !== 'date')
        add('invalid_computed', `${p}.computed`, 'Hesaplanan alan bir sayı olmalı ve iki tarih alanından hesaplanmalı.');
      if (f.required) add('computed_required', `${p}.required`, 'Hesaplanan alan zorunlu olamaz; değeri sunucu üretir.');
    }
  });

  if (def.summaryFields.length > LIMITS.summary) add('too_many_summary_fields', 'summaryFields', `En fazla ${LIMITS.summary} özet alanı seçilebilir.`);
  def.summaryFields.forEach((k, i) => { if (!byKey.has(k)) add('unknown_field', `summaryFields[${i}]`, `'${k}' adlı alan yok.`); });

  if (def.steps.length === 0) add('steps_required', 'steps', 'En az bir onay adımı gerekli.');
  if (def.steps.length > LIMITS.steps) add('too_many_steps', 'steps', `En fazla ${LIMITS.steps} adım tanımlanabilir.`);
  const ids = new Set<string>();
  def.steps.forEach((s, i) => {
    const p = `steps[${i}]`;
    if (!STEP_ID.test(s.id ?? '')) add('invalid_key', `${p}.id`, 'Adım kimliği küçük harfle başlamalı; yalnızca a-z, 0-9 ve _ içermeli (en fazla 20).');
    else if (ids.has(s.id)) add('duplicate_step_id', `${p}.id`, `'${s.id}' kimliği birden fazla adımda kullanılmış.`);
    else ids.add(s.id);
    if (!s.name || s.name.trim() === '' || s.name.length > 100) add('invalid_name', `${p}.name`, 'Adım adı 1–100 karakter olmalı.');
    const a = s.approvers;
    if ((a.type === 'user' || a.type === 'group') && !a.id) add('approver_id_required', `${p}.approvers.id`, 'Kişi veya grup seçilmeli.');
    if ((a.type === 'manager' || a.type === 'admin') && a.id) add('approver_id_unexpected', `${p}.approvers.id`, 'Bu onaylayıcı türünde kimlik verilmez.');
    (s.conditions ?? []).forEach((c, j) => validateCondition(c, `${p}.conditions[${j}]`, byKey, add));
  });

  const acc = def.completion?.accounting;
  if (acc?.enabled && (!acc.category || acc.category.trim() === '' || acc.category.length > 40))
    add('category_required', 'completion.accounting.category', 'Muhasebe kategorisi (en fazla 40 karakter) gerekli.');

  return issues;
}

function validateCondition(c: FlowCondition, path: string, byKey: Map<string, FlowField>, add: (c: string, p: string, m: string) => void) {
  const f = byKey.get(c.field);
  if (!f) { add('unknown_field', `${path}.field`, `'${c.field}' adlı alan yok.`); return; }
  if (!allowedOps(f.type).includes(c.op)) { add('invalid_condition', `${path}.op`, 'Bu alan türü için bu koşul kullanılamaz.'); return; }
  const scalar = (v: unknown) => {
    switch (f.type) {
      case 'number': case 'money': return typeof v === 'number' && Number.isFinite(v);
      case 'date': return isoDate(v);
      case 'checkbox': return typeof v === 'boolean';
      default: return typeof v === 'string';
    }
  };
  const ok = c.op === 'in' ? Array.isArray(c.value) && c.value.length > 0 && c.value.every(scalar) : c.value !== undefined && c.value !== null && scalar(c.value);
  if (!ok) add('condition_value', `${path}.value`, "Koşul değeri alan türüne uygun olmalı ('içinde' için dolu bir liste).");
}

/** Anlık bulguları yola göre gruplar (editörde ilgili satırın altında göstermek için). */
export function issuesByPath(issues: readonly FlowIssue[]): Map<string, string[]> {
  const m = new Map<string, string[]>();
  for (const i of issues) m.set(i.path, [...(m.get(i.path) ?? []), i.message]);
  return m;
}
