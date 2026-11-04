import type { ReactNode, SVGProps } from 'react';

/** Çizgi (outline) simgeler: 24px ızgara, 1.8 çizgi kalınlığı, currentColor. */
function Icon({ children, size = 20, ...rest }: { children: ReactNode; size?: number } & SVGProps<SVGSVGElement>) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8}
      strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false" {...rest}>
      {children}
    </svg>
  );
}

type P = { size?: number };

export const IconHome = (p: P) => <Icon {...p}><path d="M4 11 12 4l8 7M6 10v9h12v-9M10 19v-5h4v5" /></Icon>;
export const IconSearch = (p: P) => <Icon {...p}><circle cx="11" cy="11" r="6.5" /><path d="m20 20-4.2-4.2" /></Icon>;
export const IconFolder = (p: P) => <Icon {...p}><path d="M3.5 7.5a2 2 0 0 1 2-2h4l2 2.5h7a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z" /></Icon>;
export const IconDiagram = (p: P) => <Icon {...p}><rect x="9" y="3" width="6" height="4" rx="1.2" /><rect x="3" y="14" width="6" height="4" rx="1.2" /><rect x="15" y="14" width="6" height="4" rx="1.2" /><path d="M12 7v3M6 14v-2.5h12V14" /></Icon>;
export const IconChecklist = (p: P) => <Icon {...p}><path d="M10 6.5h10M10 12h10M10 17.5h10" /><path d="m3.5 6.3 1.3 1.3 2.4-2.6M3.5 11.8l1.3 1.3 2.4-2.6M3.5 17.3l1.3 1.3 2.4-2.6" /></Icon>;
export const IconDecision = (p: P) => <Icon {...p}><path d="M12 3v18M5 6h10.5l3 2.5-3 2.5H5zM19 13.5H8.5l-3 2.5 3 2.5H19z" /></Icon>;
export const IconHelp = (p: P) => <Icon {...p}><circle cx="12" cy="12" r="9" /><path d="M9.6 9.6a2.5 2.5 0 1 1 3.5 2.3c-.7.4-1.1.9-1.1 1.7" /><circle cx="12" cy="16.8" r=".6" fill="currentColor" /></Icon>;
export const IconFile = (p: P) => <Icon {...p}><path d="M7 3h7l5 5v13H7z" /><path d="M14 3v5h5M10 13h6M10 17h6" /></Icon>;
export const IconShield = (p: P) => <Icon {...p}><path d="M12 3 5 6v5.5c0 4.4 3 7.8 7 9.5 4-1.7 7-5.1 7-9.5V6z" /><path d="m9 12 2 2 4-4" /></Icon>;
export const IconInbox = (p: P) => <Icon {...p}><path d="M4 13 6.5 6h11L20 13v6H4z" /><path d="M4 13h5l1 2h4l1-2h5" /></Icon>;
export const IconPlus = (p: P) => <Icon {...p}><path d="M12 5v14M5 12h14" /></Icon>;
export const IconDownload = (p: P) => <Icon {...p}><path d="M12 4v11M7.5 11 12 15.5 16.5 11M5 19.5h14" /></Icon>;
export const IconSun = (p: P) => <Icon {...p}><circle cx="12" cy="12" r="4" /><path d="M12 3v2M12 19v2M3 12h2M19 12h2M5.6 5.6 7 7M17 17l1.4 1.4M18.4 5.6 17 7M7 17l-1.4 1.4" /></Icon>;
export const IconMoon = (p: P) => <Icon {...p}><path d="M20 14.5A8 8 0 0 1 9.5 4 8 8 0 1 0 20 14.5z" /></Icon>;
export const IconCollapse = (p: P) => <Icon {...p}><rect x="3" y="4" width="18" height="16" rx="4" /><path d="M10 4v16M16.5 10l-2 2 2 2" /></Icon>;
export const IconExpand = (p: P) => <Icon {...p}><rect x="3" y="4" width="18" height="16" rx="4" /><path d="M10 4v16M14 10l2 2-2 2" /></Icon>;
export const IconLogout = (p: P) => <Icon {...p}><path d="M10 4H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h4M15 8l4 4-4 4M19 12H9" /></Icon>;
export const IconMenu = (p: P) => <Icon {...p}><path d="M4 7h16M4 12h16M4 17h16" /></Icon>;
export const IconPackage = (p: P) => <Icon {...p}><path d="M12 3 4 7v10l8 4 8-4V7z" /><path d="m4 7 8 4 8-4M12 11v10" /></Icon>;
export const IconSparkle = (p: P) => <Icon {...p}><path d="m12 3 1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z" /><path d="m19 16 .7 1.8 1.8.7-1.8.7L19 21l-.7-1.8-1.8-.7 1.8-.7z" /></Icon>;
export const IconClose = (p: P) => <Icon {...p}><path d="M6 6l12 12M18 6 6 18" /></Icon>;
export const IconBack = (p: P) => <Icon {...p}><path d="m14.5 6-6 6 6 6" /></Icon>;
export const IconCommand = (p: P) => <Icon {...p}><path d="M9 6a3 3 0 1 0-3 3h12a3 3 0 1 0-3-3v12a3 3 0 1 0 3-3H6a3 3 0 1 0 3 3z" /></Icon>;

// Bileşen türü simgeleri (tuvalde marka logosu bulunamayınca kullanılır)
export const KindIcons: Record<string, (p: P) => ReactNode> = {
  client: (p) => <Icon {...p}><rect x="3" y="4" width="18" height="12" rx="2" /><path d="M8 20h8M12 16v4" /></Icon>,
  api: (p) => <Icon {...p}><path d="M9 3v5M15 3v5M7 8h10v3a5 5 0 0 1-10 0zM12 16v5" /></Icon>,
  module: (p) => <Icon {...p}><path d="M12 3 4 7.5v9L12 21l8-4.5v-9z" /><path d="m4 7.5 8 4.5 8-4.5M12 12v9" /></Icon>,
  worker: (p) => <Icon {...p}><circle cx="12" cy="12" r="3" /><path d="M12 3v2.5M12 18.5V21M3 12h2.5M18.5 12H21M5.6 5.6l1.8 1.8M16.6 16.6l1.8 1.8M18.4 5.6l-1.8 1.8M7.4 16.6l-1.8 1.8" /></Icon>,
  database: (p) => <Icon {...p}><ellipse cx="12" cy="6" rx="7" ry="3" /><path d="M5 6v12c0 1.7 3.1 3 7 3s7-1.3 7-3V6M5 12c0 1.7 3.1 3 7 3s7-1.3 7-3" /></Icon>,
  external_service: (p) => <Icon {...p}><path d="M7 18a4 4 0 0 1-.5-8A5.5 5.5 0 0 1 17 8.5 4 4 0 0 1 17 18z" /></Icon>,
  queue: (p) => <Icon {...p}><path d="M4 7h16M4 12h16M4 17h10" /></Icon>,
  storage: (p) => <Icon {...p}><path d="M5 7h14l-1.5 12h-11z" /><path d="M5 7c0-1.7 3.1-3 7-3s7 1.3 7 3" /></Icon>,
};
