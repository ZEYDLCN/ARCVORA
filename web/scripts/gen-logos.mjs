// Marka logolarını simple-icons paketinden (CC0) yalnızca kullanılanlarla src/lib/logos.generated.ts'ye yazar.
// Logolar ilgili marka sahiplerine aittir; burada yalnızca teknolojiyi belirtmek için kullanılır.
// Çalıştır: npm run gen:logos
import * as si from 'simple-icons';
import { writeFileSync } from 'node:fs';

const wanted = {
  kubernetes: 'Kubernetes', javascript: 'Javascript', typescript: 'Typescript', dotnet: 'Dotnet', postgresql: 'Postgresql',
  docker: 'Docker', react: 'React', redis: 'Redis', nginx: 'Nginx', rabbitmq: 'Rabbitmq', kafka: 'Apachekafka',
  mongodb: 'Mongodb', mysql: 'Mysql', elasticsearch: 'Elasticsearch', github: 'Github', nodejs: 'Nodedotjs',
  nextjs: 'Nextdotjs', python: 'Python', openjdk: 'Openjdk', spring: 'Spring', springboot: 'Springboot', graphql: 'Graphql',
  minio: 'Minio', sqlite: 'Sqlite', grafana: 'Grafana', prometheus: 'Prometheus', terraform: 'Terraform', vite: 'Vite',
  mermaid: 'Mermaid', anthropic: 'Anthropic', githubactions: 'Githubactions', cloudflare: 'Cloudflare',
  openapi: 'Openapiinitiative', swagger: 'Swagger', googlecloud: 'Googlecloud', linux: 'Linux', git: 'Git', rust: 'Rust',
  go: 'Go', angular: 'Angular', vuejs: 'Vuedotjs', sentry: 'Sentry', stripe: 'Stripe',
};

const rows = [];
for (const [id, name] of Object.entries(wanted)) {
  const icon = si[`si${name}`];
  if (!icon) throw new Error(`simple-icons içinde ${name} yok`);
  rows.push(`  ${id}: { title: ${JSON.stringify(icon.title)}, hex: ${JSON.stringify('#' + icon.hex)}, path: ${JSON.stringify(icon.path)} },`);
}

writeFileSync(new URL('../src/lib/logos.generated.ts', import.meta.url),
`// OTOMATİK ÜRETİLDİ (scripts/gen-logos.mjs) — elle düzenlemeyin.
// Kaynak: simple-icons (CC0-1.0). Markalar ilgili sahiplerine aittir; yalnızca teknolojiyi belirtmek için kullanılır.
export interface BrandLogo { title: string; hex: string; path: string }

export const logos = {
${rows.join('\n')}
} as const satisfies Record<string, BrandLogo>;

export type LogoId = keyof typeof logos;
`);
console.log(`${rows.length} logo yazıldı`);
