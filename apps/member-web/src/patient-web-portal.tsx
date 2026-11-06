import { useEffect, useState, type MouseEvent } from 'react';
import { PatientPortalScreen, type PatientPortalScreenKey } from './patient-portal-screens';

const base = '/patient-portal';
const pages = [
  { key: 'overview', path: base, label: 'نمای کلی', icon: '⌂', group: 'خدمات درمانی' },
  { key: 'appointments', path: `${base}/appointments`, label: 'نوبت‌های من', icon: '◷', group: 'خدمات درمانی' },
  { key: 'records', path: `${base}/records`, label: 'نسخه و پرونده', icon: '▤', group: 'خدمات درمانی' },
  { key: 'credit', path: `${base}/credit`, label: 'اعتبار درمان', icon: '◉', group: 'خدمات درمانی' },
  { key: 'family', path: `${base}/family`, label: 'اعضای خانواده', icon: '♧', group: 'حساب و همراهان' },
  { key: 'referrals', path: `${base}/referrals`, label: 'ارجاع‌های من', icon: '↗', group: 'حساب و همراهان' },
  { key: 'account', path: `${base}/account`, label: 'حساب کاربری', icon: '◎', group: 'حساب و همراهان' },
] as const;

function pageFromPath(path: string) {
  return pages.find((page) => page.path === path) ?? pages[0];
}

export function PatientWebPortal() {
  const [page, setPage] = useState(() => pageFromPath(window.location.pathname));
  const [mobileMenu, setMobileMenu] = useState(false);

  useEffect(() => {
    const sync = () => { setPage(pageFromPath(window.location.pathname)); setMobileMenu(false); };
    window.addEventListener('popstate', sync);
    return () => window.removeEventListener('popstate', sync);
  }, []);

  useEffect(() => { document.title = `ترنم | ${page.label}${['appointments','account'].includes(page.key)?'':' · پورتال نمونه'}`; }, [page]);

  function go(event: MouseEvent<HTMLAnchorElement>, href: string) {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.altKey || event.shiftKey) return;
    event.preventDefault();
    if (href === window.location.pathname) return;
    window.history.pushState({}, '', href);
    setPage(pageFromPath(href));
    setMobileMenu(false);
    document.getElementById('patient-portal-title')?.focus();
  }

  return <div className="patient-portal-app" dir="rtl">
    <a className="patient-skip" href="#patient-portal-main">پرش به محتوای اصلی</a>
    <aside className="patient-sidebar" aria-label="پورتال بیمار">
      <div className="patient-sidebar-brand"><img src="/assets/taranom-logo.png" alt="ترنم · اکوسیستم سلامت" /></div>
      <div className="patient-sidebar-account"><span className="patient-avatar">{['appointments','account'].includes(page.key)?'•':'ن'}</span><span><strong>{['appointments','account'].includes(page.key)?'حساب بیمار':'نیلوفر رضایی'}</strong><small>{['appointments','account'].includes(page.key)?'پروفایل هنوز متصل نشده':'حساب نمونه · تهران'}</small></span><i aria-hidden="true">⌄</i></div>
      <button className="patient-menu-toggle" type="button" aria-expanded={mobileMenu} onClick={() => setMobileMenu(!mobileMenu)}>{mobileMenu ? 'بستن فهرست' : 'منوی پورتال'} <span aria-hidden="true">☰</span></button>
      <nav className={mobileMenu ? 'patient-nav is-open' : 'patient-nav'} aria-label="بخش‌های پورتال">
        {['خدمات درمانی', 'حساب و همراهان'].map((group) => <div className="patient-nav-group" key={group}>
          <h2>{group}</h2>
          {pages.filter((item) => item.group === group).map((item) => <a key={item.key} href={item.path} aria-current={page.key === item.key ? 'page' : undefined} className={page.key === item.key ? 'active' : ''} onClick={(event) => go(event, item.path)}><span aria-hidden="true">{item.icon}</span>{item.label}<b aria-hidden="true">‹</b></a>)}
        </div>)}
      </nav>
      <div className="patient-sidebar-help"><strong>پشتیبانی ترنم</strong><span>راهنمایی دربارهٔ عضویت و خدمات</span><a href="mailto:support@example.invalid">تماس با پشتیبانی</a></div>
      <div className="patient-sidebar-footer">{['appointments','account'].includes(page.key)?'نوبت‌ها از حساب مجاز دریافت می‌شوند. سایر صفحات پیش‌نمایش‌اند.':'حساب و اطلاعات این محیط نمونه‌اند.'}</div>
    </aside>
    <div className="patient-workspace">
      <header className="patient-topbar"><div className="patient-breadcrumb">ترنم <span>›</span> پورتال بیمار <span>›</span> {page.label}</div><div className="patient-topbar-actions"><button type="button" className="patient-notification" aria-label="اعلان‌ها · نمونه"><img src="/assets/header-bell-dome.svg" alt="" /><img src="/assets/header-bell-clapper.svg" alt="" /><img src="/assets/header-notification-dot.svg" alt="" /></button><span className="patient-user">{['appointments','account'].includes(page.key)?'حساب بیمار':'نیلوفر رضایی'}</span></div></header>
      <main id="patient-portal-main" className="patient-main">
        {page.key === 'overview' ? <section className="patient-dashboard" aria-labelledby="patient-portal-title">
          <div className="patient-welcome"><span className="patient-welcome-badge">پورتال عضو · حساب نمونه</span><h1 id="patient-portal-title" tabIndex={-1}>سلام نیلوفر، خوش آمدی</h1><p>وضعیت خدمات و پیگیری‌های درمانی شما در یک نگاه</p><a href={`${base}/appointments`} onClick={(event) => go(event, `${base}/appointments`)}>رفتن به نوبت‌های من <span aria-hidden="true">←</span></a></div>
          <section className="patient-dashboard-stats" aria-label="خلاصه وضعیت نمونه"><article><span>نوبت آینده</span><strong>۱</strong><small>۲۴ آبان · ساعت ۱۱:۰۰</small></article><article><span>نسخه‌های ثبت‌شده</span><strong>۲</strong><small>اطلاعات نمونه</small></article><article><span>اعتبار حمایتی</span><strong>۲٬۵۰۰٬۰۰۰ تومان</strong><small>موجودی نمایشی حساب نمونه</small></article></section>
          <div className="patient-dashboard-columns"><section className="patient-dashboard-card"><header><div><small>پیگیری درمان</small><h2>نوبت پیش رو</h2></div><a href={`${base}/appointments`} onClick={(event) => go(event, `${base}/appointments`)}>همهٔ نوبت‌ها</a></header><div className="patient-next-appointment"><span className="patient-date-box"><b>۲۴</b><small>آبان</small></span><div><strong>دکتر سارا رضایی</strong><small>متخصص داخلی · درمانگاه مهر ترنم</small><small>ساعت ۱۱:۰۰ · تهران</small></div><span className="patient-sample-pill">نمونه</span></div></section><section className="patient-dashboard-card patient-shortcuts"><header><div><small>دسترسی سریع</small><h2>خدمات ترنم</h2></div></header><div className="patient-shortcut-grid">{pages.slice(2, 7).map((item) => <a key={item.key} href={item.path} onClick={(event) => go(event, item.path)}><span>{item.icon}</span>{item.label}<b>←</b></a>)}</div></section></div>
          <p className="patient-sample-disclaimer">اطلاعات و مبالغ این نما صرفاً دادهٔ نمونه برای طراحی هستند و از حساب واقعی عضو دریافت نشده‌اند.</p>
        </section> : <PatientPortalScreen pageKey={page.key as PatientPortalScreenKey} go={go} />}
      </main>
    </div>
  </div>;
}
