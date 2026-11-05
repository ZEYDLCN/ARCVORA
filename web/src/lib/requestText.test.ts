import { describe, expect, it } from 'vitest';
import { accountingTone, eventText, fallbackLabel, requestTone, taskTone } from './requestText';

const people = { a: 'Ayşe', b: 'Burak' };
const ev = (kind: string, detail?: Record<string, unknown>, actorId?: string) => ({ id: 1, at: '2026-11-05T10:00:00Z', kind, detail, actorId });

describe('eventText', () => {
  it('talep oluşturma ve adım başlangıcını anlatır', () => {
    expect(eventText(ev('submitted', {}, 'a'), people)).toBe('Ayşe talebi oluşturdu.');
    expect(eventText(ev('step_started', { stepName: 'Yönetici onayı', assignees: ['b'] }), people)).toBe('“Yönetici onayı” adımı başladı. Onaylayacak: Burak.');
  });
  it('geri düşüşü nedeniyle birlikte yazar', () => {
    const t = eventText(ev('step_started', { stepName: 'Yönetici onayı', assignees: ['a'], fallback: 'manager_missing' }), people);
    expect(t).toContain('talep edenin yöneticisi tanımlı olmadığı için master admin’e yönlendirildi');
  });
  it('onay/ret yorumunu ekler', () => {
    expect(eventText(ev('approved', { stepName: 'Yönetici onayı', comment: 'uygun' }, 'b'), people)).toBe('Burak “Yönetici onayı” adımını onayladı: uygun');
    expect(eventText(ev('rejected', { stepName: 'X', comment: 'yoğun' }, 'b'), people)).toBe('Burak “X” adımında reddetti: yoğun');
    expect(eventText(ev('approved', { stepName: 'X' }, 'b'), people)).toBe('Burak “X” adımını onayladı.');
  });
  it('atlanan adım, iptal, tamamlanma ve muhasebe olayları', () => {
    expect(eventText(ev('step_skipped', { stepName: 'GM' }), people)).toContain('atlandı');
    expect(eventText(ev('cancelled', {}, 'a'), people)).toBe('Ayşe talebi iptal etti.');
    expect(eventText(ev('completed'), people)).toBe('Talep onaylandı.');
    expect(eventText(ev('accounting_queued'), people)).toBe('Muhasebe kuyruğuna eklendi.');
    expect(eventText(ev('accounting_processed', { note: 'girildi' }, 'b'), people)).toBe('Burak muhasebede işledi: girildi');
  });
  it('bilinmeyen kişi ve olay türünde çökmez', () => {
    expect(eventText(ev('cancelled', {}, 'zzz'), people)).toBe('Bir kullanıcı talebi iptal etti.');
    expect(eventText(ev('yeni_tur'), people)).toBe('yeni_tur');
    expect(eventText(ev('step_started', { stepName: 'X', assignees: ['zzz'] }), people)).toContain('Bilinmeyen');
  });
});

describe('tonlar ve geri düşüş', () => {
  it('durumlar renklere eşlenir', () => {
    expect(requestTone('approved')).toBe('ok');
    expect(requestTone('rejected')).toBe('bad');
    expect(requestTone('in_review')).toBe('info');
    expect(taskTone('pending')).toBe('warn');
    expect(accountingTone('failed')).toBe('bad');
  });
  it('bilinmeyen geri düşüş nedeni olduğu gibi döner', () => {
    expect(fallbackLabel(undefined)).toBeNull();
    expect(fallbackLabel('group_empty')).toBe('grup boş olduğu için');
    expect(fallbackLabel('yeni_neden')).toBe('yeni_neden');
  });
});

import { stepStates } from './requestText';

describe('stepStates', () => {
  const steps = [{ id: 's1' }, { id: 's2' }, { id: 's3' }];
  it('görevlere göre tamam / sırada / gelecek', () => {
    expect(stepStates(steps, [{ stepId: 's1', status: 'approved' }, { stepId: 's2', status: 'pending' }], 'in_review')).toEqual(['done', 'current', 'upcoming']);
  });
  it('ret ve atlanan adımlar', () => {
    expect(stepStates(steps, [{ stepId: 's1', status: 'approved' }, { stepId: 's2', status: 'rejected' }], 'rejected')).toEqual(['done', 'rejected', 'skipped']);
    expect(stepStates(steps, [{ stepId: 's1', status: 'approved' }], 'approved')).toEqual(['done', 'skipped', 'skipped']);
  });
  it('any modunda diğer görev atlanmış olsa da adım tamam sayılır', () => {
    expect(stepStates([{ id: 's1' }], [{ stepId: 's1', status: 'approved' }, { stepId: 's1', status: 'skipped' }], 'approved')).toEqual(['done']);
  });
  it('iptal edilen talepte bekleyen adım atlanmış görünür', () => {
    expect(stepStates(steps, [{ stepId: 's1', status: 'skipped' }], 'cancelled')).toEqual(['skipped', 'skipped', 'skipped']);
  });
});
