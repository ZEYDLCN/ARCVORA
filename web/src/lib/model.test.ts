import { describe, expect, it } from 'vitest';
import { buildIndex, modelIdFromNode, relationsOf, textAlternative, typeOfId } from './model';
import type { Model } from '../types';

const base = (): Model => ({
  schemaVersion: '1', project: { name: 'x', description: '', techProfileId: 'aspnetcore' },
  sources: [], requirements: [], assumptions: [], decisions: [], components: [], relationships: [], entities: [],
  actors: [], flows: [], endpoints: [], traceLinks: [], openQuestions: [],
});

const mk = (): Model => ({
  ...base(),
  requirements: [{ id: 'REQ-001', text: 'Kiracı izolasyonu', kind: 'constraint', priority: 'critical', origin: 'user_statement', reviewStatus: 'confirmed', sourceRefs: [{ sourceId: 'SRC-001' }], acceptanceCriteria: [] }],
  sources: [{ id: 'SRC-001', title: 'Not', text: 'metin', format: 'text' }],
  decisions: [{ id: 'DEC-001', title: 'Kapsam', choice: '', alternatives: [], rationale: '', consequences: '', origin: 'system_proposal', reviewStatus: 'confirmed' }],
  components: [{ id: 'CMP-001', name: 'API', kind: 'api', responsibility: '', origin: 'system_proposal', reviewStatus: 'confirmed' }, { id: 'CMP-002', name: 'DB', kind: 'database', responsibility: '', origin: 'system_proposal', reviewStatus: 'confirmed' }],
  relationships: [{ id: 'REL-001', sourceId: 'CMP-001', targetId: 'CMP-002', communication: 'data', description: 'veri', origin: 'system_proposal', reviewStatus: 'confirmed' }],
  traceLinks: [
    { id: 'TRC-001', fromId: 'DEC-001', toId: 'REQ-001', kind: 'addresses', origin: 'system_proposal', reviewStatus: 'confirmed' },
    { id: 'TRC-002', fromId: 'CMP-001', toId: 'DEC-001', kind: 'implements', origin: 'system_proposal', reviewStatus: 'confirmed' },
  ],
});

describe('model helpers', () => {
  it('maps Mermaid node ids back to model ids', () => {
    expect(modelIdFromNode('flowchart-CMP_001-12')).toBe('CMP-001');
    expect(modelIdFromNode('entity-ENT_007-3')).toBe('ENT-007');
    expect(modelIdFromNode('nothing')).toBeNull();
  });

  it('labels ids by prefix', () => {
    expect(typeOfId('DEC-004')).toBe('Karar');
    expect(typeOfId('ZZZ-001')).toBe('Nesne');
  });

  it('indexes every object by id', () => {
    const idx = buildIndex(mk());
    expect(idx.get('CMP-002')?.title).toBe('DB');
    expect(idx.get('REL-001')?.title).toBe('CMP-001 → CMP-002');
  });

  it('shows the requirement → decision → implementation chain from either end', () => {
    const m = mk();
    const req = relationsOf(m, 'REQ-001');
    expect(req.find((g) => g.label === 'Bu gereksinime dayanan karar')?.ids).toEqual(['DEC-001']);
    expect(req.find((g) => g.label === 'Kaynak')?.ids).toEqual(['SRC-001']);

    const dec = relationsOf(m, 'DEC-001');
    expect(dec.find((g) => g.label === 'Dayandığı gereksinim')?.ids).toEqual(['REQ-001']);
    expect(dec.find((g) => g.label === 'Bu kararı uygulayan nesne')?.ids).toEqual(['CMP-001']);

    const cmp = relationsOf(m, 'CMP-001');
    expect(cmp.find((g) => g.label === 'Giden bağlantı')?.ids).toEqual(['REL-001']);
    expect(cmp.find((g) => g.label === 'Uyguladığı karar')?.ids).toEqual(['DEC-001']);
  });

  it('provides a text alternative for diagrams', () => {
    expect(textAlternative(mk(), 'component')).toEqual(['API → DB (veri)']);
  });
});
