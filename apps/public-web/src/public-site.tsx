import { useState } from 'react';

const memberAppUrl = import.meta.env.VITE_TARANOM_MEMBER_APP_URL?.trim();
const joinHref = memberAppUrl || '#membership';
const joinLabel = memberAppUrl ? 'عضویت در اپ ترنم' : 'آشنایی با عضویت';

const navItems = [
  { href: '#about', label: 'ترنم چیست' },
  { href: '#values', label: 'مزایای عضویت' },
  { href: '#family', label: 'عضویت خانواده' },
  { href: '#membership', label: 'مسیر عضویت' },
];

const memberBenefits = [
  {
    id: '01',
    title: 'همراهی در مسیر سلامت',
    body: 'آشنایی با شبکهٔ مراکز و پزشکان و پیگیری مسیر درمان در اکوسیستم ترنم.',
    tone: 'blue',
  },
  {
    id: '02',
    title: 'پرونده و نسخه',
    body: 'نسخه‌ها و سوابق مرتبط، در مسیر شخصی و قابل‌پیگیری عضو ترنم.',
    tone: 'mint',
  },
  {
    id: '03',
    title: 'مسیرهای حمایت درمانی',
    body: 'امکان مشاهده و پیگیری حمایت‌های ثبت‌شده در مسیر درمان.',
    tone: 'peach',
  },
  {
    id: '04',
    title: 'دستیار راهنما',
    body: 'کمک به شناخت تخصص و مسیر مراجعه؛ تشخیص بیماری با پزشک است.',
    tone: 'lilac',
  },
];

export function PublicSite() {
  const [menuOpen, setMenuOpen] = useState(false);

  const closeMenu = () => setMenuOpen(false);

  return (
    <>
      <a className="skip-link" href="#main">رفتن به محتوای اصلی</a>
      <header className="site-header">
        <div className="header-inner">
          <a className="brand" href="#top" aria-label="ترنم، صفحهٔ اصلی">
            <img src="/assets/taranom-logo.webp" alt="ترنم، اکوسیستم سلامت" width="140" height="63" />
          </a>

          <button
            className="menu-toggle"
            type="button"
            aria-expanded={menuOpen}
            aria-controls="primary-navigation"
            onClick={() => setMenuOpen((open) => !open)}
          >
            <span aria-hidden="true">{menuOpen ? '×' : '☰'}</span>
            <span className="sr-only">{menuOpen ? 'بستن فهرست' : 'باز کردن فهرست'}</span>
          </button>

          <nav id="primary-navigation" className={`primary-navigation${menuOpen ? ' is-open' : ''}`} aria-label="ناوبری اصلی">
            {navItems.map((item) => (
              <a href={item.href} key={item.href} onClick={closeMenu}>{item.label}</a>
            ))}
          </nav>

          <a className="button button-outline header-join" href={joinHref}>{joinLabel}</a>
        </div>
      </header>

      <main id="main">
        <section className="hero" id="top" aria-labelledby="hero-title">
          <div className="hero-inner">
            <div className="hero-copy">
              <p className="eyebrow">اکوسیستم سلامت ترنم؛ برای شما و خانواده‌تان</p>
              <h1 id="hero-title"><span>سلامتِ معتبر؛</span><span className="headline-accent">دسترسیِ منصفانه</span></h1>
              <p className="hero-description">
                ترنم برای همراهی شما و خانواده‌تان در مسیر سلامت ساخته شده؛ با اعتبار درمانی، دسترسی منصفانه و انتخابی آگاهانه در دست خودتان.
              </p>
              <div className="value-pills" aria-label="ارزش‌های ترنم">
                <span className="value-pill value-pill-blue">اعتبار درمانی</span>
                <span className="value-pill value-pill-mint">عدالت درمانی</span>
              </div>
              <div className="hero-actions">
                <a className="button button-primary" href={joinHref}>{joinLabel}</a>
                <a className="button button-soft" href="#about">ترنم را بشناسید</a>
              </div>
              <p className="choice-note">انتخاب پزشک و مسیر درمان همیشه با شماست.</p>
            </div>

            <div className="ecosystem-art" aria-label="نمای مفهومی عضویت خانواده در اکوسیستم سلامت ترنم">
              <img className="art-orbit art-orbit-outer" src="/assets/ecosystem-outer-orb.svg" width="468" height="468" alt="" />
              <img className="art-orbit art-orbit-inner" src="/assets/ecosystem-inner-orb.svg" width="388" height="388" alt="" />
              <img className="art-dot art-dot-red" src="/assets/accent-dot-red.svg" width="22" height="22" alt="" />
              <img className="art-dot art-dot-teal" src="/assets/accent-dot-teal.svg" width="16" height="16" alt="" />

              <div className="ecosystem-core">
                <img className="core-logo" src="/assets/taranom-logo.webp" width="152" height="68" alt="ترنم" />
                <strong>اکوسیستم سلامت</strong>
                <span className="core-subtitle">عضویت برای شما و خانواده</span>
                <div className="core-nodes" aria-label="مراکز، پرونده و حمایت">
                  <span><img src="/assets/ecosystem-node-1.svg" width="12" height="12" alt="" />مراکز</span>
                  <span><img src="/assets/ecosystem-node-2.svg" width="12" height="12" alt="" />پرونده</span>
                  <span><i aria-hidden="true" />حمایت</span>
                </div>
              </div>

              <div className="floating-card family-float">
                <strong>خانوادهٔ من</strong>
                <span>اعضای همراه در ترنم</span>
                <div className="family-avatars" aria-hidden="true">
                  <img src="/assets/family-avatar-1.svg" width="28" height="28" alt="" />
                  <img src="/assets/family-avatar-2.svg" width="28" height="28" alt="" />
                  <img src="/assets/family-avatar-3.svg" width="28" height="28" alt="" />
                  <b>+</b>
                </div>
              </div>

              <div className="floating-card age-float">
                <img src="/assets/age-icon.svg" width="40" height="40" alt="" />
                <span><strong>حق عضویت متناسب</strong><small>مبلغ بر اساس سن</small></span>
              </div>
            </div>
          </div>
        </section>

        <section className="membership-rules" id="membership" aria-labelledby="membership-title">
          <div className="membership-panel">
            <h2 id="membership-title">عضویت برای شما و خانواده‌تان</h2>
            <div className="rule-cards">
              <article className="rule-card rule-age">
                <h3>حق عضویت بر اساس سن</h3>
                <p>مبلغ هنگام عضویت مشخص می‌شود.</p>
              </article>
              <article className="rule-card rule-family">
                <h3>خانواده و افراد دلخواه</h3>
                <p>همراهانتان را اضافه کنید.</p>
              </article>
              <article className="rule-card rule-sponsor">
                <h3>پرداخت با عضو معرف</h3>
                <p>ثبت‌نام مستقل برای هر فرد.</p>
              </article>
            </div>
          </div>
        </section>

        <section className="values-section content-section" id="values" aria-labelledby="values-title">
          <div className="section-heading">
            <span className="section-kicker">وعدهٔ ترنم</span>
            <h2 id="values-title">اعتبار درمانی و عدالت درمانی؛ دو اصل ترنم</h2>
            <p>ترنم، خدمات سلامت را با شفافیت و دسترسی منصفانه به هم پیوند می‌دهد.</p>
          </div>
          <div className="value-cards">
            <article className="value-card value-card-trust">
              <span className="value-icon" aria-hidden="true">+</span>
              <div><h3>اعتبار درمانی</h3><p>اطلاعات روشن دربارهٔ مسیر درمان، مراکز و زمان‌های در دسترس برای انتخاب آگاهانه.</p></div>
              <strong>انتخاب نهایی پزشک با بیمار است</strong>
            </article>
            <article className="value-card value-card-equity">
              <span className="value-icon" aria-hidden="true">✓</span>
              <div><h3>عدالت درمانی</h3><p>دیدن مسیرهای حمایتی و سهم‌های ثبت‌شده در کنار خدمات درمانی، برای کاهش موانع دسترسی.</p></div>
              <strong>مسیر حمایت شفاف و قابل‌پیگیری</strong>
            </article>
          </div>
        </section>

        <section className="benefits-section content-section" id="about" aria-labelledby="benefits-title">
          <div className="section-heading">
            <span className="section-kicker">عضویت در ترنم</span>
            <h2 id="benefits-title">همراهی‌های ترنم برای اعضا</h2>
            <p>یک اکوسیستم سلامت برای دسترسی، پیگیری و حمایت در مسیر درمان.</p>
          </div>
          <div className="benefit-grid">
            {memberBenefits.map((benefit) => (
              <article className={`benefit-card tone-${benefit.tone}`} key={benefit.id}>
                <span className="benefit-index">{benefit.id}</span>
                <h3>{benefit.title}</h3>
                <p>{benefit.body}</p>
              </article>
            ))}
          </div>
        </section>

        <section className="family-section" id="family" aria-labelledby="family-title">
          <div className="family-inner">
            <div className="family-copy">
              <span className="family-kicker">برای خودتان و عزیزانتان</span>
              <h2 id="family-title">یک عضویت؛ همراهی برای خانواده</h2>
              <p>می‌توانید خانواده یا هر فرد دلخواه را به زیرمجموعهٔ خود اضافه کنید. هر عضو حساب مستقل دارد و حق عضویت او را معرف پرداخت می‌کند.</p>
              <ul>
                <li>افزودن اعضای خانواده یا افراد دلخواه</li>
                <li>ثبت‌نام مستقل زیرمجموعه با پرداخت معرف</li>
              </ul>
              <a className="button button-light" href="#membership">قواعد عضویت خانواده</a>
            </div>
            <div className="family-visual" aria-label="نمونهٔ مفهومی عضویت همراهان">
              <div className="family-visual-header">
                <span className="family-visual-icon" aria-hidden="true">⌂</span>
                <span><strong>عضویت گروهی در ترنم</strong><small>هر عضو حساب و دسترسی مستقل خودش را دارد</small></span>
              </div>
              <div className="family-visual-row"><span>افراد دعوت‌شده</span><b>حساب مستقل</b></div>
              <div className="family-visual-row"><span>پرداخت حق عضویت</span><b>با معرف</b></div>
              <p>مبلغ حق عضویت هر فرد بر اساس سن او مشخص می‌شود.</p>
            </div>
          </div>
        </section>

        <section className="steps-section content-section" aria-labelledby="steps-title">
          <div className="section-heading">
            <span className="section-kicker">شروع ساده</span>
          <h2 id="steps-title">مسیر عضویت برای شما و همراهانتان</h2>
          <p>شرایط عضویت، سهم پرداخت‌کننده و مسیر شروع را شفاف ببینید.</p>
          </div>
          <ol className="step-grid">
            <li className="step-card"><span>۱</span><h3>آشنایی با عضویت</h3><p>شرایط و همراهی‌های اکوسیستم سلامت ترنم را مرور کنید.</p></li>
            <li className="step-card"><span>۲</span><h3>حق عضویت متناسب با سن</h3><p>مبلغ عضویت هر فرد بر اساس سن او مشخص می‌شود.</p></li>
            <li className="step-card"><span>۳</span><h3>عضویت همراهان</h3><p>معرف می‌تواند هزینهٔ عضویت افراد دلخواهش را پرداخت کند.</p></li>
          </ol>
          <div className="app-handoff">
            <div><h3>ترنم را برای خود و خانواده‌تان بشناسید</h3><p>{memberAppUrl ? 'برای ادامهٔ فرایند عضویت، وارد اپلیکیشن ترنم شوید.' : 'جزئیات عضویت و همراهی خانواده را در این صفحه ببینید.'}</p></div>
            <a className="button button-primary" href={joinHref}>{joinLabel}</a>
          </div>
        </section>
      </main>

      <footer className="site-footer">
        <div className="footer-inner">
          <div className="footer-brand"><strong>ترنم · اکوسیستم سلامت</strong><span>اعتبار درمانی، دسترسی منصفانه</span></div>
          <p>راهنمای هوشمند جایگزین تشخیص پزشک نیست.</p>
          <nav aria-label="پیوندهای پایانی">
            <a href="#about">ترنم چیست</a><a href="#values">مزایای عضویت</a><a href="#membership">مسیر عضویت</a>
          </nav>
        </div>
      </footer>
    </>
  );
}