import { describe, expect, it } from 'vitest';
import { allowedOps, buildPayload, computeDays, emptyDefinition, formatValue, issuesByField, nextStepId, slugKey, withComputed, type FlowDefinition } from './flow';

const def: FlowDefinition = {
  ...emptyDefinition('IZN'),
  fields: [
    { key: 'start', label: 'Başlangıç', type: 'date', required: true },
    { key: 'end', label: 'Bitiş', type: 'date', required: true, notBefore: 'start' },
    { key: 'days', label: 'Gün', type: 'number', computed: { op: 'calendar_days', from: 'start', to: 'end' } },
    { key: 'amount', label: 'Tutar', type: 'money', currency: 'TRY' },
    { key: 'tags', label: 'Etiket', type: 'multi_select', options: ['a', 'b'] },
    { key: 'ok', label: 'Onay', type: 'checkbox' },
    { key: 'note', label: 'Not', type: 'long_text' },
  ],
};

describe('slugKey', () => {
  it('Türkçe etiketten sunucu desenine uygun anahtar üretir', () => {
    expect(slugKey('Başlangıç tarihi')).toBe('baslangic_tarihi');
    expect(slugKey('İzin Türü')).toBe('izin_turu');
    expect(slugKey('ÇOK ŞIK Öğe')).toBe('cok_sik_oge');
    expect(slugKey('  --Tutar (TL)--  ')).toBe('tutar_tl');
  });
  it('rakamla başlayanı ve boşu düzeltir, 40 karakterle sınırlar', () => {
    expect(slugKey('1. gün')).toBe('alan_1_gun');
    expect(slugKey('???')).toBe('alan');
    expect(slugKey('x'.repeat(80)).length).toBe(40);
    expect(/^[a-z][a-z0-9_]{0,39}$/.test(slugKey('Ağır İş Yükü 2026'))).toBe(true);
  });
  it('çakışmada _2, _3 ekler ve uzunluğu korur', () => {
    expect(slugKey('Tutar', ['tutar'])).toBe('tutar_2');
    expect(slugKey('Tutar', ['tutar', 'tutar_2'])).toBe('tutar_3');
    const long = 'y'.repeat(40);
    const k = slugKey(long, [long]);
    expect(k.length).toBeLessThanOrEqual(40);
    expect(k.endsWith('_2')).toBe(true);
  });
});

describe('nextStepId', () => {
  it('boş ve dolu listelerde benzersiz kimlik verir', () => {
    expect(nextStepId([])).toBe('s1');
    expect(nextStepId([{ id: 's1' }, { id: 's2' }])).toBe('s3');
    expect(nextStepId([{ id: 's2' }])).toBe('s3');      // dolu kimlik atlanır
    expect(nextStepId([{ id: 's2' }, { id: 's3' }])).toBe('s4');
  });
});

describe('computeDays', () => {
  it('takvim günü uçlar dahil sayılır', () => {
    expect(computeDays('calendar_days', '2026-11-10', '2026-11-14')).toBe(5);
    expect(computeDays('calendar_days', '2026-11-10', '2026-11-10')).toBe(1);
  });
  it('hafta içi günleri sayar', () => {
    expect(computeDays('weekdays', '2026-11-13', '2026-11-17')).toBe(3);   // Cuma, Pazartesi, Salı
    expect(computeDays('weekdays', '2026-11-14', '2026-11-15')).toBe(0);   // hafta sonu
  });
  it('geçersiz ya da ters aralıkta null', () => {
    expect(computeDays('calendar_days', '2026-11-12', '2026-11-10')).toBeNull();
    expect(computeDays('calendar_days', '2026-13-01', '2026-11-10')).toBeNull();
    expect(computeDays('calendar_days', undefined, '2026-11-10')).toBeNull();
    expect(computeDays('calendar_days', '10.11.2026', '2026-11-12')).toBeNull();
    expect(computeDays('weekdays', '2000-01-01', '2026-01-01')).toBeNull();
  });
  it('yıl sınırını doğru aşar (artık yıl)', () => {
    expect(computeDays('calendar_days', '2028-02-28', '2028-03-01')).toBe(3);
  });
});

describe('withComputed', () => {
  it('hesaplanan alanı ekler, aralık geçersizse kaldırır', () => {
    expect(withComputed(def, { start: '2026-11-10', end: '2026-11-12' }).days).toBe(3);
    expect('days' in withComputed(def, { start: '2026-11-12', end: '2026-11-10', days: 9 })).toBe(false);
  });
});

describe('buildPayload', () => {
  it('boşları atar, metni kırpar, sayıyı çevirir, hesaplananı göndermez', () => {
    const p = buildPayload(def, { start: '2026-11-10', end: '2026-11-12', days: 99, amount: '1250,5', tags: [], ok: false, note: '  merhaba ' });
    expect(p).toEqual({ start: '2026-11-10', end: '2026-11-12', amount: 1250.5, ok: false, note: 'merhaba' });
  });
  it('sayı olmayan girdiyi sunucunun açıklaması için olduğu gibi bırakır', () => {
    expect(buildPayload(def, { amount: 'abc' }).amount).toBe('abc');
    expect(buildPayload(def, { amount: '   ' })).toEqual({});
  });
  it('çoklu seçimi dolu ise gönderir', () => {
    expect(buildPayload(def, { tags: ['a'] }).tags).toEqual(['a']);
  });
});

describe('issuesByField', () => {
  it('data.<anahtar> bulgularını alana, diğerlerini _ altına koyar', () => {
    const r = issuesByField([
      { path: 'data.start', message: 'zorunlu' }, { path: 'data.start', message: 'geçersiz' }, { path: 'steps[0].name', message: 'x' },
    ]);
    expect(r.start).toEqual(['zorunlu', 'geçersiz']);
    expect(r._).toEqual(['x']);
  });
});

describe('formatValue', () => {
  const f = (k: string) => def.fields.find((x) => x.key === k)!;
  it('tarih, tutar, onay ve liste biçimleri', () => {
    expect(formatValue(f('start'), '2026-11-10')).toBe('10.11.2026');
    expect(formatValue(f('amount'), 1250.5)).toBe('1.250,50 TRY');
    expect(formatValue(f('ok'), true)).toBe('Evet');
    expect(formatValue(f('ok'), false)).toBe('Hayır');
    expect(formatValue(f('tags'), ['a', 'b'])).toBe('a, b');
    expect(formatValue(f('note'), undefined)).toBe('—');
  });
  it('kişi alanı adı gösterir, bilinmeyeni belirtir', () => {
    const u = { key: 'who', label: 'Kim', type: 'user' as const };
    expect(formatValue(u, 'id1', { id1: 'Ayşe' })).toBe('Ayşe');
    expect(formatValue(u, 'id2', {})).toBe('(bilinmeyen kullanıcı)');
  });
  it('HTML içeren metni olduğu gibi (kaçışsız) döndürür; kaçışı React yapar', () => {
    expect(formatValue(f('note'), '<script>alert(1)</script>')).toBe('<script>alert(1)</script>');
  });
});

describe('allowedOps', () => {
  it('sunucudaki alan türü kurallarıyla aynı', () => {
    expect(allowedOps('number')).toContain('gt');
    expect(allowedOps('select')).toEqual(['eq', 'ne', 'in']);
    expect(allowedOps('checkbox')).toEqual(['eq', 'ne']);
    expect(allowedOps('multi_select')).toEqual(['in']);
    expect(allowedOps('long_text')).toEqual([]);
  });
});
