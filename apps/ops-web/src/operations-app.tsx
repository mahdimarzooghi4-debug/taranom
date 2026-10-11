import { useEffect, useState } from 'react';

type Dimension = { id: string; label: string; status: 'BLOCKED_UNVERIFIED'; missingEvidence: string[] };
type Snapshot = {
  contract: 'TRN-TECH-071'; runtime: 'LOOPBACK_PROCESS_ALIVE';
  inventory: 'SOURCE_BLOCKED_UNVERIFIED'; releaseAllowed: false;
  verifiedIntegrations: false; dimensions: Dimension[];
};
type State = { kind: 'LOADING' | 'UNAVAILABLE' } | { kind: 'LOOPBACK_ONLY'; data: Snapshot };
const ids = [...Array.from({ length: 13 }, (_, n) => 'TRN-EP-' + String(n + 1).padStart(2, '0')),
  'MARKET-PUBLIC', 'MARKET-SUPPORT', 'MARKET-CORPORATE', 'NATIONAL-COVERAGE'];

function valid(value: unknown): value is Snapshot {
  if (!value || typeof value !== 'object') return false;
  const s = value as Partial<Snapshot>;
  if (s.contract !== 'TRN-TECH-071' || s.runtime !== 'LOOPBACK_PROCESS_ALIVE' ||
      s.inventory !== 'SOURCE_BLOCKED_UNVERIFIED' || s.releaseAllowed !== false ||
      s.verifiedIntegrations !== false || !Array.isArray(s.dimensions) ||
      s.dimensions.length !== 17) return false;
  const seen = new Set<string>();
  for (const d of s.dimensions) {
    if (!d || !ids.includes(d.id) || seen.has(d.id) ||
        typeof d.label !== 'string' || !d.label.trim() ||
        d.status !== 'BLOCKED_UNVERIFIED' || !Array.isArray(d.missingEvidence) ||
        !d.missingEvidence.length || !d.missingEvidence.every(x => typeof x === 'string' && !!x)) return false;
    seen.add(d.id);
  }
  return seen.size === 17;
}
export function OperationsApp() {
  const [state, setState] = useState<State>({ kind: 'LOADING' });
  const [sequence, setSequence] = useState(0);
  const [filter, setFilter] = useState('ALL');
  useEffect(() => {
    const ac = new AbortController();
    setState({ kind: 'LOADING' });
    async function read() {
      try {
        const response = await fetch('/internal/ops/v1/status', {
          method: 'GET', credentials: 'omit', cache: 'no-store', signal: ac.signal
        });
        if (!response.ok) throw Error('DENY');
        const data: unknown = await response.json();
        if (!valid(data)) throw Error('UNVERIFIED');
        if (!ac.signal.aborted) setState({ kind: 'LOOPBACK_ONLY', data });
      } catch {
        if (!ac.signal.aborted) setState({ kind: 'UNAVAILABLE' });
      }
    }
    void read();
    return () => ac.abort();
  }, [sequence]);
  const connected = state.kind === 'LOOPBACK_ONLY';
  const items = connected ? state.data.dimensions : [];
  const filtered = items.filter(d => filter === 'ALL' ||
    (filter === 'EPIC' && d.id.startsWith('TRN-EP-')) ||
    (filter === 'MARKET' && d.id.startsWith('MARKET-')) ||
    (filter === 'NATIONAL' && d.id === 'NATIONAL-COVERAGE'));
  return <>
    <a className="skip" href="#content">پرش به محتوای اصلی</a>
    <header><p>ترنم | عملیات فنی توسعه</p><h1>اتصال واقعی محلی، بدون ادعای آمادگی انتشار</h1>
      <p>این صفحه از سرویس .NET محلی پاسخ واقعی HTTP می‌گیرد؛ این کار اثبات احراز هویت، سلامت خدمت یا آمادگی Production نیست.</p></header>
    <main id="content">
      <section className="notice" role="note"><strong>عرضه رسمی ملی سه بازار: DENIED_BY_DEFAULT</strong>
        <p>۱۳ Epic، سه بازار و پوشش ملی مستقل‌اند. هیچ داده عضو، پزشک، کیف پول یا بیمه به این ابزار ارسال نمی‌شود.</p></section>
      <section className="panel"><div className="heading"><h2>اتصال Backend ↔ Frontend</h2>
        <button type="button" onClick={() => setSequence(n => n + 1)}>بازآزمایی اتصال محلی</button></div>
        <p role="status" aria-live="polite">
          {state.kind === 'LOADING' && 'در حال دریافت وضعیت سرویس محلی'}
          {state.kind === 'UNAVAILABLE' && 'اتصال یا شواهد سرویس معتبر نیست؛ هیچ وضعیت موفقی فرض نشده است.'}
          {connected && 'اتصال HTTP به فرایند .NET محلی برقرار است؛ همهٔ ۱۷ بُعد انتشار هنوز مسدود و تأییدنشده‌اند.'}
        </p>
        {connected && <p className="count"><strong>17 / 17</strong> بُعد مسدود انتشار</p>}
      </section>
      {connected && <section className="panel"><div className="heading"><h2>موانع ثبت‌شده در منبع عمومی</h2>
        <label htmlFor="scope">نمایش <select id="scope" value={filter} onChange={e => setFilter(e.target.value)}>
          <option value="ALL">همهٔ ۱۷ بُعد</option>
          <option value="EPIC">۱۳ Epic</option>
          <option value="MARKET">سه بازار</option>
          <option value="NATIONAL">پوشش ملی</option>
        </select></label></div>
        <p role="status">{filtered.length} بُعد با وضعیت BLOCKED_UNVERIFIED</p>
        <ul className="dimensions">{filtered.map(d => <li key={d.id}><strong>{d.id} — {d.label}</strong>
          <span>{d.status}</span><small>شواهد موردنیاز: {d.missingEvidence.join('، ')}</small></li>)}</ul>
      </section>}
      <footer>TRN-TECH-071 | فقط آزمایش محلی | بدون ورود، پرداخت، تشخیص، تغییر وضعیت یا اتصال خارجی</footer>
    </main>
  </>;
}
