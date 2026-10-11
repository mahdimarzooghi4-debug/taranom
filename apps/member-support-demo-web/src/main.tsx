import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { MemberSupportDemo } from './member-support-demo';
import './style.css';

function MemberSupportSurface() {
  return <>
    <a className="skip" href="#member-support">پرش به بخش پشتیبانی</a>
    <header>
      <p>ترنم | محیط آزمایشی مستقل عضو</p>
      <h1>درخواست و پیگیری پشتیبانی عضو — فقط شناسه‌های ساختگی</h1>
      <p>React به سرویس .NET و PostgreSQL آزمایشی متصل است. این پنل هیچ احراز هویت، شکایت قانونی یا مکاتبه بیمه‌ای واقعی انجام نمی‌دهد.</p>
    </header>
    <main id="member-support">
      <div role="note" className="notice">
        <strong>محیط مستقل نمایشی عضو — DENIED_BY_DEFAULT</strong>
        <p>این محیط از صفحه مدیریت مرکز جداست اما هنوز مرز احراز هویت واقعی ندارد. هیچ اطلاعات شخص حقیقی وارد نکنید.</p>
      </div>
      <MemberSupportDemo />
      <footer>TRN-TECH-076 | فقط localhost و test: | بدون دسترسی به پرونده بالینی یا مالی</footer>
    </main>
  </>;
}
const el = document.getElementById('root');
if (!el) throw Error('Missing member support root');
createRoot(el).render(<StrictMode><MemberSupportSurface /></StrictMode>);
