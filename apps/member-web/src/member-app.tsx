import { useEffect, useState, type MouseEvent } from 'react';

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

  useEffect(() => {
    const changed = () => { setPage(currentPage()); setMobileOpen(false); };
    window.addEventListener('popstate', changed);
    return () => window.removeEventListener('popstate', changed);
  }, []);

  useEffect(() => {
    document.title = page ? 'ترنم | ' + page.label + ' — متصل نیست' : 'ترنم | صفحه پیدا نشد';
  }, [page]);

  function go(event: MouseEvent<HTMLAnchorElement>, href: string) {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.altKey || event.shiftKey) return;
    event.preventDefault();
    window.history.pushState({}, '', href);
    setPage(currentPage());
    setMobileOpen(false);
    document.getElementById('main-content')?.focus();
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
        <button className="menu-toggle" type="button"
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
              <h1 id="not-found-title">صفحه پیدا نشد</h1>
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
                  <h1 id="page-title">{page.key === 'overview' ? 'فضایی آرام برای پیگیری سلامت' : page.label}</h1>
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
