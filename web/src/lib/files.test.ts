import { describe, expect, it } from 'vitest';
import { buildPayload, emptyDefinition, formatValue, allowedOps, fieldTypeLabels, type FlowDefinition } from './flow';
import { validateFlow } from './flowValidate';
import { checkPick, formatSize, MAX_FILE_BYTES } from './files';

const def: FlowDefinition = {
  ...emptyDefinition('MSF'),
  fields: [
    { key: 'amount', label: 'Tutar', type: 'money', required: true },
    { key: 'receipt', label: 'Fiş', type: 'file', maxFiles: 2 },
  ],
  steps: [{ id: 's1', name: 'Yönetici', approvers: { type: 'manager' } }],
};

describe('ek alanı (flow)', () => {
  it('türün etiketi vardır', () => expect(fieldTypeLabels.file).toMatch(/Ek/));
  it('koşulda kullanılamaz', () => expect(allowedOps('file')).toEqual([]));

  it('buildPayload yalnızca dosya kimliklerini gönderir ve boş listeyi atar', () => {
    const picked = [{ id: 'a', fileName: 'x.png', size: 10, contentType: 'image/png' }, { id: 'b', fileName: 'y.pdf', size: 20, contentType: 'application/pdf' }];
    expect(buildPayload(def, { amount: '5', receipt: picked })).toEqual({ amount: 5, receipt: ['a', 'b'] });
    expect(buildPayload(def, { amount: '5', receipt: [] })).toEqual({ amount: 5 });
    expect(buildPayload(def, { amount: '5' })).toEqual({ amount: 5 });
  });

  it('buildPayload kimlik dizilerini de kabul eder (ayrıntıdan gelen veri)', () => {
    expect(buildPayload(def, { amount: '5', receipt: ['a'] })).toEqual({ amount: 5, receipt: ['a'] });
  });

  it('formatValue dosya sayısını yazar', () => {
    const f = def.fields[1];
    expect(formatValue(f, ['a', 'b'])).toBe('2 dosya');
    expect(formatValue(f, ['a'])).toBe('1 dosya');
    expect(formatValue(f, [])).toBe('—');
    expect(formatValue(f, undefined)).toBe('—');
  });
});

describe('ek alanı (tasarım doğrulaması)', () => {
  it('geçerli tanım sorunsuzdur', () => expect(validateFlow(def)).toEqual([]));

  it.each([0, -1, 11])('maxFiles %i geçersizdir', (n) => {
    const d = { ...def, fields: [def.fields[0], { ...def.fields[1], maxFiles: n }] };
    expect(validateFlow(d).some((i) => i.code === 'invalid_range' && i.path === 'fields[1].maxFiles')).toBe(true);
  });

  it('maxFiles yalnızca ek alanında verilebilir', () => {
    const d = { ...def, fields: [{ ...def.fields[0], maxFiles: 2 }, def.fields[1]] };
    expect(validateFlow(d).some((i) => i.path === 'fields[0].maxFiles')).toBe(true);
  });

  it('ek alanı koşulda kullanılamaz', () => {
    const d: FlowDefinition = { ...def, steps: [{ id: 's1', name: 'x', approvers: { type: 'manager' }, conditions: [{ field: 'receipt', op: 'eq', value: 'a' }] }] };
    expect(validateFlow(d).some((i) => i.code === 'invalid_condition')).toBe(true);
  });
});

describe('dosya seçimi kontrolleri', () => {
  const file = (name: string, size: number, type: string) => ({ name, size, type }) as File;

  it('desteklenen türler ve sınırdaki boyut kabul edilir', () => {
    expect(checkPick(file('a.png', 100, 'image/png'))).toBeNull();
    expect(checkPick(file('a.pdf', MAX_FILE_BYTES, 'application/pdf'))).toBeNull();
    expect(checkPick(file('a.JPG', 100, ''))).toBeNull();           // bazı tarayıcılar türü boş bırakır: uzantıya bakılır
    expect(checkPick(file('a.webp', 100, 'image/webp'))).toBeNull();
  });

  it('büyük, boş ve desteklenmeyen dosyalar nedeniyle reddedilir', () => {
    expect(checkPick(file('a.pdf', MAX_FILE_BYTES + 1, 'application/pdf'))).toMatch(/10 MB/);
    expect(checkPick(file('a.pdf', 0, 'application/pdf'))).toMatch(/boş/);
    expect(checkPick(file('a.exe', 100, 'application/x-msdownload'))).toMatch(/PDF|PNG/);
    expect(checkPick(file('a.svg', 100, 'image/svg+xml'))).toMatch(/PDF|PNG/);
    expect(checkPick(file('a.html', 100, 'text/html'))).toMatch(/PDF|PNG/);
  });

  it('boyut okunur yazılır', () => {
    expect(formatSize(0)).toBe('0 B');
    expect(formatSize(512)).toBe('512 B');
    expect(formatSize(1536)).toBe('1,5 KB');
    expect(formatSize(5 * 1024 * 1024)).toBe('5 MB');
  });
});
