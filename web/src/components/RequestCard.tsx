import type { ReactNode } from 'react';
import { AppIcon } from './AppIcon';
import { IconClock, IconExternal } from './icons';
import { DemoChip, plainName, SummaryBlock } from './ui';
import type { SummaryPart } from '../types';

export interface RequestCardProps {
  icon: string;
  number: string;
  appName: string;
  /** Kartın sahibi olmayan kişi (talep eden); kendi taleplerinde verilmez. */
  person?: string;
  isDemo?: boolean;
  summary: string;
  parts?: SummaryPart[];
  /** Durum rozeti. */
  badge: ReactNode;
  /** Adım / vekâlet / yönlendirme gibi küçük etiketler. */
  chips?: ReactNode;
  /** Biçimlenmiş zaman metni. */
  time?: string;
  note?: string;
  actionLabel?: string;
  onOpen: () => void;
}

/**
 * Talep/görev kartı (referans tasarım): uygulama simgesi + numara, kişi ve durum satırı, etiketli tarih/tutar kutuları, adım etiketleri,
 * zaman satırı ve “Talebi incele” eylemi. Tüm kart tek düğmedir (klavye ve ekran okuyucu için tek odak noktası).
 */
export function RequestCard(p: RequestCardProps) {
  const name = p.person ? plainName(p.person, p.isDemo) : undefined;
  return (
    <button className="item req-card" onClick={p.onOpen}>
      <span className="rc-head">
        <AppIcon name={p.icon} />
        <span className="rc-body">
          <span className="rc-title">{p.number}</span>
          <span className="rc-sub">
            {name && <span>{name}</span>}
            {p.isDemo && <DemoChip />}
            {p.badge}
          </span>
          <SummaryBlock parts={p.parts} fallback={p.summary} />
        </span>
      </span>
      {p.chips && <span className="rc-chips">{p.chips}</span>}
      {p.time && <span className="rc-time"><IconClock size={15} />{p.time}</span>}
      {p.note && <span className="rc-note">“{p.note}”</span>}
      <span className="rc-action"><span className="btn soft small"><IconExternal size={15} />{p.actionLabel ?? 'Talebi incele'}</span></span>
    </button>
  );
}
