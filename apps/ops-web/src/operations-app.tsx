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

type HypotheticalTime = {
  contract: 'TRN-TECH-072'; hypotheticalOnly: true;
  officialActivationVerified: false; memberEligibilityVerified: false;
  walletCreditAuthorized: false; nationalReleaseAllowed: false;
  state: 'OfficialActivationMissing' | 'ObservationBeforeActivation' |
    'StillWaiting' | 'NinetyFullDaysElapsed';
  thresholdUtc: string | null;
};
type Preview = { phase: 'NOT_ENTERED' | 'CALCULATING' | 'INVALID' } |
  { phase: 'HYPOTHETICAL_ONLY'; result: HypotheticalTime };
const utcPattern = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/;
const timeLabels: Record<HypotheticalTime['state'], string> = {
  OfficialActivationMissing: 'بدون رویداد فعال‌سازی رسمی، دوره انتظار قابل شروع نیست.',
  ObservationBeforeActivation: 'لحظه بررسی پیش از زمان فرضی فعال‌سازی است؛ محاسبه معتبر نیست.',
  StillWaiting: 'در این سناریوی فرضی هنوز ۹۰ دوره کامل ۲۴ساعته نگذشته است.',
  NinetyFullDaysElapsed: 'فقط شرط زمانیِ فرضی ۹۰ روز کامل شده است؛ هیچ استحقاقی تأیید نمی‌شود.'
};
function validTime(value: unknown): value is HypotheticalTime {
  if (!value || typeof value !== 'object') return false;
  const v = value as Partial<HypotheticalTime>;
  return v.contract === 'TRN-TECH-072' && v.hypotheticalOnly === true &&
    v.officialActivationVerified === false && v.memberEligibilityVerified === false &&
    v.walletCreditAuthorized === false && v.nationalReleaseAllowed === false &&
    typeof v.state === 'string' && Object.hasOwn(timeLabels, v.state) &&
    (v.thresholdUtc === null ||
      (typeof v.thresholdUtc === 'string' && utcPattern.test(v.thresholdUtc)));
}

export function OperationsApp() {
  const [state, setState] = useState<State>({ kind: 'LOADING' });
  const [sequence, setSequence] = useState(0);
  const [filter, setFilter] = useState('ALL');
  const [activationUtc, setActivationUtc] = useState('');
  const [observedUtc, setObservedUtc] = useState('');
  const [preview, setPreview] = useState<Preview>({ phase: 'NOT_ENTERED' });

  async function calculateTime(): Promise<void> {
    if (activationUtc && !utcPattern.test(activationUtc) ||
        !utcPattern.test(observedUtc)) {
      setPreview({ phase: 'INVALID' });
      return;
    }
    setPreview({ phase: 'CALCULATING' });
    try {
      const response = await fetch('/internal/ops/v1/waiting-preview', {
        method: 'POST', credentials: 'omit', cache: 'no-store',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          activationUtc: activationUtc || null, observedUtc
        })
      });
      if (!response.ok) throw Error('INVALID_PREVIEW');
      const data: unknown = await response.json();
      if (!validTime(data)) throw Error('INVALID_TIME_RESULT');
      setPreview({ phase: 'HYPOTHETICAL_ONLY', result: data });
    } catch {
      setPreview({ phase: 'INVALID' });
    }
  }

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
      <section className="panel" aria-labelledby="hypothetical-title">
        <h2 id="hypothetical-title">محاسبه آموزشی دوره انتظار ۹۰ روزه</h2>
        <p>زمان‌ها صرفاً فرضی، بدون نام و بدون ذخیره‌سازی‌اند. تاریخ فعال‌سازی رسمی عضو از این فرم صادر یا تأیید نمی‌شود.</p>
        <p>هر روز دقیقاً ۲۴ ساعت است؛ «سه ماه تقویمی» الزاماً معادل ۹۰ روز کامل نیست.</p>
        <form onSubmit={event => { event.preventDefault(); void calculateTime(); }}>
          <div className="input-grid">
            <label htmlFor="hypothetical-activation">
              زمان فرضی فعال‌سازی (UTC؛ اختیاری)
              <input id="hypothetical-activation" value={activationUtc}
                onChange={e => { setActivationUtc(e.target.value); setPreview({ phase: 'NOT_ENTERED' }); }}
                placeholder="YYYY-MM-DDTHH:mm:ssZ" maxLength={20} autoComplete="off" />
            </label>
            <label htmlFor="hypothetical-observed">
              لحظه فرضی بررسی (UTC؛ لازم)
              <input id="hypothetical-observed" value={observedUtc}
                onChange={e => { setObservedUtc(e.target.value); setPreview({ phase: 'NOT_ENTERED' }); }}
                placeholder="YYYY-MM-DDTHH:mm:ssZ" maxLength={20} autoComplete="off" required />
            </label>
          </div>
          <button type="submit" disabled={preview.phase === 'CALCULATING'}>
            بررسی شرط زمانی فرضی
          </button>
        </form>
        {preview.phase === 'CALCULATING' && <p role="status">در حال محاسبه توسط هسته .NET محلی</p>}
        {preview.phase === 'INVALID' && <p role="status">ورودی یا سرویس محاسبه معتبر نیست؛ نتیجه‌ای تأیید نشده است. تاریخ باید دقیقاً UTC و با Z پایان یابد.</p>}
        {preview.phase === 'HYPOTHETICAL_ONLY' && (
          <div className="time-result" role="status" aria-live="polite">
            <strong>{timeLabels[preview.result.state]}</strong>
            {preview.result.thresholdUtc &&
              <p>مرز زمانی فرضی: <code>{preview.result.thresholdUtc}</code></p>}
            <p>فعال‌سازی رسمی: تأییدنشده | استحقاق عضو: تأییدنشده | اعتبار کیف پول: تأییدنشده</p>
          </div>
        )}
      </section>
      <footer>TRN-TECH-071/072 | فقط آزمایش محلی | بدون ورود، پرداخت، تشخیص، تغییر وضعیت یا اتصال خارجی</footer>
    </main>
  </>;
}
