import { describe, expect, it } from 'vitest';
import { wrapLabel } from './label';

describe('wrapLabel', () => {
  it('keeps a short name on one line, untouched', () => {
    expect(wrapLabel('Masraf API', 14)).toEqual(['Masraf API']);
    expect(wrapLabel('Web uygulaması', 14)).toEqual(['Web uygulaması']); // tam 14 karakter
  });

  it('breaks at a space instead of cutting a word', () => {
    expect(wrapLabel('Onay ve geçmiş modülü', 14)).toEqual(['Onay ve geçmiş', 'modülü']);
    expect(wrapLabel('Kimlik ve kiracı modülü', 14)).toEqual(['Kimlik ve', 'kiracı modülü']);
  });

  it('wraps a single long word onto the second line when the first word fits', () => {
    expect(wrapLabel('Entegrasyon işleyicisi', 14)).toEqual(['Entegrasyon', 'işleyicisi']);
    expect(wrapLabel('Uygulama veritabanı', 14)).toEqual(['Uygulama', 'veritabanı']);
  });

  it('never exceeds the line budget and marks overflow with an ellipsis on the LAST line', () => {
    const lines = wrapLabel('Çok uzun bir bileşen adı burada devam ediyor', 14);
    expect(lines).toHaveLength(2);
    expect(lines.every((l) => l.length <= 14)).toBe(true);
    expect(lines[1].endsWith('…')).toBe(true);
    expect(lines[0].endsWith('…')).toBe(false);
  });

  it('hard-cuts a single word that is longer than the line, with an ellipsis', () => {
    const lines = wrapLabel('Süperuzunbirkelimeburada', 10);
    expect(lines[0].length).toBeLessThanOrEqual(10);
    expect(lines.at(-1)!.endsWith('…')).toBe(true);
    expect(lines.length).toBeLessThanOrEqual(2);
  });

  it('honours a tighter budget (two logos leave less room)', () => {
    expect(wrapLabel('Web uygulaması', 11)).toEqual(['Web', 'uygulaması']);
  });

  it('supports a single-line mode and collapses repeated whitespace', () => {
    expect(wrapLabel('Onay   modülü', 20)).toEqual(['Onay modülü']);
    const one = wrapLabel('Onay ve geçmiş modülü', 14, 1);
    expect(one).toHaveLength(1);
    expect(one[0].endsWith('…')).toBe(true);
    expect(one[0].length).toBeLessThanOrEqual(14);
  });

  it('returns a single empty line for an empty or blank name', () => {
    expect(wrapLabel('', 14)).toEqual(['']);
    expect(wrapLabel('   ', 14)).toEqual(['']);
  });
});
