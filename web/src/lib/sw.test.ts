import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { beforeEach, describe, expect, it, vi } from 'vitest';

// public/sw.js düz bir betiktir; sahte `self`/`caches`/`fetch` ile çalıştırılıp davranışı doğrulanır.
const source = readFileSync(resolve(__dirname, '../../public/sw.js'), 'utf8');
const ORIGIN = 'https://apps.example.com';

type Listener = (event: unknown) => void;
class FakeCache {
  store = new Map<string, Response>();
  async add(url: string) { const r = await fakeNet(new Request(ORIGIN + url)); if (!r.ok) throw new Error('bad'); this.store.set(ORIGIN + url, r); }
  async put(key: string | Request, r: Response) { this.store.set(typeof key === 'string' ? ORIGIN + key : key.url, r); }
}
let caches: { stores: Map<string, FakeCache>; open: (n: string) => Promise<FakeCache>; keys: () => Promise<string[]>; delete: (n: string) => Promise<boolean>; match: (k: string | Request) => Promise<Response | undefined> };
let net: (req: Request) => Promise<Response>;
const fakeNet = (req: Request) => net(req);
let listeners: Record<string, Listener>;
const skipWaiting = vi.fn(async () => {});
const claim = vi.fn(async () => {});

function boot() {
  listeners = {};
  const stores = new Map<string, FakeCache>();
  caches = {
    stores,
    open: async (n) => { if (!stores.has(n)) stores.set(n, new FakeCache()); return stores.get(n)!; },
    keys: async () => [...stores.keys()],
    delete: async (n) => stores.delete(n),
    match: async (k) => {
      const key = typeof k === 'string' ? ORIGIN + k : k.url;
      for (const c of stores.values()) { const hit = c.store.get(key); if (hit) return hit.clone(); }
      return undefined;
    },
  };
  const self = {
    location: { origin: ORIGIN },
    addEventListener: (t: string, l: Listener) => { listeners[t] = l; },
    skipWaiting, clients: { claim },
  };
  new Function('self', 'caches', 'fetch', 'Response', 'Request', 'URL', source)(self, caches, fakeNet, Response, Request, URL);
}

/** Bir fetch olayı üretir; service worker respondWith çağırdıysa yanıtı, çağırmadıysa null (tarayıcı kendisi halleder) döner. */
async function fire(url: string, init: { method?: string; mode?: RequestMode; headers?: Record<string, string> } = {}): Promise<Response | null> {
  const request = new Request(url, { method: init.method ?? 'GET', headers: init.headers });
  Object.defineProperty(request, 'mode', { value: init.mode ?? 'cors' });
  let promise: Promise<Response> | null = null;
  listeners.fetch({ request, respondWith: (p: Promise<Response>) => { promise = p; } });
  return promise ? await promise : null;
}
const html = (t = '<html>shell</html>') => new Response(t, { status: 200, headers: { 'Content-Type': 'text/html' } });

beforeEach(() => { vi.clearAllMocks(); net = async () => html(); boot(); });

describe('service worker: araya girmeme kuralları', () => {
  it.each([
    ['/api/me'], ['/api/requests/abc/pdf'], ['/api/attachments/1'], ['/api'], ['/health'],
    ['/api/auth/oidc/login'], ['/api/auth/oidc/callback?code=x&state=y'],
  ])('%s hiç işlenmez (ağ/oturum akışı tarayıcıda kalır)', async (path) => {
    expect(await fire(ORIGIN + path)).toBeNull();
    expect(await fire(ORIGIN + path, { mode: 'navigate' })).toBeNull();
  });

  it.each(['POST', 'PUT', 'DELETE', 'PATCH'])('%s istekleri işlenmez', async (method) => {
    expect(await fire(ORIGIN + '/assets/a.js', { method })).toBeNull();
  });

  it('başka köken (CDN, IdP) işlenmez', async () => {
    expect(await fire('https://login.microsoftonline.com/x')).toBeNull();
    expect(await fire('https://fonts.googleapis.com/css')).toBeNull();
  });

  it('Range isteği (kısmi içerik) işlenmez', async () => {
    expect(await fire(ORIGIN + '/assets/a.js', { headers: { Range: 'bytes=0-10' } })).toBeNull();
  });

  it('hiçbir yol /api yanıtını önbelleğe yazmaz', async () => {
    net = async () => new Response('{"secret":1}', { status: 200 });
    for (const p of ['/api/me', '/api/requests', '/api/auth/oidc/ticket']) await fire(ORIGIN + p);
    await fire(ORIGIN + '/assets/x.js'); // bir şey yazılmış olsun
    for (const c of caches.stores.values()) for (const k of c.store.keys()) expect(k).not.toContain('/api');
  });
});

describe('service worker: kabuk ve varlıklar', () => {
  it('gezinti: ağ yanıtını döndürür ve kabuğu önbelleğe alır', async () => {
    net = async () => html('<html>v1</html>');
    const r = await fire(ORIGIN + '/', { mode: 'navigate' });
    expect(await r!.text()).toBe('<html>v1</html>');
    expect((await caches.match('/index.html'))!.status).toBe(200);
  });

  it('gezinti: ağ yoksa önbellekteki kabuğu verir', async () => {
    await fire(ORIGIN + '/', { mode: 'navigate' });
    net = async () => { throw new TypeError('offline'); };
    const r = await fire(ORIGIN + '/', { mode: 'navigate' });
    expect(await r!.text()).toBe('<html>shell</html>');
  });

  it('gezinti: ağ da önbellek de yoksa Türkçe çevrimdışı sayfası (503)', async () => {
    net = async () => { throw new TypeError('offline'); };
    const r = await fire(ORIGIN + '/#/inbox', { mode: 'navigate' });
    expect(r!.status).toBe(503);
    expect(await r!.text()).toContain('Bağlantı yok');
  });

  it('gezinti: sunucu hatası (5xx) kabuğun üzerine yazılmaz', async () => {
    await fire(ORIGIN + '/', { mode: 'navigate' });
    net = async () => new Response('hata', { status: 502 });
    const r = await fire(ORIGIN + '/', { mode: 'navigate' });
    expect(r!.status).toBe(502);
    expect(await (await caches.match('/index.html'))!.text()).toBe('<html>shell</html>');
  });

  it('özetli varlıklar: ilk istekte ağdan alır, sonra önbellekten verir (ağa gitmez)', async () => {
    const spy = vi.fn(async () => new Response('js', { status: 200 }));
    net = spy;
    await fire(ORIGIN + '/assets/index-abc123.js');
    expect(spy).toHaveBeenCalledTimes(1);
    net = async () => { throw new TypeError('offline'); };
    const r = await fire(ORIGIN + '/assets/index-abc123.js');
    expect(await r!.text()).toBe('js');
  });

  it('başarısız (404) varlık yanıtı önbelleğe alınmaz', async () => {
    net = async () => new Response('yok', { status: 404 });
    await fire(ORIGIN + '/assets/old-123.js');
    expect(await caches.match(ORIGIN + '/assets/old-123.js')).toBeUndefined();
  });

  it('diğer dosyalar (manifest): ağ öncelikli, ağ yoksa önbellek', async () => {
    net = async () => new Response('{"v":1}', { status: 200 });
    await fire(ORIGIN + '/manifest.webmanifest');
    net = async () => { throw new TypeError('offline'); };
    const r = await fire(ORIGIN + '/manifest.webmanifest');
    expect(await r!.text()).toBe('{"v":1}');
  });
});

describe('service worker: yaşam döngüsü', () => {
  it('kurulumda kabuğu önbelleğe alır ve hemen etkinleşmeyi ister', async () => {
    const waits: Promise<unknown>[] = [];
    listeners.install({ waitUntil: (p: Promise<unknown>) => waits.push(p) });
    await Promise.all(waits);
    expect(await caches.match('/index.html')).toBeDefined();
    expect(skipWaiting).toHaveBeenCalled();
  });

  it('kabuk indirilemese bile kurulum başarısız olmaz', async () => {
    net = async () => { throw new TypeError('offline'); };
    const waits: Promise<unknown>[] = [];
    listeners.install({ waitUntil: (p: Promise<unknown>) => waits.push(p) });
    await expect(Promise.all(waits)).resolves.toBeDefined();
    expect(skipWaiting).toHaveBeenCalled();
  });

  it('etkinleşmede eski sürüm önbelleklerini siler, başkalarına dokunmaz, istemcileri devralır', async () => {
    await caches.open('arcvora-shell-v0'); await caches.open('arcvora-shell-v1'); await caches.open('baska-uygulama');
    const waits: Promise<unknown>[] = [];
    listeners.activate({ waitUntil: (p: Promise<unknown>) => waits.push(p) });
    await Promise.all(waits);
    expect(await caches.keys()).toEqual(expect.arrayContaining(['arcvora-shell-v1', 'baska-uygulama']));
    expect(await caches.keys()).not.toContain('arcvora-shell-v0');
    expect(claim).toHaveBeenCalled();
  });
});
