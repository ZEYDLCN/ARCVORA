import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api';
import { formatDate } from '../../lib/flow';
import type { Holiday, LeaveRow } from '../../types';
import { errorText } from './common';

const num = (n: number) => String(n).replace('.', ',');

/** İzin ve tatiller: resmî tatil takvimi (iş günü hesabı için), yıllık izin hakları/bakiyeleri, kıdem hakkı dağıtımı ve devir (master admin). */
export function LeaveAdmin({ ws }: { ws: string }) {
  const now = new Date().getFullYear();
  const [year, setYear] = useState(now);
  const [holidays, setHolidays] = useState<Holiday[] | null>(null);
  const [rows, setRows] = useState<LeaveRow[] | null>(null);
  const [hol, setHol] = useState({ date: '', name: '', half: false });
  const [entry, setEntry] = useState({ userId: '', kind: 'adjustment', days: '', note: '' });
  const [cap, setCap] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    api.holidays(ws, year).then(setHolidays).catch((e) => setError(errorText(e, 'Tatiller yüklenemedi.')));
    api.leaveBalances(ws, year).then(setRows).catch((e) => setError(errorText(e, 'Bakiyeler yüklenemedi.')));
  }, [ws, year]);
  useEffect(load, [load]);

  const run = async (fn: () => Promise<void>, ok?: string) => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try { await fn(); if (ok) setNotice(ok); } catch (e) { setError(errorText(e)); } finally { setBusy(false); load(); }
  };
  const years = [now - 1, now, now + 1];

  return (
    <div className="cards" style={{ maxWidth: 960 }}>
      <div className="card">
        <div className="row" style={{ gap: 10 }}>
          <label htmlFor="lv-year" style={{ margin: 0 }}>Yıl</label>
          <select id="lv-year" value={year} onChange={(e) => setYear(Number(e.target.value))}>{years.map((y) => <option key={y} value={y}>{y}</option>)}</select>
        </div>
        {error && <div className="notice error" role="alert" style={{ marginTop: 8 }}>{error}</div>}
        {notice && <div className="notice" role="status" style={{ marginTop: 8 }}>{notice}</div>}
      </div>

      <div className="card">
        <h3>Resmî tatil takvimi · {year}</h3>
        <p className="muted">“İş günü” hesabı bu takvimi kullanır. Tarihi sabit tatiller tek tuşla eklenir; dinî bayramların tarihleri her yıl değiştiği için elle girin.</p>
        <p><button className="btn" disabled={busy} onClick={() => void run(async () => { const r = await api.seedHolidays(ws, year); setNotice(`${r.added} tatil eklendi${r.skipped ? `, ${r.skipped} zaten vardı` : ''}.`); })}>Sabit resmî tatilleri ekle</button></p>
        {holidays?.length === 0 && <p className="muted">Bu yıl için tatil tanımlı değil.</p>}
        <ul className="list">
          {holidays?.map((h) => (
            <li key={h.id} className="row" style={{ gap: 10 }}>
              <span style={{ flex: 1 }}><b>{formatDate(h.date)}</b> · {h.name}{h.halfDay && <span className="badge info" style={{ marginLeft: 6 }}>yarım gün</span>}</span>
              <button className="btn small danger" disabled={busy} aria-label={`${h.name} tatilini sil`} onClick={() => void run(() => api.deleteHoliday(ws, h.id))}>Sil</button>
            </li>
          ))}
        </ul>
        <form className="inline-form" style={{ marginTop: 10 }} onSubmit={(e) => { e.preventDefault(); void run(async () => { await api.addHoliday(ws, { date: hol.date, name: hol.name, halfDay: hol.half }); setHol({ date: '', name: '', half: false }); }, 'Tatil eklendi.'); }}>
          <div><label htmlFor="hl-date">Tarih</label><input id="hl-date" type="date" required value={hol.date} onChange={(e) => setHol({ ...hol, date: e.target.value })} /></div>
          <div><label htmlFor="hl-name">Ad</label><input id="hl-name" type="text" required maxLength={100} placeholder="Ramazan Bayramı 1. gün" value={hol.name} onChange={(e) => setHol({ ...hol, name: e.target.value })} /></div>
          <label className="check-row" style={{ margin: 0 }}><input type="checkbox" checked={hol.half} onChange={(e) => setHol({ ...hol, half: e.target.checked })} /> Yarım gün</label>
          <button className="btn primary" disabled={busy || !hol.date || !hol.name.trim()}>Tatil ekle</button>
        </form>
      </div>

      <div className="card">
        <h3>İzin bakiyeleri · {year}</h3>
        <p className="muted">
          Kıdem hakkı 4857 sayılı Kanun m.53’e göre işe giriş tarihinden hesaplanır (1–5 yıl: 14, 5–15 yıl: 20, 15+ yıl: 26 gün; yaş/sözleşme istisnaları için düzeltme ekleyin).
          İşe giriş tarihini Kullanıcılar sekmesinden girin.
        </p>
        <p className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
          <button className="btn" disabled={busy} onClick={() => void run(async () => { const r = await api.distributeLeave(ws, year); setNotice(`${r.created} kişiye hak yazıldı; ${r.skippedExisting} kişide zaten vardı, ${r.skippedNoHireDate} kişinin işe giriş tarihi yok, ${r.skippedNoEntitlement} kişinin henüz hakkı doğmadı.`); })}>Kıdem haklarını dağıt</button>
          <span className="row" style={{ gap: 6 }}>
            <label htmlFor="lv-cap" style={{ margin: 0 }}>Devir üst sınırı</label>
            <input id="lv-cap" type="number" min={0} max={366} style={{ width: 80 }} value={cap} onChange={(e) => setCap(e.target.value)} placeholder="sınırsız" />
            <button className="btn" disabled={busy} onClick={() => void run(async () => { const r = await api.carryoverLeave(ws, year, cap === '' ? undefined : Number(cap)); setNotice(`${year + 1} yılına ${r.created} kişi için toplam ${num(r.totalDays)} gün devredildi${r.skippedExisting ? `; ${r.skippedExisting} kişide devir zaten vardı` : ''}.`); })}>{year} bakiyesini {year + 1}’e devret</button>
          </span>
        </p>
        <div className="table-wrap"><table>
          <thead><tr><th>Kişi</th><th>İşe giriş</th><th>Hak</th><th>Devir</th><th>Düzeltme</th><th>Kullanılan</th><th>Kalan</th></tr></thead>
          <tbody>
            {rows?.map((r) => (
              <tr key={r.userId}><td>{r.name}</td><td>{r.hireDate ? formatDate(r.hireDate) : '—'}</td><td>{num(r.entitled)}</td><td>{num(r.carried)}</td><td>{num(r.adjusted)}</td><td>{num(r.used)}</td><td><b>{num(r.available)}</b></td></tr>
            ))}
          </tbody>
        </table></div>

        <form className="inline-form" style={{ marginTop: 12 }} onSubmit={(e) => { e.preventDefault(); void run(async () => { await api.addLeaveEntry(ws, { userId: entry.userId, year, kind: entry.kind, days: Number(entry.days.replace(',', '.')), note: entry.note.trim() || undefined }); setEntry({ ...entry, days: '', note: '' }); }, 'Kayıt eklendi.'); }}>
          <div><label htmlFor="le-user">Kişi</label>
            <select id="le-user" value={entry.userId} onChange={(e) => setEntry({ ...entry, userId: e.target.value })}><option value="">Seçin…</option>{rows?.map((r) => <option key={r.userId} value={r.userId}>{r.name}</option>)}</select></div>
          <div><label htmlFor="le-kind">Tür</label>
            <select id="le-kind" value={entry.kind} onChange={(e) => setEntry({ ...entry, kind: e.target.value })}><option value="entitlement">Hak</option><option value="carryover">Devir</option><option value="adjustment">Düzeltme (+/−)</option></select></div>
          <div><label htmlFor="le-days">Gün</label><input id="le-days" type="text" inputMode="decimal" required placeholder="14" value={entry.days} onChange={(e) => setEntry({ ...entry, days: e.target.value })} /></div>
          <div><label htmlFor="le-note">Not</label><input id="le-note" type="text" maxLength={200} value={entry.note} onChange={(e) => setEntry({ ...entry, note: e.target.value })} /></div>
          <button className="btn primary" disabled={busy || !entry.userId || !entry.days}>Kaydı ekle</button>
        </form>
      </div>
    </div>
  );
}
