import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { MemberSupportDemo } from './member-support-demo';
import { CostJournalDemo } from './cost-journal-demo';
import { PayerEvidenceWorkbench } from './payer-evidence-workbench';
import { ProofingCorrectionDemo } from './proofing-correction-demo';
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
      <p><a href="/member-demo/payer">بازرسی آزمایشی تعهد پرداخت در بازارهای فردی، حمایتی و سازمانی</a></p>
      <p><a href="/member-demo/proofing">چرخه آزمایشی اصلاح هویت و احراز مجدد، بدون داده واقعی</a></p>
      <MemberSupportDemo />
      <footer>TRN-TECH-076 | فقط localhost و test: | بدون دسترسی به پرونده بالینی یا مالی</footer>
    </main>
  </>;
}
const el = document.getElementById('root');
if (!el) throw Error('Missing member support root');
createRoot(el).render(<StrictMode>{window.location.pathname === '/member-demo/costs'
  ? <main dir="rtl"><a href="/member-demo/">بازگشت به محیط آزمایشی پشتیبانی</a>
      <CostJournalDemo /></main>
  : window.location.pathname === '/member-demo/payer'
  ? <main dir="rtl"><a href="/member-demo/">بازگشت به محیط آزمایشی عضو</a>
      <PayerEvidenceWorkbench /></main>
  : window.location.pathname === '/member-demo/proofing'
  ? <main dir="rtl"><a href="/member-demo/">بازگشت به محیط آزمایشی عضو</a>
      <ProofingCorrectionDemo /></main>
  : <MemberSupportSurface />}</StrictMode>);
