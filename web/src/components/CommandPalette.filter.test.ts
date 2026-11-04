import { describe, expect, it } from 'vitest';
import { filterItems, type PaletteItem } from './CommandPalette';

const item = (id: string, label: string, extra: Partial<PaletteItem> = {}): PaletteItem => ({ id, label, group: 'g', onSelect: () => {}, ...extra });

const items = [
  item('CMP-001', 'Uygulama API', { hint: 'CMP-001 · component' }),
  item('CMP-002', 'Onay modülü', { hint: 'CMP-002 · component' }),
  item('REQ-001', 'Çalışanlar masraf girebilmeli', { hint: 'REQ-001 · requirement' }),
  item('DEC-001', 'Modüler monolit', { keywords: 'mimari yaklaşım' }),
  item('X', 'İstanbul ofisi'),
];

describe('filterItems', () => {
  it('returns everything (up to the limit) for an empty or whitespace query', () => {
    expect(filterItems(items, '')).toHaveLength(items.length);
    expect(filterItems(items, '   ')).toHaveLength(items.length);
    expect(filterItems(items, '', 2)).toHaveLength(2);
  });

  it('matches on id, label, hint and keywords', () => {
    expect(filterItems(items, 'cmp-002').map((i) => i.id)).toEqual(['CMP-002']);
    expect(filterItems(items, 'masraf').map((i) => i.id)).toEqual(['REQ-001']);
    expect(filterItems(items, 'requirement').map((i) => i.id)).toEqual(['REQ-001']);
    expect(filterItems(items, 'yaklaşım').map((i) => i.id)).toEqual(['DEC-001']);
  });

  it('requires every word to match (AND), in any order', () => {
    expect(filterItems(items, 'api uygulama').map((i) => i.id)).toEqual(['CMP-001']);
    expect(filterItems(items, 'api onay')).toEqual([]);
  });

  it('folds Turkish dotted/dotless I so İ/I/i/ı typed either way still find the item', () => {
    expect(filterItems(items, 'istanbul').map((i) => i.id)).toEqual(['X']);
    expect(filterItems(items, 'İSTANBUL').map((i) => i.id)).toEqual(['X']);
    expect(filterItems(items, 'ISPARTA')).toEqual([]);
  });

  it('puts labels that START with the query before labels that merely contain it', () => {
    const list = [item('a', 'Onay akışı modülü'), item('b', 'Modül sınırları'), item('c', 'Modüler monolit')];
    const ordered = filterItems(list, 'modül').map((i) => i.id);
    expect(ordered.slice(0, 2).sort()).toEqual(['b', 'c']);
    expect(ordered[2]).toBe('a');
  });

  it('returns an empty list for no match and never mutates the input', () => {
    const copy = [...items];
    expect(filterItems(items, 'zzzz-yok')).toEqual([]);
    expect(items).toEqual(copy);
  });

  it('respects the limit after filtering', () => {
    const many = Array.from({ length: 100 }, (_, i) => item(`N-${i}`, `Düğüm ${i}`));
    expect(filterItems(many, 'düğüm', 5)).toHaveLength(5);
    expect(filterItems(many, 'düğüm')).toHaveLength(40);
  });
});
