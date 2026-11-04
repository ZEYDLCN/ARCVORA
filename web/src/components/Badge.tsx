import type { Labeled } from '../lib/model';

export function Badge({ info, title }: { info: Labeled; title?: string }) {
  return (
    <span className={`badge ${info.tone}`} title={title}>
      <span aria-hidden="true">{info.glyph}</span>
      {info.label}
    </span>
  );
}
