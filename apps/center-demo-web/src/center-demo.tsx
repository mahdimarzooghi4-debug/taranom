import { useEffect, useState } from 'react';
import {DepartmentRoster} from './department-roster';
import {DoctorAffiliationHistory} from './doctor-affiliation-history';
import {CenterAuditWorkspace} from './center-audit-workspace';
import {InternalSupportRead} from './internal-support-read';
import {makeCenterRecoveryReceipt, parseCenterRecoveryReceipt} from './center-command-receipt';
import type {RecoveryKind, UncertainCenterCommand} from './center-command-receipt';

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
  kind: 'DefineDepartment' | 'RecordDoctorAffiliation' | 'AssignDoctorToDepartment' | 'EndDoctorAffiliation' | 'UnassignDoctorFromDepartment';
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
  EndDoctorAffiliation: 'پایان وابستگی',
  UnassignDoctorFromDepartment: 'برداشتن انتساب فقط از یک بخش'
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
type Kind = RecoveryKind;
// TRN-TECH-106 retains the exact original request bytes for manual replay.
// TRN-TECH-107 can export those bytes to human custody across browser reloads.
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
  END_DOCTOR: 'پایان وابستگی آزمایشی پزشک',
  UNASSIGN_DOCTOR: 'حذف انتساب پزشک فقط از بخش انتخابی (آزمایشی)'
};
type AdjustmentStep = {
  code:'READ_FIRST'|'INPUT_DENIED'|'MISSING_DEPARTMENT'|'NO_ACTIVE_DOCTOR'|
       'SOURCE_NOT_ASSIGNED'|'ADD_TARGET'|'REMOVE_SOURCE'|'TARGET_ONLY';
  kind:Kind|null; departmentRef:string|null; label:string;
};
// Two independent, human-triggered existing journal commands. No atomic
// transfer, clinical assignment, medical or managerial authority.
function nextDepartmentAdjustment(
  source:Snapshot|null, center:string, from:string, to:string, doctor:string
):AdjustmentStep{
  const stop=(code:AdjustmentStep['code'],label:string):AdjustmentStep=>
    ({code,kind:null,departmentRef:null,label});
  if(!validRef(center)||!validRef(from)||!validRef(to)||!validRef(doctor)||
     from===to)return stop('INPUT_DENIED','مبدأ و مقصد باید دو بخش متفاوت با شناسه معتبر test: باشند.');
  if(!source||source.centerRef!==center)
    return stop('READ_FIRST','ابتدا نسخه جاری مرکز را از PostgreSQL بخوانید.');
  if(!source.departments.includes(from)||!source.departments.includes(to))
    return stop('MISSING_DEPARTMENT','هر دو بخش باید قبلاً در همین مرکز ثبت شده باشند.');
  const affiliation=source.doctors.find(d=>d.doctorRef===doctor);
  if(!affiliation||affiliation.affiliation!=='PendingIndependentVerification')
    return stop('NO_ACTIVE_DOCTOR','وابستگی فعال آزمایشی برای پزشک مشاهده نشد.');
  const inSource=affiliation.departmentRefs.includes(from);
  const inTarget=affiliation.departmentRefs.includes(to);
  if(!inSource)return stop(inTarget?'TARGET_ONLY':'SOURCE_NOT_ASSIGNED',
    inTarget?'در نسخه جاری فقط بخش مقصد منتسب است؛ اقدام تازه لازم نیست.':
      'پزشک در بخش مبدأ انتسابی ندارد؛ دستور جدید مجاز نیست.');
  if(!inTarget)return {code:'ADD_TARGET',kind:'ASSIGN_DOCTOR',
    departmentRef:to,label:'گام ۱: ثبت مستقل انتساب آزمایشی به بخش مقصد'};
  return {code:'REMOVE_SOURCE',kind:'UNASSIGN_DOCTOR',
    departmentRef:from,label:'گام ۲: مقصد ثبت است؛ برداشتن مستقل انتساب بخش مبدأ'};
}
export function CenterDemo() {
  const [center, setCenter] = useState('test:center-demo');
  const [department, setDepartment] = useState('test:department-demo');
  const [doctor, setDoctor] = useState('test:doctor-demo');
  const [targetDepartment, setTargetDepartment] = useState('test:department-target');
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [pending, setPending] = useState(false);
  const [uncertain, setUncertain] = useState<UncertainCenterCommand | null>(null);
  const [importText, setImportText] = useState('');
  const [importFeedback, setImportFeedback] = useState(
    'ورود رسید فقط به درخواست دستی انجام می‌شود و هیچ دستور شبکه‌ای ارسال نمی‌کند.');
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
      // Source read alone cannot conclude an earlier uncertain command.
      // Do not re-expose a current snapshot as actionable while unresolved.
      setSnapshot(uncertain ? null : payload);
      setStatus(uncertain
        ? 'منبع خوانده شد، اما نتیجه دستور قبلی نامعلوم است؛ فقط همان درخواست را دستی بازیابی کنید.'
        : 'اطلاعات آزمایشی از PostgreSQL خوانده شد. هیچ مدیر یا پزشک واقعی احراز نشده است.');
    } catch {
      setStatus('خواندن پایگاه‌داده آزمایشی امکان‌پذیر نیست؛ وضعیت معتبر فرض نمی‌شود.');
    } finally {
      setPending(false);
    }
  }
  // Manual replay has exactly the same URI, byte-for-byte JSON body,
  // original requestId and original expectedVersion, even if a later read
  // sees another center revision. The PostgreSQL journal decides the result.
  async function sendCommand(receipt: UncertainCenterCommand) {
    setPending(true);
    try {
      const response = await fetch('/internal/demo/v1/centers/' +
        encodeURIComponent(receipt.center) + '/commands', {
        method: 'POST', credentials: 'omit', cache: 'no-store',
        headers: { 'Content-Type': 'application/json' }, body: receipt.body
      });
      const payload: unknown = await response.json();
      if (!valid(payload, receipt.center)) throw Error('UNVERIFIED_COMMAND_RESULT');

      const accepted = response.status === 200 &&
        ((payload.outcome === 'Applied' &&
          payload.currentVersion === receipt.expectedVersion + 1) ||
         (payload.outcome === 'Replayed' &&
          payload.currentVersion > receipt.expectedVersion));
      const rejected = response.status === 409 &&
        (payload.outcome === 'StaleRevision' ||
         payload.outcome === 'ConflictingReplay' ||
         payload.outcome === 'NotApplicable') &&
        payload.currentVersion >= receipt.expectedVersion;
      if (!accepted && !rejected) throw Error('UNRECOGNIZED_COMMAND_RESULT');

      // Only a verified conclusive database response resolves uncertainty.
      setUncertain(null);
      setSnapshot(payload);
      setHistory(null);
      setHistorical(null);
      setHistoryStatus('با پاسخ قطعی ژورنال، نماهای تاریخی قبلی پاک شدند؛ دوباره بخوانید.');
      setStatus(accepted
        ? (payload.outcome === 'Applied'
          ? texts[receipt.kind] + ' — فقط در پایگاه آزمایشی ثبت شد.'
          : 'درخواست تکراری با همان شناسه بازیابی شد؛ هیچ رخداد جدیدی ایجاد نشد.')
        : 'عملیات اعمال نشد: ' + payload.outcome + '؛ نسخه واقعی آزمایشی نمایش داده شد.');
    } catch {
      // Dropped HTTP ACK, invalid JSON, non-attested response or 503 cannot
      // tell the browser whether PostgreSQL committed. No automatic retry.
      setUncertain(receipt);
      setSnapshot(null);
      setHistory(null);
      setHistorical(null);
      setHistoryStatus('نتیجه نوشتن نامعلوم است؛ اطلاعات تاریخی قبلی نامعتبر شد.');
      setStatus('نتیجه ثبت نامعلوم است؛ درخواست اصلی فقط در حافظه همین صفحه نگه داشته شد. از ارسال دستور تازه خودداری کنید.');
    } finally {
      setPending(false);
    }
  }

  async function apply(kind: Kind) {
    if (!snapshot || pending || uncertain) return;
    const dep = kind === 'DEFINE_DEPARTMENT' || kind === 'ASSIGN_DOCTOR' ||
      kind === 'UNASSIGN_DOCTOR' ? department : null;
    const doc = kind === 'DEFINE_DEPARTMENT' ? null : doctor;
    if (!validRef(center) || dep !== null && !validRef(dep) || doc !== null && !validRef(doc)) {
      setStatus('شناسه‌های بخش و پزشک باید آزمایشی و با پیشوند test: باشند.');
      return;
    }
    if (!Number.isSafeInteger(snapshot.currentVersion) ||
        snapshot.currentVersion >= Number.MAX_SAFE_INTEGER) {
      setStatus('نسخه خوانده‌شده برای ثبت امن نیست؛ هیچ دستور جدیدی ارسال نشد.');
      return;
    }
    const receipt: UncertainCenterCommand = {
      center, expectedVersion: snapshot.currentVersion, kind,
      body: JSON.stringify({
        requestId: crypto.randomUUID(), expectedVersion: snapshot.currentVersion,
        kind, departmentRef: dep, doctorRef: doc
      })
    };
    await sendCommand(receipt);
  }

  async function adjustDepartment(kind:Kind,ward:string){
    const step=nextDepartmentAdjustment(
      snapshot,center,department,targetDepartment,doctor);
    if(pending||uncertain||!snapshot||
       step.kind!==kind||step.departmentRef!==ward)return;
    // Fresh request ID and exact current event version. Existing CAS journal
    // decides conflicts, idempotency and uncertain HTTP acknowledgment.
    await sendCommand({center,expectedVersion:snapshot.currentVersion,kind,
      body:JSON.stringify({requestId:crypto.randomUUID(),
        expectedVersion:snapshot.currentVersion,kind,
        departmentRef:ward,doctorRef:doctor})});
  }

  async function retryUncertain() {
    if (pending || !uncertain || uncertain.center !== center) return;
    await sendCommand(uncertain);
  }

  function importReceipt() {
    if (pending || uncertain) return;
    const receipt = parseCenterRecoveryReceipt(importText);
    if (!receipt) {
      setImportFeedback('رسید رد شد: ساختار یا شناسه یا دستور آزمایشی معتبر نیست. هیچ درخواست HTTP ارسال نشد.');
      return;
    }
    // Explicit import NEVER calls sendCommand or refresh. Only a later
    // separate human click can submit the original source-pinned bytes.
    const original = JSON.parse(receipt.body) as {departmentRef: string | null; doctorRef: string | null};
    setCenter(receipt.center);
    if (original.departmentRef !== null) setDepartment(original.departmentRef);
    if (original.doctorRef !== null) setDoctor(original.doctorRef);
    setSnapshot(null);
    setHistory(null);
    setHistorical(null);
    setHistoryStatus('رسید وارد شد؛ هیچ وضعیت تاریخی معتبر تلقی نمی‌شود.');
    setUncertain(receipt);
    setImportText('');
    setImportFeedback('رسیدِ بدون امضا وارد شد؛ هیچ POST انجام نشده است. فقط با کلیک جداگانه، درخواست قبلی دوباره ارسال می‌شود.');
    setStatus('درخواست قبلی از رسید دستی بارگذاری شد؛ نتیجه آن نامعلوم است.');
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

  const adjustment=nextDepartmentAdjustment(
    snapshot,center,department,targetDepartment,doctor);
  const guidedStep=uncertain
    ? {code:'READ_FIRST', action:null, label:'نتیجه دستور پیشین نامعلوم است؛ ثبت مرحله تازه تا بازیابی همان درخواست ممنوع است.'} as NextCenterStep
    : nextCenterStep(snapshot,center,department,doctor);
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
            <input id="center" dir="ltr" value={center} disabled={pending || !!uncertain}
              onChange={e => setCenter(e.target.value)} autoComplete="off" />
          </label>
          <label htmlFor="department">شناسه بخش آزمایشی
            <input id="department" dir="ltr" value={department} disabled={pending || !!uncertain}
              onChange={e => setDepartment(e.target.value)} autoComplete="off" />
          </label>
          <label htmlFor="doctor">شناسه پزشک آزمایشی
            <input id="doctor" dir="ltr" value={doctor} disabled={pending || !!uncertain}
              onChange={e => setDoctor(e.target.value)} autoComplete="off" />
          </label>
        </div>
        <div className="actions">
          <button type="button" disabled={pending} onClick={() => void refresh()}>بازخوانی مرکز از PostgreSQL</button>
          {(Object.keys(texts) as Kind[]).map(kind =>
            <button key={kind} type="button" disabled={!snapshot || pending || !!uncertain} onClick={() => void apply(kind)}>
              {texts[kind]}
            </button>)}
        </div>
        <p className="muted">حذف انتساب از بخش انتخابی فقط همان رابطه آزمایشی را پایان می‌دهد؛
          سایر بخش‌های پزشک و وابستگی او به مرکز باقی می‌مانند. «پایان وابستگی»
          جداگانه همه روابط جاری را پایان می‌دهد؛ هیچ‌کدام مجوز پزشکی نیستند.</p>
        {uncertain && <section role="alert" className="center-uncertain" aria-label="بازیابی دستور با نتیجه نامعلوم">
          <h3>وضعیت ثبت دستور نامعلوم است — فقط آزمایشی</h3>
          <p>پاسخ قطعی PostgreSQL به مرورگر نرسیده است؛ ممکن است رویداد قبلاً ثبت شده باشد.
            دکمه زیر فقط با همان شناسه درخواست، نسخه قبلی و متن تغییرناکرده دستور دوباره ارسال می‌کند.
            هیچ تکرار خودکار یا دستور تازه‌ای مجاز نیست.</p>
          <p>مرکز: <code>{uncertain.center}</code> | نسخه اولیه: {uncertain.expectedVersion}</p>
          <button type="button" disabled={pending} onClick={() => void retryUncertain()}>
            تلاش دستی مجدد با همان درخواست
          </button>
          <label htmlFor="center-receipt-export">متن رسید آزمایشی برای کپی دستی
            <textarea id="center-receipt-export" dir="ltr" rows={5} readOnly
              value={makeCenterRecoveryReceipt(uncertain)} />
          </label>
          <p>این JSON بدون امضا و غیرمحرمانه است، صرفاً برای test:. اگر خودتان کپی نکنید،
            با بستن یا بارگذاری مجدد صفحه از دست می‌رود. کپی آن اثبات ثبت، هویت یا اختیار نیست.</p>
        </section>}
        <section className="center-receipt-import" aria-label="ورود دستی رسید پس از بارگذاری مجدد">
          <h3>بازیابی دستی رسید آزمایشی قبلی</h3>
          <p>فقط متن JSON قبلاً کپی‌شده را وارد کنید. ورود رسید هیچ فرمانی اجرا نمی‌کند؛
            درخواست فقط پس از کلیک مستقل روی دکمه تلاش مجدد ارسال می‌شود.</p>
          <label htmlFor="center-receipt-import">چسباندن رسید آزمایشی پس از بارگذاری مجدد
            <textarea id="center-receipt-import" dir="ltr" rows={4}
              maxLength={2400} autoComplete="off" spellCheck={false}
              disabled={pending || !!uncertain} value={importText}
              onChange={e => setImportText(e.target.value)} />
          </label>
          <button type="button" disabled={pending || !!uncertain || !importText.trim()}
            onClick={importReceipt}>بارگذاری رسید بدون ارسال</button>
          <p data-testid="center-receipt-feedback" aria-live="polite">{importFeedback}</p>
        </section>
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
          {guidedStep.action && <button type="button" disabled={!snapshot || pending || !!uncertain}
            onClick={() => void apply(guidedStep.action!)}>
            ثبت مرحله بعد در PostgreSQL
          </button>}
          <p>وضعیت مدیر، صلاحیت حرفه‌ای و انتشار ملی همواره تأییدنشده باقی می‌ماند.
            این راهنما جایگزین مجوز، قرارداد یا نظارت بالینی نیست.</p>
        </section>
        <section className="guided-workflow" aria-label="تنظیم دستی انتساب پزشک میان دو بخش"
          data-testid="center-department-adjustment">
          <h3>تنظیم انتساب آزمایشی پزشک بین بخش‌های همین مرکز</h3>
          <p>مبدأ همان «شناسه بخش آزمایشی» بالاست. مقصد جداگانه وارد می‌شود.
            این مسیر دو فرمان مستقل است؛ هیچ تغییر خودکار یا انتقال اتمیک ندارد.</p>
          <label htmlFor="target-ward">شناسه بخش مقصد آزمایشی
            <input id="target-ward" dir="ltr" value={targetDepartment}
              disabled={pending||!!uncertain} autoComplete="off"
              onChange={e=>setTargetDepartment(e.target.value)}/>
          </label>
          <p data-testid="center-adjustment-step" aria-live="polite">
            {pending?'در حال ثبت؛ اقدام تازه ممنوع است.':
             uncertain?'پاسخ ثبت قبلی نامعلوم است؛ ابتدا همان درخواست را بازیابی کنید.':
             adjustment.label}
          </p>
          <p>بین دو فرمان ممکن است پزشک در هر دو بخش منتسب باشد.
            این داده صرفاً آزمایشی است و نه مجوز خدمت بالینی یا تأیید مدیر.</p>
          {adjustment.kind&&<button type="button" disabled={pending||!!uncertain||!snapshot}
            onClick={()=>void adjustDepartment(adjustment.kind!,adjustment.departmentRef!)}>
            {adjustment.code==='ADD_TARGET'?'ثبت مستقل انتساب به بخش مقصد':
             'برداشتن مستقل انتساب از بخش مبدأ'}
          </button>}
          <p>مدیر: احرازنشده | پزشک: تأییدنشده | انتشار: DENIED_BY_DEFAULT</p>
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
      {snapshot && <DepartmentRoster
        key={center+':'+department+':'+snapshot.currentVersion}
        center={center} department={department} version={snapshot.currentVersion}/>}
      {snapshot && validRef(doctor) && <DoctorAffiliationHistory
        key={center+':'+doctor+':'+snapshot.currentVersion}
        center={center} doctor={doctor} version={snapshot.currentVersion}/>}
      {snapshot && <CenterAuditWorkspace key={center+':'+snapshot.currentVersion}
        center={center} version={snapshot.currentVersion}/>}
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
      <InternalSupportRead />
      <footer>TRN-TECH-073/074 | پایگاه‌داده موقت | فقط محیط localhost | بدون احراز هویت یا پذیرش واقعی</footer>
    </main>
  </>;
}
