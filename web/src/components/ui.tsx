import type { ReactNode } from 'react';
import { IconCalendar, IconCheckCircle, IconClock, IconFile, IconMinusCircle, IconXCircle } from './icons';
import type { Tone } from '../lib/requestText';
import type { SummaryPart } from '../types';

/** Referans tasarımın ortak parçaları: kart başlığı, boş durum, istatistik kutusu, etiketli mini alan, demo rozeti, özet bloğu, durum simgesi. */

export function CardTitle({ icon, children, aside }: { icon?: ReactNode; children: ReactNode; aside?: ReactNode }) {
  return (
    <div className="card-title">
      {icon && <span className="ct-ico" aria-hidden="true">{icon}</span>}
      <h3>{children}</h3>
      {aside && <span className="ct-aside">{aside}</span>}
    </div>
  );
}

export function EmptyState({ icon, title, children }: { icon?: ReactNode; title: ReactNode; children?: ReactNode }) {
  return (
    <div className="empty-state">
      <span className="es-ico" aria-hidden="true">{icon ?? <IconFile size={30} />}</span>
      <b>{title}</b>
      {children && <span>{children}</span>}
    </div>
  );
}

export function StatTile({ icon, label, value, tone }: { icon: ReactNode; label: string; value: ReactNode; tone?: Tone }) {
  return (
    <div className={`stat-tile${tone ? ` ${tone}` : ''}`}>
      <span className="st-label"><span className="st-ico" aria-hidden="true">{icon}</span>{label}</span>
      <b className="st-value">{value}</b>
    </div>
  );
}

/** Başında simge olan etiketli küçük kutu (ör. “Başlangıç 📅 26.11.2026”). */
export function MiniField({ icon, label, value }: { icon?: ReactNode; label: string; value: ReactNode }) {
  return (
    <span className="mini-field">
      <span className="mf-label">{label}</span>
      <span className="mf-value">{icon && <span className="mf-ico" aria-hidden="true">{icon}</span>}{value}</span>
    </span>
  );
}

export function DemoChip() { return <span className="chip-demo" title="Örnek (demo) veri">Demo</span>; }

/** Demo kullanıcıların adlarına eklenen “(demo)” sonekini ayıklar; rozet ayrıca gösterilir. */
export function plainName(name: string, isDemo?: boolean): string { return isDemo ? name.replace(/\s*\(demo\)\s*$/i, '') : name; }

const toneIcon: Record<Tone, (size: number) => ReactNode> = {
  ok: (s) => <IconCheckCircle size={s} />, bad: (s) => <IconXCircle size={s} />, warn: (s) => <IconClock size={s} />, info: (s) => <IconClock size={s} />, neutral: (s) => <IconMinusCircle size={s} />,
};
export const ToneIcon = ({ tone, size = 14 }: { tone: Tone; size?: number }) => <>{toneIcon[tone](size)}</>;

/**
 * Talebin özet parçaları: tarih/tutar/sayı parçaları etiketli mini kutular olur, metin parçaları (tür, açıklama) tek satırda toplanır.
 * Parça yoksa (eski kayıt/bozuk tanım) düz özet yazısı gösterilir.
 */
export function SummaryBlock({ parts, fallback }: { parts?: SummaryPart[]; fallback?: string }) {
  if (!parts || parts.length === 0) return fallback ? <span className="sb-line">{fallback}</span> : null;
  const boxes = parts.filter((p) => p.kind !== 'text').slice(0, 4);
  const lines = parts.filter((p) => p.kind === 'text').map((p) => p.text);
  return (
    <>
      {lines.length > 0 && <span className="sb-line">{lines.join(' · ')}</span>}
      {boxes.length > 0 && (
        <span className="mini-fields">
          {boxes.map((p) => <MiniField key={p.key} label={p.label} value={p.text} icon={p.kind === 'date' ? <IconCalendar size={15} /> : undefined} />)}
        </span>
      )}
    </>
  );
}
