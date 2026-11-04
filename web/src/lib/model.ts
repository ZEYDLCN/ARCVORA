import type { ImpactLevel, Model, Origin, Priority, ReviewStatus, RequirementKind, TraceLink } from '../types';

export interface Labeled { label: string; glyph: string; tone: 'ok' | 'warn' | 'info' | 'bad' | 'neutral' }

// Durumlar yalnızca renkle değil, simge ve etiketle de anlatılır (§12.1).
export const reviewInfo: Record<ReviewStatus, Labeled> = {
  confirmed: { label: 'Onaylandı', glyph: '✓', tone: 'ok' },
  proposed: { label: 'Önerildi', glyph: '◔', tone: 'info' },
  deferred: { label: 'Ertelendi', glyph: '⏸', tone: 'warn' },
  rejected: { label: 'Reddedildi', glyph: '✕', tone: 'bad' },
};

export const originInfo: Record<Origin, Labeled> = {
  user_statement: { label: 'Kullanıcı', glyph: '●', tone: 'neutral' },
  system_proposal: { label: 'Sistem önerisi', glyph: '◆', tone: 'info' },
  assumption: { label: 'Varsayım', glyph: '?', tone: 'warn' },
};

export const priorityLabel: Record<Priority, string> = { critical: 'Kritik', high: 'Yüksek', medium: 'Orta', low: 'Düşük' };
export const kindLabel: Record<RequirementKind, string> = {
  functional: 'İşlevsel', quality: 'Kalite', constraint: 'Kısıt', existing_decision: 'Mevcut karar',
};
export const levelInfo: Record<ImpactLevel, Labeled> = {
  direct: { label: 'Doğrudan', glyph: '■', tone: 'bad' },
  dependent: { label: 'Bağımlı', glyph: '▲', tone: 'warn' },
  possible: { label: 'Olası (inceleme gerekir)', glyph: '◇', tone: 'info' },
  uncertain: { label: 'Belirsiz', glyph: '?', tone: 'neutral' },
};
export const changeLabel: Record<string, string> = { added: 'Eklendi', changed: 'Değişti', removed: 'Silindi', none: '' };

export const findingKindLabel: Record<string, string> = {
  schema: 'Şema', reference: 'Referans', consistency: 'Tutarlılık', traceability: 'İzlenebilirlik', profile: 'Profil',
  review_suggestion: 'İnceleme önerisi',
};
export const severityInfo: Record<string, Labeled> = {
  error: { label: 'Hata', glyph: '✕', tone: 'bad' },
  warning: { label: 'Uyarı', glyph: '!', tone: 'warn' },
  info: { label: 'Bilgi', glyph: 'i', tone: 'neutral' },
};

const typeByPrefix: Record<string, string> = {
  SRC: 'Kaynak', REQ: 'Gereksinim', ASM: 'Varsayım', DEC: 'Karar', CMP: 'Bileşen', REL: 'Bağlantı', ENT: 'Varlık',
  ACT: 'Aktör', FLW: 'Akış', API: 'Endpoint', TRC: 'İzlenebilirlik', OQ: 'Açık soru',
};
export const typeOfId = (id: string): string => typeByPrefix[id.split('-')[0]] ?? 'Nesne';

export interface Indexed { id: string; type: string; title: string; obj: unknown }

export function buildIndex(m: Model): Map<string, Indexed> {
  const map = new Map<string, Indexed>();
  const add = (id: string, title: string, obj: unknown) => map.set(id, { id, type: typeOfId(id), title, obj });
  m.sources.forEach((x) => add(x.id, x.title || x.text.slice(0, 60), x));
  m.requirements.forEach((x) => add(x.id, x.text, x));
  m.assumptions.forEach((x) => add(x.id, x.text, x));
  m.decisions.forEach((x) => add(x.id, x.title, x));
  m.components.forEach((x) => add(x.id, x.name, x));
  m.relationships.forEach((x) => add(x.id, `${x.sourceId} → ${x.targetId}`, x));
  m.entities.forEach((x) => add(x.id, x.name, x));
  m.actors.forEach((x) => add(x.id, x.name, x));
  m.flows.forEach((x) => add(x.id, x.name, x));
  m.endpoints.forEach((x) => add(x.id, `${x.method} ${x.path}`, x));
  m.openQuestions.forEach((x) => add(x.id, x.text, x));
  return map;
}

export interface RelationGroup { label: string; ids: string[] }

const forward: Record<TraceLink['kind'], string> = {
  addresses: 'Dayandığı gereksinim', implements: 'Uyguladığı karar', depends_on: 'Bağlı olduğu karar',
  assumes: 'Dayandığı varsayım', blocked_by: 'Cevap bekleyen soru',
};
const backward: Record<TraceLink['kind'], string> = {
  addresses: 'Bu gereksinime dayanan karar', implements: 'Bu kararı uygulayan nesne', depends_on: 'Buna bağlı karar',
  assumes: 'Bu varsayıma dayanan karar', blocked_by: 'Bu soruya bağlı nesne',
};

/** Seçili nesnenin izlenebilirlik ve yapısal ilişkileri; gerekçe → karar → tasarım zincirini gösterir. */
export function relationsOf(m: Model, id: string): RelationGroup[] {
  const groups = new Map<string, Set<string>>();
  const put = (label: string, other: string) => {
    if (!groups.has(label)) groups.set(label, new Set());
    groups.get(label)!.add(other);
  };
  for (const t of m.traceLinks) {
    if (t.fromId === id) put(forward[t.kind], t.toId);
    if (t.toId === id) put(backward[t.kind], t.fromId);
  }
  m.relationships.forEach((r) => {
    if (r.sourceId === id) put('Giden bağlantı', r.id);
    if (r.targetId === id) put('Gelen bağlantı', r.id);
  });
  m.entities.forEach((e) => {
    if (e.ownerComponentId === id) put('Sahip olduğu veri', e.id);
    if (e.id === id && e.ownerComponentId) put('Sahibi', e.ownerComponentId);
    e.relations.forEach((r) => { if (e.id === id) put('İlişkili varlık', r.targetEntityId); if (r.targetEntityId === id) put('İlişkili varlık', e.id); });
  });
  m.endpoints.forEach((e) => {
    if (e.componentId === id) put('Sunduğu endpoint', e.id);
    if (e.id === id && e.componentId) put('Bileşen', e.componentId);
  });
  m.flows.forEach((f) => {
    if (f.participants.includes(id)) put('Katıldığı akış', f.id);
    if (f.id === id) f.participants.forEach((p) => put('Katılımcı', p));
  });
  m.requirements.forEach((r) => r.sourceRefs.forEach((s) => {
    if (r.id === id) put('Kaynak', s.sourceId);
    if (s.sourceId === id) put('Bu kaynaktan çıkan gereksinim', r.id);
  }));
  return [...groups].map(([label, ids]) => ({ label, ids: [...ids].sort() }));
}

/** Mermaid düğüm kimliğini (CMP_001) model kimliğine (CMP-001) çevirir. */
export function modelIdFromNode(nodeId: string): string | null {
  const m = /([A-Z]{2,3})_(\d{3,})/.exec(nodeId);
  return m ? `${m[1]}-${m[2]}` : null;
}

/** Diyagramın metinsel karşılığı: erişilebilirlik ve büyük diyagramlar için (§12.3). */
export function textAlternative(m: Model, kind: 'component' | 'er' | 'sequence', flowId?: string): string[] {
  const idx = buildIndex(m);
  const name = (id: string) => idx.get(id)?.title ?? id;
  if (kind === 'component') {
    return m.relationships.map((r) => `${name(r.sourceId)} → ${name(r.targetId)}${r.description ? ` (${r.description})` : ''}`);
  }
  if (kind === 'er') {
    return m.entities.map((e) => `${e.name}: ${e.fields.map((f) => f.name).join(', ') || '—'}`);
  }
  const flow = m.flows.find((f) => f.id === flowId);
  return flow ? flow.steps.map((s, i) => `${i + 1}. ${name(s.from)} → ${name(s.to)}: ${s.message}${s.condition ? ` [${s.condition}]` : ''}`) : [];
}
