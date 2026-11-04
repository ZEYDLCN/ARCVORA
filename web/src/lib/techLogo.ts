import { logos, type LogoId } from './logos.generated';

/**
 * Serbest metindeki teknoloji adlarını marka logolarına eşler (ör. "ASP.NET Core Worker Service" → .NET).
 * Sıra önemlidir: özel olanlar (Spring Boot, Next.js) genel olanlardan (Java, JavaScript) önce gelir.
 */
const rules: [RegExp, LogoId][] = [
  [/kubernetes|\bk8s\b/i, 'kubernetes'],
  [/docker|container/i, 'docker'],
  [/asp\.?net|\.net\b|dotnet|entity framework|ef core|blazor/i, 'dotnet'],
  [/spring\s*boot/i, 'springboot'],
  [/\bspring\b/i, 'spring'],
  [/postgres|npgsql/i, 'postgresql'],
  [/typescript/i, 'typescript'],
  [/next\.?js/i, 'nextjs'],
  [/\breact\b/i, 'react'],
  [/node\.?js|\bnode\b/i, 'nodejs'],
  [/javascript|(?<![.\w])js\b/i, 'javascript'],
  [/\bvue/i, 'vuejs'],
  [/angular/i, 'angular'],
  [/redis/i, 'redis'],
  [/nginx/i, 'nginx'],
  [/rabbitmq/i, 'rabbitmq'],
  [/kafka/i, 'kafka'],
  [/mongo/i, 'mongodb'],
  [/mysql|mariadb/i, 'mysql'],
  [/sqlite/i, 'sqlite'],
  [/elastic|opensearch/i, 'elasticsearch'],
  [/minio/i, 'minio'],
  [/graphql/i, 'graphql'],
  [/openapi|swagger/i, 'openapi'],
  [/grafana/i, 'grafana'],
  [/prometheus/i, 'prometheus'],
  [/terraform/i, 'terraform'],
  [/cloudflare/i, 'cloudflare'],
  [/google cloud|\bgcp\b/i, 'googlecloud'],
  [/github actions/i, 'githubactions'],
  [/github/i, 'github'],
  [/\bgit\b/i, 'git'],
  [/python|django|flask|fastapi/i, 'python'],
  [/java\b|openjdk|jpa|hibernate/i, 'openjdk'],
  [/\brust\b/i, 'rust'],
  [/\bgo(lang)?\b/i, 'go'],
  [/stripe/i, 'stripe'],
  [/sentry/i, 'sentry'],
  [/linux/i, 'linux'],
  [/vite\b/i, 'vite'],
  [/mermaid/i, 'mermaid'],
  [/anthropic|claude/i, 'anthropic'],
];

/** Metinde geçen tüm teknolojilerin logo kimlikleri (tekrarsız, ilk geçiş sırasıyla). */
export function techLogoIds(text: string | undefined | null, max = 3): LogoId[] {
  if (!text) return [];
  const hits: { id: LogoId; at: number }[] = [];
  for (const [re, id] of rules) {
    const m = re.exec(text);
    if (m && !hits.some((h) => h.id === id)) hits.push({ id, at: m.index });
  }
  // Aynı yerde birden fazla kural eşleşirse (ör. "Spring Boot" → springboot ve spring) yalnızca ilki kalsın
  const byPos = new Map<number, { id: LogoId; at: number }>();
  for (const h of hits) if (!byPos.has(h.at)) byPos.set(h.at, h);
  return [...byPos.values()].sort((a, b) => a.at - b.at).slice(0, max).map((h) => h.id);
}

export const brandTitle = (id: LogoId): string => logos[id].title;

/** Göreli parlaklık (0..1): koyu marka renkleri koyu temada okunmaz, o zaman metin rengine düşülür. */
export function luminance(hex: string): number {
  const n = parseInt(hex.replace('#', ''), 16);
  const [r, g, b] = [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((v) => {
    const c = v / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** Logonun dolgu rengi: resmi marka rengi; zemin için yeterince kontrast yoksa `currentColor`. */
export function logoFill(hex: string, surfaceIsDark: boolean): string {
  const l = luminance(hex);
  if (surfaceIsDark && l < 0.08) return 'currentColor';
  if (!surfaceIsDark && l > 0.82) return 'currentColor';   // örn. çok açık sarı zeminde okunmayabilir
  return hex;
}
