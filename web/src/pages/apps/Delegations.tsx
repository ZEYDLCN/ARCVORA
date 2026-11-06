import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api';
import { formatDate } from '../../lib/flow';
import type { Delegation, DelegationStatus, DirectoryEntry } from '../../types';
import { errorText, type Ctx } from './common';

const statusLabel: Record<DelegationStatus, string> = { active: 'Yürürlükte', upcoming: 'Başlayacak', expired: 'Süresi doldu', revoked: 'İptal edildi' };
const statusTone: Record<DelegationStatus, string> = { active: 'ok', upcoming: 'info', expired: 'neutral', revoked: 'neutral' };

const isoToday = (offset = 0) => {
  const d = new Date();
  d.setDate(d.getDate() + offset);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};

/** Vekâlet: izindeyken onay görevlerinizi belirli günler arasında bir arkadaşınızın yapmasına izin verin. Geçişsizdir; istediğiniz an iptal edebilirsiniz. */
export function Delegations({ user, workspace }: Ctx) {
  const isAdmin = workspace.role === 'admin';
  const [all, setAll] = useState(false);
  const [rows, setRows] = useState<Delegation[] | null>(null);
  const [people, setPeople] = useState<DirectoryEntry[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({ from: '', to: '', startsOn: isoToday(), endsOn: isoToday(7), reason: '' });

  const load = useCallback(() => {
    api.delegations(workspace.id, all ? 'all' : 'mine').then((r) => { setRows(r); setError(null); }).catch((e) => setError(errorText(e, 'Vekâletler yüklenemedi.')));
  }, [workspace.id, all]);
  useEffect(load, [load]);
  useEffect(() => { api.directory(workspace.id).then(setPeople).catch(() => setPeople([])); }, [workspace.id]);

  const create = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.createDelegation(workspace.id, { toUserId: form.to, startsOn: form.startsOn, endsOn: form.endsOn, reason: form.reason.trim() || undefined, ...(form.from ? { fromUserId: form.from } : {}) });
      setForm((f) => ({ ...f, to: '', reason: '' }));
      load();
    } catch (err) {
      const lines = err instanceof Error && 'detailLines' in err ? (err as { detailLines: string[] }).detailLines : [];
      setError([errorText(err), ...lines].join(' '));
    } finally { setBusy(false); }
  };

  const revoke = async (d: Delegation) => {
    if (!window.confirm('Bu vekâleti iptal etmek istediğinize emin misiniz? Vekilin yetkisi hemen sona erer.')) return;
    setBusy(true);
    try { await api.revokeDelegation(workspace.id, d.id); load(); } catch (err) { setError(errorText(err)); } finally { setBusy(false); }
  };

  const others = people.filter((p) => p.id !== (form.from || user.id));
  return (
    <>
      <div className="page-head"><h1>Vekâletlerim</h1>
        <p className="tagline">İzinde veya ulaşılamazken onay görevlerinizi belirli günler arasında bir iş arkadaşınızın görmesine ve karara bağlamasına izin verin.</p></div>
      <div className="page-body">
        <form className="card" style={{ maxWidth: 720 }} onSubmit={create}>
          <h3>Yeni vekâlet</h3>
          {isAdmin && (
            <div className="field"><label htmlFor="dg-from">Adına <span className="muted">(boş bırakırsanız kendi adınıza)</span></label>
              <select id="dg-from" value={form.from} onChange={(e) => setForm({ ...form, from: e.target.value, to: '' })}>
                <option value="">Kendi adıma</option>
                {people.filter((p) => p.id !== user.id).map((p) => <option key={p.id} value={p.id}>{p.displayName}</option>)}
              </select></div>
          )}
          <div className="field"><label htmlFor="dg-to">Vekil</label>
            <select id="dg-to" value={form.to} onChange={(e) => setForm({ ...form, to: e.target.value })}>
              <option value="">Seçin…</option>
              {others.map((p) => <option key={p.id} value={p.id}>{p.displayName}</option>)}
            </select></div>
          <div className="mini-grid">
            <div><label htmlFor="dg-start">Başlangıç</label><input id="dg-start" type="date" value={form.startsOn} min={isoToday()} onChange={(e) => setForm({ ...form, startsOn: e.target.value })} /></div>
            <div><label htmlFor="dg-end">Bitiş (dahil)</label><input id="dg-end" type="date" value={form.endsOn} min={form.startsOn} onChange={(e) => setForm({ ...form, endsOn: e.target.value })} /></div>
          </div>
          <div className="field"><label htmlFor="dg-reason">Açıklama <span className="muted">(isteğe bağlı)</span></label>
            <input id="dg-reason" type="text" maxLength={200} value={form.reason} onChange={(e) => setForm({ ...form, reason: e.target.value })} /></div>
          <p><button className="btn primary" disabled={busy || !form.to || !form.startsOn || !form.endsOn}>Vekâlet ver</button></p>
        </form>

        {error && <div className="notice error" role="alert" style={{ marginTop: 12 }}>{error}</div>}

        <div className="card" style={{ maxWidth: 860, marginTop: 14 }}>
          <div className="row" style={{ justifyContent: 'space-between' }}>
            <h3 style={{ margin: 0 }}>{all ? 'Tüm vekâletler' : 'Verdiğim ve bana verilen vekâletler'}</h3>
            {isAdmin && <button className="btn small" onClick={() => setAll((v) => !v)}>{all ? 'Yalnızca benimkiler' : 'Tümünü göster'}</button>}
          </div>
          {rows === null && !error && <p className="muted">Yükleniyor…</p>}
          {rows?.length === 0 && <p className="muted">Vekâlet yok.</p>}
          <ul className="list" style={{ marginTop: 8 }}>
            {rows?.map((d) => (
              <li key={d.id} className="row" style={{ gap: 10, alignItems: 'center' }}>
                <span style={{ flex: 1, minWidth: 0 }}>
                  <b>{d.fromName}</b> → <b>{d.toName}</b>
                  <span className="muted" style={{ display: 'block' }}>{formatDate(d.startsOn)} – {formatDate(d.endsOn)}{d.reason ? ` · ${d.reason}` : ''}</span>
                </span>
                <span className={`badge ${statusTone[d.status]}`}>{statusLabel[d.status]}</span>
                {(d.status === 'active' || d.status === 'upcoming') && (
                  <button className="btn small danger" disabled={busy} onClick={() => void revoke(d)}>{d.toUserId === user.id ? 'Geri ver' : 'İptal et'}</button>
                )}
              </li>
            ))}
          </ul>
        </div>
      </div>
    </>
  );
}
