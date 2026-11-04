import { LogoMark } from './Logo';
import { TechLogo } from './TechLogo';
import type { LogoId } from '../lib/logos.generated';

const sources: { id: LogoId; x: number; y: number }[] = [
  { id: 'kubernetes', x: 40, y: 40 }, { id: 'dotnet', x: 150, y: 40 }, { id: 'typescript', x: 260, y: 40 },
  { id: 'react', x: 95, y: 130 }, { id: 'postgresql', x: 205, y: 130 }, { id: 'docker', x: 315, y: 130 },
  { id: 'javascript', x: 40, y: 220 }, { id: 'redis', x: 150, y: 220 }, { id: 'springboot', x: 260, y: 220 },
];

const outputs = [
  { label: 'Gereksinimler', y: 54, color: '#7c6cf7' },
  { label: 'Kararlar', y: 142, color: '#4b4f6b' },
  { label: 'Mimari görünümler', y: 230, color: '#ff8a8a' },
  { label: 'Teknik devir', y: 318, color: '#3dd6c6' },
];

/** Giriş sayfası illüstrasyonu: teknolojiler → Arcvora → çıktılar (şeritli akış). */
export function Hero() {
  return (
    <svg className="hero" viewBox="0 0 980 380" role="img" aria-label="Teknolojiler ve gereksinimler Arcvora'da gereksinim, karar, görünüm ve teknik devire dönüşür">
      <defs>
        <radialGradient id="hero-glow"><stop offset="0" stopColor="#8be3f5" stopOpacity=".8" /><stop offset="1" stopColor="#8be3f5" stopOpacity="0" /></radialGradient>
        <filter id="hero-shadow" x="-30%" y="-30%" width="160%" height="170%"><feDropShadow dx="0" dy="6" stdDeviation="6" floodColor="#1b1f3b" floodOpacity=".14" /></filter>
        {outputs.map((o, i) => (
          <linearGradient key={i} id={`rib-${i}`} x1="0" x2="1"><stop offset="0" stopColor={o.color} stopOpacity=".15" /><stop offset=".5" stopColor={o.color} stopOpacity=".75" /><stop offset="1" stopColor={o.color} stopOpacity=".9" /></linearGradient>
        ))}
      </defs>

      <circle cx="470" cy="190" r="150" fill="url(#hero-glow)" />

      {outputs.map((o, i) => (
        <path key={o.label} className="ribbon" d={`M498 190 C 600 190, 600 ${o.y}, 720 ${o.y}`} stroke={`url(#rib-${i})`} />
      ))}
      {sources.map((s, i) => (
        <path key={s.id} className="ribbon thin" d={`M${s.x + 50} ${s.y + 25} C 400 ${s.y + 25}, 400 190, 442 190`} style={{ animationDelay: `${i * 0.25}s` }} />
      ))}

      {sources.map((s) => (
        <g key={s.id} transform={`translate(${s.x} ${s.y})`} className="hero-chip">
          <rect width="100" height="50" rx="16" filter="url(#hero-shadow)" />
          <g transform="translate(36 11)"><TechLogo id={s.id} size={28} surfaceDark={false} /></g>
        </g>
      ))}

      <circle cx="470" cy="190" r="48" className="hero-core" filter="url(#hero-shadow)" />
      <g transform="translate(446 166)"><LogoMark size={48} /></g>
      <text x="470" y="262" textAnchor="middle" className="hero-caption">Arcvora</text>

      {outputs.map((o) => (
        <g key={o.label} transform={`translate(720 ${o.y - 22})`} className="hero-out">
          <circle cx="22" cy="22" r="22" className="out-dot" />
          <circle cx="22" cy="22" r="6" fill={o.color} />
          <text x="58" y="27">{o.label}</text>
        </g>
      ))}
    </svg>
  );
}
