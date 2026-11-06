import {SupportDesk} from './support-desk';
import {AdminCenters} from './admin-centers';
import {AdminManagerRequests} from './admin-manager-requests';
import { useEffect, useState, type MouseEvent } from 'react';

type PortalKind = 'admin' | 'institution';
type PortalPage = { key: string; title: string; group: string; icon: string; path: string };
const adminPages: PortalPage[] = [
  {key:'support',title:'پشتیبانی کاربران',group:'مدیریت سامانه',icon:'◌',path:'/admin/support-requests'},
  { key: 'dashboard', title: 'نمای کلی', group: 'مدیریت سامانه', icon: '⌂', path: '/admin' },
  { key: 'users', title: 'اعضا و کاربران', group: 'مدیریت سامانه', icon: '♙', path: '/admin/users' },
  { key: 'centers', title: 'مراکز درمانی', group: 'خدمات درمانی', icon: '▦', path: '/admin/centers' },
  { key: 'manager-requests', title: 'درخواست مدیریت مراکز', group: 'خدمات درمانی', icon: '♙', path: '/admin/center-manager-requests' },
  { key: 'appointments', title: 'نوبت‌ها', group: 'خدمات درمانی', icon: '◷', path: '/admin/appointments' },
  { key: 'prescriptions', title: 'نسخه‌ها', group: 'خدمات درمانی', icon: '▤', path: '/admin/prescriptions' },
  { key: 'finance', title: 'گزارش‌های مالی', group: 'مالی و حمایت', icon: '◈', path: '/admin/finance' },
  { key: 'institutions', title: 'نهادهای حمایتی', group: 'مالی و حمایت', icon: '♧', path: '/admin/support-institutions' },
  { key: 'funds', title: 'صندوق‌های ترنم', group: 'مالی و حمایت', icon: '◉', path: '/admin/funds' },
  { key: 'access', title: 'سطوح دسترسی', group: 'مدیریت سامانه', icon: '⌑', path: '/admin/access' },
];
const institutionPages: PortalPage[] = [
  { key: 'dashboard', title: 'نمای کلی', group: 'مدیریت پرونده‌ها', icon: '⌂', path: '/support-institution' },
  { key: 'beneficiaries', title: 'مددجویان', group: 'مدیریت پرونده‌ها', icon: '♙', path: '/support-institution/beneficiaries' },
  { key: 'programs', title: 'برنامه‌های حمایتی', group: 'مدیریت پرونده‌ها', icon: '▦', path: '/support-institution/programs' },
  { key: 'register', title: 'تعریف مددجو', group: 'مدیریت پرونده‌ها', icon: '+', path: '/support-institution/register-beneficiary' },
  { key: 'case', title: 'گردش کار پرونده', group: 'مدیریت پرونده‌ها', icon: '↗', path: '/support-institution/cases' },
  { key: 'budget', title: 'تخصیص بودجه', group: 'بودجه و پرداخت', icon: '◈', path: '/support-institution/budget' },
  { key: 'payments', title: 'ثبت پرداخت', group: 'بودجه و پرداخت', icon: '↙', path: '/support-institution/payments' },
  { key: 'transactions', title: 'تراکنش‌ها', group: 'بودجه و پرداخت', icon: '≋', path: '/support-institution/transactions' },
  { key: 'reports', title: 'گزارش مالی', group: 'بودجه و پرداخت', icon: '▤', path: '/support-institution/reports' },
  { key: 'taranom-fund', title: 'حمایت صندوق ترنم', group: 'منابع حمایتی', icon: '♥', path: '/support-institution/taranom-fund' },
  { key: 'charity-fund', title: 'صندوق نیکوکاری ترنم', group: 'منابع حمایتی', icon: '♡', path: '/support-institution/charity-fund' },
  { key: 'profile', title: 'مشخصات نهاد', group: 'تنظیمات نهاد', icon: '▧', path: '/support-institution/profile' },
  { key: 'users', title: 'کاربران نهاد', group: 'تنظیمات نهاد', icon: '♧', path: '/support-institution/users' },
  { key: 'edit-profile', title: 'ویرایش مشخصات', group: 'تنظیمات نهاد', icon: '✎', path: '/support-institution/edit-profile' },
  { key: 'invite-user', title: 'دعوت کاربر', group: 'تنظیمات نهاد', icon: '+', path: '/support-institution/invite-user' },
];

const funds = [
  { title: 'صندوق توسعهٔ درمان', amount: '۲۴٬۸۰۰٬۰۰۰٬۰۰۰ تومان', meta: 'سرمایهٔ نمونه · گردش سه‌ماهه ۴٬۲۶۰٬۰۰۰٬۰۰۰ تومان' },
  { title: 'صندوق درآمد ثابت', amount: '۳۱٬۷۰۰٬۰۰۰٬۰۰۰ تومان', meta: 'سرمایهٔ نمونه · گردش سه‌ماهه ۵٬۱۲۰٬۰۰۰٬۰۰۰ تومان' },
  { title: 'صندوق توسعهٔ سلامت', amount: '۱۸٬۳۵۰٬۰۰۰٬۰۰۰ تومان', meta: 'سرمایهٔ نمونه · گردش سه‌ماهه ۲٬۹۴۰٬۰۰۰٬۰۰۰ تومان' },
  { title: 'صندوق نیکوکاری', amount: '۶٬۴۰۰٬۰۰۰٬۰۰۰ تومان', meta: 'سرمایهٔ نمونه · گردش سه‌ماهه ۱٬۱۸۰٬۰۰۰٬۰۰۰ تومان' },
];

const sampleRows: Record<string, string[][]> = {
  users: [['مینا کریمی', 'عضو', 'تهران', 'فعال · نمونه'], ['علی رضایی', 'عضو', 'کرج', 'فعال · نمونه'], ['سارا احمدی', 'مدیر نهاد', 'مشهد', 'نیازمند بازبینی · نمونه']],
  centers: [['بیمارستان مهرآفرین', 'تهران', '۲۴ بخش', 'نمونه'], ['درمانگاه سپیدار', 'کرج', '۶ بخش', 'نمونه'], ['مرکز تخصصی بهار', 'مشهد', '۹ بخش', 'نمونه']],
  appointments: [['۲۴ آبان · ۱۱:۰۰', 'دکتر سارا رضایی', 'درمانگاه مهرآفرین', 'تأیید مرکز · نمونه'], ['۲۵ آبان · ۰۹:۳۰', 'دکتر امیر کاظمی', 'بیمارستان سپیدار', 'در انتظار پاسخ · نمونه'], ['۲۶ آبان · ۱۴:۰۰', 'دکتر نازنین مرادی', 'مرکز بهار', 'درخواست عضو · نمونه']],
  prescriptions: [['RX-TRN-2048', 'نیلوفر رضایی', 'دکتر سارا رضایی', 'ثبت‌شده · نمونه'], ['RX-TRN-2049', 'مینا کریمی', 'دکتر امیر کاظمی', 'ثبت‌شده · نمونه'], ['RX-TRN-2050', 'علی رضایی', 'دکتر نازنین مرادی', 'در انتظار تأیید · نمونه']],
  institutions: [['بنیاد مهرآفرین', 'تهران', '۱٬۲۸۰ مددجو', 'فعال · نمونه'], ['مؤسسه همیاران فردا', 'کرج', '۷۴۰ مددجو', 'فعال · نمونه'], ['مرکز نیکوکاری باران', 'مشهد', '۳۹۵ مددجو', 'در حال تکمیل · نمونه']],
  access: [['مدیر کل سامانه', 'مدیریت کاربران و تنظیمات', '۲ کاربر · نمونه', 'سطح نمونه'], ['مدیر مرکز', 'مدیریت یک مرکز و بخش‌های آن', '۱۸ کاربر · نمونه', 'سطح نمونه'], ['کارشناس حمایت', 'پرونده‌های مجاز و گزارش مالی', '۳۲ کاربر · نمونه', 'سطح نمونه']],
  finance: [['مهر ۱۴۰۵', 'درآمد خدمات قراردادی', '۲۴۸٬۰۰۰٬۰۰۰ تومان', 'نمونه'], ['مهر ۱۴۰۵', 'کمک صندوق ترنم', '۱۴۰٬۰۰۰٬۰۰۰ تومان', 'نمونه'], ['مهر ۱۴۰۵', 'هزینهٔ خدمات حمایتی', '۵۲۴٬۰۰۰٬۰۰۰ تومان', 'نمونه']],
  beneficiaries: [['مریم احمدی', 'پروندهٔ حمایتی', 'تهران', 'در حال پیگیری · نمونه'], ['فاطمه موسوی', 'حمایت درمانی', 'کرج', 'نیازمند بودجه · نمونه'], ['زینب رضایی', 'پروندهٔ حمایتی', 'مشهد', 'حمایت فعال · نمونه']],
  programs: [['حمایت درمانی خانواده', 'درمان و خدمات مجاز', '۱۴۰ پرونده', 'فعال · نمونه'], ['پشتیبانی دارو', 'پوشش حمایتی دارو', '۸۶ پرونده', 'فعال · نمونه'], ['مراقبت پیوسته', 'ارجاع و پیگیری درمان', '۵۲ پرونده', 'در حال اجرا · نمونه']],
  payments: [['۲۹ مهر', 'کمک‌هزینه درمان', '۳٬۵۰۰٬۰۰۰ تومان', 'ثبت نمونه'], ['۲۸ مهر', 'حمایت دارو', '۱٬۲۰۰٬۰۰۰ تومان', 'ثبت نمونه'], ['۲۵ مهر', 'پرداخت از صندوق نیکوکاری', '۵٬۰۰۰٬۰۰۰ تومان', 'ثبت نمونه']],
  transactions: [['TRN-TX-2048', 'تخصیص بودجه', '۱۰٬۰۰۰٬۰۰۰ تومان', 'نمونه'], ['TRN-TX-2047', 'پرداخت درمانی', '۳٬۵۰۰٬۰۰۰ تومان', 'نمونه'], ['TRN-TX-2046', 'حمایت نیکوکاری', '۵٬۰۰۰٬۰۰۰ تومان', 'نمونه']],
  reports: [['مهر ۱۴۰۵', 'بودجهٔ مصوب', '۸۵۰٬۰۰۰٬۰۰۰ تومان', 'نمونه'], ['مهر ۱۴۰۵', 'پرداخت‌شده', '۵۲۴٬۰۰۰٬۰۰۰ تومان', 'نمونه'], ['مهر ۱۴۰۵', 'ماندهٔ نمایشی', '۳۲۶٬۰۰۰٬۰۰۰ تومان', 'نمونه']],
  'taranom-fund': [['حمایت درمانی عضو', 'از صندوق‌های ترنم', '۲٬۴۰۰٬۰۰۰ تومان', 'درخواست نمونه'], ['هزینهٔ دارو', 'از صندوق‌های ترنم', '۱٬۱۰۰٬۰۰۰ تومان', 'درخواست نمونه']],
  'charity-fund': [['حمایت اضطراری درمان', 'صندوق نیکوکاری ترنم', '۵٬۰۰۰٬۰۰۰ تومان', 'پرداخت نمونه'], ['کمک‌هزینه رفت‌وآمد', 'صندوق نیکوکاری ترنم', '۹۰۰٬۰۰۰ تومان', 'پرداخت نمونه']],
  'case': [['۱', 'ثبت معرفی مددجو', 'تکمیل اطلاعات پایه · نمونه', 'انجام‌شده'], ['۲', 'بررسی نیاز حمایتی', 'نیازمند بررسی مسئول مجاز · نمونه', 'در جریان'], ['۳', 'تخصیص بودجه', 'بدون ثبت تراکنش واقعی · نمونه', 'در انتظار'], ['۴', 'ثبت پرداخت و پیگیری', 'پس از تأیید نهاد · نمونه', 'در انتظار']],
  'profile': [['نام نهاد', 'بنیاد مهرآفرین · نمونه'], ['شناسه سازمانی', 'ORG-TRN-2048 · نمونه'], ['شهر فعالیت', 'تهران'], ['مسئول نهاد', 'سارا احمدی · نمونه']],
};

const fieldSets: Record<string, [string, string][]> = {
  register: [['نام و نام خانوادگی', 'مریم احمدی'], ['شناسهٔ نمایشی پرونده', 'CASE-TRN-2048'], ['شهر', 'تهران'], ['برنامهٔ حمایتی', 'حمایت درمانی خانواده']],
  budget: [['نام برنامه', 'حمایت درمانی خانواده'], ['مبلغ بودجهٔ نمونه', '۸۵۰٬۰۰۰٬۰۰۰ تومان'], ['دورهٔ تخصیص', 'مهر تا اسفند ۱۴۰۵'], ['مسئول ثبت', 'سارا احمدی · نمونه']],
  'edit-profile': [['نام نهاد', 'بنیاد مهرآفرین'], ['شهر اصلی', 'تهران'], ['ایمیل سازمانی', 'info@example.invalid'], ['تلفن سازمانی', '۰۲۱-۴۴۰۰۱۲۳۴']],
  'invite-user': [['نام کاربر', 'کارشناس جدید · نمونه'], ['شماره همراه', '۰۹۱۲ *** ۴۵۶۷'], ['سمت', 'کارشناس حمایت'], ['دامنهٔ دسترسی', 'پرونده‌های تخصیص‌یافته']],
  payments: [['شناسهٔ پروندهٔ نمایشی', 'CASE-TRN-2048'], ['منبع پرداخت', 'بودجهٔ نهاد · نمونه'], ['مبلغ نمونه', '۳٬۵۰۰٬۰۰۰ تومان'], ['تاریخ ثبت نمایشی', '۲۹ مهر ۱۴۰۵']],
};

function pageKeyFor(kind: PortalKind, path: string) {
  const pages = kind === 'admin' ? adminPages : institutionPages;
  return pages.find((p) => p.path === path) ?? pages[0];
}

export function FigmaPortal({ kind }: { kind: PortalKind }) {
  const pages = kind === 'admin' ? adminPages : institutionPages;
  const isAdmin = kind === 'admin';
  const portalTitle = isAdmin ? 'پنل مدیریت ترنم' : 'پنل نهاد حمایتگر';
  const organization = isAdmin ? 'سامانه ترنم' : 'بنیاد مهرآفرین · نمونه';
  const [page, setPage] = useState(() => pageKeyFor(kind, window.location.pathname));
  const [mobileMenu, setMobileMenu] = useState(false);
  useEffect(() => {
    const onPop = () => { setPage(pageKeyFor(kind, window.location.pathname)); setMobileMenu(false); };
    window.addEventListener('popstate', onPop);
    return () => window.removeEventListener('popstate', onPop);
  }, [kind]);
  useEffect(() => { document.title = `ترنم | ${page.title} · ${portalTitle}`; }, [page, portalTitle]);
  function navigate(event: MouseEvent<HTMLAnchorElement>, path: string) {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.altKey || event.shiftKey) return;
    event.preventDefault();
    if (path === window.location.pathname) return;
    window.history.pushState({}, '', path);
    setPage(pageKeyFor(kind, path));
    setMobileMenu(false);
    document.getElementById('figma-portal-title')?.focus();
  }
  const groups = [...new Set(pages.map((item) => item.group))];
  return <div className={`figma-portal${page.key==='support'?' support-connected':''}`} dir="rtl">
    <aside className="figma-sidebar" aria-label={portalTitle}>
      <div className="figma-brand"><img src="/assets/taranom-logo.png" alt="ترنم · اکوسیستم سلامت" /></div>
      <div className="figma-organization"><span className="figma-organization-mark">{isAdmin ? 'ت' : 'م'}</span><span><small>{isAdmin ? 'محیط مدیریت' : 'نهاد فعال'}</small><strong>{organization}</strong></span><i aria-hidden="true">⌄</i></div>
      <button className="figma-mobile-menu" type="button" aria-expanded={mobileMenu} onClick={() => setMobileMenu(!mobileMenu)}>{mobileMenu ? 'بستن منو' : 'فهرست پنل'} <span aria-hidden="true">☰</span></button>
      <nav className={mobileMenu ? 'figma-nav is-open' : 'figma-nav'} aria-label="صفحه‌های پنل">
        {groups.map((group) => <section className="figma-nav-group" key={group}><h2>{group}</h2>{pages.filter((item) => item.group === group).map((item) => <a key={item.key} href={item.path} className={item.key === page.key ? 'active' : ''} aria-current={item.key === page.key ? 'page' : undefined} onClick={(event) => navigate(event, item.path)}><span aria-hidden="true">{item.icon}</span>{item.title}<b aria-hidden="true">‹</b></a>)}</section>)}
      </nav>
      <div className="figma-sidebar-footer"><strong>{['support','centers','manager-requests'].includes(page.key)?'دسترسی از مجوز جاری':'حساب نمونه'}</strong><span>{['support','centers','manager-requests'].includes(page.key)?'دسترسی و ثبت با مجوز مرکزی حساب':'اطلاعات این پنل نمایشی است.'}</span></div>
    </aside>
    <div className="figma-workspace">
      <header className="figma-topbar"><div className="figma-crumb">ترنم <span>›</span> {portalTitle} <span>›</span> {page.title}</div><div className="figma-topbar-user"><span className="figma-status-dot" />{page.key==='support'?'پشتیبانی کاربران':page.key==='centers'?'مدیریت مراکز':page.key==='manager-requests'?'بررسی درخواست‌های مدیریت':'اطلاعات نمونه'}<span className="figma-user-avatar">ن</span></div></header>
      <main className="figma-main"><div className="figma-page-heading"><div><p>{portalTitle} / {page.group}</p><h1 id="figma-portal-title" tabIndex={-1}>{page.title}</h1><span>{page.key==='support'?'رسیدگی به درخواست‌های ثبت‌شده':page.key==='centers'?'تعریف مراکز صرفاً توسط ادمین · فاقد تأیید صلاحیت و انتشار':page.key==='manager-requests'?'بررسی انسانی درخواست‌ها · بدون صدور خودکار مجوز مرکز':'نمایش و گردش کار نمونه · اتصال عملیاتی هنوز برقرار نیست'}</span></div>{!['support','centers','manager-requests'].includes(page.key)&&<button type="button" disabled>عملیات در انتظار اتصال</button>}</div>
        <PortalContent kind={kind} page={page} navigate={navigate} />
      </main>
    </div>
  </div>;
}

function PortalContent({ kind, page, navigate }: { kind: PortalKind; page: PortalPage; navigate: (event: MouseEvent<HTMLAnchorElement>, path: string) => void }) {
  if (kind==='admin'&&page.key==='support') return <SupportDesk/>;
  if (kind==='admin'&&page.key==='centers') return <AdminCenters/>;
  if (kind==='admin'&&page.key==='manager-requests') return <AdminManagerRequests/>;
  if (page.key === 'dashboard') {
    const stats = kind === 'admin' ? [['اعضای ثبت‌شده', '۱۲٬۸۴۰'], ['مراکز درمانی', '۲۴۸'], ['نوبت‌های امروز', '۳۶۴'], ['پرونده‌های حمایتی', '۱٬۲۸۰']] : [['مددجویان تحت پوشش', '۱٬۲۸۰'], ['پرونده‌های فعال', '۴۲۶'], ['بودجهٔ دوره', '۸۵۰٬۰۰۰٬۰۰۰ تومان'], ['پرداخت‌های ماه', '۵۲۴٬۰۰۰٬۰۰۰ تومان']];
    return <><div className="figma-stat-grid">{stats.map(([label, value], i) => <article key={label}><span className={`figma-stat-icon tone-${i}`}>{['◷','♙','▦','◈'][i]}</span><small>{label}</small><strong>{value}</strong><em>عدد نمونه</em></article>)}</div>
      {kind === 'admin' ? <><div className="figma-content-grid"><section className="figma-card"><header><div><small>پیگیری جاری</small><h2>گردش فعالیت ترنم</h2></div><span className="figma-sample-tag">نمونه</span></header><div className="figma-mini-bars">{[['نوبت‌دهی',76],['مراکز درمانی',58],['حمایت مالی',64],['نسخه‌ها',42]].map(([label,width])=><div key={label as string}><span>{label}</span><b><i style={{width:`${width}%`}} /></b><small>{width}٪</small></div>)}</div></section><section className="figma-card"><header><div><small>منابع مالی</small><h2>چهار صندوق ترنم</h2></div><a href="/admin/funds" onClick={(event)=>navigate(event,'/admin/funds')}>مشاهدهٔ صندوق‌ها</a></header>{funds.map((fund)=><div className="figma-fund-row" key={fund.title}><span>{fund.title}<small>موجودی نمایشی</small></span><strong>{fund.amount}</strong></div>)}</section></div><Table title="آخرین فعالیت‌ها" rows={sampleRows.appointments} headers={['زمان', 'ارائه‌دهنده', 'مرکز', 'وضعیت']} /></> : <><div className="figma-content-grid"><section className="figma-card"><header><div><small>بودجه و گردش مالی</small><h2>خلاصهٔ منابع حمایتی</h2></div><span className="figma-sample-tag">نمونه</span></header><div className="figma-budget-total"><strong>۸۵۰٬۰۰۰٬۰۰۰ تومان</strong><small>بودجهٔ دورهٔ نمایشی</small><div><i /></div><span>۶۲٪ تخصیص نمونه</span></div><div className="figma-budget-sources"><span>کمک نهاد <b>۵۱۰٬۰۰۰٬۰۰۰ تومان</b></span><span>صندوق ترنم <b>۱۴۰٬۰۰۰٬۰۰۰ تومان</b></span><span>صندوق نیکوکاری <b>۷۰٬۰۰۰٬۰۰۰ تومان</b></span></div></section><section className="figma-card"><header><div><small>کارهای پیگیری</small><h2>اقدام‌های در جریان</h2></div></header><ul className="figma-task-list"><li><b>۱۲</b><span>پرونده در انتظار بررسی</span><em>نمونه</em></li><li><b>۸</b><span>درخواست بودجهٔ جدید</span><em>نمونه</em></li><li><b>۳</b><span>ثبت پرداخت در انتظار تکمیل</span><em>نمونه</em></li></ul></section></div><Table title="آخرین گردش پرونده‌ها" rows={sampleRows.beneficiaries} headers={['مددجو', 'نوع پیگیری', 'شهر', 'وضعیت']} /></>}
      <p className="figma-disclaimer">تمام اعداد و ردیف‌ها ساختگی و صرفاً برای نمایش چیدمان‌اند. هیچ پرونده، بودجه، پرداخت، سطح دسترسی یا تصمیم درمانی واقعی از این نمونه نتیجه نمی‌شود.</p>
    </>;
  }
  if (page.key === 'funds') return <><section className="figma-fund-grid">{funds.map((fund,i)=><article className="figma-card" key={fund.title}><div className={`figma-fund-icon tone-${i}`}>◈</div><small>صندوق · نمونه</small><h2>{fund.title}</h2><strong>{fund.amount}</strong><p>{fund.meta}</p><button type="button" disabled>جزئیات گردش · غیرفعال</button></article>)}</section><p className="figma-disclaimer">نام‌ها از طرح و محدوده‌های محصول گردآوری شده‌اند؛ ارقام نمایشی‌اند. ساختار مالکیت، مجوز و حسابداری صندوق‌ها از این صفحه تأیید نمی‌شود.</p></>;
  if (page.key === 'case' || page.key === 'budget' || page.key === 'register' || page.key === 'payments' || page.key === 'edit-profile' || page.key === 'invite-user') {
    const fields = fieldSets[page.key] ?? fieldSets.register;
    return <div className="figma-form-layout"><section className="figma-card figma-form-card"><header><div><small>فرم نمایشی</small><h2>{page.title}</h2></div><span className="figma-sample-tag">نمونه</span></header><div className="figma-form-grid">{fields.map(([label,value])=><label key={label}>{label}<input value={value} readOnly aria-readonly="true" /></label>)}</div><button type="button" disabled className="figma-form-submit">ثبت نهایی پس از اتصال بک‌اند</button><p>مقادیر صرفاً نمونه‌اند و قابل ثبت یا ارسال نیستند.</p></section>{page.key==='case'&&<section className="figma-card"><header><div><small>پروندهٔ نمونه</small><h2>گردش کار حمایت</h2></div></header><ol className="figma-case-timeline">{(sampleRows.case||[]).map((row)=><li key={row[0]}><b>{row[0]}</b><div><strong>{row[1]}</strong><small>{row[2]}</small></div><em>{row[3]}</em></li>)}</ol><p className="figma-privacy-note">مشاهدهٔ امور مالی مددجو به‌معنی دسترسی به پروندهٔ درمانی او نیست.</p></section>}</div>;
  }
  const rows = sampleRows[page.key] || sampleRows.users;
  const headers = page.key === 'users' ? ['نام', 'نقش', 'محدوده', 'وضعیت'] : page.key === 'appointments' ? ['زمان', 'پزشک', 'مرکز', 'وضعیت'] : page.key === 'prescriptions' ? ['شناسهٔ نمونه', 'عضو', 'صادرکننده', 'وضعیت'] : page.key === 'centers' ? ['مرکز درمانی', 'شهر', 'بخش‌ها', 'برچسب'] : page.key === 'profile' ? ['مشخصه', 'مقدار'] : ['تاریخ / شناسه', 'موضوع', 'مبلغ / جزئیات', 'وضعیت'];
  return <><div className="figma-list-toolbar"><div><strong>{page.title}</strong><span>{rows.length} ردیف نمایشی</span></div><button type="button" disabled>افزودن مورد جدید</button></div><Table title={page.title} rows={rows} headers={headers} /><p className="figma-disclaimer">ردیف‌ها با برچسب نمونه نمایش داده شده‌اند و از اطلاعات واقعی عضو یا نهاد خوانده نشده‌اند. تغییرات تا تکمیل API و مجوزها غیرفعال است.</p></>;
}

function Table({ title, rows, headers }: { title: string; rows: string[][]; headers: string[] }) {
  return <section className="figma-card figma-table-card"><header><div><small>فهرست نمونه</small><h2>{title}</h2></div><span className="figma-sample-tag">دادهٔ ساختگی</span></header><div className="figma-table-wrap"><table><thead><tr>{headers.map((h)=><th key={h}>{h}</th>)}</tr></thead><tbody>{rows.map((row,i)=><tr key={`${row[0]}-${i}`}>{row.map((cell,j)=><td key={`${j}-${cell}`}>{cell}</td>)}</tr>)}</tbody></table></div></section>;
}
