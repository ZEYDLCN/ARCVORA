// Üretim yapılandırmasında (yayınlanmış uygulama + wwwroot) arayüzün sunulması: SPA yedeği, varlık dosyaları, /api sınırı.
//   ARCVORA_API_URL=http://127.0.0.1:8081 node scripts/e2e/prod-serving.mjs
import { API_URL, check, fatal, finish, section, waitHealth } from './lib.mjs';

if (!(await waitHealth(60_000))) fatal('uygulama açılmadı');
const get = (path, init) => fetch(API_URL + path, init);

section('Arayüzün sunulması');
const index = await get('/');
const html = await index.text();
check('/ → 200 ve HTML (React kökü var)', index.status === 200 && /text\/html/.test(index.headers.get('content-type') ?? '') && html.includes('id="root"'), [index.status, index.headers.get('content-type')]);
check('HTML yanıtında güvenlik başlıkları var (nosniff, DENY)', index.headers.get('x-content-type-options') === 'nosniff' && index.headers.get('x-frame-options') === 'DENY', [...index.headers.entries()]);
const deep = await get('/projeler/herhangi-bir-yol');
check('arayüz içi derin bağlantı (SPA yedeği) → 200 HTML', deep.status === 200 && /text\/html/.test(deep.headers.get('content-type') ?? ''), deep.status);

section('Varlık dosyaları');
const script = /src="(\/assets\/[^"]+\.js)"/.exec(html)?.[1];
check('index.html derlenmiş bir betik dosyasına işaret eder', !!script, html.slice(0, 200));
if (script) {
  const js = await get(script);
  check('betik dosyası 200 ve JavaScript türünde', js.status === 200 && /javascript/.test(js.headers.get('content-type') ?? ''), [js.status, js.headers.get('content-type')]);
  check('betik dosyası uzun süreli önbelleğe uygun (içerik özetli ad)', /-[A-Za-z0-9_-]{6,}\.js$/.test(script), script);
  const map = await get(`${script}.map`);
  check('kaynak haritası (.map) üretimde sunulmaz', map.status === 404 || !/json/.test(map.headers.get('content-type') ?? ''), [map.status, map.headers.get('content-type')]);
}
const missing = await get('/assets/yok-olan-dosya-abc123.js');
const missingBody = await missing.text();
check('olmayan varlık dosyası → 404 (index.html DEĞİL; dağıtım sonrası eski önbellekli sayfalar "Unexpected token <" almasın)',
  missing.status === 404 && !missingBody.includes('id="root"'), [missing.status, missingBody.slice(0, 60)]);
const missingCss = await get('/assets/yok.css');
check('olmayan .css de 404', missingCss.status === 404, missingCss.status);

section('/api sınırı');
const unknownApi = await get('/api/yok');
check('bilinmeyen /api yolu JSON 404 (HTML değil)', unknownApi.status === 404 && /json/.test(unknownApi.headers.get('content-type') ?? ''), [unknownApi.status, unknownApi.headers.get('content-type')]);
const health = await get('/health');
check('/health JSON ve 200', health.status === 200 && (await health.json()).status === 'ok');
check('geliştirme sayfaları açık değil (/swagger, /_framework HTML 404/yedek)', (await get('/swagger')).headers.get('content-type')?.includes('json') !== true);

finish();
