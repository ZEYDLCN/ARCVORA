import type { Component, Relationship } from '../types';

export const NODE_W = 184;
export const NODE_H = 66;
const COL_GAP = 136;
const ROW_GAP = 34;
const PAD_X = 36;
const PAD_Y = 36;

/** Soldan sağa akış: istemci → API → modüller → işleyici/kuyruk → veri → dış sistem. */
const layerOfKind: Record<string, number> = {
  client: 0, api: 1, module: 2, worker: 3, queue: 3, database: 4, storage: 4, external_service: 5,
};

export interface CanvasNode {
  id: string; name: string; kind: string; technology?: string;
  x: number; y: number; layer: number; degree: number;
}

export interface CanvasEdge {
  id: string; from: string; to: string; label: string; communication: string; failure?: string;
  d: string; lx: number; ly: number;
}

export interface CanvasLayout {
  width: number; height: number; nodes: CanvasNode[]; edges: CanvasEdge[];
  /** En çok bağlantılı bileşen (≥3 bağlantı): tuvalde vurgulanır. */
  hub: string | null;
}

export function layoutComponents(components: Component[], relationships: Relationship[]): CanvasLayout {
  const live = components.filter((c) => c.reviewStatus !== 'rejected').sort((a, b) => a.id.localeCompare(b.id));
  const ids = new Set(live.map((c) => c.id));
  // Girdi sırasından bağımsız, kararlı çıktı için kimliğe göre sıralanır
  const rels = relationships.filter((r) => ids.has(r.sourceId) && ids.has(r.targetId) && r.reviewStatus !== 'rejected')
    .sort((a, b) => a.id.localeCompare(b.id));

  // Boş kolonları sıkıştır
  const used = [...new Set(live.map((c) => layerOfKind[c.kind] ?? 2))].sort((a, b) => a - b);
  const layerIndex = new Map(used.map((l, i) => [l, i]));
  const columns: Component[][] = used.map(() => []);
  for (const c of live) columns[layerIndex.get(layerOfKind[c.kind] ?? 2)!].push(c);

  // Kesişimleri azaltmak için barycenter sıralaması (ileri/geri 3 tur)
  const neighbours = new Map<string, string[]>();
  for (const r of rels) {
    neighbours.set(r.sourceId, [...(neighbours.get(r.sourceId) ?? []), r.targetId]);
    neighbours.set(r.targetId, [...(neighbours.get(r.targetId) ?? []), r.sourceId]);
  }
  const position = () => new Map(columns.flatMap((col) => col.map((c, i) => [c.id, col.length > 1 ? i / (col.length - 1) : 0.5] as const)));
  const sweep = (order: number[]) => {
    for (const li of order) {
      const pos = position();
      const score = (c: Component) => {
        const ns = (neighbours.get(c.id) ?? []).filter((n) => columns.findIndex((col) => col.some((x) => x.id === n)) !== li);
        return ns.length ? ns.reduce((s, n) => s + (pos.get(n) ?? 0.5), 0) / ns.length : (pos.get(c.id) ?? 0.5);
      };
      columns[li] = [...columns[li]].sort((a, b) => score(a) - score(b) || a.id.localeCompare(b.id));
    }
  };
  const forward = columns.map((_, i) => i);
  for (let pass = 0; pass < 3; pass++) { sweep(forward); sweep([...forward].reverse()); }

  const tallest = Math.max(0, ...columns.map((c) => c.length * NODE_H + Math.max(0, c.length - 1) * ROW_GAP));
  const degree = new Map<string, number>();
  for (const r of rels) {
    degree.set(r.sourceId, (degree.get(r.sourceId) ?? 0) + 1);
    degree.set(r.targetId, (degree.get(r.targetId) ?? 0) + 1);
  }

  const nodes: CanvasNode[] = [];
  columns.forEach((col, li) => {
    const colH = col.length * NODE_H + Math.max(0, col.length - 1) * ROW_GAP;
    const top = PAD_Y + (tallest - colH) / 2;
    col.forEach((c, i) => nodes.push({
      id: c.id, name: c.name, kind: c.kind, technology: c.technology,
      x: PAD_X + li * (NODE_W + COL_GAP), y: top + i * (NODE_H + ROW_GAP), layer: li, degree: degree.get(c.id) ?? 0,
    }));
  });
  const byId = new Map(nodes.map((n) => [n.id, n]));

  // Aynı iki bileşen arasındaki birden çok bağlantı birbirinden ayrışsın
  const pairCount = new Map<string, number>();
  const pairSeen = new Map<string, number>();
  const pairKey = (a: string, b: string) => [a, b].sort().join('|');
  rels.forEach((r) => pairCount.set(pairKey(r.sourceId, r.targetId), (pairCount.get(pairKey(r.sourceId, r.targetId)) ?? 0) + 1));

  const edges: CanvasEdge[] = rels.map((r) => {
    const s = byId.get(r.sourceId)!;
    const t = byId.get(r.targetId)!;
    const key = pairKey(r.sourceId, r.targetId);
    const k = pairSeen.get(key) ?? 0;
    pairSeen.set(key, k + 1);
    const n = pairCount.get(key)!;
    const off = (k - (n - 1) / 2) * 12;

    let x1: number, y1: number, x2: number, y2: number, d: string;
    const cy1 = s.y + NODE_H / 2 + off;
    const cy2 = t.y + NODE_H / 2 + off;
    if (t.layer > s.layer) {
      x1 = s.x + NODE_W; y1 = cy1; x2 = t.x; y2 = cy2;
      const dx = Math.min(120, Math.max(48, (x2 - x1) / 2));
      d = `M${x1} ${y1}C${x1 + dx} ${y1} ${x2 - dx} ${y2} ${x2} ${y2}`;
    } else if (t.layer < s.layer) {
      x1 = s.x; y1 = cy1; x2 = t.x + NODE_W; y2 = cy2;
      const dx = Math.min(120, Math.max(48, (x1 - x2) / 2));
      d = `M${x1} ${y1}C${x1 - dx} ${y1} ${x2 + dx} ${y2} ${x2} ${y2}`;
    } else {
      // Aynı kolon: alt/üst kenarlardan, dışa doğru kavisle
      const down = t.y > s.y;
      x1 = s.x + NODE_W / 2 + off; x2 = t.x + NODE_W / 2 + off;
      y1 = down ? s.y + NODE_H : s.y; y2 = down ? t.y : t.y + NODE_H;
      const bow = down ? 40 : -40;
      d = `M${x1} ${y1}C${x1} ${y1 + bow} ${x2} ${y2 - bow} ${x2} ${y2}`;
    }
    // Kübik Bézier'in orta noktası (t = 0.5) etiket konumu
    const m = /M([-\d.]+) ([-\d.]+)C([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+)/.exec(d)!.slice(1).map(Number);
    const lx = (m[0] + 3 * m[2] + 3 * m[4] + m[6]) / 8;
    const ly = (m[1] + 3 * m[3] + 3 * m[5] + m[7]) / 8;
    return { id: r.id, from: r.sourceId, to: r.targetId, label: r.description, communication: r.communication, failure: r.failureBehavior, d, lx, ly };
  });

  const hubNode = [...nodes].sort((a, b) => b.degree - a.degree || a.id.localeCompare(b.id))[0];
  const width = nodes.length ? PAD_X * 2 + columns.length * NODE_W + (columns.length - 1) * COL_GAP : 0;
  return { width, height: PAD_Y * 2 + tallest, nodes, edges, hub: hubNode && hubNode.degree >= 3 ? hubNode.id : null };
}
