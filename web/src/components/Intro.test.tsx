// @vitest-environment jsdom
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Intro } from './Intro';
import { INTRO_KEY } from '../lib/intro';

// jsdom'da AnimationEvent yoktur; React bu durumda olayları 'webkitAnimationEnd' adıyla dinler. Gerçek tarayıcıların davranışını
// yansıtmak için React yüklenmeden önce tanımlanır.
vi.hoisted(() => {
  const g = globalThis as { AnimationEvent?: unknown; Event: typeof Event };
  if (!g.AnimationEvent) g.AnimationEvent = class AnimationEvent extends g.Event {};
});

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;

const mount = (onDone: () => void) => { act(() => { root.render(<Intro onDone={onDone} />); }); return host.querySelector('.intro') as HTMLElement; };
/** jsdom CSS animasyonu çalıştırmaz; tarayıcının göndereceği olayı taklit eder. */
const animation = (el: Element, type: 'animationstart' | 'animationend', name: string) =>
  act(() => { const e = new Event(type, { bubbles: true }); Object.defineProperty(e, 'animationName', { value: name }); el.dispatchEvent(e); });

beforeEach(() => {
  vi.useFakeTimers();
  sessionStorage.clear();
  document.documentElement.className = 'intro-boot';
  delete document.documentElement.dataset.intro;
  host = document.createElement('div');
  document.body.appendChild(host);
  root = createRoot(host);
});
afterEach(() => { act(() => root.unmount()); host.remove(); vi.useRealTimers(); });

describe('Intro', () => {
  it('is purely decorative, covers the screen without taking focus, and remembers it was shown', () => {
    const el = mount(() => {});
    expect(el.getAttribute('aria-hidden')).toBe('true');
    expect(el.querySelector('button, a, input, [tabindex]')).toBeNull();
    expect(sessionStorage.getItem(INTRO_KEY)).toBe('1');
    expect(document.documentElement.classList.contains('intro-boot')).toBe(false);   // boot zemini kaldırıldı
    expect(el.textContent).toContain('Arcvora');
  });

  it('hands over to the app exactly once, driven by the exit animation end', () => {
    const done = vi.fn();
    const el = mount(done);
    animation(el, 'animationend', 'intro-rot');             // iç animasyonların bitişi devri tetiklemez
    expect(done).not.toHaveBeenCalled();
    animation(el, 'animationstart', 'intro-exit');
    expect(el.getAttribute('data-phase')).toBe('leave');
    expect(document.documentElement.dataset.intro).toBe('leaving');
    animation(el, 'animationend', 'intro-exit');
    animation(el, 'animationend', 'intro-exit');
    expect(done).toHaveBeenCalledTimes(1);
  });

  it('can be skipped by click or any key without breaking the hand-over', () => {
    const done = vi.fn();
    const el = mount(done);
    act(() => { el.dispatchEvent(new Event('pointerdown', { bubbles: true })); });
    expect(el.getAttribute('data-phase')).toBe('skip');
    expect(document.documentElement.dataset.intro).toBe('leaving');
    animation(el, 'animationend', 'intro-skip');       // atlama AYRI keyframe adı kullanır (yerinde güncelleme anında bitirirdi)
    expect(done).toHaveBeenCalledTimes(1);

    const done2 = vi.fn();
    act(() => root.unmount());
    root = createRoot(host);
    const el2 = mount(done2);
    act(() => { window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })); });
    expect(el2.getAttribute('data-phase')).toBe('skip');
  });

  it('ignores a skip once the exit has begun, so the fade never restarts and flickers', () => {
    const el = mount(() => {});
    animation(el, 'animationstart', 'intro-exit');
    act(() => { el.dispatchEvent(new Event('pointerdown', { bubbles: true })); });
    expect(el.getAttribute('data-phase')).toBe('leave');
  });

  it('still closes through the safety timeout if the animation event never arrives', () => {
    const done = vi.fn();
    mount(done);
    act(() => { vi.advanceTimersByTime(4600); });
    expect(done).toHaveBeenCalledTimes(1);
  });

  it('keeps the leaving marker when the parent re-renders with a new callback during the exit', () => {
    const el = mount(() => {});
    animation(el, 'animationstart', 'intro-exit');
    act(() => { root.render(<Intro onDone={() => {}} />); });           // örn. /api/me yanıtı gelince yeniden render
    expect(document.documentElement.dataset.intro).toBe('leaving');
  });

  it('cleans up the global marker when it unmounts', () => {
    const el = mount(() => {});
    animation(el, 'animationstart', 'intro-exit');
    act(() => root.unmount());
    expect(document.documentElement.dataset.intro).toBeUndefined();
    root = createRoot(host);
  });
});

describe('intro.css contract', () => {
  const css = readFileSync(resolve(process.cwd(), 'src/intro.css'), 'utf8');   // testler web/ dizininden çalışır
  const keyframes = [...css.matchAll(/@keyframes\s+([\w-]+)\s*\{((?:[^{}]|\{[^{}]*\})*)\}/g)];

  it('animates only compositor-friendly properties (opacity / transform / translate)', () => {
    expect(keyframes.length).toBeGreaterThan(8);
    const allowed = new Set(['opacity', 'transform', 'translate']);
    for (const [, name, body] of keyframes) {
      const props = [...body.matchAll(/([\w-]+)\s*:/g)].map((m) => m[1]);
      for (const p of props) expect(allowed.has(p), `@keyframes ${name} '${p}' canlandırıyor`).toBe(true);
    }
  });

  it('rotates exactly one full turn around the vertical axis with the refined easing', () => {
    const rot = keyframes.find(([, n]) => n === 'intro-rot')![2];
    expect(rot).toMatch(/rotateY\(0deg\)/);
    expect(rot).toMatch(/rotateY\(360deg\)/);
    expect(css).toMatch(/--ease-rot:\s*cubic-bezier\(0\.65,\s*0,\s*0\.35,\s*1\)/);
    expect(css).toMatch(/\.intro-rot\s*\{[^}]*animation:\s*intro-rot 800ms var\(--ease-rot\) 500ms both/);
  });

  it('runs the phases in order and finishes in about 2.5 seconds', () => {
    const delay = (re: RegExp) => Number(re.exec(css)?.[1]);
    const fadeIn = delay(/\.intro-in\s*\{[^}]*animation:\s*intro-in (\d+)ms[^;]* 0ms both/);
    const rotateStart = delay(/animation:\s*intro-rot \d+ms var\(--ease-rot\) (\d+)ms both/);
    const glowStart = delay(/animation:\s*intro-glow-l \d+ms var\(--ease-out\) (\d+)ms both/);
    const wordStart = delay(/animation:\s*intro-rise \d+ms var\(--ease-out\) (\d+)ms both/);
    const exit = /animation:\s*intro-exit (\d+)ms cubic-bezier\([^)]*\) (\d+)ms both/.exec(css)!;
    expect(rotateStart).toBeGreaterThanOrEqual(fadeIn);                 // Faz 1 → Faz 2
    expect(glowStart).toBeGreaterThanOrEqual(rotateStart + 800);        // Faz 2 → Faz 3 (dönüş bittikten sonra)
    expect(wordStart).toBeGreaterThan(glowStart);                       // Faz 4
    const total = Number(exit[1]) + Number(exit[2]);
    expect(total).toBeGreaterThanOrEqual(2000);
    expect(total).toBeLessThanOrEqual(2600);
  });

  it('has a motion-free variant for prefers-reduced-motion', () => {
    const block = css.slice(css.indexOf('@media (prefers-reduced-motion: reduce)'));
    expect(block).toMatch(/\.intro-rot\s*\{\s*animation:\s*none/);
    expect(block).toMatch(/intro-skip/);
    expect(block).toMatch(/\.intro-sheen-mask/);
    expect(block).not.toMatch(/rotateY/);
  });

  it('uses a separate keyframes name for skipping (same-name redefinition would end the running animation instantly)', () => {
    expect(css).toMatch(/\.intro\[data-phase='skip'\]\s*\{\s*animation:\s*intro-skip/);
    expect(css).toMatch(/@keyframes intro-skip/);
  });

  it('does not hide the covered app (measured: no fps gain, and it adds a costly first paint at reveal)', () => {
    expect(css).not.toMatch(/visibility:\s*hidden/);
  });

  it('uses the brand palette', () => {
    for (const hex of ['#153d46', '#091a20', '#a98af2', '#72bfc0', '#d4e7d2']) expect(css.toLowerCase()).toContain(hex);
  });
});
