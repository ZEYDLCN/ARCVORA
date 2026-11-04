import { logos, type LogoId } from '../lib/logos.generated';
import { logoFill } from '../lib/techLogo';
import { useTheme } from '../theme';

/** Resmi marka logosu (simple-icons, CC0). `surfaceDark` verilmezse tema kullanılır. */
export function TechLogo({ id, size = 20, surfaceDark, title }: { id: LogoId; size?: number; surfaceDark?: boolean; title?: string }) {
  const { dark } = useTheme();
  const logo = logos[id];
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" role="img" aria-label={title ?? logo.title} className="tech-logo">
      <title>{title ?? logo.title}</title>
      <path d={logo.path} fill={logoFill(logo.hex, surfaceDark ?? dark)} />
    </svg>
  );
}

export function TechLogos({ ids, size = 18 }: { ids: LogoId[]; size?: number }) {
  if (ids.length === 0) return null;
  return <span className="tech-logos">{ids.map((id) => <TechLogo key={id} id={id} size={size} />)}</span>;
}
