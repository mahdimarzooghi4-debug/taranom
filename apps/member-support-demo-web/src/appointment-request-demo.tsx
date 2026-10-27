import { useState, type FormEvent } from 'react';

type RequestBody = {
  requestId: string;
  expectedRevision: 0;
  memberRef: string;
  centerRef: string;
  departmentRef: string;
  requestedDoctorRef: string;
  slotRef: string;
};
type Attempt = { requestRef: string; body: RequestBody };
type Receipt = {
  contract?: string;
  outcome?: string;
  revision?: number;
  requestCount?: number;
  requests?: Array<{ command?: RequestBody }>;
  memberAuthenticated?: boolean;
  centerAuthenticated?: boolean;
  requestedDoctorLicensed?: boolean;
  slotAvailabilityVerified?: boolean;
  bookingConfirmed?: boolean;
  clinicalActionAllowed?: boolean;
  nationalReleaseAllowed?: boolean;
};

const referencePattern = '^test:[A-Za-z0-9:_-]{1,123}$';
const authorityFlags: Array<keyof Receipt> = [
  'memberAuthenticated', 'centerAuthenticated', 'requestedDoctorLicensed',
  'slotAvailabilityVerified', 'bookingConfirmed', 'clinicalActionAllowed',
  'nationalReleaseAllowed',
];
const sameCommand = (left: RequestBody, right: RequestBody) =>
  left.requestId === right.requestId &&
  left.expectedRevision === right.expectedRevision &&
  left.memberRef === right.memberRef &&
  left.centerRef === right.centerRef &&
  left.departmentRef === right.departmentRef &&
  left.requestedDoctorRef === right.requestedDoctorRef &&
  left.slotRef === right.slotRef;

export function AppointmentRequestDemo() {
  const [values, setValues] = useState({
    memberRef: '', centerRef: '', departmentRef: '', requestedDoctorRef: '', slotRef: '',
  });
  const [attempt, setAttempt] = useState<Attempt | null>(null);
  const [receipt, setReceipt] = useState<Receipt | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');

  const sendExactAttempt = async (candidate: Attempt) => {
    setBusy(true);
    setMessage('درخواست ساختگی در حال ارسال است؛ هنوز نوبتی تأیید نشده است.');
    try {
      const response = await fetch(
        '/internal/demo/v1/appointment-requests/' + encodeURIComponent(candidate.requestRef),
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
          body: JSON.stringify(candidate.body),
        },
      );
      const result = await response.json() as Receipt;
      const validReceipt = response.ok &&
        result.contract === 'TRN-TECH-216' &&
        (result.outcome === 'RecordedRequest' || result.outcome === 'Replayed') &&
        result.revision === 1 && result.requestCount === 1 &&
        result.requests?.length === 1 &&
        result.requests[0]?.command !== undefined &&
        sameCommand(result.requests[0].command, candidate.body) &&
        authorityFlags.every(flag => result[flag] === false);
      if (!validReceipt) {
        setReceipt(null);
        setMessage('پاسخ سرویس قابل تأیید نیست. همان شناسه و همان بدنه را فقط با اقدام دستی دوباره بفرستید.');
        return;
      }
      setReceipt(result);
      setMessage('درخواست ثبت شد؛ نوبت هنوز تأیید نشده است. مرکز باید جداگانه پاسخ دهد.');
    } catch {
      setReceipt(null);
      setMessage('پاسخی دریافت نشد؛ ممکن است درخواست ثبت شده باشد. برای جلوگیری از درخواست تکراری، فقط ارسال دستیِ دقیق همان درخواست ممکن است.');
    } finally {
      setBusy(false);
    }
  };

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (busy || attempt || !event.currentTarget.reportValidity()) return;
    const body: RequestBody = {
      requestId: crypto.randomUUID(),
      expectedRevision: 0,
      memberRef: values.memberRef.trim(),
      centerRef: values.centerRef.trim(),
      departmentRef: values.departmentRef.trim(),
      requestedDoctorRef: values.requestedDoctorRef.trim(),
      slotRef: values.slotRef.trim(),
    };
    const candidate = {
      requestRef: 'test:appointment-' + crypto.randomUUID().replaceAll('-', ''),
      body,
    };
    setAttempt(candidate);
    void sendExactAttempt(candidate);
  };

  const retry = () => {
    if (!attempt || busy || receipt) return;
    void sendExactAttempt(attempt);
  };

  const field = (key: keyof typeof values, label: string, description: string) =>
    <label key={key}>
      {label}
      <input
        required
        pattern={referencePattern}
        maxLength={128}
        autoComplete="off"
        dir="ltr"
        value={values[key]}
        disabled={attempt !== null || busy}
        onChange={event => setValues(previous => ({ ...previous, [key]: event.target.value }))}
        aria-describedby={key + '-help'}
      />
      <span className="muted" id={key + '-help'}>{description}</span>
    </label>;

  return <section className="appointment-request-demo" aria-labelledby="appointment-request-title">
    <h1 id="appointment-request-title">درخواست نوبت — محیط آزمایشی عضو</h1>
    <div role="note" className="notice">
      <strong>فقط شناسه‌های ساختگی — بدون احراز هویت واقعی</strong>
      <p>این فرم فقط قصد درخواست را در پایگاه آزمایشی ثبت می‌کند. ظرفیت پزشک بررسی نمی‌شود و رزرو، نسخه یا اقدام بالینی انجام نمی‌شود.</p>
      <p>پزشک را خود بیمار انتخاب می‌کند؛ ترنم انتخاب نهایی را به‌جای بیمار انجام نمی‌دهد.</p>
    </div>
    <form onSubmit={submit}>
      <fieldset disabled={busy || attempt !== null}>
        <legend>مرکز، تخصص، پزشک و بازه‌ای که بیمار درخواست می‌کند</legend>
        <div className="fields">
          {field('memberRef', 'شناسه عضو آزمایشی', 'نمونه: test:member-001')}
          {field('centerRef', 'شناسه مرکز درمانی آزمایشی', 'نمونه: test:center-001')}
          {field('departmentRef', 'شناسه تخصص یا بخش آزمایشی', 'نمونه: test:department-001')}
          {field('requestedDoctorRef', 'شناسه پزشک انتخاب‌شده توسط بیمار', 'نمونه: test:doctor-001')}
          {field('slotRef', 'شناسه بازه زمانی درخواستی آزمایشی', 'نمونه: test:slot-001')}
        </div>
      </fieldset>
      {!attempt && <div className="actions">
        <button type="submit" disabled={busy}>ارسال درخواست نوبت آزمایشی</button>
      </div>}
    </form>
    {message && <p role="status" aria-live="polite" className="status">{message}</p>}
    {attempt && !receipt && !busy && <div className="actions">
      <button type="button" onClick={retry}>ارسال مجدد دقیق همان درخواست</button>
    </div>}
    {receipt && attempt && <section className="panel" aria-labelledby="appointment-receipt-title">
      <h2 id="appointment-receipt-title">رسید درخواست آزمایشی</h2>
      <p>شناسه درخواست: <code data-testid="appointment-request-ref">{attempt.requestRef}</code></p>
      <p>پزشک درخواستی بیمار: <code>{attempt.body.requestedDoctorRef}</code></p>
      <p>نتیجه فنی: <code>{receipt.outcome}</code> — نسخه {receipt.revision}</p>
      <strong>نوبت تأیید نشده است؛ پاسخ مرکز و تأیید ظرفیت در این محیط وجود ندارد.</strong>
    </section>}
  </section>;
}
