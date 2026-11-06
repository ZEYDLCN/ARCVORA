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

type P = { size?: number; style?: React.CSSProperties };

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
export const IconRefresh = (p: P) => <Icon {...p}><path d="M20 11a8 8 0 0 0-14.3-4.6L4 8.5M4 4v4.5h4.5M4 13a8 8 0 0 0 14.3 4.6L20 15.5M20 20v-4.5h-4.5" /></Icon>;
export const IconInfo = (p: P) => <Icon {...p}><circle cx="12" cy="12" r="9" /><path d="M12 11v5" /><circle cx="12" cy="7.8" r=".6" fill="currentColor" /></Icon>;
export const IconCommand = (p: P) => <Icon {...p}><path d="M9 6a3 3 0 1 0-3 3h12a3 3 0 1 0-3-3v12a3 3 0 1 0 3-3H6a3 3 0 1 0 3 3z" /></Icon>;

// ---- Apps arayüzü simgeleri (referans tasarım: çizgi simgeler, alan içi ikonlar, rozetler, istatistik kutuları)
export const IconCalendar = (p: P) => <Icon {...p}><rect x="3.5" y="5" width="17" height="15.5" rx="3" /><path d="M3.5 10h17M8 3v4M16 3v4" /></Icon>;
export const IconClock = (p: P) => <Icon {...p}><circle cx="12" cy="12" r="8.5" /><path d="M12 7.5V12l3 2" /></Icon>;
export const IconCheckCircle = (p: P) => <Icon {...p}><circle cx="12" cy="12" r="8.5" /><path d="m8.3 12.2 2.6 2.6 4.9-5.3" /></Icon>;
export const IconXCircle = (p: P) => <Icon {...p}><circle cx="12" cy="12" r="8.5" /><path d="m9 9 6 6M15 9l-6 6" /></Icon>;
export const IconMinusCircle = (p: P) => <Icon {...p}><circle cx="12" cy="12" r="8.5" /><path d="M8.5 12h7" /></Icon>;
export const IconLayers = (p: P) => <Icon {...p}><path d="m12 3.5 8.5 4.5-8.5 4.5L3.5 8z" /><path d="m3.5 12.2 8.5 4.5 8.5-4.5M3.5 16.4 12 21l8.5-4.6" /></Icon>;
export const IconUser = (p: P) => <Icon {...p}><circle cx="12" cy="8.5" r="3.6" /><path d="M4.8 20c.6-3.6 3.5-5.6 7.2-5.6s6.6 2 7.2 5.6" /></Icon>;
export const IconUsers = (p: P) => <Icon {...p}><circle cx="9" cy="8.8" r="3.2" /><path d="M2.8 19.5c.5-3.2 3-5 6.2-5s5.7 1.8 6.2 5M16 5.8a3 3 0 0 1 0 6M18.6 14.8c1.6.8 2.5 2.3 2.8 4.4" /></Icon>;
export const IconExternal = (p: P) => <Icon {...p}><path d="M14 4h6v6M20 4l-9 9M18 14v4.5a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 4 18.5v-11A1.5 1.5 0 0 1 5.5 6H10" /></Icon>;
export const IconChevronDown = (p: P) => <Icon {...p}><path d="m6.5 9.5 5.5 5.5 5.5-5.5" /></Icon>;
export const IconSliders = (p: P) => <Icon {...p}><path d="M4 7h9M17 7h3M4 17h3M11 17h9" /><circle cx="15" cy="7" r="2" /><circle cx="9" cy="17" r="2" /></Icon>;
export const IconComment = (p: P) => <Icon {...p}><path d="M5 5.5h14a1.5 1.5 0 0 1 1.5 1.5v8.5a1.5 1.5 0 0 1-1.5 1.5h-7l-4.5 3.5V17H5a1.5 1.5 0 0 1-1.5-1.5V7A1.5 1.5 0 0 1 5 5.5z" /></Icon>;
export const IconMail = (p: P) => <Icon {...p}><rect x="3.5" y="5.5" width="17" height="13" rx="2.5" /><path d="m4.5 7.5 7.5 6 7.5-6" /></Icon>;
export const IconChart = (p: P) => <Icon {...p}><path d="M4 20V4M4 20h16M8 16v-4M12.5 16V8M17 16v-6" /></Icon>;
export const IconTrash = (p: P) => <Icon {...p}><path d="M4.5 7h15M9.5 7V4.8h5V7M6.5 7l.8 12.2h9.4L17.5 7M10 11v5M14 11v5" /></Icon>;
export const IconUpload = (p: P) => <Icon {...p}><path d="M12 16V5M7.5 9 12 4.5 16.5 9M5 19.5h14" /></Icon>;
export const IconCamera = (p: P) => <Icon {...p}><path d="M4 8.5A1.5 1.5 0 0 1 5.5 7H8l1.3-2h5.4L16 7h2.5A1.5 1.5 0 0 1 20 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-13A1.5 1.5 0 0 1 4 17.5z" /><circle cx="12" cy="13" r="3.4" /></Icon>;
export const IconPaperclip = (p: P) => <Icon {...p}><path d="m19 11.5-7.2 7.2a4.5 4.5 0 0 1-6.4-6.4l7.6-7.6a3 3 0 0 1 4.3 4.3l-7.6 7.6a1.5 1.5 0 0 1-2.1-2.1l6.9-6.9" /></Icon>;
export const IconKey = (p: P) => <Icon {...p}><circle cx="8" cy="15" r="3.8" /><path d="m10.8 12.2 8.2-8.2M15.5 7.5l2.5 2.5M13.5 9.5l2 2" /></Icon>;
export const IconWallet = (p: P) => <Icon {...p}><path d="M4 8.5A2.5 2.5 0 0 1 6.5 6H18a2 2 0 0 1 2 2v1.5" /><path d="M4 8.5v8A2.5 2.5 0 0 0 6.5 19H18a2 2 0 0 0 2-2v-7.5H6.5A2.5 2.5 0 0 1 4 8.5z" /><circle cx="16" cy="14" r="1.1" fill="currentColor" /></Icon>;
export const IconReceipt = (p: P) => <Icon {...p}><path d="M6 3.5h12v17l-2.5-1.7-1.8 1.7-1.7-1.7-1.7 1.7-1.8-1.7L6 20.5z" /><path d="M9.5 8.5h5M9.5 12h5M9.5 15.5h3" /></Icon>;
export const IconPlane = (p: P) => <Icon {...p}><path d="m3.5 11 17-6.5-5 16-3.6-6.4z" /><path d="m11.9 14.1 8.6-9.6" /></Icon>;
export const IconBriefcase = (p: P) => <Icon {...p}><rect x="3.5" y="7.5" width="17" height="12" rx="2.5" /><path d="M9 7.5V5.8A1.3 1.3 0 0 1 10.3 4.5h3.4A1.3 1.3 0 0 1 15 5.8v1.7M3.5 13h17" /></Icon>;
export const IconCar = (p: P) => <Icon {...p}><path d="M5 16.5v-4.2L7 7.5a1.5 1.5 0 0 1 1.4-1h7.2a1.5 1.5 0 0 1 1.4 1l2 4.8v4.2zM3.5 12.5h17" /><circle cx="8" cy="16.5" r="1.6" /><circle cx="16" cy="16.5" r="1.6" /></Icon>;
export const IconSettings = (p: P) => <Icon {...p}><circle cx="12" cy="12" r="3.2" /><path d="M12 3.5v2.3M12 18.2v2.3M3.5 12h2.3M18.2 12h2.3M6 6l1.6 1.6M16.4 16.4 18 18M18 6l-1.6 1.6M7.6 16.4 6 18" /></Icon>;
export const IconSend = (p: P) => <Icon {...p}><path d="m4 11.5 16-7-6 15.5-3-6.5z" /><path d="m11 13.5 3.2-3.2" /></Icon>;
export const IconSearchPlus = (p: P) => <Icon {...p}><circle cx="11" cy="11" r="6.5" /><path d="m20 20-4.2-4.2M11 8.5v5M8.5 11h5" /></Icon>;
export const IconList = (p: P) => <Icon {...p}><path d="M8.5 6.5H20M8.5 12H20M8.5 17.5H20" /><circle cx="4.6" cy="6.5" r="1" fill="currentColor" /><circle cx="4.6" cy="12" r="1" fill="currentColor" /><circle cx="4.6" cy="17.5" r="1" fill="currentColor" /></Icon>;
export const IconEye = (p: P) => <Icon {...p}><path d="M2.8 12S6 6.5 12 6.5 21.2 12 21.2 12 18 17.5 12 17.5 2.8 12 2.8 12z" /><circle cx="12" cy="12" r="2.8" /></Icon>;

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
