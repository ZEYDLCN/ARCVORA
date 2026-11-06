import { useEffect, useState } from 'react';
import { api } from '../../api';
import { go } from '../../components/AppsShell';
import { IconChart, IconCheckCircle, IconClock, IconFile, IconLayers, IconMinusCircle, IconUsers, IconXCircle } from '../../components/icons';
import { CardTitle, EmptyState, StatTile } from '../../components/ui';
import { formatDate } from '../../lib/flow';
import { requestStatusLabel } from '../../lib/requestText';
import type { Report } from '../../types';
import { errorText } from './common';

const iso = (offset = 0) => { const d = new Date(); d.setDate(d.getDate() + offset); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; };
const hours = (h?: number) => (h === undefined ? '—' : h >= 48 ? `${(h / 24).toFixed(1).replace('.', ',')} gün` : `${h.toFixed(1).replace('.', ',')} sa`);

/** Özet rapor: durum dağılımı, uygulama bazında süreler, onaylayıcı yükü ve geciken görevler (master admin). */
export function Reports({ ws }: { ws: string }) {
  const [from, setFrom] = useState(iso(-29));
  const [to, setTo] = useState(iso());
  const [overdue, setOverdue] = useState('48');
  const [demo, setDemo] = useState(false);
  const [r, setR] = useState<Report | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setR(null);
    api.report(ws, { from, to, overdueHours: overdue, includeDemo: demo }).then((x) => { setR(x); setError(null); }).catch((e) => setError(errorText(e, 'Rapor yüklenemedi.')));
  }, [ws, from, to, overdue, demo]);

  const maxDay = Math.max(1, ...(r?.daily.map((d) => d.count) ?? [1]));
  return (
    <div className="cards" style={{ maxWidth: 960 }}>
      <div className="card">
        <div className="mini-grid">
          <div><label htmlFor="rp-from">Başlangıç</label><input id="rp-from" type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} /></div>
          <div><label htmlFor="rp-to">Bitiş (dahil)</label><input id="rp-to" type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} /></div>
          <div><label htmlFor="rp-od">Geciken görev eşiği (saat)</label><input id="rp-od" type="number" min={0} max={8760} value={overdue} onChange={(e) => setOverdue(e.target.value)} /></div>
        </div>
        <div className="check-row" style={{ marginTop: 8 }}>
          <input id="rp-demo" type="checkbox" checked={demo} onChange={(e) => setDemo(e.target.checked)} />
          <label htmlFor="rp-demo" style={{ margin: 0 }}>Demo verisini dahil et</label>
        </div>
      </div>
      {error && <div className="notice error" role="alert">{error}</div>}
      {!r && !error && <p className="muted">Yükleniyor…</p>}
      {r && (
        <>
          <div className="card">
            <CardTitle icon={<IconChart size={19} />}><b>Özet</b> <span className="muted" style={{ fontWeight: 500 }}>· {formatDate(r.from)} – {formatDate(r.to)}</span></CardTitle>
            <div className="stat-grid wide">
              <StatTile icon={<IconLayers size={17} />} label="Toplam" value={r.total} />
              <StatTile icon={<IconClock size={17} />} label={requestStatusLabel.in_review} value={r.byStatus.in_review ?? 0} tone="warn" />
              <StatTile icon={<IconCheckCircle size={17} />} label={requestStatusLabel.approved} value={r.byStatus.approved ?? 0} tone="ok" />
              <StatTile icon={<IconXCircle size={17} />} label={requestStatusLabel.rejected} value={r.byStatus.rejected ?? 0} tone="bad" />
              <StatTile icon={<IconMinusCircle size={17} />} label={requestStatusLabel.cancelled} value={r.byStatus.cancelled ?? 0} tone="neutral" />
            </div>
            {r.daily.length > 0 && (
              <div className="bars" role="img" aria-label="Günlük talep sayısı">
                {r.daily.map((d) => <span key={d.day} className="bar" title={`${formatDate(d.day)}: ${d.count}`} style={{ height: `${Math.max(6, (d.count / maxDay) * 60)}px` }} />)}
              </div>
            )}
          </div>

          <div className="card">
            <CardTitle icon={<IconFile size={19} />}>Uygulamalar</CardTitle>
            {r.apps.length === 0 && <EmptyState icon={<IconFile size={34} />} title="Bu dönemde talep yok." />}
            {r.apps.length > 0 && <div className="table-wrap"><table>
              <thead><tr><th>Uygulama</th><th>Toplam</th><th>İncelemede</th><th>Onaylı</th><th>Reddedilen</th><th>İptal</th><th>Ort. süre</th><th>Medyan</th></tr></thead>
              <tbody>{r.apps.map((a) => <tr key={a.appId}><td>{a.name}</td><td>{a.total}</td><td>{a.inReview}</td><td>{a.approved}</td><td>{a.rejected}</td><td>{a.cancelled}</td><td>{hours(a.avgHours)}</td><td>{hours(a.medianHours)}</td></tr>)}</tbody>
            </table></div>}
          </div>

          <div className="card">
            <CardTitle icon={<IconUsers size={19} />}>Onaylayıcı yükü</CardTitle>
            {r.approvers.length === 0 && <EmptyState icon={<IconUsers size={34} />} title="Kayıt yok." />}
            {r.approvers.length > 0 && <div className="table-wrap"><table>
              <thead><tr><th>Kişi</th><th>Bekleyen</th><th>En eski bekleyen</th><th>Karar verilen</th></tr></thead>
              <tbody>{r.approvers.map((a) => <tr key={a.userId}><td>{a.name}</td><td>{a.pending}</td><td>{hours(a.oldestPendingHours)}</td><td>{a.decided}</td></tr>)}</tbody>
            </table></div>}
          </div>

          <div className="card">
            <CardTitle icon={<IconClock size={19} />} aside={<>en eski 20, {overdue} saatten uzun</>}>Geciken görevler</CardTitle>
            {r.overdue.length === 0 && <EmptyState icon={<IconCheckCircle size={34} />} title="Geciken görev yok." />}
            <ul className="list">
              {r.overdue.map((o) => (
                <li key={`${o.requestId}-${o.step}-${o.assignee}`}><button className="item" onClick={() => go({ name: 'request', id: o.requestId })}>
                  <span className="title">{o.number} · {o.appName}</span>
                  <span className="muted">{o.step} · {o.assignee} · {hours(o.ageHours)} bekliyor</span>
                </button></li>
              ))}
            </ul>
          </div>
        </>
      )}
    </div>
  );
}
