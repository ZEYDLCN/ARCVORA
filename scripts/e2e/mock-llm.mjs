// Sahte "yerel model" sunucusu: Ollama / llama.cpp'nin OpenAI uyumlu /v1/chat/completions ucunu taklit eder.
// AMAÇ: ARCVORA'nın `Ai__Provider=local` yolunu (istek biçimi, şemalı çıktı, iş kuyruğu, doğrulama, kabul) gerçek bir
// model indirmeden uçtan uca sınamak. Burada üretilen taslaklar sabit kalıplardır; MODEL KALİTESİ hakkında hiçbir şey söylemez.
//
//   node scripts/e2e/mock-llm.mjs                       # :11599, model adı "qwen-mock"
//   MOCK_LLM_PORT=11434 MOCK_LLM_MODEL=qwen3:8b node scripts/e2e/mock-llm.mjs
//   MOCK_LLM_DELAY_MS=3000 → her yanıtı gecikmeli verir (yavaş yerel model; iş iptali ve zaman aşımı sınamaları için)
//   MOCK_LLM_FAULT=repair  → ilk mimari yanıtı bilerek bozuk (olmayan bileşene bağlantı) döndürür; ARCVORA'nın onarım turunu sınar
//   GET /__requests → gelen isteklerin özeti (e2e testi, istemcinin doğru biçimde konuştuğunu buradan doğrular)
import http from 'node:http';

const PORT = Number(process.env.MOCK_LLM_PORT ?? 11599);
const MODEL = process.env.MOCK_LLM_MODEL ?? 'qwen-mock';
const FAULT = process.env.MOCK_LLM_FAULT ?? '';
const DELAY = Number(process.env.MOCK_LLM_DELAY_MS ?? 0);
const log = [];
let architectureCalls = 0;

const between = (s, open, close) => {
  const a = s.indexOf(open);
  if (a < 0) return null;
  const b = s.indexOf(close, a + open.length);
  return b < 0 ? null : s.slice(a + open.length, b);
};

function currentModel(prompt) {
  const raw = between(prompt, '<current_model>\n', '\n</current_model>');
  try {
    return raw ? JSON.parse(raw) : {};
  } catch {
    return {};
  }
}

function sentences(text) {
  return text
    .split(/(?<=[.!?])\s+/)
    .map((s) => s.trim())
    .filter((s) => s.length > 8)
    .slice(0, 3);
}

function extractDraft(prompt) {
  const sources = [...prompt.matchAll(/<source id="([^"]+)">\n([\s\S]*?)\n<\/source>/g)].map((m) => ({ id: m[1], text: m[2] }));
  const requirements = [];
  let n = 1;
  for (const s of sources) {
    for (const sentence of sentences(s.text)) {
      requirements.push({
        id: `new-${n++}`,
        text: sentence.replace(/[.!?]$/, ''),
        kind: 'functional',
        priority: requirements.length === 0 ? 'critical' : 'high',
        origin: 'user_statement',
        sourceRefs: [{ sourceId: s.id, excerpt: sentence }], // kaynaktan AYNEN alıntı (koruyucu bunu doğrular)
        acceptanceCriteria: requirements.length === 0 ? [{ id: 'new-ac-1', text: 'Kullanıcı ilk akışı hatasız tamamlayabilir.' }] : [],
      });
    }
  }
  return {
    summary: `${requirements.length} gereksinim çıkarıldı (sahte yerel model).`,
    requirements,
    openQuestions: [{ id: `new-${n}`, text: 'Aynı anda kaç kullanıcı bekleniyor ve en kritik işlem hangisi?', impact: 'high' }],
    groups: [],
  };
}

function architectureDraft(prompt, { broken = false } = {}) {
  const model = currentModel(prompt);
  // İyi davranan bir model gibi: kabul edilmiş mimari varsa yeniden üretmez (istemde "mevcut nesneleri yeniden üretme" yazar).
  if ((model.components ?? []).length > 0 && !broken) {
    return { summary: 'Mimari zaten kabul edilmiş; yeni nesne önerilmedi (sahte yerel model).', groups: [] };
  }
  const reqs = model.requirements ?? [];
  const web = 'new-2', api = 'new-3', db = 'new-4';
  const traceLinks = [];
  let n = 7;
  for (const r of reqs.slice(0, 3)) traceLinks.push({ id: `new-${n++}`, fromId: 'new-1', toId: r.id, kind: 'addresses' });
  for (const c of [web, api, db]) traceLinks.push({ id: `new-${n++}`, fromId: c, toId: 'new-1', kind: 'implements' });
  const coreIds = ['new-1', web, api, db, 'new-5', 'new-6'];
  return {
    summary: 'Modüler monolit ve PostgreSQL ile başlangıç mimarisi (sahte yerel model).',
    decisions: [{
      id: 'new-1', title: 'Modüler monolit ve tek veritabanı', choice: 'Tek dağıtım birimi, modül sınırları net',
      alternatives: [{ name: 'Mikroservis', tradeoff: 'Operasyon yükü küçük ekip için ağır.' }],
      rationale: 'Küçük ekip ve kısa teslim süresi.', consequences: 'Modül sınırları disiplinle korunmalı.', reviewStatus: 'proposed',
    }],
    components: [
      { id: web, name: 'Web arayüzü', kind: 'client', responsibility: 'Kullanıcı etkileşimi', technology: 'React' },
      { id: api, name: 'Uygulama API', kind: 'api', responsibility: 'İş kuralları ve kimlik', technology: 'ASP.NET Core' },
      { id: db, name: 'PostgreSQL', kind: 'database', responsibility: 'Kalıcı veri', technology: 'PostgreSQL' },
    ],
    relationships: [
      { id: 'new-5', sourceId: web, targetId: api, communication: 'sync', description: 'HTTPS/JSON' },
      { id: 'new-6', sourceId: api, targetId: broken ? 'CMP-999' : db, communication: 'data', description: 'SQL' },
    ],
    traceLinks,
    openQuestions: [],
    // İzlenebilirlik bağlantıları çekirdek nesnelere atıf yapar → ARCVORA bu grubu çekirdeğe bağımlı sayar (kısmi kabul testi).
    groups: [
      { id: 'grp-core', title: 'Çekirdek yaklaşım', description: '', objectIds: coreIds },
      { id: 'grp-trace', title: 'İzlenebilirlik bağlantıları', description: '', objectIds: traceLinks.map((t) => t.id) },
    ],
  };
}

function changeDraft(prompt) {
  const model = currentModel(prompt);
  const request = between(prompt, '<change_request>\n', '\n</change_request>') ?? '';
  const apiComponent = (model.components ?? []).find((c) => c.kind === 'api') ?? (model.components ?? [])[0];
  const firstReq = (model.requirements ?? [])[0];
  const draft = {
    summary: 'Değişiklik talebi için bildirim bileşeni önerildi (sahte yerel model).',
    decisions: [{
      id: 'new-2', title: 'Olay tabanlı bildirim', choice: 'Onay sonrası olay yayımla, bildirim ayrı çalışsın',
      alternatives: [], rationale: `Talep: ${request.slice(0, 140)}`, consequences: 'Teslimat garantisi için yeniden deneme gerekir.', reviewStatus: 'proposed',
    }],
    components: [{ id: 'new-1', name: 'Bildirim modülü', kind: 'worker', responsibility: 'Onaydan sonra dış sisteme bildirim gönderir', technology: 'ASP.NET Core' }],
    relationships: apiComponent ? [{ id: 'new-3', sourceId: apiComponent.id, targetId: 'new-1', communication: 'event', description: 'Onay olayı' }] : [],
    traceLinks: [{ id: 'new-4', fromId: 'new-1', toId: 'new-2', kind: 'implements' }, ...(firstReq ? [{ id: 'new-5', fromId: 'new-2', toId: firstReq.id, kind: 'addresses' }] : [])],
    possibleImpacts: apiComponent ? [{ objectId: apiComponent.id, reason: 'Onay akışı olay yayımlayacak; mevcut davranış etkilenebilir.' }] : [],
    groups: [{ id: 'grp-notify', title: 'Bildirim', description: '', objectIds: ['new-1', 'new-2', 'new-3', 'new-4', 'new-5'] }],
  };
  return draft;
}

function answer(body) {
  const user = body.messages?.find((m) => m.role === 'user')?.content ?? '';
  let task, draft;
  if (user.includes('doğrulaması başarısız oldu')) {
    task = 'repair';
    draft = architectureDraft(user); // onarım turu: düzgün taslak
  } else if (user.includes('gereksinimleri çıkar')) {
    task = 'extract';
    draft = extractDraft(user);
  } else if (user.includes('mimari taslak öner')) {
    task = 'architecture';
    architectureCalls++;
    draft = architectureDraft(user, { broken: FAULT === 'repair' && architectureCalls === 1 });
  } else if (user.includes('yeni talebin')) {
    task = 'change';
    draft = changeDraft(user);
  } else {
    task = 'unknown';
    draft = { summary: 'tanınmayan görev' };
  }
  return { task, content: JSON.stringify(draft) };
}

const server = http.createServer((req, res) => {
  const send = (code, obj) => {
    res.writeHead(code, { 'content-type': 'application/json' });
    res.end(JSON.stringify(obj));
  };
  if (req.method === 'GET' && req.url === '/__requests') return send(200, log);
  if (req.method === 'GET' && (req.url === '/v1/models' || req.url === '/api/tags')) {
    return send(200, { object: 'list', data: [{ id: MODEL, object: 'model' }], models: [{ name: MODEL }] });
  }
  if (req.method === 'POST' && req.url === '/v1/chat/completions') {
    const chunks = [];
    req.on('data', (c) => chunks.push(c));
    req.on('end', () => {
      let body;
      try {
        body = JSON.parse(Buffer.concat(chunks).toString('utf8'));
      } catch {
        return send(400, { error: { message: 'geçersiz JSON' } });
      }
      const entry = {
        model: body.model,
        stream: body.stream,
        maxTokens: body.max_tokens,
        temperature: body.temperature,
        responseFormat: body.response_format?.type,
        strict: body.response_format?.json_schema?.strict,
        schemaType: body.response_format?.json_schema?.schema?.type,
        hasContentLength: req.headers['content-length'] !== undefined,
        authorization: req.headers.authorization ? 'var' : 'yok',
      };
      if (body.model !== MODEL) {
        log.push({ ...entry, task: 'model-bulunamadı' });
        return send(404, { error: { message: `model '${body.model}' not found` } });
      }
      const { task, content } = answer(body);
      log.push({ ...entry, task });
      console.log(`[mock-llm] ${task} (${body.model}) → ${content.length} karakter`);
      setTimeout(() => send(200, {
        id: 'mock-1', object: 'chat.completion', model: body.model,
        choices: [{ index: 0, finish_reason: 'stop', message: { role: 'assistant', content } }],
        usage: { prompt_tokens: Math.ceil(JSON.stringify(body.messages).length / 4), completion_tokens: Math.ceil(content.length / 4) },
      }), DELAY);
    });
    return;
  }
  send(404, { error: { message: 'bulunamadı' } });
});

server.listen(PORT, '127.0.0.1', () => console.log(`[mock-llm] http://127.0.0.1:${PORT}  model=${MODEL}${FAULT ? `  fault=${FAULT}` : ''}${DELAY ? `  delay=${DELAY}ms` : ''}`));
