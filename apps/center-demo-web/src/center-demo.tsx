import { useEffect, useState } from 'react';

type Doctor = { doctorRef: string; affiliation: 'PendingIndependentVerification' | 'Ended'; departmentRefs: string[] };
type Snapshot = {
  contract: 'TRN-TECH-073'; syntheticOnly: true;
  managerAuthenticated: false; professionalQualificationVerified: false;
  nationalReleaseAllowed: false; centerRef: string;
  currentVersion: number; outcome: string;
  departments: string[]; doctors: Doctor[]; eventCount: number;
};

type HistoryEvent = {
  version: number;
  kind: 'DefineDepartment' | 'RecordDoctorAffiliation' | 'AssignDoctorToDepartment' | 'EndDoctorAffiliation';
  departmentRef: string | null;
  doctorRef: string | null;
  hypotheticalOnly: true;
};
type HistoryPage = {
  contract: 'TRN-TECH-074'; syntheticOnly: true; managerAuthenticated: false;
  professionalQualificationVerified: false; nationalReleaseAllowed: false;
  centerRef: string; currentVersion: number; afterVersion: number;
  hasMore: boolean; nextAfterVersion: number | null; events: HistoryEvent[];
};
type Historical = {
  contract: 'TRN-TECH-074'; syntheticOnly: true; managerAuthenticated: false;
  professionalQualificationVerified: false; nationalReleaseAllowed: false;
  currentVersion: number; asOfVersion: number; snapshot: Snapshot;
};
const eventNames: Record<HistoryEvent['kind'], string> = {
  DefineDepartment: 'تعریف بخش',
  RecordDoctorAffiliation: 'وابستگی پزشک در انتظار بررسی',
  AssignDoctorToDepartment: 'انتساب به بخش',
  EndDoctorAffiliation: 'پایان وابستگی'
};
function validHistory(v: unknown, center: string, after: number, expected: number): v is HistoryPage {
  if (!v || typeof v !== 'object') return false;
  const x = v as Partial<HistoryPage>;
  if (x.contract !== 'TRN-TECH-074' || x.syntheticOnly !== true ||
      x.managerAuthenticated !== false || x.professionalQualificationVerified !== false ||
      x.nationalReleaseAllowed !== false || x.centerRef !== center ||
      x.currentVersion !== expected || x.afterVersion !== after ||
      typeof x.hasMore !== 'boolean' || !Array.isArray(x.events) ||
      x.events.length > 2) return false;
  let previous = after;
  for (const event of x.events) {
    if (!event || !Number.isSafeInteger(event.version) || event.version <= previous ||
        event.version > expected || typeof event.kind !== 'string' ||
        !Object.hasOwn(eventNames, event.kind) || event.hypotheticalOnly !== true ||
        (event.departmentRef !== null && !validRef(event.departmentRef)) ||
        (event.doctorRef !== null && !validRef(event.doctorRef))) return false;
    previous = event.version;
  }
  return x.hasMore ? x.events.length === 2 && x.nextAfterVersion === previous
                   : x.nextAfterVersion === null;
}
function validHistorical(v: unknown, center: string, version: number, current: number): v is Historical {
  if (!v || typeof v !== 'object') return false;
  const x = v as Partial<Historical>;
  return x.contract === 'TRN-TECH-074' && x.syntheticOnly === true &&
    x.managerAuthenticated === false && x.professionalQualificationVerified === false &&
    x.nationalReleaseAllowed === false && x.currentVersion === current &&
    x.asOfVersion === version && valid(x.snapshot, center) &&
    x.snapshot.currentVersion === version;
}
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
// TRN-TECH-093: a manual, version-derived workspace plan over the
// already persisted synthetic-only center event journal. NO automated apply.
type NextCenterStep={
  code:'READ_FIRST'|'INPUT_DENIED'|'DEPARTMENT'|'AFFILIATION'|'ASSIGNMENT'|'END'|'FINISHED';
  action:Kind|null; label:string;
};
function nextCenterStep(
  source:Snapshot|null, center:string, department:string, doctor:string
):NextCenterStep{
  if(!validRef(center)||!validRef(department)||!validRef(doctor))
    return {code:'INPUT_DENIED',action:null,label:'شناسه‌ها باید صرفاً test: و معتبر باشند؛ هیچ مرحله‌ای مجاز نیست.'};
  if(!source||source.centerRef!==center)
    return {code:'READ_FIRST',action:null,label:'ابتدا نسخه جاری مرکز را از PostgreSQL بخوانید.'};
  if(!source.departments.includes(department))
    return {code:'DEPARTMENT',action:'DEFINE_DEPARTMENT',label:'مرحله ۱ از ۴: تعریف بخش آزمایشی'};
  const affiliation=source.doctors.find(d=>d.doctorRef===doctor);
  if(!affiliation)
    return {code:'AFFILIATION',action:'RECORD_DOCTOR',label:'مرحله ۲ از ۴: ثبت وابستگی پزشک تأییدنشده'};
  if(affiliation.affiliation==='Ended')
    return {code:'FINISHED',action:null,label:'مسیر آزمایشی برای این پزشک پایان یافته؛ هیچ مجوز حرفه‌ای ایجاد نشده است.'};
  if(!affiliation.departmentRefs.includes(department))
    return {code:'ASSIGNMENT',action:'ASSIGN_DOCTOR',label:'مرحله ۳ از ۴: انتساب آزمایشی پزشک به بخش'};
  return {code:'END',action:'END_DOCTOR',label:'مرحله ۴ از ۴: پایان وابستگی آزمایشی'};
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
  const [history, setHistory] = useState<HistoryPage | null>(null);
  const [historical, setHistorical] = useState<Historical | null>(null);
  const [historicalVersion, setHistoricalVersion] = useState('0');
  const [historyStatus, setHistoryStatus] = useState('تاریخچه هنوز درخواست نشده است.');

  const [status, setStatus] = useState('هنوز وضعیت پایگاه‌داده آزمایشی خوانده نشده است.');
  useEffect(() => { setSnapshot(null); setHistory(null); setHistorical(null); }, [center]);

  async function refresh() {
    setPending(true);
    setSnapshot(null);
    setHistory(null);
    setHistorical(null);
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
      setHistory(null);
      setHistorical(null);
      setHistoryStatus('با تغییر نسخه، تاریخچه قبلی نامعتبر شد؛ دوباره بخوانید.');
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


  async function loadHistory(afterVersion: number) {
    if (!snapshot || pending) return;
    setPending(true);
    setHistory(null);
    setHistorical(null);
    const expected = snapshot.currentVersion;
    try {
      const url = '/internal/demo/v1/centers/' + encodeURIComponent(center) +
        '/history?afterVersion=' + afterVersion + '&limit=2';
      const response = await fetch(url, { credentials: 'omit', cache: 'no-store' });
      if (!response.ok) throw Error('HISTORY_DENIED');
      const payload: unknown = await response.json();
      if (!validHistory(payload, center, afterVersion, expected)) throw Error('HISTORY_UNVERIFIED');
      setHistory(payload);
      setHistoryStatus(payload.events.length
        ? 'رخدادهای این صفحه از تاریخچه بازپخش‌شده پایگاه آزمایشی خوانده شدند.'
        : 'در این بازه رخداد پذیرفته‌شده‌ای وجود ندارد.');
    } catch {
      setHistoryStatus('خواندن تاریخچه یا یکپارچگی داده تأیید نشد؛ هیچ رخدادی نمایش داده نمی‌شود.');
    } finally {
      setPending(false);
    }
  }

  async function readVersion() {
    if (!snapshot || pending) return;
    setPending(true);
    setHistorical(null);
    const version = Number(historicalVersion);
    if (!/^(0|[1-9][0-9]*)$/.test(historicalVersion) ||
        !Number.isSafeInteger(version) || version > snapshot.currentVersion) {
      setHistoryStatus('نسخه درخواستی نامعتبر یا مربوط به آینده است.');
      setPending(false);
      return;
    }
    try {
      const url = '/internal/demo/v1/centers/' + encodeURIComponent(center) + '/versions/' + version;
      const response = await fetch(url, { credentials: 'omit', cache: 'no-store' });
      if (!response.ok) throw Error('VERSION_DENIED');
      const payload: unknown = await response.json();
      if (!validHistorical(payload, center, version, snapshot.currentVersion))
        throw Error('VERSION_UNVERIFIED');
      setHistorical(payload);
      setHistoryStatus('وضعیت نسخه گذشته تنها با بازپخش رخدادهای همان مرکز بازسازی شد.');
    } catch {
      setHistoryStatus('بازسازی نسخه تأیید نشد؛ نمای تاریخی موجود نیست.');
    } finally {
      setPending(false);
    }
  }

  const guidedStep=nextCenterStep(snapshot,center,department,doctor);
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
        <section className="guided-workflow" aria-label="راهنمای چهارمرحله‌ای رویداد مرکز" data-testid="center-guided-flow">
          <h3>راهنمای عملی ثبت رخدادهای مرکز — فقط داده ساختگی</h3>
          <p>مرحله بعد صرفاً از آخرین نسخه خوانده‌شده PostgreSQL انتخاب می‌شود.
            هیچ دکمه‌ای چند دستور را خودکار اجرا نمی‌کند؛ هر تغییر نیازمند اقدام جداگانه است.</p>
          <p data-testid="center-guided-step" aria-live="polite">
            {pending ? 'در حال انتظار برای پاسخ پایگاه آزمایشی؛ مرحله جدیدی مجاز نیست.' : guidedStep.label}
          </p>
          <ol className="guided-stages">
            <li>تعریف بخش آزمایشی</li>
            <li>ثبت وابستگی پزشک در انتظار بررسی مستقل</li>
            <li>انتساب آزمایشی پزشک به بخش</li>
            <li>پایان وابستگی آزمایشی پزشک</li>
          </ol>
          {guidedStep.action && <button type="button" disabled={!snapshot || pending}
            onClick={() => void apply(guidedStep.action!)}>
            ثبت مرحله بعد در PostgreSQL
          </button>}
          <p>وضعیت مدیر، صلاحیت حرفه‌ای و انتشار ملی همواره تأییدنشده باقی می‌ماند.
            این راهنما جایگزین مجوز، قرارداد یا نظارت بالینی نیست.</p>
        </section>
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
      <section className="panel" aria-labelledby="history-heading">
        <h2 id="history-heading">تاریخچه ثبت‌شده و بازسازی نسخه‌های گذشته (صرفاً آزمایشی)</h2>
        <p>رخدادها از ژورنال PostgreSQL همراه با کنترل یکپارچگی مجدد خوانده می‌شوند؛ نمایش آن‌ها مجوز فعالیت بالینی یا تأیید مدیر نیست.</p>
        <div className="actions">
          <button type="button" disabled={!snapshot || pending} onClick={() => void loadHistory(0)}>
            بازخوانی نخستین صفحه تاریخچه
          </button>
          <button type="button" disabled={!history || !history.hasMore || pending}
            onClick={() => { if (history?.nextAfterVersion !== null && history) void loadHistory(history.nextAfterVersion); }}>
            صفحه بعد رخدادها
          </button>
        </div>
        <p role="status" aria-live="polite" className="status">{historyStatus}</p>
        {history && <section aria-label="رخدادهای صفحه فعلی">
          <p>نسخه پایگاه: {history.currentVersion} | رخدادهای صفحه: {history.events.length}</p>
          <ol>{history.events.map(evt =>
            <li key={evt.version}>
              <strong>نسخه {evt.version}: {eventNames[evt.kind]}</strong>
              {evt.departmentRef && <span> | بخش: <code>{evt.departmentRef}</code></span>}
              {evt.doctorRef && <span> | پزشک: <code>{evt.doctorRef}</code></span>}
            </li>)}</ol>
          <p>{history.hasMore ? 'رخدادهای بیشتری برای صفحه بعد موجود است.' :
            'این صفحه آخرین رخداد پذیرفته‌شده را نشان می‌دهد.'}</p>
        </section>}
        <div className="fields">
          <label htmlFor="historic-version">
            شماره نسخه برای بازسازی (از صفر تا نسخه جاری)
            <input id="historic-version" type="number" min="0" step="1"
              value={historicalVersion} onChange={e => { setHistoricalVersion(e.target.value); setHistorical(null); }} />
          </label>
        </div>
        <button type="button" disabled={!snapshot || pending} onClick={() => void readVersion()}>
          بازسازی نسخه انتخابی
        </button>
        {historical && <section aria-label="نمای بازسازی‌شده نسخه گذشته">
          <h3>وضعیت در نسخه {historical.asOfVersion} — فقط تاریخچه آزمایشی</h3>
          <p>تعداد رخداد پذیرفته‌شده تا این نسخه: {historical.snapshot.eventCount}</p>
          <p>بخش‌ها: {historical.snapshot.departments.length
            ? historical.snapshot.departments.join('، ') : 'هیچ بخش ثبت نشده'}</p>
          <ul>{historical.snapshot.doctors.map(d =>
            <li key={d.doctorRef}><code>{d.doctorRef}</code> — {d.affiliation === 'Ended'
              ? 'پایان‌یافته' : 'در انتظار بررسی مستقل'} —
              بخش‌های منتسب: {d.departmentRefs.length ? d.departmentRefs.join('، ') : 'هیچ'}</li>)}</ul>
          <p>تأیید مدیر: خیر | صلاحیت پزشکی: تأییدنشده | مجوز انتشار: خیر</p>
        </section>}
      </section>
      <footer>TRN-TECH-073/074 | پایگاه‌داده موقت | فقط محیط localhost | بدون احراز هویت یا پذیرش واقعی</footer>
    </main>
  </>;
}
