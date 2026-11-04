import { describe, expect, it } from 'vitest';
import { INTRO_KEY, markIntroSeen, shouldShowIntro } from './intro';

const memory = () => {
  const m = new Map<string, string>();
  return { getItem: (k: string) => m.get(k) ?? null, setItem: (k: string, v: string) => void m.set(k, v) };
};

describe('shouldShowIntro', () => {
  it('shows on the first visit of a session and not again afterwards', () => {
    const s = memory();
    expect(shouldShowIntro('', s)).toBe(true);
    markIntroSeen(s);
    expect(s.getItem(INTRO_KEY)).toBe('1');
    expect(shouldShowIntro('', s)).toBe(false);
  });

  it('can be forced with ?intro, also next to other parameters', () => {
    const s = memory();
    markIntroSeen(s);
    expect(shouldShowIntro('?intro', s)).toBe(true);
    expect(shouldShowIntro('?a=1&intro', s)).toBe(true);
    expect(shouldShowIntro('?intro=1', s)).toBe(true);
    expect(shouldShowIntro('?introduction=1', s)).toBe(false);   // başka bir parametre adı tetiklemez
  });

  it('never breaks the app when storage is unavailable or throws', () => {
    expect(shouldShowIntro('', null)).toBe(true);
    const throwing = { getItem: () => { throw new Error('SecurityError'); }, setItem: () => { throw new Error('SecurityError'); } };
    expect(shouldShowIntro('', throwing)).toBe(true);
    expect(() => markIntroSeen(throwing)).not.toThrow();
    expect(() => markIntroSeen(null)).not.toThrow();
  });
});
