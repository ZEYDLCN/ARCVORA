// Uçtan uca betikler için ortak yardımcılar (Node 20+; bağımlılık yok).
export const API_URL = (process.env.ARCVORA_API_URL ?? 'http://127.0.0.1:5217').replace(/\/$/, '');

let passed = 0;
const failures = [];

/** Bir doğrulama adımı: başarısızlık betiği hemen durdurmaz, sonda toplu raporlanır. */
export function check(name, ok, detail) {
  if (ok) {
    passed++;
    console.log(`  ✓ ${name}`);
  } else {
    failures.push(name);
    console.log(`  ✗ ${name}${detail === undefined ? '' : `\n      ${typeof detail === 'string' ? detail : JSON.stringify(detail).slice(0, 600)}`}`);
  }
  return ok;
}

export function section(title) {
  console.log(`\n▶ ${title}`);
}

/** Beklenmeyen durumda (sonraki adımlar anlamsız kalırsa) hemen çık. */
export function fatal(message, detail) {
  console.log(`\n✗ ${message}${detail === undefined ? '' : `\n  ${JSON.stringify(detail).slice(0, 800)}`}`);
  failures.push(message);
  finish();
}

export function finish() {
  console.log(`\n${failures.length === 0 ? '✓' : '✗'} ${passed} doğrulama geçti, ${failures.length} başarısız.`);
  for (const f of failures) console.log(`   - ${f}`);
  process.exit(failures.length === 0 ? 0 : 1);
}

/** Küçük JSON istemcisi: { status, body } döner, hata durumlarında da fırlatmaz. */
export async function call(method, path, { token, body, headers = {}, raw = false } = {}) {
  const res = await fetch(API_URL + path, {
    method,
    headers: {
      ...(body === undefined ? {} : { 'content-type': 'application/json' }),
      ...(token ? { authorization: `Bearer ${token}` } : {}),
      ...headers,
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  if (raw) return { status: res.status, body: text, headers: res.headers };
  let parsed = text;
  try {
    parsed = text ? JSON.parse(text) : null;
  } catch {
    /* JSON değil */
  }
  return { status: res.status, body: parsed, headers: res.headers };
}

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** Arka plan işi bitene kadar yoklar. */
export async function waitJob(token, jobId, timeoutMs = 120_000) {
  const t0 = Date.now();
  for (;;) {
    const r = await call('GET', `/api/jobs/${jobId}`, { token });
    if (r.status !== 200) return { status: 'error', http: r.status, body: r.body };
    if (['succeeded', 'failed', 'cancelled'].includes(r.body.status)) return r.body;
    if (Date.now() - t0 > timeoutMs) return { status: 'timeout', ...r.body };
    await sleep(400);
  }
}

export async function waitHealth(timeoutMs = 120_000) {
  const t0 = Date.now();
  while (Date.now() - t0 < timeoutMs) {
    try {
      const r = await fetch(`${API_URL}/health`, { signal: AbortSignal.timeout(2000) });
      if (r.ok) return true;
    } catch {
      /* henüz hazır değil */
    }
    await sleep(1000);
  }
  return false;
}
