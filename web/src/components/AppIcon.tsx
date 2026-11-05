export const appIcons: Record<string, string> = {
  file: '📄', calendar: '📅', wallet: '👛', receipt: '🧾', plane: '✈️', briefcase: '💼', users: '👥', clock: '🕒', check: '✅', shield: '🛡️', car: '🚗', home: '🏠',
};
export const appIconKeys = Object.keys(appIcons);

export function AppIcon({ name }: { name: string }) {
  return <span className="app-icon" aria-hidden="true">{appIcons[name] ?? appIcons.file}</span>;
}
