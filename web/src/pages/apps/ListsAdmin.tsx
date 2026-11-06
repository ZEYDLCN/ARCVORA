import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api';
import type { ListDetail, ListSummary } from '../../types';
import { errorText } from './common';

interface Row { id?: string; value: string; active: boolean }

/** Yönetilen listeler (ana veri): birim, şehir, masraf kalemi gibi seçenekleri tek yerden yönetin; formlardaki seçim alanları bir listeye bağlanabilir. */
export function ListsAdmin({ ws }: { ws: string }) {
  const [lists, setLists] = useState<ListSummary[] | null>(null);
  const [open, setOpen] = useState<ListDetail | null>(null);
  const [rows, setRows] = useState<Row[]>([]);
  const [meta, setMeta] = useState({ name: '', description: '' });
  const [fresh, setFresh] = useState({ name: '', items: '' });
  const [bulk, setBulk] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => { api.lists(ws).then(setLists).catch((e) => setError(errorText(e, 'Listeler yüklenemedi.'))); }, [ws]);
  useEffect(load, [load]);

  const show = (d: ListDetail) => { setOpen(d); setRows(d.items.map((i) => ({ id: i.id, value: i.value, active: i.active }))); setMeta({ name: d.name, description: d.description ?? '' }); };
  const run = async (fn: () => Promise<void>, ok?: string) => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try { await fn(); if (ok) setNotice(ok); } catch (e) { setError(errorText(e)); } finally { setBusy(false); load(); }
  };
  const lines = (t: string) => t.split('\n').map((x) => x.trim()).filter(Boolean);

  return (
    <div className="cards" style={{ maxWidth: 900 }}>
      {error && <div className="notice error" role="alert">{error}</div>}
      {notice && <div className="notice" role="status">{notice}</div>}

      <form className="card" onSubmit={(e) => { e.preventDefault(); void run(async () => { show(await api.createList(ws, { name: fresh.name, items: lines(fresh.items) })); setFresh({ name: '', items: '' }); }, 'Liste oluşturuldu.'); }}>
        <h3>Yeni liste</h3>
        <div className="mini-grid">
          <div><label htmlFor="ln-name">Ad</label><input id="ln-name" type="text" required maxLength={100} placeholder="Birimler" value={fresh.name} onChange={(e) => setFresh({ ...fresh, name: e.target.value })} /></div>
        </div>
        <div className="field"><label htmlFor="ln-items">Öğeler <span className="muted">(her satıra bir tane; sonra da ekleyebilirsiniz)</span></label>
          <textarea id="ln-items" rows={4} value={fresh.items} onChange={(e) => setFresh({ ...fresh, items: e.target.value })} /></div>
        <p><button className="btn primary" disabled={busy || fresh.name.trim() === ''}>Listeyi oluştur</button></p>
      </form>

      <div className="card">
        <h3>Listeler</h3>
        {lists === null && !error && <p className="muted">Yükleniyor…</p>}
        {lists?.length === 0 && <p className="muted">Henüz liste yok.</p>}
        <ul className="list">
          {lists?.map((l) => (
            <li key={l.id}>
              <button className="item" onClick={() => void run(async () => show(await api.list(ws, l.id)))}>
                <span className="title">{l.name}</span>
                <span className="muted">{l.activeCount} aktif öğe{l.itemCount !== l.activeCount ? ` · ${l.itemCount - l.activeCount} pasif` : ''}</span>
              </button>
            </li>
          ))}
        </ul>
      </div>

      {open && (
        <div className="card" data-testid="list-editor">
          <div className="row" style={{ justifyContent: 'space-between' }}>
            <h3 style={{ margin: 0 }}>{open.name}</h3>
            <button className="btn small" onClick={() => setOpen(null)}>Kapat</button>
          </div>
          <div className="mini-grid" style={{ marginTop: 8 }}>
            <div><label htmlFor="le-name">Ad</label><input id="le-name" type="text" maxLength={100} value={meta.name} onChange={(e) => setMeta({ ...meta, name: e.target.value })} /></div>
            <div><label htmlFor="le-desc">Açıklama</label><input id="le-desc" type="text" maxLength={500} value={meta.description} onChange={(e) => setMeta({ ...meta, description: e.target.value })} /></div>
          </div>
          <p><button className="btn small" disabled={busy || meta.name.trim() === ''} onClick={() => void run(async () => show(await api.updateList(ws, open.id, { name: meta.name, description: meta.description })), 'Ad ve açıklama kaydedildi.')}>Adı/açıklamayı kaydet</button></p>

          <h4>Öğeler</h4>
          {rows.length === 0 && <p className="muted">Öğe yok.</p>}
          {rows.map((r, i) => (
            <div key={r.id ?? `new-${i}`} className="row" style={{ gap: 6, marginBottom: 4 }}>
              <input aria-label={`Öğe ${i + 1}`} type="text" maxLength={100} style={{ flex: 1 }} value={r.value} onChange={(e) => setRows(rows.map((x, k) => (k === i ? { ...x, value: e.target.value } : x)))} />
              <label className="check-row" style={{ margin: 0 }}><input type="checkbox" aria-label={`Öğe ${i + 1} aktif`} checked={r.active} onChange={(e) => setRows(rows.map((x, k) => (k === i ? { ...x, active: e.target.checked } : x)))} /> Aktif</label>
              <button className="icon-btn" aria-label={`Öğe ${i + 1} yukarı`} disabled={i === 0} onClick={() => { const c = [...rows]; [c[i - 1], c[i]] = [c[i], c[i - 1]]; setRows(c); }}>↑</button>
              <button className="icon-btn" aria-label={`Öğe ${i + 1} aşağı`} disabled={i === rows.length - 1} onClick={() => { const c = [...rows]; [c[i + 1], c[i]] = [c[i], c[i + 1]]; setRows(c); }}>↓</button>
              <button className="btn small danger" aria-label={`Öğe ${i + 1} sil`} onClick={() => setRows(rows.filter((_, k) => k !== i))}>Sil</button>
            </div>
          ))}
          <div className="field" style={{ marginTop: 8 }}><label htmlFor="le-bulk">Toplu ekle <span className="muted">(her satıra bir tane)</span></label>
            <textarea id="le-bulk" rows={3} value={bulk} onChange={(e) => setBulk(e.target.value)} /></div>
          <p className="row">
            <button className="btn" disabled={bulk.trim() === ''} onClick={() => { setRows([...rows, ...lines(bulk).map((value) => ({ value, active: true }))]); setBulk(''); }}>Satırları ekle</button>
            <button className="btn primary" disabled={busy} onClick={() => void run(async () => show(await api.setListItems(ws, open.id, rows.map((r) => ({ ...(r.id ? { id: r.id } : {}), value: r.value, active: r.active })))), 'Öğeler kaydedildi.')}>Öğeleri kaydet</button>
            <button className="btn danger" disabled={busy} onClick={() => { if (window.confirm(`“${open.name}” listesi silinsin mi?`)) void run(async () => { await api.deleteList(ws, open.id); setOpen(null); }, 'Liste silindi.'); }}>Listeyi sil</button>
          </p>
          <p className="muted">Pasif öğeler yeni taleplerde seçilemez; eski talepler değişmez. Bir uygulama listeyi kullanıyorsa liste silinemez.</p>
        </div>
      )}
    </div>
  );
}
