import { describe, expect, it } from 'vitest';
import { generatePassword, roleLabels, roleOptions } from './roles';

describe('generatePassword', () => {
  it('istenen uzunlukta ve karışan karakterler olmadan üretir', () => {
    for (let i = 0; i < 50; i++) {
      const p = generatePassword();
      expect(p).toHaveLength(14);
      expect(p).toMatch(/^[A-HJ-NP-Za-km-z2-9]+$/);
    }
  });
  it('sunucunun alt sınırını (10) sağlar ve her çağrıda farklıdır', () => {
    expect(generatePassword(10)).toHaveLength(10);
    expect(new Set(Array.from({ length: 20 }, () => generatePassword())).size).toBe(20);
  });
  it('rastgele kaynağı kullanır', () => {
    expect(generatePassword(4, () => new Uint32Array([0, 1, 2, 3]))).toBe('ABCD');
  });
});

describe('roller', () => {
  it('her seçeneğin Türkçe etiketi var', () => {
    for (const r of roleOptions) expect(roleLabels[r]).toBeTruthy();
  });
});
