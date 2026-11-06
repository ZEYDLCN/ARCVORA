/* ARCVORA Apps service worker.
 *
 * Amaç: ana ekrana eklenen uygulamanın kabuğunun (HTML + derlenmiş JS/CSS/simgeler) çevrimdışı/yavaş ağda açılabilmesi.
 * Kural: /api/ altındaki hiçbir şey (kişisel/şirket verisi, ekler, PDF) ASLA önbelleğe alınmaz ve service worker araya girmez;
 * yalnızca aynı kökenli GET istekleri işlenir. Çevrimdışıyken talep göndermek/onaylamak mümkün değildir (veri tutarlılığı için bilinçli).
 */
const VERSION = 'arcvora-shell-v1';
const SHELL = '/index.html';

const OFFLINE_HTML = '<!doctype html><html lang="tr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
  + '<title>ARCVORA · Çevrimdışı</title><style>body{font-family:system-ui,sans-serif;background:#091a20;color:#e6f0f2;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0;padding:24px;text-align:center}'
  + 'button{margin-top:16px;padding:10px 18px;border-radius:8px;border:0;background:#8b6cf6;color:#fff;font-size:15px}</style></head>'
  + '<body><main><h1>Bağlantı yok</h1><p>ARCVORA Apps\'e şu an ulaşılamıyor. İnternet bağlantınızı kontrol edip yeniden deneyin.</p>'
  + '<button onclick="location.reload()">Yeniden dene</button></main></body></html>';

self.addEventListener('install', (event) => {
  // Kabuk yüklenemese bile kurulum başarısız sayılmasın (ilk ziyarette ağ yoksa vb.).
  event.waitUntil(caches.open(VERSION).then((c) => c.add(SHELL)).catch(() => undefined).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k.startsWith('arcvora-shell-') && k !== VERSION).map((k) => caches.delete(k))))
      .then(() => self.clients.claim()),
  );
});

/** Önbelleğe alınabilir mi? Yalnızca aynı kökenli GET; /api/ ve /health hariç. */
function handled(request, origin) {
  if (request.method !== 'GET') return false;
  const url = new URL(request.url);
  if (url.origin !== origin) return false;
  if (url.pathname === '/api' || url.pathname.startsWith('/api/') || url.pathname === '/health') return false;
  if (request.headers.get('range')) return false;
  return true;
}

async function putIfOk(key, response) {
  if (response && response.ok && response.type !== 'opaque' && response.type !== 'opaqueredirect') {
    const cache = await caches.open(VERSION);
    await cache.put(key, response.clone());
  }
  return response;
}

self.addEventListener('fetch', (event) => {
  const request = event.request;
  if (!handled(request, self.location.origin)) return;
  const url = new URL(request.url);

  // Sayfa gezintisi: önce ağ (her zaman güncel kabuk), olmazsa önbellekteki kabuk, o da yoksa çevrimdışı sayfası.
  if (request.mode === 'navigate') {
    event.respondWith(
      fetch(request)
        .then((r) => (r.ok ? putIfOk(SHELL, r.clone()).then(() => r) : r))
        .catch(async () => (await caches.match(SHELL)) || new Response(OFFLINE_HTML, { status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8' } })),
    );
    return;
  }

  // Derlenmiş (adında özet bulunan, değişmez) varlıklar: önce önbellek.
  if (url.pathname.startsWith('/assets/') || url.pathname.startsWith('/icons/')) {
    event.respondWith(
      caches.match(request).then((hit) => hit || fetch(request).then((r) => putIfOk(request, r))),
    );
    return;
  }

  // Diğer aynı kökenli dosyalar (manifest, sw.js vb.): ağ öncelikli, olmazsa önbellek.
  event.respondWith(fetch(request).then((r) => putIfOk(request, r)).catch(async () => (await caches.match(request)) || Response.error()));
});
