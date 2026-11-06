import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it, vi } from 'vitest';
import { isStandalone, registerServiceWorker } from './pwa';

const pub = (p: string) => resolve(__dirname, '../../public', p);

describe('service worker kaydı', () => {
  it('üretimde /sw.js adresini kök kapsamla kaydeder', async () => {
    const register = vi.fn().mockResolvedValue({});
    expect(await registerServiceWorker({ serviceWorker: { register } }, true)).toBe(true);
    expect(register).toHaveBeenCalledWith('/sw.js', { scope: '/' });
  });

  it('geliştirmede kaydetmez', async () => {
    const register = vi.fn();
    expect(await registerServiceWorker({ serviceWorker: { register } }, false)).toBe(false);
    expect(register).not.toHaveBeenCalled();
  });

  it('tarayıcı desteklemiyorsa sessizce vazgeçer', async () => {
    expect(await registerServiceWorker({}, true)).toBe(false);
    expect(await registerServiceWorker(undefined, true)).toBe(false);
  });

  it('kayıt reddedilirse hata fırlatmaz', async () => {
    const register = vi.fn().mockRejectedValue(new Error('SecurityError'));
    await expect(registerServiceWorker({ serviceWorker: { register } }, true)).resolves.toBe(false);
  });
});

describe('bağımsız pencere algısı', () => {
  it('display-mode: standalone ise true', () => {
    expect(isStandalone({ matchMedia: () => ({ matches: true }) })).toBe(true);
  });
  it('iOS navigator.standalone ise true', () => {
    expect(isStandalone({ matchMedia: () => ({ matches: false }), navigator: { standalone: true } })).toBe(true);
  });
  it('normal sekmede false; matchMedia yoksa da çökmez', () => {
    expect(isStandalone({ matchMedia: () => ({ matches: false }) })).toBe(false);
    expect(isStandalone({})).toBe(false);
  });
});

describe('web uygulama manifesti', () => {
  const m = JSON.parse(readFileSync(pub('manifest.webmanifest'), 'utf8')) as {
    name: string; short_name: string; start_url: string; scope: string; display: string; lang: string; theme_color: string; background_color: string;
    icons: { src: string; sizes: string; type: string; purpose: string }[]; shortcuts: { url: string; name: string }[];
  };

  it('kurulabilir olmanın gerektirdiği alanlar var', () => {
    expect(m.name).toBeTruthy();
    expect(m.short_name.length).toBeLessThanOrEqual(12);
    expect(m.display).toBe('standalone');
    expect(m.lang).toBe('tr');
    expect(m.start_url.startsWith(m.scope)).toBe(true);
    expect(m.theme_color).toMatch(/^#[0-9a-f]{6}$/i);
    expect(m.background_color).toMatch(/^#[0-9a-f]{6}$/i);
  });

  it('192 ve 512 piksellik "any" simgeleri ile bir "maskable" simge vardır', () => {
    const has = (sizes: string, purpose: string) => m.icons.some((i) => i.sizes === sizes && i.purpose === purpose);
    expect(has('192x192', 'any')).toBe(true);
    expect(has('512x512', 'any')).toBe(true);
    expect(has('512x512', 'maskable')).toBe(true);
  });

  it('her simge dosyası vardır ve gerçek boyutu bildirilen boyutla aynıdır (PNG başlığı)', () => {
    for (const i of m.icons) {
      const file = pub(i.src.replace(/^\//, ''));
      expect(existsSync(file), i.src).toBe(true);
      const buf = readFileSync(file);
      expect(buf.subarray(1, 4).toString('latin1')).toBe('PNG');
      const w = buf.readUInt32BE(16), h = buf.readUInt32BE(20);
      expect(`${w}x${h}`, i.src).toBe(i.sizes);
    }
    expect(existsSync(pub('icons/apple-touch-icon.png'))).toBe(true);
  });

  it('kısayollar kapsam içinde hash yönlendirmesi kullanır ve gerçek yollara denk gelir', () => {
    for (const s of m.shortcuts) expect(s.url).toMatch(/^\/#\/(inbox|requests)$/);
  });
});

describe('index.html bağlantıları', () => {
  const html = readFileSync(resolve(__dirname, '../../index.html'), 'utf8');
  it('manifest, tema rengi ve iOS simgesi bağlıdır', () => {
    expect(html).toContain('<link rel="manifest" href="/manifest.webmanifest"');
    expect(html).toMatch(/<meta name="theme-color" content="#[0-9a-f]{6}"/i);
    expect(html).toContain('rel="apple-touch-icon" href="/icons/apple-touch-icon.png"');
    expect(html).toContain('viewport-fit=cover');
  });
});
