import type { ReactNode } from 'react';
import { IconBriefcase, IconCalendar, IconCar, IconCheckCircle, IconClock, IconFile, IconHome, IconPlane, IconReceipt, IconShield, IconUsers, IconWallet } from './icons';

/** Uygulama simgeleri: çizgi (outline) SVG, açık mor karo içinde. Anahtarlar tasarlayıcıda seçilebilen simge adlarıdır (eskiden emoji). */
const glyphs: Record<string, (size: number) => ReactNode> = {
  file: (s) => <IconFile size={s} />, calendar: (s) => <IconCalendar size={s} />, wallet: (s) => <IconWallet size={s} />, receipt: (s) => <IconReceipt size={s} />,
  plane: (s) => <IconPlane size={s} />, briefcase: (s) => <IconBriefcase size={s} />, users: (s) => <IconUsers size={s} />, clock: (s) => <IconClock size={s} />,
  check: (s) => <IconCheckCircle size={s} />, shield: (s) => <IconShield size={s} />, car: (s) => <IconCar size={s} />, home: (s) => <IconHome size={s} />,
};
export const appIconKeys = Object.keys(glyphs);

export function AppIcon({ name, size = 22 }: { name: string; size?: number }) {
  return <span className="app-icon" aria-hidden="true">{(glyphs[name] ?? glyphs.file)(size)}</span>;
}
