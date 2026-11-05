// Akış uygulamaları: sunucudaki çekirdek kurallarla aynı biçim ve önizleme hesapları (kesin doğrulama her zaman sunucudadır).

export type FieldType = 'text' | 'long_text' | 'number' | 'money' | 'date' | 'select' | 'multi_select' | 'checkbox' | 'user' | 'file';
export type ComputedOp = 'calendar_days' | 'weekdays';
export type ApproverType = 'manager' | 'user' | 'group' | 'admin';
export type ConditionOp = 'eq' | 'ne' | 'gt' | 'gte' | 'lt' | 'lte' | 'in';

export interface FlowField {
  key: string; label: string; type: FieldType; required?: boolean; helpText?: string; options?: string[];
  min?: number; max?: number; maxLength?: number; notBefore?: string; currency?: string; maxFiles?: number;
  computed?: { op: ComputedOp; from: string; to: string };
}
export interface ApproverSpec { type: ApproverType; id?: string }
export interface FlowCondition { field: string; op: ConditionOp; value?: unknown }
export interface FlowStep { id: string; name: string; approvers: ApproverSpec; mode?: 'any' | 'all'; conditions?: FlowCondition[] }
export interface FlowDefinition {
  schema: number; prefix: string; summaryFields: string[]; fields: FlowField[]; steps: FlowStep[];
  completion?: { accounting?: { enabled: boolean; category?: string } };
}

export const fieldTypeLabels: Record<FieldType, string> = {
  text: 'Kısa metin', long_text: 'Uzun metin', number: 'Sayı', money: 'Tutar', date: 'Tarih', select: 'Tek seçim',
  multi_select: 'Çoklu seçim', checkbox: 'Onay kutusu', user: 'Kişi', file: 'Ek (fiş / belge / tarama)',
};

export const approverLabels: Record<ApproverType, string> = {
  manager: 'Talep edenin yöneticisi', user: 'Belirli bir kişi', group: 'Bir grup', admin: 'Master admin',
};

export const opLabels: Record<ConditionOp, string> = { eq: 'eşit', ne: 'eşit değil', gt: 'büyük', gte: 'büyük veya eşit', lt: 'küçük', lte: 'küçük veya eşit', in: 'içinde' };

export function emptyDefinition(prefix = 'APP'): FlowDefinition {
  return { schema: 1, prefix, summaryFields: [], fields: [], steps: [] };
}

const trMap: Record<string, string> = { ç: 'c', ğ: 'g', ı: 'i', ö: 'o', ş: 's', ü: 'u', â: 'a', î: 'i', û: 'u' };

/** "Başlangıç tarihi" → "baslangic_tarihi": sunucunun beklediği anahtar biçimi (a-z0-9_, harfle başlar, en fazla 40), benzersiz. */
export function slugKey(label: string, existing: readonly string[] = []): string {
  let s = label.replace(/İ/g, 'i').replace(/I/g, 'ı').toLowerCase().replace(/[çğıöşüâîû]/g, (c) => trMap[c] ?? c)
    .replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '');
  if (!/^[a-z]/.test(s)) s = `alan_${s}`.replace(/_$/, '');
  s = s.slice(0, 40).replace(/_+$/, '') || 'alan';
  if (!existing.includes(s)) return s;
  for (let i = 2; ; i++) {
    const suffix = `_${i}`;
    const candidate = s.slice(0, 40 - suffix.length) + suffix;
    if (!existing.includes(candidate)) return candidate;
  }
}

/** Adım kimliği: s1, s2… (en fazla 20 karakter; sunucu deseni). */
export function nextStepId(steps: readonly { id: string }[]): string {
  for (let i = steps.length + 1; ; i++) if (!steps.some((s) => s.id === `s${i}`)) return `s${i}`;
}

function parseDate(s: unknown): Date | null {
  if (typeof s !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(s)) return null;
  const [y, m, d] = s.split('-').map(Number);
  const date = new Date(Date.UTC(y, m - 1, d));
  return date.getUTCFullYear() === y && date.getUTCMonth() === m - 1 && date.getUTCDate() === d ? date : null;
}

/** Sunucudaki hesapla aynı: takvim günü (uçlar dahil) veya hafta içi gün sayısı; aralık geçersizse null. */
export function computeDays(op: ComputedOp, from: unknown, to: unknown): number | null {
  const a = parseDate(from), b = parseDate(to);
  if (!a || !b || b < a) return null;
  const span = Math.round((b.getTime() - a.getTime()) / 86_400_000) + 1;
  if (op === 'calendar_days') return span;
  if (span > 3660) return null;
  let n = 0;
  for (let i = 0; i < span; i++) {
    const day = new Date(a.getTime() + i * 86_400_000).getUTCDay();
    if (day !== 0 && day !== 6) n++;
  }
  return n;
}

export type FormValues = Record<string, unknown>;

/** Hesaplanan alanlar dahil ekranda gösterilecek değerler (yalnızca önizleme). */
export function withComputed(def: FlowDefinition, values: FormValues): FormValues {
  const out: FormValues = { ...values };
  for (const f of def.fields) if (f.computed) {
    const v = computeDays(f.computed.op, values[f.computed.from], values[f.computed.to]);
    if (v === null) delete out[f.key]; else out[f.key] = v;
  }
  return out;
}

/** Formdaki ham girdilerden sunucuya gönderilecek veri: boşlar atılır, sayılar sayıya çevrilir, hesaplanan alanlar gönderilmez. */
export function buildPayload(def: FlowDefinition, values: FormValues): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const f of def.fields) {
    if (f.computed) continue;
    const v = values[f.key];
    switch (f.type) {
      case 'text': case 'long_text': case 'select': case 'user': case 'date':
        if (typeof v === 'string' && v.trim() !== '') out[f.key] = v.trim();
        break;
      case 'number': case 'money': {
        const n = typeof v === 'number' ? v : typeof v === 'string' && v.trim() !== '' ? Number(v.trim().replace(',', '.')) : NaN;
        if (Number.isFinite(n)) out[f.key] = n;
        else if (typeof v === 'string' && v.trim() !== '') out[f.key] = v;      // sayı değil: sunucu invalid_type ile açıklasın
        break;
      }
      case 'multi_select':
        if (Array.isArray(v) && v.length > 0) out[f.key] = v;
        break;
      case 'checkbox':
        if (typeof v === 'boolean') out[f.key] = v;
        break;
      case 'file': {
        const ids = Array.isArray(v) ? v.map((x) => (typeof x === 'string' ? x : (x as { id?: unknown })?.id)).filter((x): x is string => typeof x === 'string') : [];
        if (ids.length > 0) out[f.key] = ids;
        break;
      }
    }
  }
  return out;
}

/** Sunucu bulgularını alan anahtarına göre gruplar: "data.start" → start. Alana bağlı olmayanlar "_" altına düşer. */
export function issuesByField(issues: readonly { path: string; message: string }[]): Record<string, string[]> {
  const out: Record<string, string[]> = {};
  for (const i of issues) {
    const key = i.path.startsWith('data.') ? i.path.slice(5) : '_';
    (out[key] ??= []).push(i.message);
  }
  return out;
}

const trNumber = new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 2 });
const trMoney = new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export function formatDate(iso: string): string {
  const d = parseDate(iso);
  return d ? `${String(d.getUTCDate()).padStart(2, '0')}.${String(d.getUTCMonth() + 1).padStart(2, '0')}.${d.getUTCFullYear()}` : iso;
}

/** Kayıtlı bir değeri okunur metne çevirir. Metinler olduğu gibi döner (React kaçışı gösterimde yapılır). */
export function formatValue(f: FlowField, v: unknown, people: Record<string, string> = {}): string {
  if (v === undefined || v === null || v === '') return '—';
  switch (f.type) {
    case 'date': return formatDate(String(v));
    case 'money': return typeof v === 'number' ? `${trMoney.format(v)} ${f.currency ?? 'TRY'}` : String(v);
    case 'number': return typeof v === 'number' ? trNumber.format(v) : String(v);
    case 'checkbox': return v === true ? 'Evet' : 'Hayır';
    case 'multi_select': return Array.isArray(v) ? v.join(', ') : String(v);
    case 'user': return people[String(v)] ?? '(bilinmeyen kullanıcı)';
    case 'file': return Array.isArray(v) && v.length > 0 ? `${v.length} dosya` : '—';
    default: return String(v);
  }
}

/** Alan türüne göre koşulda kullanılabilecek işleçler (sunucudaki doğrulamayla aynı). */
export function allowedOps(type: FieldType): ConditionOp[] {
  switch (type) {
    case 'number': case 'money': case 'date': return ['eq', 'ne', 'gt', 'gte', 'lt', 'lte', 'in'];
    case 'text': case 'select': case 'user': return ['eq', 'ne', 'in'];
    case 'checkbox': return ['eq', 'ne'];
    case 'multi_select': return ['in'];
    default: return [];
  }
}
