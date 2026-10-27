import { useEffect, useRef, useState, type MouseEvent } from 'react';
import {ReleaseSourcePanel} from './release-source-panel';

type SectionKey = 'overview' | 'departments' | 'doctors' | 'appointments' | 'qualifications' | 'audit';
type ActionKey = 'department' | 'doctor' | 'assignment' | 'end-affiliation';
type Section = { key: SectionKey; path: string; title: string; kicker: string; description: string; symbol: string };

export const centerSections: readonly Section[] = [
  { key: 'overview', path: '/center', title: 'نمای کلی مرکز', kicker: 'مدیریت یک مرکز درمانی',
    description: 'نمای یکپارچه مرکز، بخش‌ها و پزشکان آن تنها پس از اتصال به مرجع معتبر مرکز قابل ارائه است.', symbol: '⌂' },
  { key: 'departments', path: '/center/departments', title: 'بخش‌های مرکز', kicker: 'ساختار داخلی مرکز',
    description: 'هر مرکز می‌تواند چند بخش داشته باشد؛ بیمارستان زنجیره‌ای نیز در این مدل یک مرکز با بخش‌های مختلف است.', symbol: '▦' },
  { key: 'doctors', path: '/center/doctors', title: 'پزشکان وابسته', kicker: 'مدیریت ارتباط پزشک و مرکز',
    description: 'مدیر مجاز مرکز در آینده می‌تواند پزشکان همان مرکز را معرفی کند؛ این اقدام تأیید صلاحیت یا فعال‌سازی پزشک نیست.', symbol: '♧' },
  { key: 'appointments', path: '/center/appointments', title: 'نوبت‌های مرکز', kicker: 'هماهنگی برنامه پزشکان',
    description: 'برنامه و درخواست نوبت مرکز فقط با اتصال معتبر به هویت مرکز، پزشکان واجدصلاحیت و ظرفیت واقعی نمایش داده می‌شود.', symbol: '◷' },
  { key: 'qualifications', path: '/center/qualifications', title: 'بررسی صلاحیت', kicker: 'کنترل‌های مستقل حرفه‌ای',
    description: 'ثبت وابستگی یک پزشک با تأیید هویت، پروانه پزشکی، قرارداد ترنم و حق دسترسی درمانی تفاوت دارد.', symbol: '◇' },
  { key: 'audit', path: '/center/audit', title: 'ردپای فعالیت', kicker: 'تاریخچه و ممیزی تغییرات',
    description: 'ثبت هر تغییر و مسئول آن نیازمند هویت مرکز، مرجع معتبر و مسیر ممیزی قابل اعتماد است.', symbol: '▤' },
] as const;

const actionText: Record<ActionKey, { title: string; description: string }> = {
  department: {
    title: 'تعریف بخش جدید',
    description: 'ثبت بخش فقط پس از شناسایی مرکز، احراز اختیار مدیر و آماده‌بودن ذخیره‌سازی و ممیزی معتبر فعال می‌شود.',
  },
  doctor: {
    title: 'معرفی پزشک وابسته',
    description: 'معرفی پزشک توسط مرکز تنها یک درخواست رابطه است؛ تأیید صلاحیت و اعتبار حرفه‌ای پزشک و قرارداد ترنم باید جداگانه احراز شوند.',
  },
  assignment: {
    title: 'انتساب پزشک به بخش',
    description: 'برای انتساب، بخش و رابطه پزشک با همین مرکز باید از منبع معتبر تأیید شوند.',
  },
  'end-affiliation': {
    title: 'پایان ارتباط پزشک با مرکز',
    description: 'قطع ارتباط نیازمند ثبت ممیزی و لغو دسترسی‌های مرتبط در مرجع مسئول است؛ این نمونه هیچ رابطه واقعی را تغییر نمی‌دهد.',
  },
};

// TRN-BIZ-044/045: organizational responsibilities, NOT actual licensed
// provider review records or notified medical incidents.
const qualityOversightLanes = [
  {
    key: 'taranom',
    label: 'نظارت ترنم بر الزامات یکسان کیفیت',
    owner: 'ترنم: تعریف الزامات واحد و پیگیری رعایت در دو مسیر مستقیم و شبکه‌ای',
    limit: 'هیچ سیاست عملیاتی، ارزیابی رسمی، معیار عددی یا تأیید صلاحیت موجود نیست.'
  },
  {
    key: 'network',
    label: 'کنترل ارائه‌دهندگان توسط شبکه همکار',
    owner: 'شبکه درمانی واجدصلاحیت: کنترل ارائه‌دهنده فقط در حدود قانون و قرارداد معتبر',
    limit: 'هیچ تفویض اختیار، پروانه، قرارداد شبکه یا ارزیابی واقعی تأیید نشده است.'
  },
  {
    key: 'incident',
    label: 'ارجاع تخصصی شکایت یا حادثه پزشکی',
    owner: 'مرجع تخصصی پزشکی و حقوقی ذی‌صلاح: رسیدگی به حادثه و مسئولیت حرفه‌ای',
    limit: 'ثبت یادداشت داخلی به‌معنای ارسال ارجاع، تعیین تقصیر یا حل شکایت نیست.'
  }
] as const;

function resolveSection(): Section | undefined {
  const path = window.location.pathname.replace(/\/$/, '') || '/';
  return centerSections.find(item => item.path === path);
}

function LockedCard({ title, message }: { title: string; message: string }) {
  return (
    <section className="locked-card" role="status" aria-label="بخش متصل نیست">
      <div className="locked-symbol" aria-hidden="true">⌁</div>
      <div>
        <p className="eyebrow">داده معتبر دریافت نشده</p>
        <h2>{title}</h2>
        <p>{message}</p>
        <p className="muted">نبود اتصال به معنی «صفر بخش» یا «صفر پزشک» نیست.</p>
      </div>
    </section>
  );
}

export function CenterApp() {
  const [section, setSection] = useState<Section | undefined>(resolveSection);
  const [mobileMenu, setMobileMenu] = useState(false);
  const [action, setAction] = useState<ActionKey | null>(null);
  const triggerRef = useRef<HTMLButtonElement | null>(null);
  const closeActionRef = useRef<HTMLButtonElement>(null);
  const menuButtonRef = useRef<HTMLButtonElement>(null);
  const focusOnRoute = useRef(false);

  useEffect(() => {
    const sync = () => {
      focusOnRoute.current = true;
      setSection(resolveSection());
      setMobileMenu(false);
      // Browser Back/Forward must not leave a stale action dialog on another page.
      setAction(null);
    };
    window.addEventListener('popstate', sync);
    return () => window.removeEventListener('popstate', sync);
  }, []);
  useEffect(() => {
    document.title = section ? 'ترنم | ' + section.title + ' — متصل نیست' : 'ترنم | صفحه مرکز پیدا نشد';
    if (focusOnRoute.current) {
      focusOnRoute.current = false;
      document.getElementById(section ? 'section-title' : 'missing-title')?.focus();
    }
  }, [section]);
  useEffect(() => {
    if (action) closeActionRef.current?.focus();
  }, [action]);
  useEffect(() => {
    if (!mobileMenu && !action) return;
    const onKey = (e: KeyboardEvent) => {
      if (action) {
        // Only the non-mutating close control is enabled in this dialog.
        // Keep both Tab directions inside the modal instead of moving focus
        // to underlying navigation, disabled save, or browser content.
        if (e.key === 'Tab') {
          e.preventDefault();
          closeActionRef.current?.focus();
        } else if (e.key === 'Escape') {
          e.preventDefault();
          dismissAction();
        }
        return;
      }
      if (e.key === 'Escape') {
        setMobileMenu(false);
        menuButtonRef.current?.focus();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [mobileMenu, action]);

  function navigate(e: MouseEvent<HTMLAnchorElement>, path: string) {
    // Inert background also blocks physical interaction; this is a second
    // guard against synthetic/programmatic navigation while the dialog is open.
    if (action) { e.preventDefault(); return; }
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.altKey || e.shiftKey) return;
    e.preventDefault();
    setMobileMenu(false);
    if (window.location.pathname === path) {
      document.getElementById(section ? 'section-title' : 'missing-title')?.focus();
      return;
    }
    window.history.pushState({}, '', path);
    focusOnRoute.current = true;
    setSection(resolveSection());
  }
  function showAction(e: MouseEvent<HTMLButtonElement>, next: ActionKey) {
    triggerRef.current = e.currentTarget;
    setMobileMenu(false);
    setAction(next);
  }
  function dismissAction() {
    setAction(null);
    // Restore focus after the modal is no longer rendered.
    queueMicrotask(() => triggerRef.current?.focus());
  }

  return (
    <div className="center-shell">
      <a className="skip-link" href="#center-main"
        tabIndex={action ? -1 : undefined} aria-hidden={action ? true : undefined}>
        پرش به محتوای اصلی
      </a>
      <aside className="sidebar" aria-label="پنل مرکز درمانی" inert={Boolean(action)}>
        <div className="brand"><div className="brand-logo" aria-hidden="true">ت</div>
          <div><strong>ترنم</strong><span>فضای مرکز درمانی</span></div>
        </div>
        <div className="sidebar-caption">مدیریت مرکز <span>پیش‌نمایش</span></div>
        <button className="mobile-toggle" ref={menuButtonRef}
          type="button" aria-expanded={mobileMenu} aria-controls="center-navigation"
          onClick={() => setMobileMenu(x => !x)}>
          {mobileMenu ? 'بستن فهرست' : 'باز کردن فهرست'} <span aria-hidden="true">{mobileMenu ? '×' : '☰'}</span>
        </button>
        <nav id="center-navigation" className={'navigation' + (mobileMenu ? ' expanded' : '')}
          aria-label="بخش‌های پنل مرکز">
          {centerSections.map(item => (
            <a key={item.key} href={item.path}
              aria-current={section?.key === item.key ? 'page' : undefined}
              className={'nav-item' + (section?.key === item.key ? ' active' : '')}
              onClick={e => navigate(e, item.path)}>
              <span className="nav-icon" aria-hidden="true">{item.symbol}</span>
              {item.title} <span className="nav-arrow" aria-hidden="true">‹</span>
            </a>
          ))}
        </nav>
        <div className="sidebar-bottom">امنیت و حقوق بیمار
          <span>هیچ پزشک یا مرکز واقعی در این محیط ثبت نشده است.</span>
        </div>
      </aside>

      <div className="workspace" inert={Boolean(action)}>
        <header className="topbar">
          <div className="breadcrumbs">ترنم <span>/</span> مرکز درمانی <span>/</span> {section?.title ?? 'صفحه نامعتبر'}</div>
          <span className="connection-state"><i aria-hidden="true"></i> بدون اتصال عملیاتی</span>
        </header>
        <main id="center-main" tabIndex={-1}>
          {!section ? (
            <section className="not-found">
              <p className="eyebrow">آدرس نامعتبر</p>
              <h1 id="missing-title" tabIndex={-1}>صفحه مرکز پیدا نشد</h1>
              <p>در این مسیر هیچ داده سازمانی یا درمانی نمایش داده نمی‌شود.</p>
              <a href="/center" onClick={e => navigate(e, '/center')}>بازگشت به نمای کلی مرکز</a>
            </section>
          ) : (
            <>
              <div className="preview-strip" role="note">
                <span>پیش‌نمایش ساختاری</span>
                بدون ورود واقعی، بدون پزشک نمونه، بدون اطلاعات بیمار
              </div>
              <ReleaseSourcePanel audience="CENTER"/>
              <section className="hero">
                <div className="hero-copy">
                  <p className="hero-kicker">پنل مرکز درمانی / {section.kicker}</p>
                  <h1 id="section-title" tabIndex={-1}>{section.title}</h1>
                  <p>{section.description}</p>
                  <span className="status-chip">مدیر مرکز هنوز احراز نشده است</span>
                </div>
                <div className="hero-art" aria-hidden="true"><span>✚</span></div>
              </section>

              {section.key === 'overview' ? (
                <>
                  {import.meta.env.VITE_TARANOM_LOCAL_QA_HOP === '1' &&
                    window.location.hostname === '127.0.0.1' &&
                    window.location.port === '4318' && (
                    <nav aria-label="گردش‌کار آزمایشی ایزوله" className="module">
                      <h2>آزمایش گردش‌کار مرکز در PostgreSQL ایزوله</h2>
                      <p>این مسیر فقط برای CI توسعه است؛ هیچ مدیر، پزشک، مجوز یا خدمت درمانی واقعی احراز نمی‌شود.</p>
                      <a href="http://127.0.0.1:4320/">
                        آزمایش رویدادهای مرکز با PostgreSQL
                      </a>
                    </nav>
                  )}
                  <div className="section-heading"><div><p className="eyebrow">ساختار مرکز</p><h2>مدیریت یکپارچه و محدود به همان مرکز</h2></div></div>
                  <div className="tiles">
                    <article className="tile"><span className="tile-mark">▦</span><h3>بخش‌ها</h3>
                      <p>ساختار داخلی هر مرکز با چند بخش؛ بدون حساب مادر یا شبکه درمانی مستقل.</p>
                      <a href="/center/departments" onClick={e => navigate(e, '/center/departments')}>مشاهده بخش‌ها ←</a></article>
                    <article className="tile"><span className="tile-mark">♧</span><h3>پزشکان وابسته</h3>
                      <p>ثبت ارتباط پزشک با مرکز از تأیید صلاحیت حرفه‌ای جدا است.</p>
                      <a href="/center/doctors" onClick={e => navigate(e, '/center/doctors')}>مشاهده پزشکان ←</a></article>
                    <article className="tile"><span className="tile-mark">◇</span><h3>بررسی صلاحیت</h3>
                      <p>هیچ مجوز پزشکی، قرارداد ترنم یا حق دسترسی به پرونده بیمار فرض نشده است.</p>
                      <a href="/center/qualifications" onClick={e => navigate(e, '/center/qualifications')}>مشاهده گیت‌ها ←</a></article>
                  </div>
                </>
              ) : null}

              {section.key === 'departments' ? (
                <section aria-labelledby="department-heading" className="module">
                  <div className="section-heading"><div><p className="eyebrow">مرکز با چند بخش</p><h2 id="department-heading">فهرست بخش‌های مرکز</h2></div>
                    <button type="button" onClick={e => showAction(e, 'department')}>+ تعریف بخش</button></div>
                  <LockedCard title="بخش‌های مرکز هنوز قابل مشاهده نیستند"
                    message="ثبت بخش و مشاهده فهرست آن فقط پس از تأیید مرجع مرکز و مجوز مدیر ممکن خواهد بود."/>
                </section>
              ) : null}

              {section.key === 'doctors' ? (
                <section aria-labelledby="doctors-heading" className="module">
                  <div className="section-heading"><div><p className="eyebrow">وابستگی پزشکان</p><h2 id="doctors-heading">پزشکان وابسته به همین مرکز</h2></div>
                    <button type="button" onClick={e => showAction(e, 'doctor')}>+ معرفی پزشک</button></div>
                  <LockedCard title="اطلاعات پزشکان مرکز متصل نیست"
                    message="هیچ پزشک نمونه یا رابطه فعال ساختگی نمایش داده نمی‌شود. مرکز دیگر نیز در این پنل قابل مدیریت نیست."/>
                  <div className="action-line">
                    <button type="button" className="secondary" onClick={e => showAction(e, 'assignment')}>انتساب پزشک به بخش</button>
                    <button type="button" className="secondary" onClick={e => showAction(e, 'end-affiliation')}>پایان همکاری پزشک</button>
                  </div>
                </section>
              ) : null}

              {section.key === 'appointments' ? (
                <section className="module" aria-labelledby="appointments-heading">
                  <div className="section-heading">
                    <div>
                      <p className="eyebrow">نوبت‌دهی از خدمات اصلی ترنم</p>
                      <h2 id="appointments-heading">برنامه و درخواست‌های نوبت</h2>
                    </div>
                  </div>
                  <LockedCard title="برنامه نوبت‌دهی مرکز متصل نیست"
                    message="برای ثبت یا تغییر نوبت باید هویت مجاز مرکز، پزشک واجدصلاحیت، برنامه واقعی و منبع پایدار نوبت‌دهی به ترنم متصل باشند. در این نسخه هیچ ظرفیت یا نوبتی ساخته نمی‌شود."/>
                  <p className="care-warning">
                    وابستگی پزشک به مرکز به‌تنهایی تأیید صلاحیت بالینی یا اجازه پذیرش نوبت نیست.
                  </p>
                </section>
              ) : null}

              {section.key === 'qualifications' ? (
                <section className="module" aria-labelledby="qualification-heading">
                  <div className="section-heading"><div><p className="eyebrow">گیت‌های مستقلی که هنوز متصل نیستند</p>
                    <h2 id="qualification-heading">ثبت وابستگی ≠ تأیید صلاحیت</h2></div></div>
                  <ol className="process">
                    <li><span>۱</span><div><strong>ثبت ارتباط پزشک با مرکز</strong><p>در نسخه واقعی باید توسط مدیر مجاز همان مرکز انجام شود.</p></div><em>متصل نیست</em></li>
                    <li><span>۲</span><div><strong>احراز هویت و پروانه حرفه‌ای</strong><p>نیازمند مرجع قانونی و تأیید مستقل خارج از اختیار صرف مرکز است.</p></div><em>متصل نیست</em></li>
                    <li><span>۳</span><div><strong>احراز قرارداد و حدود خدمت</strong><p>قرارداد معتبر ترنم، صلاحیت خدمت و مسئول پرداخت باید جداگانه بررسی شوند.</p></div><em>متصل نیست</em></li>
                    <li><span>۴</span><div><strong>مجوز دسترسی درمانی</strong><p>حقوق بیمار، مبنای قانونی و نیاز مرتبط با خدمت مستقلاً کنترل می‌شوند.</p></div><em>متصل نیست</em></li>
                  </ol>
                  <div className="section-heading">
                    <div><p className="eyebrow">شبکه درمانی ترکیبی</p>
                      <h2 id="provider-network-paths">دو مسیر مستقل احراز صلاحیت و قرارداد</h2></div>
                  </div>
                  <div className="tiles" role="list" aria-label="مسیرهای شبکه درمانی">
                    <article className="tile" role="listitem">
                      <h3>قرارداد مستقیم با ارائه‌دهنده</h3>
                      <p>احراز صلاحیت حرفه‌ای و مجوز خدمتِ همان پزشک یا مرکز باید مستقل از معرفی داخلی بررسی شود.</p>
                      <p>قرارداد معتبر ترنم و مسئولیت حقوقی هر خدمت هنوز احراز نشده‌اند.</p>
                      <strong>شواهد معتبر موجود نیست</strong>
                    </article>
                    <article className="tile" role="listitem">
                      <h3>همکاری با شبکه درمانی واجدصلاحیت</h3>
                      <p>مجوز و قرارداد شبکه، کنترل ارائه‌دهندگان زیرمجموعه و نظارت ترنم سه موضوع جدا هستند.</p>
                      <p>تأیید شبکه به معنی پروانه مستقلِ هر پزشک یا مرکز و اختیار پرداخت نیست.</p>
                      <strong>شواهد معتبر موجود نیست</strong>
                    </article>
                  </div>
                  <div className="module" aria-labelledby="payee-gate-title">
                    <h2 id="payee-gate-title">گیرنده اعتبار درمان، قرارداد مستقل لازم دارد</h2>
                    <p>حتی در مسیر همکاری شبکه‌ای، دریافت‌کننده وجه باید ارائه‌دهنده مجاز و دارای قرارداد معتبر مستقیم با ترنم باشد.
                      قرارداد شبکه، جای قرارداد گیرنده پرداخت را نمی‌گیرد.</p>
                    <p>هیچ ارائه‌دهنده، پروانه، قرارداد، دریافت‌کننده وجه یا خدمت واقعی در این پیش‌نمایش تأیید نشده است.</p>
                    <button type="button" disabled>بررسی شواهد شبکه — نیازمند مرجع قانونی و قرارداد واقعی</button>
                  </div>
                  <p className="care-warning">پزشک صرفاً با تعریف در مرکز، اجازه مشاهده پرونده بیماران یا دریافت وجه درمانی پیدا نمی‌کند.</p>
                  <section className="module" aria-labelledby="provider-quality-oversight-title">
                    <div className="section-heading">
                      <div>
                        <p className="eyebrow">کیفیت شبکه با اختیار مستقل رسیدگی به حوادث متفاوت است</p>
                        <h2 id="provider-quality-oversight-title">مسئولیت‌های جداگانه نظارت کیفیت و پاسخ‌گویی</h2>
                      </div>
                    </div>
                    <div className="tiles" role="list" aria-label="مسیرهای نظارت کیفیت و رسیدگی پزشکی">
                      {qualityOversightLanes.map((item) => (
                        <article key={item.key} className="tile" role="listitem">
                          <h3>{item.label}</h3>
                          <strong>شواهد معتبر موجود نیست</strong>
                          <p>{item.owner}</p>
                          <p>{item.limit}</p>
                        </article>
                      ))}
                    </div>
                    <p className="muted" id="provider-quality-oversight-limit">
                      معرفی پزشک، یادداشت نظارت، قرارداد شبکه یا پیگیری شکایت، مجوز ارائه خدمت،
                      امتیاز کیفیت، تعلیق خودکار، مجوز دیدن پرونده بیمار یا تأیید صلاحیت نیست.
                      ثبت شکایت عضو و تصمیم تخصصی پزشکی مسیرهای مستقلی دارند.
                    </p>
                    <button type="button" disabled aria-describedby="provider-quality-oversight-limit">
                      بررسی ردپای کیفیت — نیازمند اسناد و ناظر واقعی
                    </button>
                    <button type="button" disabled aria-describedby="provider-quality-oversight-limit">
                      ارسال ارجاع حادثه پزشکی — فقط مرجع قانونی و بالینی معتبر
                    </button>
                    <p className="care-warning">
                      در فوریت پزشکی، کمک فوری و رسیدگی بالینی را به ثبت پرونده اداری یا پیگیری شکایت موکول نکنید.
                    </p>
                  </section>
                </section>
              ) : null}

              {section.key === 'audit' ? (
                <section className="module" aria-labelledby="audit-heading">
                  <div className="section-heading"><div><p className="eyebrow">قابلیت ممیزی</p><h2 id="audit-heading">تاریخچه تغییرات مرکز</h2></div></div>
                  <LockedCard title="سوابق ممیزی واقعی در دسترس نیستند"
                    message="بعد از اتصال معتبر، ثبت‌کننده، مرکز مربوط، نسخه تغییر و رویدادهای ثبت و قطع ارتباط باید از مرجع مجاز دریافت شوند."/>
                </section>
              ) : null}
              <section className="privacy-band">
                <strong>مرز دسترسی محفوظ است</strong>
                <span>پنل مرکز فقط برای همان مرکز طراحی شده است. این نسخه نه احراز هویت می‌کند، نه پزشک ایجاد می‌کند و نه درخواست شبکه‌ای می‌فرستد.</span>
              </section>
            </>
          )}
        </main>
        <footer className="footer">ترنم · پنل ساختاری مرکز درمانی <span>بدون سرویس و داده واقعی</span></footer>
      </div>
      {action && (
        <div className="modal-backdrop">
          <section className="modal" role="dialog" aria-modal="true" aria-labelledby="modal-title"
            aria-describedby="modal-description">
            <span className="modal-symbol" aria-hidden="true">!</span>
            <p className="eyebrow">عملیات هنوز فعال نیست</p>
            <h2 id="modal-title">{actionText[action].title}</h2>
            <p id="modal-description">{actionText[action].description}</p>
            <div className="modal-warning">بدون احراز هویت و اتصال معتبر، هیچ تغییری ذخیره نمی‌شود.</div>
            <div className="modal-actions">
              <button type="button" disabled>ثبت در سامانه — غیرفعال</button>
              <button type="button" className="secondary" ref={closeActionRef} onClick={dismissAction}>بازگشت به پنل</button>
            </div>
          </section>
        </div>
      )}
    </div>
  );
}
