import { useEffect, useRef, useState, type MouseEvent } from 'react';

type PageKey =
  | 'overview' | 'membership' | 'credit' | 'consents' | 'care'
  | 'records' | 'costs' | 'billing' | 'support';

type MemberPage = {
  key: PageKey;
  path: string;
  label: string;
  subtitle: string;
  owner: string;
  detail: string;
  symbol: string;
};

export const memberPages: readonly MemberPage[] = [
  { key: 'overview', path: '/member', label: 'نمای کلی', subtitle: 'فضای شخصی عضو', owner: 'هویت و عضویت',
    detail: 'برای مشاهده اطلاعات شخصی، ابتدا باید اتصال امن و احراز هویت واقعی برقرار شود.', symbol: '◈' },
  { key: 'membership', path: '/member/membership', label: 'عضویت من', subtitle: 'وضعیت و سوابق عضویت', owner: 'سامانه عضویت',
    detail: 'وضعیت عضویت و دوره انتظار فقط از سرویس معتبر عضویت قابل دریافت است.', symbol: '◇' },
  { key: 'credit', path: '/member/credit', label: 'اعتبار درمان', subtitle: 'کیف پول غیرنقدی عضو', owner: 'هسته اعتبار و مالی',
    detail: 'موجودی و اعتبار درمان تنها پس از اتصال به منبع مالی معتبر نشان داده می‌شوند. هیچ عدد پیش‌فرضی وجود ندارد.', symbol: '◎' },
  { key: 'consents', path: '/member/consents', label: 'رضایت‌ها و حریم خصوصی', subtitle: 'مجوزهای مستقل داده', owner: 'حریم خصوصی و رضایت',
    detail: 'مجوزهای درمان، اشتراک داده و پژوهش هوش مصنوعی مستقل‌اند و بدون شواهد معتبر فعال نمی‌شوند.', symbol: '⊙' },
  { key: 'care', path: '/member/care', label: 'هماهنگی درمان', subtitle: 'پیگیری خدمات سلامت', owner: 'هماهنگی درمان',
    detail: 'ارجاع و خدمات واقعی به اتصال معتبر شبکه ارائه‌دهندگان و تصمیم متخصص مجاز نیاز دارد.', symbol: '✳' },
  { key: 'records', path: '/member/records', label: 'سوابق سلامت', subtitle: 'دسترسی‌های مجاز بالینی', owner: 'سوابق سلامت',
    detail: 'هیچ اطلاعات بالینی تا زمان احراز هویت و دریافت مجوز قانونی نمایش داده نمی‌شود.', symbol: '▤' },
  { key: 'costs', path: '/member/costs', label: 'برآورد هزینه', subtitle: 'آگاهی از هزینه پیش از خدمت', owner: 'هزینه و هماهنگی بیمه',
    detail: 'برآوردهای احتمالی باید دارای منبع و عدم‌قطعیت روشن باشند؛ برآورد، صورتحساب قطعی یا تعهد مالی نیست.', symbol: '≋' },
  { key: 'billing', path: '/member/billing', label: 'صورتحساب‌ها', subtitle: 'اسناد و وضعیت پرداخت', owner: 'صورتحساب و صادرکننده قانونی',
    detail: 'صورتحساب معتبر تنها از صادرکننده قانونی و سرویس متصل دریافت می‌شود.', symbol: '▧' },
  { key: 'support', path: '/member/support', label: 'پشتیبانی و شکایات', subtitle: 'پیگیری درخواست‌ها', owner: 'کیفیت و شکایات',
    detail: 'پیگیری واقعی درخواست و اختلاف بیمه‌ای نیازمند اتصال به مرجع مسئول است.', symbol: '☷' },
] as const;

// No money, quote, insurer response or credit balance is present in this
// disconnected page. All five positions deliberately remain unknown.
const costShares = [
  { key: 'service', label: 'هزینه احتمالی خدمت', owner: 'ارائه‌دهنده واجدصلاحیت و دارای قرارداد معتبر' },
  { key: 'primary', label: 'سهم احتمالی بیمه پایه', owner: 'مرجع قانونی بیمه پایه' },
  { key: 'supplemental', label: 'سهم احتمالی بیمه تکمیلی', owner: 'مرجع قانونی بیمه تکمیلی' },
  { key: 'credit', label: 'سهم احتمالی اعتبار درمان', owner: 'متولی دارای مجوز کیف پول درمانی' },
  { key: 'member', label: 'سهم احتمالی عضو', owner: 'اسناد معتبر نهایی مراجع مربوط' },
] as const;

// Two legally distinct paths share a future member-facing contact point, but
// neither a medical verdict nor an insurer decision can be issued here.
const supportPaths = [
  { key: 'care', label: 'شکایت از خدمت یا حادثه پزشکی',
    authority: 'رسیدگی تخصصی فقط با مرجع پزشکی و حقوقی صاحب‌اختیار است.',
    disclosure: 'مدارک سلامت صرفاً با مبنای قانونی و دسترسی محدود قابل بررسی‌اند.' },
  { key: 'insurance', label: 'اعتراض به پوشش یا سهم بیمه',
    authority: 'تصمیم پوشش و اصلاح سهم بیمه فقط با بیمه‌گر یا مرجع قانونی است.',
    disclosure: 'ثبت اعتراض موجب برداشت، مسدودی یا اصلاح خودکار کیف پول نمی‌شود.' },
] as const;

// Coordinating a visit and making clinical decisions are DIFFERENT duties.
// All entries are static responsibility descriptions, not existing activities.
const careResponsibilitySteps = [
  { key: 'contact', label: 'درخواست و هماهنگی غیراضطراری',
    owner: 'ترنم: هماهنگی ارتباط و پیگیری تجربه عضو',
    restriction: 'هیچ درخواست یا پرونده واقعی هنوز ثبت نشده است.' },
  { key: 'clinical', label: 'تصمیم بالینی و ارجاع تخصصی',
    owner: 'پزشک یا مرکز درمانی دارای صلاحیت، مجوز و مسئولیت قانونی',
    restriction: 'سامانه نمی‌تواند تشخیص، درمان یا ارجاع پزشکی را خودکار تأیید کند.' },
  { key: 'followup', label: 'پیگیری پس از خدمت',
    owner: 'ترنم: هماهنگی تجربه؛ متخصص مجاز: تصمیم مراقبت و پیگیری بالینی',
    restriction: 'هیچ مراجعه، برنامه مراقبتی یا وضعیت تکمیل خدمت تأیید نشده است.' },
] as const;

const descriptions = [
  { label: 'هویت و دسترسی', value: 'اتصال برقرار نیست', help: 'احراز هویت واقعی هنوز پیاده‌سازی نشده است.' },
  { label: 'اطلاعات درمانی', value: 'در دسترس نیست', help: 'بدون دسترسی مجاز هیچ پرونده‌ای نمایش داده نمی‌شود.' },
  { label: 'خدمات و اعتبار', value: 'در دسترس نیست', help: 'هیچ موجودی، بیمه یا ارائه‌دهنده‌ای شبیه‌سازی نشده است.' },
] as const;

function currentPage(): MemberPage | undefined {
  const path = window.location.pathname.replace(/\/$/, '') || '/';
  return memberPages.find((p) => p.path === path);
}

function InterruptionNotice({ detail }: { detail: string }) {
  return (
    <section className="unavailable" role="status" aria-labelledby="connection-title">
      <div className="unavailable-mark" aria-hidden="true">!</div>
      <div>
        <p className="eyebrow">وضعیت اتصال داده</p>
        <h2 id="connection-title">این بخش هنوز به سرویس واقعی متصل نیست</h2>
        <p>{detail}</p>
        <p className="quiet">این صفحه فقط ساختار رابط کاربری را نشان می‌دهد؛ حساب عضو یا داده واقعی در آن وجود ندارد.</p>
      </div>
    </section>
  );
}

export function MemberApp() {
  const [page, setPage] = useState<MemberPage | undefined>(currentPage);
  const [mobileOpen, setMobileOpen] = useState(false);
  const menuButtonRef = useRef<HTMLButtonElement>(null);
  const routeNeedsFocus = useRef(false);

  useEffect(() => {
    const changed = () => {
      routeNeedsFocus.current = true;
      setPage(currentPage());
      setMobileOpen(false);
    };
    window.addEventListener('popstate', changed);
    return () => window.removeEventListener('popstate', changed);
  }, []);

  useEffect(() => {
    document.title = page ? 'ترنم | ' + page.label + ' — متصل نیست' : 'ترنم | صفحه پیدا نشد';
    // Focus after React has rendered the new route heading (including browser Back/Forward).
    // Do not steal focus on initial deep links or during normal keyboard navigation.
    if (routeNeedsFocus.current) {
      routeNeedsFocus.current = false;
      document.getElementById(page ? 'page-title' : 'not-found-title')?.focus();
    }
  }, [page]);

  useEffect(() => {
    if (!mobileOpen) return;
    const closeWithEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setMobileOpen(false);
        menuButtonRef.current?.focus();
      }
    };
    window.addEventListener('keydown', closeWithEscape);
    return () => window.removeEventListener('keydown', closeWithEscape);
  }, [mobileOpen]);

  function go(event: MouseEvent<HTMLAnchorElement>, href: string) {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.altKey || event.shiftKey) return;
    event.preventDefault();
    if (window.location.pathname === href) {
      setMobileOpen(false);
      document.getElementById(page ? 'page-title' : 'not-found-title')?.focus();
      return;
    }
    routeNeedsFocus.current = true;
    window.history.pushState({}, '', href);
    setPage(currentPage());
    setMobileOpen(false);
  }

  return (
    <div className="app">
      <a className="skip-link" href="#main-content">پرش به محتوای اصلی</a>
      <aside className="side-panel" aria-label="ناحیه ناوبری عضو">
        <div className="identity">
          <div className="logo-emblem" aria-hidden="true">ت</div>
          <div><strong>ترنم</strong><span>فضای عضو</span></div>
        </div>
        <div className="side-heading">
          <span className="side-small">پنل عضو</span>
          <span className="side-tag">نسخه پایه</span>
        </div>
        <button ref={menuButtonRef} className="menu-toggle" type="button"
          aria-expanded={mobileOpen} aria-controls="member-nav"
          onClick={() => setMobileOpen((v) => !v)}>
          {mobileOpen ? 'بستن فهرست بخش‌ها' : 'باز کردن فهرست بخش‌ها'}
          <span aria-hidden="true">{mobileOpen ? '×' : '☰'}</span>
        </button>
        <nav id="member-nav" className={'member-nav' + (mobileOpen ? ' expanded' : '')}
          aria-label="بخش‌های وب‌اپ عضو">
          {memberPages.map((item) => (
            <a key={item.key} href={item.path} onClick={(event) => go(event, item.path)}
              className={'nav-link' + (page?.key === item.key ? ' selected' : '')}
              aria-current={page?.key === item.key ? 'page' : undefined}>
              <span className="nav-symbol" aria-hidden="true">{item.symbol}</span>
              <span>{item.label}</span>
              <span className="nav-chevron" aria-hidden="true">‹</span>
            </a>
          ))}
        </nav>
        <div className="side-footer">
          <span className="footer-icon" aria-hidden="true">⌁</span>
          <div><strong>حریم خصوصی در اولویت</strong><span>دسترسی واقعی عضو هنوز فعال نیست.</span></div>
        </div>
      </aside>
      <div className="workspace">
        <header className="topbar">
          <div className="topbar-context"><span aria-hidden="true">⌂</span> <span>ترنم</span>
            <span className="crumb-slash" aria-hidden="true">/</span>
            <span>{page?.label ?? 'مسیر نامعتبر'}</span>
          </div>
          <div className="topbar-status" aria-label="وضعیت محیط: اتصال واقعی برقرار نیست">
            <span className="status-dot" aria-hidden="true"></span> بدون اتصال واقعی
          </div>
        </header>
        <main id="main-content" tabIndex={-1}>
          {!page ? (
            <section className="not-found" aria-labelledby="not-found-title">
              <p className="eyebrow">مسیر ناشناخته</p>
              <h1 id="not-found-title" tabIndex={-1}>صفحه پیدا نشد</h1>
              <p>این آدرس بخشی از فضای عضو نیست. هیچ محتوای خصوصی در این مسیر نمایش داده نمی‌شود.</p>
              <a className="return-link" href="/member" onClick={(event) => go(event, '/member')}>بازگشت به نمای کلی</a>
            </section>
          ) : (
            <>
              <div className="preview-bar" role="note">
                <span className="preview-label">پیش‌نمایش ساختاری</span>
                <span>بدون حساب واقعی، بدون داده درمانی و بدون اتصال مالی</span>
              </div>
              <section className="hero" aria-labelledby="page-title">
                <div className="hero-content">
                  <span className="hero-kicker">وب‌اپ عضو ترنم <span aria-hidden="true">/</span> رابط اولیه</span>
                  <h1 id="page-title" tabIndex={-1}>{page.key === 'overview' ? 'فضایی آرام برای پیگیری سلامت' : page.label}</h1>
                  <p>{page.key === 'overview'
                    ? 'اینجا در آینده مسیرهای عضویت و خدمات سلامت عضو در یک فضای یکپارچه قرار می‌گیرند؛ در حال حاضر هیچ اطلاعات شخصی نمایش داده نمی‌شود.'
                    : page.subtitle + ' — فقط پس از اتصال به سرویس معتبر و احراز هویت قابل استفاده خواهد بود.'}</p>
                  <span className="hero-chip">اتصال عضو هنوز برقرار نیست</span>
                </div>
                <div className="hero-orbit" aria-hidden="true"><span>ت</span></div>
              </section>
              <div className="section-header">
                <div><p className="eyebrow">وضعیت دسترسی</p><h2>اطلاعات این بخش</h2></div>
                <span className="readonly-pill">صرفاً نمایشی</span>
              </div>
              {page.key === 'overview' ? (
                <div className="status-grid" aria-label="بخش‌های بدون اتصال">
                  {descriptions.map((item) => (
                    <section key={item.label} className="status-card">
                      <span className="status-card-icon" aria-hidden="true">◇</span>
                      <p className="status-card-label">{item.label}</p>
                      <h3>{item.value}</h3>
                      <p>{item.help}</p>
                    </section>
                  ))}
                </div>
              ) : null}
              {page.key === 'costs' ? (
                <section aria-labelledby="nonbinding-cost-title">
                  <div className="section-header">
                    <div>
                      <p className="eyebrow">شفافیت هزینه بدون فرض مبلغ</p>
                      <h2 id="nonbinding-cost-title">اجزای برآورد غیرقطعی</h2>
                    </div>
                    <span className="readonly-pill">همه مقادیر نامعلوم‌اند</span>
                  </div>
                  <div className="status-grid" role="list" aria-label="سهم‌های نامشخص پیش از خدمت">
                    {costShares.map((share) => (
                      <article key={share.key} role="listitem" className="status-card">
                        <p className="status-card-label">{share.label}</p>
                        <h3>اطلاعات موجود نیست</h3>
                        <p>منبع مورد انتظار: {share.owner}؛ هیچ سند معتبر یا مبلغی دریافت نشده است.</p>
                      </article>
                    ))}
                  </div>
                  <p id="cost-awareness-limits" className="quiet">
                    این نما تضمین قیمت، پوشش بیمه، موجودی کیف پول یا بدهی عضو نیست.
                    برآورد، مجوز برداشت یا رزرو اعتبار نیست و تصمیم نهایی فقط با اسناد معتبر مراجع قانونی ممکن است.
                  </p>
                  <button type="button" disabled aria-describedby="cost-awareness-limits">
                    ثبت آگاهی و انتخاب — نیازمند اتصال امن
                  </button>
                </section>
              ) : null}
              {page.key === 'care' ? (
                <section aria-labelledby="care-boundary-title">
                  <div className="section-header">
                    <div>
                      <p className="eyebrow">هماهنگی خدمت، جدا از تصمیم پزشکی</p>
                      <h2 id="care-boundary-title">مراحل و مسئولیت‌های مسیر درمان</h2>
                    </div>
                    <span className="readonly-pill">هیچ مسیر فعالی وجود ندارد</span>
                  </div>
                  <div className="status-grid" role="list" aria-label="مرزهای هماهنگی و درمان">
                    {careResponsibilitySteps.map((step) => (
                      <article className="status-card" role="listitem" key={step.key}>
                        <p className="status-card-label">{step.label}</p>
                        <h3>اطلاعات موجود نیست</h3>
                        <p>{step.owner}</p>
                        <p>{step.restriction}</p>
                      </article>
                    ))}
                  </div>
                  <p id="care-safety-limit" className="quiet">
                    هماهنگی ترنم جایگزین ارزیابی متخصص، تأیید ارجاع یا مجوز دسترسی به پرونده سلامت نیست.
                    مشارکت عضو و استفاده از اطلاعات سلامت فقط با مبنای قانونی و مجوز معتبر انجام می‌شود.
                  </p>
                  <button type="button" disabled aria-describedby="care-safety-limit">
                    درخواست هماهنگی — نیازمند هویت و مجوز واقعی
                  </button>
                  <p className="emergency-note">
                    در احتمال فوریت پزشکی، دریافت کمک فوری را به هماهنگی غیراضطراری یا پاسخ پزشک در سامانه موکول نکنید.
                  </p>
                </section>
              ) : null}
              {page.key === 'support' ? (
                <section aria-labelledby="support-paths-title">
                  <div className="section-header">
                    <div>
                      <p className="eyebrow">مسیر واحد پیگیری؛ اختیارهای مستقل</p>
                      <h2 id="support-paths-title">موضوعات شکایت و اعتراض</h2>
                    </div>
                    <span className="readonly-pill">ثبت واقعی غیرفعال</span>
                  </div>
                  <div className="status-grid" role="list" aria-label="مسیرهای مستقل رسیدگی">
                    {supportPaths.map((path) => (
                      <article className="status-card" role="listitem" key={path.key}>
                        <p className="status-card-label">{path.label}</p>
                        <h3>ثبت و پیگیری هنوز متصل نیست</h3>
                        <p>{path.authority}</p>
                        <p>{path.disclosure}</p>
                        <button type="button" disabled>
                          ثبت درخواست — نیازمند احراز هویت و مرجع معتبر
                        </button>
                      </article>
                    ))}
                  </div>
                  <p className="quiet" id="support-legal-limit">
                    این صفحه رأی پزشکی، تصمیم بیمه‌گر، حکم جبران خسارت یا وضعیت یک پرونده واقعی نیست.
                    هیچ شماره پرونده، زمان پاسخ یا نتیجه رسیدگی ساخته نشده است.
                  </p>
                  <p className="emergency-note">
                    در فوریت پزشکی، کمک پزشکی و تماس با خدمات فوریت را به ثبت شکایت یا بررسی بیمه موکول نکنید.
                  </p>
                </section>
              ) : null}
              <InterruptionNotice detail={page.detail} />
              <div className="source-strip" aria-label="مالک داده">
                <span className="source-symbol" aria-hidden="true">↗</span>
                <div><strong>منبع معتبر این بخش</strong><span>{page.owner} — اتصال و قرارداد عملیاتی هنوز برقرار نشده است.</span></div>
              </div>
              <p className="emergency-note">در وضعیت اضطراری، دریافت کمک پزشکی را به ورود به سامانه یا مسائل اداری و پرداخت موکول نکنید.</p>
            </>
          )}
        </main>
        <footer className="site-footer">
          <span>ترنم · چارچوب اولیه وب‌اپ عضو</span>
          <span>بدون احراز هویت و بدون داده واقعی</span>
        </footer>
      </div>
    </div>
  );
}
