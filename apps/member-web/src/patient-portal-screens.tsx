import type { MouseEvent } from 'react';

export type PatientPortalScreenKey = 'appointments' | 'records' | 'prescriptions' | 'credit' | 'family' | 'referrals' | 'account';
type Navigate = (event: MouseEvent<HTMLAnchorElement>, href: string) => void;

export function PatientPortalScreen({ pageKey, go }: { pageKey: PatientPortalScreenKey; go: Navigate }) {
  const goTo = (event: MouseEvent<HTMLAnchorElement>, href: string) => go(event, href);

  if (pageKey === 'appointments') return (
    <section className="portal-page" aria-labelledby="portal-page-title">
      <header className="portal-page-heading"><div><h1 id="portal-page-title" tabIndex={-1}>نوبت‌های من</h1><p>درخواست‌ها و نوبت‌های ثبت‌شدهٔ خود را دنبال کنید.</p></div><button type="button" disabled>گرفتن نوبت جدید</button></header>
      <section className="portal-search-card" aria-label="جست‌وجوی پزشک یا مرکز درمانی">
        <strong>جست‌وجوی پزشک یا مرکز درمانی</strong><div className="portal-search-fields"><span><small>شهر</small><b>تهران</b></span><span><small>تخصص</small><b>متخصص داخلی</b></span><button type="button" disabled>جست‌وجو</button></div>
      </section>
      <div className="portal-page-subtitle"><h2>وضعیت نوبت‌ها</h2><span>نمونهٔ نمایشی</span></div>
      <div className="appointment-stats"><article><small>نوبت‌های انجام‌شده</small><strong>۱۲</strong><span>در سوابق شما</span></article><article><small>در انتظار پاسخ</small><strong>۱</strong><span>درخواست ثبت‌شده</span></article><article><small>نوبت‌های آینده</small><strong>۱</strong><span>در انتظار مراجعه</span></article></div>
      <section className="portal-list-section"><h2>نوبت‌های پیش رو</h2><article className="appointment-card"><div className="appointment-card-main"><span className="sample-status">تأیید شده · نمونه</span><h3>دکتر سارا رضایی</h3><p>متخصص داخلی</p><strong>۲۴ آبان ۱۴۰۵ · ساعت ۱۱:۰۰</strong><small>درمانگاه مهر ترنم · تهران</small></div><div className="appointment-card-actions"><a href="/member/appointments" onClick={(event) => goTo(event, '/member/appointments')}>جزئیات نوبت</a><button type="button" disabled aria-describedby="appointment-sample-note">لغو نوبت</button></div></article></section>
      <section className="portal-list-section"><h2>در انتظار پاسخ مرکز</h2><article className="request-card"><span className="pending-status">در حال بررسی</span><div><strong>دکتر امیر کاظمی · متخصص قلب و عروق</strong><small>بیمارستان مهر ترنم · ۲۶ آبان · ساعت ۱۰:۳۰</small></div></article></section>
      <section className="portal-list-section"><h2>سوابق نوبت</h2><div className="portal-table"><div><span>۲۹ مهر</span><strong>ویزیت انجام‌شده</strong><small>دکتر سارا رضایی</small><em>انجام‌شده</em></div><div><span>۲۵ مهر</span><strong>لغو نوبت توسط بیمار</strong><small>درمانگاه مهر ترنم</small><em>لغوشده</em></div></div></section>
      <p className="portal-sample-note" id="appointment-sample-note">تمام نام‌ها، زمان‌ها و وضعیت‌ها نمونه‌اند. ثبت، تأیید یا لغو نوبت هنوز به سرویس واقعی نوبت‌دهی متصل نیست.</p>
    </section>
  );

  if (pageKey === 'records' || pageKey === 'prescriptions') return (
    <section className="portal-page" aria-labelledby="portal-page-title">
      <header className="portal-page-heading"><div><h1 id="portal-page-title" tabIndex={-1}>نسخه‌ها و پرونده</h1><p>نسخه‌های ثبت‌شده و سوابق درمانی خود را مرور کنید.</p></div><span className="portal-date-chip">آخرین به‌روزرسانی: ۲۹ مهر</span></header>
      <div className="record-stats"><article><small>مدارک پیوست</small><strong>۳</strong><span>فایل نمونه</span></article><article><small>سوابق درمانی</small><strong>۴</strong><span>رویداد نمونه</span></article><article><small>نسخه‌های ثبت‌شده</small><strong>۲</strong><span>نسخهٔ نمونه</span></article></div>
      <div className="portal-tabs"><a className="active" href="/member/prescriptions" onClick={(event) => goTo(event, '/member/prescriptions')}>نسخه‌های من</a><a href="/member/records" onClick={(event) => goTo(event, '/member/records')}>سوابق درمان</a></div>
      <section className="portal-list-section"><h2>آخرین نسخه</h2><article className="prescription-card"><div className="prescription-head"><span className="sample-status">ثبت‌شده در ترنم · نمونه</span><span>۲۹ مهر · اطلاعات نسخه نمونه</span></div><strong className="prescription-id">RX-TRN-2048</strong><small>شما هستید · نسخهٔ نمونه</small><div className="medicine-list"><div><b>Medicine A 250 mg</b><span>Take as directed by physician</span></div><div><b>Medicine B 10 mg</b><span>Use as prescribed</span></div></div><button type="button" disabled>مشاهده نسخه</button><span className="prescription-english-note">نام داروها و دستور مصرف به انگلیسی</span></article></section>
      <section className="portal-list-section"><h2>سوابق درمانی</h2><div className="portal-table"><div><span>۲۹ مهر</span><strong>ثبت نسخه نمونه</strong><small>دکتر سارا رضایی · درمانگاه مهر ترنم</small><em>نسخه</em></div><div><span>۲۵ مهر</span><strong>ویزیت درمانی نمونه</strong><small>مرکز درمان مهر ترنم</small><em>ویزیت</em></div><div><span>۱۸ مهر</span><strong>افزودن مدرک نمونه</strong><small>پرونده سلامت</small><em>مدرک</em></div></div></section>
      <p className="portal-warning">این صفحه و اطلاعات درمانی آن صرفاً نمونهٔ نمایشی هستند و برای استفاده درمانی کاربرد ندارند.</p>
    </section>
  );

  if (pageKey === 'credit') return (
    <section className="portal-page" aria-labelledby="portal-page-title">
      <header className="portal-page-heading"><div><h1 id="portal-page-title" tabIndex={-1}>حمایت درمانی</h1><p>اعتبار حمایتی و گردش مالی مرتبط با خدمات درمانی را ببینید.</p></div></header>
      <section className="wallet-hero"><div><span>اعتبار حمایتی ترنم · نمونه</span><strong>۲٬۵۰۰٬۰۰۰ تومان</strong><small>موجودی نمایشی حساب نمونه</small></div><div className="wallet-used"><small>استفاده از اعتبار</small><strong>۸۰۰٬۰۰۰ تومان</strong><span>نمونهٔ نمایشی</span></div></section>
      <section className="portal-list-section"><h2>نمونهٔ تفکیک هزینهٔ درمان</h2><div className="portal-table"><div><span>۲۹ مهر · دادهٔ نمایشی</span><strong>ویزیت متخصص داخلی · درمانگاه مهر ترنم</strong><small>هزینهٔ خدمت طبق سند صادرکننده</small><em>نمونه</em></div><div><span>منبع بیمه</span><strong>بیمهٔ پایه و تکمیلی</strong><small>سهم بیمه از API همان بیمه‌گر دریافت می‌شود</small><em>متصل نیست</em></div><div><span>سهم ترنم</span><strong>اعتبار حمایتی ترنم</strong><small>ارزش‌ها در این نمونه واقعی نیستند</small><em>نمونه</em></div></div></section>
      <section className="portal-list-section"><h2>گردش اعتبار</h2><div className="portal-table"><div><span>۲۹ مهر</span><strong>استفاده از اعتبار برای خدمت نمونه</strong><small>− ۸۰۰٬۰۰۰ تومان · نمایشی</small><em>مصرف</em></div><div><span>۲۴ مهر</span><strong>افزایش اعتبار نمونه</strong><small>+ ۲٬۵۰۰٬۰۰۰ تومان · نمایشی</small><em>ثبت‌شده</em></div></div></section>
      <p className="portal-warning">مبالغ این صفحه ساختگی و صرفاً برای نمایش‌اند. ترنم سهم بیمه را محاسبه نمی‌کند؛ مبلغ و استحقاق را بیمه‌گر از طریق API خودش اعلام می‌کند.</p>
    </section>
  );

  if (pageKey === 'family') return (
    <section className="portal-page" aria-labelledby="portal-page-title">
      <header className="portal-page-heading"><div><h1 id="portal-page-title" tabIndex={-1}>اعضای خانواده</h1><p>افرادی که می‌خواهید در زیرمجموعهٔ خود مدیریت کنید.</p></div><button type="button" disabled>افزودن عضو جدید</button></header>
      <div className="record-stats"><article><small>اعضای ثبت‌شده</small><strong>۳</strong><span>در فهرست نمونه</span></article><article><small>عضویت‌های فعال</small><strong>۳</strong><span>با پرداخت نمونه</span></article><article><small>دعوت در انتظار</small><strong>۱</strong><span>در انتظار تکمیل ثبت‌نام</span></article></div>
      <section className="portal-list-section"><h2>اعضای ترنم</h2><div className="portal-table family-table"><div><span className="family-member-name">نیلوفر رضایی</span><strong>عضو اصلی</strong><small>۳۳ سال</small><em>عضویت نمونه</em></div><div><span className="family-member-name">آزاد رضایی</span><strong>فرزند</strong><small>۸ سال</small><em>معرف پرداخت‌کننده</em></div><div><span className="family-member-name">کیان رضایی</span><strong>فرزند</strong><small>۲ سال</small><em>معرف پرداخت‌کننده</em></div><div><span className="family-member-name">مریم احمدی</span><strong>دعوت‌شده</strong><small>۳۹ سال</small><em>در انتظار ثبت‌نام</em></div></div></section>
      <p className="portal-info-note">هر فرد می‌تواند حساب مستقل خودش را داشته باشد. حق عضویت هر نفر بر اساس سن او تعیین می‌شود و معرف می‌تواند هزینهٔ عضویت او را بپردازد.</p>
    </section>
  );

  if (pageKey === 'referrals') return (
    <section className="portal-page" aria-labelledby="portal-page-title">
      <header className="portal-page-heading"><div><h1 id="portal-page-title" tabIndex={-1}>ارجاع‌های من</h1><p>راهنمای مراجعه و پیشنهادهای ثبت‌شده را دنبال کنید.</p></div></header>
      <section className="referral-hero"><span className="welcome-badge">ارجاع درمانی · دادهٔ نمونه</span><h2>پیگیری ارجاع‌های ثبت‌شده</h2><p>ارجاع جاری شما از درمانگاه مهر ترنم به مرکز تخصصی بهار برای مراجعه به متخصص داخلی نمونه است.</p><a href="/member/appointments" onClick={(event) => goTo(event, '/member/appointments')}>جست‌وجوی نوبت در مرکز منتخب</a></section>
      <section className="portal-list-section"><h2>فهرست ارجاع‌ها</h2><div className="referral-list"><article><span>در انتظار مراجعه</span><div><strong>درمانگاه مهر ترنم ← مرکز تخصصی بهار</strong><small>تخصص داخلی · ارجاع نمونه · ۲۹ مهر</small></div><a href="/member/appointments" onClick={(event) => goTo(event, '/member/appointments')}>مشاهده جزئیات</a></article><article><span>پیگیری‌شده</span><div><strong>مرکز سلامت پارس ← درمانگاه مهر ترنم</strong><small>تخصص قلب · ارجاع نمونه · ۱۸ مهر</small></div><a href="/member/appointments" onClick={(event) => goTo(event, '/member/appointments')}>مشاهده جزئیات</a></article></div></section>
      <p className="portal-info-note">ارجاع نمونه تشخیص پزشکی نیست و انتخاب پزشک با شماست. اطلاعات و پیشنهادهای این صفحه واقعی نیستند.</p>
    </section>
  );

  if (pageKey === 'account') return (
    <section className="portal-page" aria-labelledby="portal-page-title">
      <header className="portal-page-heading"><div><h1 id="portal-page-title" tabIndex={-1}>حساب کاربری</h1><p>مشخصات حساب و وضعیت عضویت را ببینید.</p></div><span className="portal-date-chip">اطلاعات نمونه</span></header>
      <div className="account-grid"><section className="account-card"><h2>مشخصات حساب</h2><div className="account-fields"><div><small>نام و نام خانوادگی</small><strong>نیلوفر رضایی</strong></div><div><small>تاریخ تولد</small><strong>۱۵ خرداد ۱۳۷۲</strong></div><div><small>شماره همراه</small><strong dir="ltr">۰۹۱۲ *** ۶۷۸۹</strong></div><div><small>کد ملی</small><strong dir="ltr">۰۳۶ ***** ۱۲</strong></div><div><small>شهر</small><strong>تهران</strong></div><div><small>جنسیت</small><strong>زن</strong></div></div><button type="button" disabled>ویرایش مشخصات</button></section><section className="account-membership"><h2>عضویت ترنم</h2><span>عضویت نمونه · فعال</span><small>مبنای محاسبهٔ حق عضویت</small><strong>سن عضو</strong><hr /><small>پرداخت‌کنندهٔ نمونه</small><strong>خود عضو</strong><hr /><p>اعضای زیرمجموعه را می‌توانید مدیریت کنید.</p><a href="/member/family" onClick={(event) => goTo(event, '/member/family')}>مدیریت اعضا</a></section></div>
      <p className="portal-info-note">حق عضویت براساس سن محاسبه می‌شود. این صفحه با داده‌های ساختگی نمایش داده می‌شود و هیچ حساب واقعی یا وضعیت عضویتی را تأیید نمی‌کند.</p>
    </section>
  );

  return null;
}
