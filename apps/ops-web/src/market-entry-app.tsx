import { useEffect, useState } from 'react';

type MarketId = 'MARKET-PUBLIC' | 'MARKET-SUPPORT' | 'MARKET-CORPORATE';
type Market = {
  id: MarketId; label: string; description: string;
  thirdPartyPayerContractRequired: boolean;
  status: 'BLOCKED_UNVERIFIED'; missingEvidence: string[];
  ownershipAlwaysIndividual: true; sponsorClinicalAccessAuthorized: false;
  allowedActions: [];
};
type Source = {
  contract: 'TRN-TECH-081'; source: 'PUBLIC_17_DENIAL_INVENTORY';
  scope: 'THREE_MARKETS_AND_NATIONAL_FIRST_RELEASE';
  marketCount: 3; blockerCount: 17; markets: Market[];
  onboardingEnabled: false; memberAuthenticated: false;
  sponsorClinicalAccessAuthorized: false; paymentsEnabled: false;
  providerIntegrationsVerified: false; officialMembershipActivated: false;
  releaseAllowed: false; nationalReleaseAllowed: false;
};
type State = { phase: 'LOADING' | 'UNAVAILABLE' } |
             { phase: 'SOURCE_BLOCKED'; data: Source };

const marketRoutes: {id:MarketId, path:string}[] = [
  {id:'MARKET-PUBLIC',path:'/markets/individuals'},
  {id:'MARKET-SUPPORT',path:'/markets/support'},
  {id:'MARKET-CORPORATE',path:'/markets/corporate'}
];

const actualRoute = window.location.pathname;
const selected = marketRoutes.find(r => r.path === actualRoute)?.id;

function verified(input: unknown): input is Source {
  if (!input || typeof input !== 'object') return false;
  const v = input as Partial<Source>;
  if (v.contract !== 'TRN-TECH-081' ||
      v.source !== 'PUBLIC_17_DENIAL_INVENTORY' ||
      v.scope !== 'THREE_MARKETS_AND_NATIONAL_FIRST_RELEASE' ||
      v.marketCount !== 3 || v.blockerCount !== 17 ||
      v.onboardingEnabled !== false || v.memberAuthenticated !== false ||
      v.sponsorClinicalAccessAuthorized !== false ||
      v.paymentsEnabled !== false || v.providerIntegrationsVerified !== false ||
      v.officialMembershipActivated !== false ||
      v.releaseAllowed !== false || v.nationalReleaseAllowed !== false ||
      !Array.isArray(v.markets) || v.markets.length !== 3) return false;
  const seen = new Set<string>();
  for (const market of v.markets) {
    if (!market || !marketRoutes.some(r => r.id === market.id) ||
        seen.has(market.id) || market.status !== 'BLOCKED_UNVERIFIED' ||
        market.ownershipAlwaysIndividual !== true ||
        market.sponsorClinicalAccessAuthorized !== false ||
        market.thirdPartyPayerContractRequired !== (market.id !== 'MARKET-PUBLIC') ||
        !Array.isArray(market.allowedActions) || market.allowedActions.length !== 0 ||
        !Array.isArray(market.missingEvidence) ||
        market.missingEvidence.length === 0 ||
        !market.missingEvidence.every(c => typeof c === 'string' && /^[A-Z0-9_]+$/.test(c)) ||
        typeof market.label !== 'string' || market.label.trim().length === 0 ||
        typeof market.description !== 'string' || market.description.trim().length === 0)
      return false;
    seen.add(market.id);
  }
  return seen.size === 3;
}

export function MarketEntryApp() {
  const [state, setState] = useState<State>({ phase: 'LOADING' });
  const [revision, setRevision] = useState(0);
  const [payerMode, setPayerMode] = useState<'INDIVIDUAL' | 'THIRD_PARTY'>('INDIVIDUAL');
  useEffect(() => {
    const controller = new AbortController();
    setState({phase:'LOADING'});
    async function load() {
      try {
        const response = await fetch('/internal/ops/v1/market-entry', {
          method:'GET', credentials:'omit', cache:'no-store', signal:controller.signal
        });
        if (!response.ok) throw Error('SOURCE_UNAVAILABLE');
        const value:unknown = await response.json();
        if (!verified(value)) throw Error('SOURCE_INVALID');
        if (!controller.signal.aborted) setState({phase:'SOURCE_BLOCKED',data:value});
      } catch {
        if (!controller.signal.aborted) setState({phase:'UNAVAILABLE'});
      }
    }
    void load();
    return () => controller.abort();
  },[revision]);

  const source = state.phase === 'SOURCE_BLOCKED' ? state.data : null;
  const active = selected && source?.markets.find(m => m.id === selected);
  return <>
    <a className="skip" href="#market-main">پرش به محتوای اصلی</a>
    <header className="market-hero">
      <p>ترنم · راهنمای سه بازار در محیط توسعه</p>
      <h1>مسیرهای مشارکت افراد، نهادهای حمایتی و سازمان‌ها</h1>
      <p>اطلاعات برنامه‌ریزی مصوب؛ هیچ خدمت، ثبت‌نام، قرارداد یا پرداخت واقعی فعال نشده است.</p>
    </header>
    <main id="market-main">
      <section className="notice" role="note">
        <strong>انتشار رسمی جامع و کشوری: DENIED_BY_DEFAULT</strong>
        <p>این درگاه فقط روی سرویس محلی توسعه اجرا می‌شود. اطلاعات زیر دعوت به ثبت‌نام یا پیشنهاد خدمت فعال نیست.</p>
      </section>
      <section className="panel">
        <div className="heading"><h2>وضعیت سه مسیر از منبع .NET</h2>
          <button type="button" onClick={() => setRevision(n => n + 1)}>بازخوانی وضعیت</button>
        </div>
        <p role="status" aria-live="polite">
          {state.phase === 'LOADING' && 'در حال کنترل منبع رسمی موانع فنی'}
          {state.phase === 'UNAVAILABLE' && 'منبع وضعیت قابل تأیید نیست؛ هیچ مسیر یا خدمتی قابل اعلام نیست.'}
          {source && 'سه بازار برنامه‌ریزی شده‌اند؛ تمامی مسیرها BLOCKED_UNVERIFIED هستند.'}
        </p>
      </section>
      {source && <>
        <aside className="panel" aria-label="محیط‌های نمایشی مستقل">
          <h2>بازدید از محیط‌های وب ترنم در توسعه</h2>
          <p>این پیوندها فقط ساختار رابط را نشان می‌دهند؛ ورود عضو، خدمات مرکز و دسترسی درمانی فعال نیستند.</p>
          <nav className="market-nav" aria-label="محیط‌های توسعه بدون داده واقعی">
            <a href="/member">ورود به پیش‌نمایش عضو بدون حساب واقعی</a>
            <a href="/center">ورود به پیش‌نمایش مرکز بدون پزشک واقعی</a>
          </nav>
        </aside>
        <nav className="market-nav" aria-label="مسیرهای سه بازار">
          <a href="/markets" aria-current={!selected ? 'page' : undefined}>هر سه بازار</a>
          {source.markets.map(m => <a key={m.id}
            href={marketRoutes.find(r => r.id === m.id)!.path}
            aria-current={selected === m.id ? 'page' : undefined}>{m.label}</a>)}
        </nav>

        <section className="panel" aria-labelledby="payer-guide-title" data-testid="payer-guide">
          <h2 id="payer-guide-title">راهنمای انتخاب بازار و تأمین حق اشتراک</h2>
          <p>این راهنما فقط تصمیم‌های مصوب را بر اساس وضعیت واقعیِ مسدودبودن سه بازار توضیح می‌دهد. هیچ قیمت، قرارداد، معافیت، عضویت یا مجوز درمانی از پاسخ استنتاج نمی‌شود.</p>
          <fieldset>
            <legend>پرداخت‌کننده احتمالیِ حق اشتراک</legend>
            <label><input type="radio" name="payer-guide" value="INDIVIDUAL"
              checked={payerMode === 'INDIVIDUAL'}
              onChange={() => setPayerMode('INDIVIDUAL')}/> خود فرد</label>
            <label><input type="radio" name="payer-guide" value="THIRD_PARTY"
              checked={payerMode === 'THIRD_PARTY'}
              onChange={() => setPayerMode('THIRD_PARTY')}/> حامی یا سازمان، صرفاً پس از قرارداد معتبر</label>
          </fieldset>
          <div className="market-grid" aria-label="نتیجه مقایسه سه بازار">
            {source.markets.map(m => {
              const conditional = payerMode === 'THIRD_PARTY';
              const potentiallyRelevant = !conditional || m.thirdPartyPayerContractRequired;
              return <article className="market-tile" key={m.id}
                data-testid={'payer-market-' + m.id}>
                <h3>{m.label}</h3>
                <p>{potentiallyRelevant
                  ? conditional
                    ? 'این مسیر امکان بررسی پرداخت توسط طرف ثالث را فقط با قرارداد معتبرِ آتی پیش‌بینی می‌کند.'
                    : 'پرداخت حق اشتراک توسط خود فرد در دامنه تصمیم‌های مصوب است؛ هیچ پرداختی فعال نیست.'
                  : 'این بازار به‌عنوان مسیر پرداخت شخص ثالث تعریف نشده است.'}</p>
                <p>مالک عضویت و کیف پول درمانی: همیشه شخص عضو.</p>
                <p>حق دسترسی خودکار حامی به پرونده سلامت: وجود ندارد.</p>
                <p>وضعیت منبع: {m.status}</p>
                <p>قرارداد معتبر، پرداخت و فعال‌سازی: تأیید نشده.</p>
                <a href={marketRoutes.find(route => route.id === m.id)!.path}>
                  مطالعه مسیر {m.label}
                </a>
              </article>;
            })}
          </div>
          <p role="note">این مقایسه مجوز پرداخت، صدور فاکتور، دسترسی سلامت یا پیشنهاد تجاری محسوب نمی‌شود. انتخاب بازار به تنهایی هیچ استحقاقی نمی‌سازد.</p>
        </section>
        <section aria-labelledby="market-scope-title">
          <h2 id="market-scope-title">{active ? 'مسیر انتخابی' : 'سه مسیر مصوب در دامنه برنامه'}</h2>
          <div className="market-grid">
            {(active ? [active] : source.markets).map(m => <article key={m.id}
              className="market-tile">
              <h3>{m.label}</h3><p>{m.description}</p>
              <p className="market-denial">وضعیت: {m.status}</p>
              <p>مالک عضویت و کیف پول درمانی، همیشه خود فرد است.</p>
              {m.thirdPartyPayerContractRequired && <p>تأمین حق اشتراک توسط حامی فقط بر اساس قرارداد معتبرِ آتی قابل بررسی است؛ قراردادی تأیید نشده است.</p>}
              <p>حامی هیچ حق خودکاری برای دسترسی به پرونده سلامت یا کیف پول فرد ندارد.</p>
              <small>شواهد هنوز ارائه/راستی‌آزمایی نشده: {m.missingEvidence.join('، ')}</small>
              <p>ثبت‌نام، پرداخت و فعال‌سازی: غیرفعال</p>
            </article>)}
          </div>
        </section>
        <section className="panel" aria-label="دامنه انتشار">
          <p><strong>۱۷/۱۷ الزام انتشار همچنان مسدود است.</strong></p>
          <p>این وضعیت اثبات پوشش خدمات ملی، قرارداد بیمه، مجوز پزشکی یا وجود ارائه‌دهنده نیست.</p>
        </section>
      </>}
      <footer>TRN-TECH-081 | فقط اطلاع‌رسانی محلی | بدون نشست، داده سلامت، تراکنش یا اتصال خارجی</footer>
    </main>
  </>;
}
