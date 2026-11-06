import { describe, expect, it } from 'vitest';
import { emptyDefinition, type FlowDefinition } from './flow';
import { issuesByPath, validateFlow } from './flowValidate';
import { flowTemplates } from './templates';

const base = (): FlowDefinition => structuredClone(flowTemplates[0].definition);
const codes = (d: FlowDefinition) => validateFlow(d).map((i) => i.code);

describe('hazır şablonlar', () => {
  for (const t of flowTemplates) {
    it(`${t.name} şablonu sunucu kurallarına göre geçerlidir`, () => {
      expect(validateFlow(t.definition)).toEqual([]);
    });
  }
  it('önekler ve kategoriler benzersizdir', () => {
    expect(new Set(flowTemplates.map((t) => t.definition.prefix)).size).toBe(flowTemplates.length);
    expect(new Set(flowTemplates.map((t) => t.id)).size).toBe(flowTemplates.length);
  });
  it('muhasebesi açık şablonların kategorisi vardır; izin muhasebeye gitmez', () => {
    expect(flowTemplates.find((t) => t.id === 'leave')!.definition.completion?.accounting?.enabled).toBe(false);
    for (const t of flowTemplates.filter((x) => x.definition.completion?.accounting?.enabled)) expect(t.definition.completion!.accounting!.category).toBeTruthy();
  });
});

describe('validateFlow (sunucu doğrulayıcısının aynası)', () => {
  it('boş taslak eksikleri söyler', () => {
    const c = codes(emptyDefinition());
    expect(c).toContain('fields_required');
    expect(c).toContain('steps_required');
  });
  it('anahtar ve kimlik desenleri', () => {
    const d = base();
    d.fields[0].key = 'Bad Key';
    expect(codes(d)).toContain('invalid_key');
    const e = base();
    e.fields.push({ ...e.fields[0] });
    expect(codes(e)).toContain('duplicate_key');
    const f = base();
    f.steps[0].id = 'Bad Id';
    expect(codes(f)).toContain('invalid_key');
    const g = base();
    g.steps.push({ ...g.steps[0] });
    expect(codes(g)).toContain('duplicate_step_id');
  });
  it('seçenekler ve aralıklar', () => {
    const d = base();
    d.fields.push({ key: 'sec', label: 'S', type: 'select' });
    expect(codes(d)).toContain('options_required');
    d.fields[d.fields.length - 1].options = ['A', 'A'];
    expect(codes(d)).toContain('duplicate_option');
    const e = base();
    e.fields.push({ key: 'm', label: 'M', type: 'money', min: 5, max: 1 }, { key: 't', label: 'T', type: 'text', maxLength: 0 });
    expect(codes(e).filter((c) => c === 'invalid_range')).toHaveLength(2);
  });
  it('notBefore ve hesaplanan alan kuralları', () => {
    const d = base();
    d.fields.find((f) => f.key === 'end')!.notBefore = 'end';
    expect(codes(d)).toContain('invalid_reference');
    const e = base();
    e.fields.find((f) => f.key === 'days')!.computed!.from = 'kind';
    expect(codes(e)).toContain('invalid_computed');
    const f = base();
    f.fields.find((x) => x.key === 'days')!.required = true;
    expect(codes(f)).toContain('computed_required');
  });
  it('onaylayıcı kimliği', () => {
    const d = base();
    d.steps[0].approvers = { type: 'group' };
    expect(codes(d)).toContain('approver_id_required');
    d.steps[0].approvers = { type: 'manager', id: 'x' };
    expect(codes(d)).toContain('approver_id_unexpected');
    d.steps[0].approvers = { type: 'admin' };
    expect(validateFlow(d)).toEqual([]);
  });
  it('koşul alanı, işleci ve değeri', () => {
    const mk = (c: FlowDefinition['steps'][0]['conditions']) => { const d = base(); d.steps[1].conditions = c; return codes(d); };
    expect(mk([{ field: 'yok', op: 'eq', value: 1 }])).toContain('unknown_field');
    expect(mk([{ field: 'kind', op: 'gt', value: 'A' }])).toContain('invalid_condition');
    expect(mk([{ field: 'days', op: 'gt', value: 'on' }])).toContain('condition_value');
    expect(mk([{ field: 'kind', op: 'in', value: 'Yıllık izin' }])).toContain('condition_value');
    expect(mk([{ field: 'days', op: 'eq' }])).toContain('condition_value');
    expect(mk([{ field: 'note', op: 'eq', value: 'x' }])).toContain('invalid_condition');
    expect(mk([{ field: 'kind', op: 'in', value: ['Yıllık izin', 'Rapor'] }])).toEqual([]);
    expect(mk([{ field: 'start', op: 'gte', value: '2026-01-01' }])).toEqual([]);
    expect(mk([{ field: 'start', op: 'gte', value: '01.01.2026' }])).toContain('condition_value');
  });
  it('özet alanı, önek ve muhasebe kategorisi', () => {
    const d = base();
    d.summaryFields = ['yok'];
    expect(codes(d)).toContain('unknown_field');
    d.summaryFields = ['start', 'end', 'kind', 'proxy'];
    expect(codes(d)).toContain('too_many_summary_fields');
    const p = base();
    for (const bad of ['', 'izn', 'TOOLONGPREFIX', 'A-B']) { p.prefix = bad; expect(codes(p)).toContain('invalid_prefix'); }
    const a = base();
    a.completion = { accounting: { enabled: true } };
    expect(codes(a)).toContain('category_required');
  });
  it('adım sayısı sınırı', () => {
    const d = base();
    d.steps = Array.from({ length: 11 }, (_, i) => ({ ...base().steps[0], id: `s${i}` }));
    expect(codes(d)).toContain('too_many_steps');
  });
});

describe('issuesByPath', () => {
  it('aynı yola düşen bulguları birleştirir', () => {
    const m = issuesByPath([{ code: 'a', path: 'fields[0].key', message: 'x' }, { code: 'b', path: 'fields[0].key', message: 'y' }]);
    expect(m.get('fields[0].key')).toEqual(['x', 'y']);
  });
});

describe('listeye bağlı seçim alanı', () => {
  const base = { schema: 1, prefix: 'LST', summaryFields: [], steps: [{ id: 's1', name: 'Y', approvers: { type: 'manager' as const } }] };
  const L = '33333333-3333-3333-3333-333333333333';

  it('seçeneksiz liste bağlantısı geçerlidir', () => {
    expect(validateFlow({ ...base, fields: [{ key: 'd', label: 'Birim', type: 'select', listId: L }] })).toEqual([]);
    expect(validateFlow({ ...base, fields: [{ key: 'd', label: 'Birim', type: 'multi_select', listId: L }] })).toEqual([]);
  });
  it('seçenek ve liste birlikte kullanılamaz; ikisi de yoksa seçenek zorunludur', () => {
    expect(validateFlow({ ...base, fields: [{ key: 'd', label: 'Birim', type: 'select', listId: L, options: ['A'] }] }).map((i) => i.code)).toContain('options_conflict');
    expect(validateFlow({ ...base, fields: [{ key: 'd', label: 'Birim', type: 'select' }] }).map((i) => i.code)).toContain('options_required');
  });
  it('yalnızca seçim alanları listeye bağlanabilir', () => {
    expect(validateFlow({ ...base, fields: [{ key: 'd', label: 'Not', type: 'text', listId: L }] }).map((i) => i.code)).toContain('invalid_list');
  });
});

describe('izin bakiyesi tanımı', () => {
  const base = {
    schema: 1, prefix: 'IZN', summaryFields: [], steps: [{ id: 's1', name: 'Y', approvers: { type: 'manager' as const } }],
    fields: [
      { key: 'start', label: 'Başlangıç', type: 'date' as const }, { key: 'end', label: 'Bitiş', type: 'date' as const },
      { key: 'days', label: 'Gün', type: 'number' as const, computed: { op: 'work_days' as const, from: 'start', to: 'end' } },
      { key: 'kind', label: 'Tür', type: 'select' as const, options: ['Yıllık', 'Mazeret'] },
    ],
  };
  const codes = (leave: object) => validateFlow({ ...base, completion: { leave } } as never).map((i) => `${i.code}:${i.path}`);

  it('geçerli tanım sorunsuzdur; kapalıyken yok sayılır', () => {
    expect(codes({ enabled: true, daysField: 'days', startField: 'start', typeField: 'kind', countsFor: ['Yıllık'] })).toEqual([]);
    expect(codes({ enabled: false, daysField: 'yok' })).toEqual([]);
  });
  it('alan türleri ve tür/değer uyumu denetlenir', () => {
    expect(codes({ enabled: true, daysField: 'start', startField: 'start' })).toContain('invalid_leave:completion.leave.daysField');
    expect(codes({ enabled: true, daysField: 'days', startField: 'days' })).toContain('invalid_leave:completion.leave.startField');
    expect(codes({ enabled: true, daysField: 'days', startField: 'start', typeField: 'start', countsFor: ['x'] })).toContain('invalid_leave:completion.leave.typeField');
    expect(codes({ enabled: true, daysField: 'days', startField: 'start', typeField: 'kind', countsFor: [] })).toContain('invalid_leave:completion.leave.countsFor');
    expect(codes({ enabled: true, daysField: 'days', startField: 'start', typeField: 'kind', countsFor: ['Rapor'] })).toContain('invalid_leave:completion.leave.countsFor');
    expect(codes({ enabled: true, daysField: 'days', startField: 'start', countsFor: ['Yıllık'] })).toContain('invalid_leave:completion.leave.countsFor');
  });
});
