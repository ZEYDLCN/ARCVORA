import { useId } from 'react';

/** Tırtıklı halka: yarıçap, kenar boyunca 8 yumuşak lob ile dalgalanır (r = R + a·cos(8θ)). */
function scallopedRing(cx: number, cy: number, R: number, a: number, lobes: number, steps = 120): string {
  const pts: string[] = [];
  for (let i = 0; i <= steps; i++) {
    const t = (i / steps) * Math.PI * 2;
    const r = R + a * Math.cos(lobes * t);
    pts.push(`${i === 0 ? 'M' : 'L'}${(cx + r * Math.cos(t)).toFixed(2)} ${(cy + r * Math.sin(t)).toFixed(2)}`);
  }
  return pts.join('') + 'Z';
}

const RING = scallopedRing(16, 16, 11.2, 2.1, 8);

/**
 * Arcvora işareti: tırtıklı bir halka içinde bir mimari kemeri ve ona bağlanan iki düğüm
 * (doküman §12.1 logo fikri: "mimari kemeri ve birbirine bağlanan iki düğüm").
 */
export function LogoMark({ size = 36 }: { size?: number }) {
  const g = useId();
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" role="img" aria-label="Arcvora logosu">
      <defs>
        <linearGradient id={g} x1="4" y1="3" x2="28" y2="29" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor="#c4a8ff" />
          <stop offset="1" stopColor="#8b6cf6" />
        </linearGradient>
      </defs>
      <path d={RING} fill="none" stroke={`url(#${g})`} strokeWidth={3.1} strokeLinejoin="round" />
      <path d="M11.2 21.2v-4.1a4.8 4.8 0 0 1 9.6 0v4.1" fill="none" stroke={`url(#${g})`} strokeWidth={2.4} strokeLinecap="round" />
      <circle cx="11.2" cy="21.6" r="1.9" fill="#3dd6c6" />
      <circle cx="20.8" cy="21.6" r="1.9" fill="#3dd6c6" />
    </svg>
  );
}

export function Logo({ size = 36, showWord = true }: { size?: number; showWord?: boolean }) {
  return (
    <span className="logo">
      <LogoMark size={size} />
      {showWord && <span className="logo-word" style={{ fontSize: size * 0.82 }}>Arcvora</span>}
    </span>
  );
}
