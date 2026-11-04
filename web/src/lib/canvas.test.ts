import { describe, expect, it } from 'vitest';
import { layoutComponents, NODE_H, NODE_W } from './canvas';
import type { Component, Relationship } from '../types';

const c = (id: string, kind: string, extra: Partial<Component> = {}): Component => ({
  id, name: id, kind, responsibility: '', origin: 'system_proposal', reviewStatus: 'confirmed', ...extra,
});
const r = (id: string, s: string, t: string, communication = 'sync', extra: Partial<Relationship> = {}): Relationship => ({
  id, sourceId: s, targetId: t, communication, description: id, origin: 'system_proposal', reviewStatus: 'confirmed', ...extra,
});

const comps = [
  c('CMP-001', 'client'), c('CMP-002', 'api'), c('CMP-003', 'module'), c('CMP-004', 'module'),
  c('CMP-005', 'database'), c('CMP-006', 'worker'), c('CMP-007', 'external_service'),
];
const rels = [
  r('REL-001', 'CMP-001', 'CMP-002'), r('REL-002', 'CMP-002', 'CMP-003'), r('REL-003', 'CMP-002', 'CMP-004'),
  r('REL-004', 'CMP-003', 'CMP-005', 'data'), r('REL-005', 'CMP-004', 'CMP-005', 'data'),
  r('REL-006', 'CMP-006', 'CMP-005', 'data'), r('REL-007', 'CMP-006', 'CMP-007'),
];

describe('layoutComponents', () => {
  it('places components left-to-right by kind', () => {
    const l = layoutComponents(comps, rels);
    const x = (id: string) => l.nodes.find((n) => n.id === id)!.x;
    expect(x('CMP-001')).toBeLessThan(x('CMP-002'));
    expect(x('CMP-002')).toBeLessThan(x('CMP-003'));
    expect(x('CMP-003')).toBeLessThan(x('CMP-006'));
    expect(x('CMP-006')).toBeLessThan(x('CMP-005'));
    expect(x('CMP-005')).toBeLessThan(x('CMP-007'));
    expect(x('CMP-003')).toBe(x('CMP-004'));              // aynı tür aynı kolon
  });

  it('never overlaps nodes and keeps them inside the canvas', () => {
    const l = layoutComponents(comps, rels);
    for (const a of l.nodes) {
      expect(a.x + NODE_W).toBeLessThanOrEqual(l.width);
      expect(a.y + NODE_H).toBeLessThanOrEqual(l.height);
      for (const b of l.nodes) {
        if (a.id >= b.id) continue;
        const overlap = a.x < b.x + NODE_W && b.x < a.x + NODE_W && a.y < b.y + NODE_H && b.y < a.y + NODE_H;
        expect(overlap, `${a.id} ile ${b.id} çakışıyor`).toBe(false);
      }
    }
  });

  it('compresses empty columns instead of leaving gaps', () => {
    const l = layoutComponents([c('CMP-001', 'client'), c('CMP-002', 'database')], [r('REL-001', 'CMP-001', 'CMP-002')]);
    expect(l.nodes.map((n) => n.layer)).toEqual([0, 1]);
  });

  it('draws one edge per valid relationship and skips dangling or rejected ones', () => {
    const l = layoutComponents(
      [...comps, c('CMP-008', 'module', { reviewStatus: 'rejected' })],
      [...rels, r('REL-099', 'CMP-001', 'CMP-404'), r('REL-098', 'CMP-002', 'CMP-008')],
    );
    expect(l.edges).toHaveLength(rels.length);
    expect(l.nodes.find((n) => n.id === 'CMP-008')).toBeUndefined();
    for (const e of l.edges) {
      expect(e.d).toMatch(/^M[-\d.]+ [-\d.]+C/);
      expect(Number.isFinite(e.lx) && Number.isFinite(e.ly)).toBe(true);
    }
  });

  it('keeps parallel relationships between the same pair visually apart', () => {
    const l = layoutComponents([c('CMP-001', 'api'), c('CMP-002', 'database')], [r('REL-001', 'CMP-001', 'CMP-002'), r('REL-002', 'CMP-001', 'CMP-002', 'data')]);
    expect(l.edges[0].d).not.toBe(l.edges[1].d);
  });

  it('routes same-column and backward relationships without NaN', () => {
    const l = layoutComponents([c('CMP-001', 'module'), c('CMP-002', 'module'), c('CMP-003', 'database')],
      [r('REL-001', 'CMP-001', 'CMP-002'), r('REL-002', 'CMP-003', 'CMP-001')]);
    expect(l.edges.every((e) => !e.d.includes('NaN'))).toBe(true);
  });

  it('highlights the busiest component only when it is a real hub', () => {
    expect(layoutComponents(comps, rels).hub).toBe('CMP-002');       // CMP-002 ve CMP-005 üçer bağlantılı; eşitlikte küçük kimlik
    expect(layoutComponents([c('CMP-001', 'client'), c('CMP-002', 'api')], [r('REL-001', 'CMP-001', 'CMP-002')]).hub).toBeNull();
  });

  it('is deterministic and handles an empty model', () => {
    expect(JSON.stringify(layoutComponents(comps, rels))).toBe(JSON.stringify(layoutComponents([...comps].reverse(), [...rels].reverse())));
    const e = layoutComponents([], []);
    expect(e.nodes).toEqual([]);
    expect(e.width).toBe(0);
  });
});
