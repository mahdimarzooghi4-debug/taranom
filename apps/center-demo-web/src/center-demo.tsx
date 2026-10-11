import { useEffect, useState } from 'react';

type Doctor = { doctorRef: string; affiliation: 'PendingIndependentVerification' | 'Ended'; departmentRefs: string[] };
type Snapshot = {
  contract: 'TRN-TECH-073'; syntheticOnly: true;
  managerAuthenticated: false; professionalQualificationVerified: false;
  nationalReleaseAllowed: false; centerRef: string;
  currentVersion: number; outcome: string;
  departments: string[]; doctors: Doctor[]; eventCount: number;
};
type Kind = 'DEFINE_DEPARTMENT' | 'RECORD_DOCTOR' | 'ASSIGN_DOCTOR' | 'END_DOCTOR';
const validRef = (value: string) => /^test:[A-Za-z0-9_-]{1,123}$/.test(value) && value.length <= 128;
function valid(value: unknown, center: string): value is Snapshot {
  if (!value || typeof value !== 'object') return false;
  const v = value as Partial<Snapshot>;
  if (v.contract !== 'TRN-TECH-073' || v.syntheticOnly !== true ||
      v.managerAuthenticated !== false || v.professionalQualificationVerified !== false ||
      v.nationalReleaseAllowed !== false || v.centerRef !== center ||
      typeof v.currentVersion !== 'number' || !Number.isSafeInteger(v.currentVersion) ||
      v.currentVersion < 0 || typeof v.outcome !== 'string' ||
      !Array.isArray(v.departments) || !Array.isArray(v.doctors) ||
      typeof v.eventCount !== 'number' || v.eventCount !== v.currentVersion ||
      !v.departments.every(x => typeof x === 'string' && validRef(x))) return false;
  return v.doctors.every(x =>
    x && validRef(x.doctorRef) &&
    (x.affiliation === 'PendingIndependentVerification' || x.affiliation === 'Ended') &&
    Array.isArray(x.departmentRefs) && x.departmentRefs.every(d => validRef(d))
  );
}
const texts: Record<Kind, string> = {
  DEFINE_DEPARTMENT: 'ثبت بخش آزمایشی',
  RECORD_DOCTOR: 'ثبت وابستگی تأییدنشده پزشک آزمایشی',
  ASSIGN_DOCTOR: 'تخصیص آزمایشی پزشک به بخش',
  END_DOCTOR: 'پایان وابستگی آزمایشی پزشک'
};
export function CenterDemo() {
  const [center, setCenter] = useState('test:center-demo');
  const [department, setDepartment] = useState('test:department-demo');
  const [doctor, setDoctor] = useState('test:doctor-demo');
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [pending, setPending] = useState(false);
  const [status, setStatus] = useState('هنوز وضعیت پایگاه‌داده آزمایشی خوانده نشده است.');
  useEffect(() => { setSnapshot(null); }, [center]);

  async function refresh() {
    setPending(true);
    setSnapshot(null);
    if (!validRef(center)) {
      setStatus('فقط شناسه‌های آزمایشی با پیشوند test: قابل استفاده‌اند.');
      setPending(false);
      return;
    }
    try {
      const response = await fetch('/internal/demo/v1/centers/' + encodeURIComponent(center),
        { credentials: 'omit', cache: 'no-store' });
      if (!response.ok) throw Error('READ_DENIED');
      const payload: unknown = await response.json();
      if (!valid(payload, center)) throw Error('INVALID_RESPONSE');
      setSnapshot(payload);
      setStatus('اطلاعات آزمایشی از PostgreSQL خوانده شد. هیچ مدیر یا پزشک واقعی احراز نشده است.');
    } catch {
      setStatus('خواندن پایگاه‌داده آزمایشی امکان‌پذیر نیست؛ وضعیت معتبر فرض نمی‌شود.');
    } finally {
      setPending(false);
    }
  }
  async function apply(kind: Kind) {
    if (!snapshot || pending) return;
    const dep = kind === 'DEFINE_DEPARTMENT' || kind === 'ASSIGN_DOCTOR' ? department : null;
    const doc = kind === 'DEFINE_DEPARTMENT' ? null : doctor;
    if (!validRef(center) || dep !== null && !validRef(dep) || doc !== null && !validRef(doc)) {
      setStatus('شناسه‌های بخش و پزشک باید آزمایشی و با پیشوند test: باشند.');
      return;
    }
    setPending(true);
    try {
      const response = await fetch('/internal/demo/v1/centers/' + encodeURIComponent(center) + '/commands', {
        method: 'POST', credentials: 'omit', cache: 'no-store',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          requestId: crypto.randomUUID(), expectedVersion: snapshot.currentVersion,
          kind, departmentRef: dep, doctorRef: doc
        })
      });
      const payload: unknown = await response.json();
      if (!valid(payload, center)) throw Error('INVALID_RESULT');
      setSnapshot(payload);
      setStatus(response.ok
        ? (payload.outcome === 'Applied' ? texts[kind] + ' — فقط در پایگاه آزمایشی ثبت شد.' :
          'درخواست تکراری؛ هیچ رخداد جدیدی ایجاد نشد.')
        : 'عملیات اعمال نشد: ' + payload.outcome + '؛ نسخه آزمایشی فعلی نمایش داده شد.');
    } catch {
      setSnapshot(null);
      setStatus('عملیات ناموفق بود؛ وضعیت پایگاه‌داده نامعلوم است. ابتدا دوباره بخوانید.');
    } finally {
      setPending(false);
    }
  }

  return <>
    <a href="#main" className="skip">پرش به محتوای اصلی</a>
    <header>
      <p>ترنم | محیط یکپارچه آزمایش فنی مرکز</p>
      <h1>گردش بخش و وابستگی پزشک در پایگاه‌داده آزمایشی</h1>
      <p>رابط React به .NET و PostgreSQL واقعیِ آزمایشی متصل است. این محیط فقط test: را می‌پذیرد و هیچ هویت مدیر مرکز، پروانه پزشک یا خدمت درمانی واقعی را تأیید نمی‌کند.</p>
    </header>
    <main id="main">
      <section className="notice" role="note">
        <strong>فقط نمونه فنی و غیرقابل انتشار — DENIED_BY_DEFAULT</strong>
        <p>عملیات زیر صرفاً بر داده آزمایشی انجام می‌شوند. ادعای «مدیر مرکز تأییدشده»، «پزشک دارای صلاحیت» یا مجوز دسترسی بیمار وجود ندارد.</p>
      </section>
      <section className="panel" aria-labelledby="center-workflow">
        <h2 id="center-workflow">گردش کامل آزمایشی مرکز</h2>
        <div className="fields">
          <label htmlFor="center">شناسه مرکز آزمایشی
            <input id="center" dir="ltr" value={center} onChange={e => setCenter(e.target.value)} autoComplete="off" />
          </label>
          <label htmlFor="department">شناسه بخش آزمایشی
            <input id="department" dir="ltr" value={department} onChange={e => setDepartment(e.target.value)} autoComplete="off" />
          </label>
          <label htmlFor="doctor">شناسه پزشک آزمایشی
            <input id="doctor" dir="ltr" value={doctor} onChange={e => setDoctor(e.target.value)} autoComplete="off" />
          </label>
        </div>
        <div className="actions">
          <button type="button" disabled={pending} onClick={() => void refresh()}>بازخوانی مرکز از PostgreSQL</button>
          {(Object.keys(texts) as Kind[]).map(kind =>
            <button key={kind} type="button" disabled={!snapshot || pending} onClick={() => void apply(kind)}>
              {texts[kind]}
            </button>)}
        </div>
        <p role="status" aria-live="polite" className="status">{pending ? 'در حال ارتباط با پایگاه آزمایشی...' : status}</p>
        {snapshot && <section aria-label="وضعیت ذخیره‌شده آزمایشی">
          <p><strong>نسخه ثبت‌شده:</strong> {snapshot.currentVersion} | <strong>تعداد رخدادهای پذیرفته‌شده:</strong> {snapshot.eventCount}</p>
          <p className="muted">تأیید مدیر: خیر | تأیید صلاحیت حرفه‌ای: خیر | اجازه انتشار: خیر</p>
          <div className="details">
            <article><h3>بخش‌های آزمایشی</h3>
              {snapshot.departments.length ?
                <ul>{snapshot.departments.map(x => <li key={x}><code>{x}</code></li>)}</ul> :
                <p>هیچ بخش آزمایشی ثبت نشده است.</p>}
            </article>
            <article><h3>وابستگی پزشکان آزمایشی</h3>
              {snapshot.doctors.length ?
                <ul>{snapshot.doctors.map(d => <li key={d.doctorRef}>
                  <code>{d.doctorRef}</code> — {d.affiliation === 'Ended' ? 'پایان‌یافته' : 'در انتظار بررسی مستقل'}
                  <ul>{d.departmentRefs.map(x => <li key={x}><code>{x}</code></li>)}</ul>
                </li>)}</ul> :
                <p>هیچ وابستگی آزمایشی ثبت نشده است.</p>}
            </article>
          </div>
        </section>}
      </section>
      <footer>TRN-TECH-073 | پایگاه‌داده موقت | فقط محیط localhost | بدون احراز هویت یا پذیرش واقعی</footer>
    </main>
  </>;
}
