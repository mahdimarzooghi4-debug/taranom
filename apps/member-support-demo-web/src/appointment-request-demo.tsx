import { useState, type FormEvent } from 'react';

type CenterDirectory = {
  contract?: string;
  syntheticOnly?: boolean;
  centerRef?: string;
  currentVersion?: number;
  departments?: string[];
  managerAuthenticated?: boolean;
  professionalQualificationVerified?: boolean;
  nationalReleaseAllowed?: boolean;
};
type RosterDoctor = {
  doctorRef: string;
  affiliation: string;
  assignmentUnverified: boolean;
  clinicalPrivilegesGranted: boolean;
  licensureVerified: boolean;
};
type DepartmentRoster = {
  contract?: string;
  syntheticOnly?: boolean;
  outcome?: string;
  centerRef?: string;
  departmentRef?: string;
  currentVersion?: number;
  asOfVersion?: number;
  doctors?: RosterDoctor[];
  doctorCount?: number;
  managerAuthenticated?: boolean;
  professionalQualificationVerified?: boolean;
  clinicalAccessGranted?: boolean;
  providerContractVerified?: boolean;
  billingAllowed?: boolean;
  nationalReleaseAllowed?: boolean;
};
type RequestBody = {
  requestId: string;
  expectedRevision: 0;
  memberRef: string;
  centerRef: string;
  departmentRef: string;
  requestedDoctorRef: string;
  slotRef: string;
  unverifiedScheduleSelection: null;
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
type AppointmentHistoryRow = {
  requestRef: string; centerRef: string; revision: number;
  departmentRef: string; requestedDoctorRef: string; slotRef: string;
};
type AppointmentHistory = {
  contract?: string; syntheticOnly?: boolean;
  source?: string; memberRef?: string; afterRequestRef?: string; limit?: number;
  hasMore?: boolean; nextAfterRequestRef?: string | null;
  requests?: AppointmentHistoryRow[];
  memberAuthenticated?: boolean; centerAuthenticated?: boolean;
  requestedDoctorLicensed?: boolean; slotAvailabilityVerified?: boolean;
  bookingConfirmed?: boolean; clinicalActionAllowed?: boolean;
  nationalReleaseAllowed?: boolean;
};
type RecoveryState = 'UNRESOLVED' | 'CONFIRMED_ABSENT' | null;

const referencePattern = '^test:[A-Za-z0-9:_-]{1,123}$';
const receiptFlags: Array<keyof Receipt> = [
  'memberAuthenticated', 'centerAuthenticated', 'requestedDoctorLicensed',
  'slotAvailabilityVerified', 'bookingConfirmed', 'clinicalActionAllowed',
  'nationalReleaseAllowed',
];
const directoryFlags: Array<keyof CenterDirectory> = [
  'managerAuthenticated', 'professionalQualificationVerified', 'nationalReleaseAllowed',
];
const rosterFlags: Array<keyof DepartmentRoster> = [
  'managerAuthenticated', 'professionalQualificationVerified', 'clinicalAccessGranted',
  'providerContractVerified', 'billingAllowed', 'nationalReleaseAllowed',
];
const isTestRef = (value: unknown): value is string =>
  typeof value === 'string' && /^test:[A-Za-z0-9:_-]{1,123}$/.test(value);
const allFalse = (value: Record<string, unknown>, flags: string[]) =>
  flags.every(flag => value[flag] === false);
const sameCommand = (left: RequestBody, right: RequestBody) =>
  left.requestId === right.requestId &&
  left.expectedRevision === right.expectedRevision &&
  left.memberRef === right.memberRef &&
  left.centerRef === right.centerRef &&
  left.departmentRef === right.departmentRef &&
  left.requestedDoctorRef === right.requestedDoctorRef &&
  left.slotRef === right.slotRef &&
  (left.unverifiedScheduleSelection ?? null) ===
    (right.unverifiedScheduleSelection ?? null);

const recoveryStorageKey = 'taranom.syntheticAppointmentAttempt.v1';
function readStoredAttempt(): Attempt | null {
  try {
    const stored = sessionStorage.getItem(recoveryStorageKey);
    if (!stored) return null;
    const envelope = JSON.parse(stored) as {
      version?: unknown; attempt?: unknown;
    };
    if (envelope.version !== 1 ||
        !envelope.attempt || typeof envelope.attempt !== 'object' ||
        Array.isArray(envelope.attempt)) return null;
    const candidate = envelope.attempt as Partial<Attempt>;
    const body = candidate.body as Partial<RequestBody> | undefined;
    if (typeof candidate.requestRef !== 'string' ||
        !/^test:appointment-[a-f0-9]{32}$/.test(candidate.requestRef) ||
        !body || typeof body !== 'object' || Array.isArray(body) ||
        Object.keys(candidate).some(key => !['requestRef', 'body'].includes(key)) ||
        ![7, 8].includes(Object.keys(body).length) ||
        Object.keys(body).some(key => ![
          'requestId', 'expectedRevision', 'memberRef', 'centerRef',
          'departmentRef', 'requestedDoctorRef', 'slotRef',
          'unverifiedScheduleSelection',
        ].includes(key)) ||
        (body.unverifiedScheduleSelection !== undefined &&
          body.unverifiedScheduleSelection !== null) ||
        typeof body.requestId !== 'string' ||
        !/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(body.requestId) ||
        body.expectedRevision !== 0 ||
        ![body.memberRef, body.centerRef, body.departmentRef,
          body.requestedDoctorRef, body.slotRef].every(isTestRef)) return null;
    return {
      requestRef: candidate.requestRef,
      body: { ...body, unverifiedScheduleSelection: null } as RequestBody,
    };
  } catch {
    return null;
  }
}
function persistAttempt(attempt: Attempt): boolean {
  try {
    sessionStorage.setItem(recoveryStorageKey, JSON.stringify({
      version: 1, attempt,
    }));
    return true;
  } catch {
    return false;
  }
}
function clearStoredAttempt() {
  try { sessionStorage.removeItem(recoveryStorageKey); } catch { /* best effort */ }
}

export function AppointmentRequestDemo() {
  const [memberRef, setMemberRef] = useState('');
  const [centerRef, setCenterRef] = useState('');
  const [slotRef, setSlotRef] = useState('');
  const [directory, setDirectory] = useState<CenterDirectory | null>(null);
  const [departmentRef, setDepartmentRef] = useState('');
  const [roster, setRoster] = useState<DepartmentRoster | null>(null);
  const [requestedDoctorRef, setRequestedDoctorRef] = useState('');
  const [catalogBusy, setCatalogBusy] = useState(false);
  const [catalogMessage, setCatalogMessage] = useState('');
  const [attempt, setAttempt] = useState<Attempt | null>(() => readStoredAttempt());
  const [receipt, setReceipt] = useState<Receipt | null>(null);
  const [recovery, setRecovery] = useState<RecoveryState>(() =>
    readStoredAttempt() ? 'UNRESOLVED' : null,
  );
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState(() =>
    readStoredAttempt()
      ? 'درخواست آزمایشی قبلی بازیابی شد؛ پیش از ارسال مجدد، وضعیت همان شناسه را بررسی کنید.'
      : '',
  );
  const [history, setHistory] = useState<AppointmentHistory | null>(null);
  const [historyBusy, setHistoryBusy] = useState(false);
  const [historyMessage, setHistoryMessage] = useState('');


  const loadMemberHistory = async (after = '') => {
    const selectedMember = memberRef.trim();
    if (!isTestRef(selectedMember) || historyBusy || attempt !== null) return;
    setHistoryBusy(true);
    setHistory(null);
    setHistoryMessage('در حال خواندن درخواست‌های آزمایشی از دفتر ثبت…');
    const query = new URLSearchParams({ limit: '10' });
    if (after) query.set('afterRequestRef', after);
    try {
      const response = await fetch(
        '/internal/demo/v1/members/' + encodeURIComponent(selectedMember) +
          '/appointment-requests?' + query.toString(),
        { headers: { Accept: 'application/json' }, credentials: 'omit', cache: 'no-store' },
      );
      const raw: unknown = await response.json();
      const value = raw as AppointmentHistory;
      const rows = value.requests;
      const allFlagsFalse = [
        'memberAuthenticated', 'centerAuthenticated', 'requestedDoctorLicensed',
        'slotAvailabilityVerified', 'bookingConfirmed', 'clinicalActionAllowed',
        'nationalReleaseAllowed',
      ].every(flag => (raw as Record<string, unknown>)[flag] === false);
      let previous = after;
      const rowsValid = Array.isArray(rows) && rows.length <= 10 && rows.every(row => {
        const valid = isTestRef(row.requestRef) && row.requestRef > previous &&
          isTestRef(row.centerRef) && Number.isSafeInteger(row.revision) && row.revision >= 1 &&
          isTestRef(row.departmentRef) && isTestRef(row.requestedDoctorRef) &&
          isTestRef(row.slotRef) &&
          Object.keys(row).every(key => [
            'requestRef', 'centerRef', 'revision', 'departmentRef',
            'requestedDoctorRef', 'slotRef',
          ].includes(key));
        if (valid) previous = row.requestRef;
        return valid;
      });
      const valid = response.ok && value.contract === 'TRN-TECH-222' &&
        value.syntheticOnly === true &&
        value.source === 'VERIFIED_ISOLATED_POSTGRES_APPOINTMENT_JOURNAL' &&
        value.memberRef === selectedMember && value.afterRequestRef === after &&
        value.limit === 10 && typeof value.hasMore === 'boolean' && rowsValid &&
        (value.hasMore
          ? rows!.length === 10 && value.nextAfterRequestRef === previous
          : value.nextAfterRequestRef === null) && allFlagsFalse;
      if (!valid) {
        setHistoryMessage('تاریخچه یا یکپارچگی آن قابل تأیید نیست؛ درخواستی نمایش داده نشد.');
        return;
      }
      setHistory(value);
      setHistoryMessage('درخواست‌های ساختگی از دفتر ثبت خوانده شدند؛ این شناسه احراز هویت عضو نیست.');
    } catch {
      setHistoryMessage('تاریخچه از دفتر ثبت دریافت نشد.');
    } finally {
      setHistoryBusy(false);
    }
  };

  const loadCenterDirectory = async () => {
    const requestedCenter = centerRef.trim();
    if (!isTestRef(requestedCenter) || catalogBusy || attempt) return;
    setCatalogBusy(true);
    setDirectory(null);
    setDepartmentRef('');
    setRoster(null);
    setRequestedDoctorRef('');
    setCatalogMessage('در حال خواندن فهرست آزمایشی مرکز…');
    try {
      const response = await fetch('/internal/demo/v1/centers/' + encodeURIComponent(requestedCenter), {
        headers: { Accept: 'application/json' },
      });
      const result = await response.json() as CenterDirectory;
      const validDirectory = response.ok &&
        result.contract === 'TRN-TECH-073' &&
        result.syntheticOnly === true &&
        result.centerRef === requestedCenter &&
        Number.isSafeInteger(result.currentVersion) &&
        Array.isArray(result.departments) &&
        result.departments.every(isTestRef) &&
        allFalse(result as Record<string, unknown>, directoryFlags);
      if (!validDirectory) {
        setCatalogMessage('فهرست مرکز خوانا یا قابل تأیید نیست؛ شناسه ساختگی مرکز را بررسی کنید.');
        return;
      }
      setDirectory(result);
      setCatalogMessage('فهرست مرکز در نسخه ' + result.currentVersion + ' خوانده شد؛ پزشکان هنوز احراز صلاحیت نشده‌اند.');
    } catch {
      setCatalogMessage('فهرست مرکز دریافت نشد؛ انتخاب تخصص و پزشک غیرفعال مانده است.');
    } finally {
      setCatalogBusy(false);
    }
  };

  const loadDepartmentRoster = async () => {
    if (!directory || !departmentRef || catalogBusy || attempt) return;
    const selectedCenter = directory.centerRef!;
    const selectedDepartment = departmentRef;
    setCatalogBusy(true);
    setRoster(null);
    setRequestedDoctorRef('');
    setCatalogMessage('در حال بازخوانی فهرست پزشکان همین تخصص…');
    const query = new URLSearchParams({
      expectedCurrentVersion: String(directory.currentVersion),
      asOfVersion: String(directory.currentVersion),
    });
    try {
      const response = await fetch(
        '/internal/demo/v1/centers/' + encodeURIComponent(selectedCenter) +
          '/departments/' + encodeURIComponent(selectedDepartment) + '/roster?' + query,
        { headers: { Accept: 'application/json' } },
      );
      const result = await response.json() as DepartmentRoster;
      const doctors = result.doctors;
      const validRoster = response.ok &&
        result.contract === 'TRN-TECH-102' &&
        result.syntheticOnly === true &&
        result.outcome === 'UNVERIFIED_DEPARTMENT_ROSTER' &&
        result.centerRef === selectedCenter &&
        result.departmentRef === selectedDepartment &&
        result.currentVersion === directory.currentVersion &&
        result.asOfVersion === directory.currentVersion &&
        Array.isArray(doctors) &&
        result.doctorCount === doctors.length &&
        doctors.every(doctor =>
          isTestRef(doctor.doctorRef) &&
          doctor.affiliation === 'PendingIndependentVerification' &&
          doctor.assignmentUnverified === true &&
          doctor.clinicalPrivilegesGranted === false &&
          doctor.licensureVerified === false
        ) &&
        allFalse(result as Record<string, unknown>, rosterFlags);
      if (!validRoster) {
        setCatalogMessage(
          response.status === 409
            ? 'نسخه مرکز تغییر کرده؛ فهرست مرکز را دوباره بخوانید.'
            : 'فهرست تخصص قابل تأیید نیست؛ پزشک برای درخواست قابل انتخاب نیست.',
        );
        return;
      }
      setRoster(result);
      setCatalogMessage(doctors.length
        ? 'پزشکان ثبت‌شده این تخصص بارگذاری شدند؛ صلاحیت حرفه‌ای هیچ‌کدام تأیید نشده است.'
        : 'برای این تخصص پزشک آزمایشی ثبت نشده است.');
    } catch {
      setCatalogMessage('فهرست پزشکان دریافت نشد؛ انتخاب پزشک غیرفعال مانده است.');
    } finally {
      setCatalogBusy(false);
    }
  };

  const sendExactAttempt = async (candidate: Attempt) => {
    setBusy(true);
    setRecovery('UNRESOLVED');
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
        result.contract === 'TRN-TECH-232' &&
        (result.outcome === 'RecordedRequest' || result.outcome === 'Replayed') &&
        result.revision === 1 && result.requestCount === 1 &&
        result.requests?.length === 1 &&
        result.requests[0]?.command !== undefined &&
        sameCommand(result.requests[0].command, candidate.body) &&
        allFalse(result as Record<string, unknown>, receiptFlags);
      if (!validReceipt) {
        setReceipt(null);
        setRecovery('UNRESOLVED');
        setMessage('پاسخ ارسال قابل تأیید نیست؛ ابتدا وضعیت همین شناسه را از دفتر ثبت بررسی کنید.');
        return;
      }
      setReceipt(result);
      clearStoredAttempt();
      setRecovery(null);
      setMessage('درخواست ثبت شد؛ نوبت هنوز تأیید نشده است. مرکز باید جداگانه پاسخ دهد.');
    } catch {
      setReceipt(null);
      setRecovery('UNRESOLVED');
      setMessage('پاسخی دریافت نشد و نتیجه نامشخص است؛ پیش از هر ارسال مجدد، وضعیت ثبت را بررسی کنید.');
    } finally {
      setBusy(false);
    }
  };

  const checkExactAttempt = async () => {
    if (!attempt || busy || receipt) return;
    setBusy(true);
    setMessage('در حال بررسی همان شناسه درخواست در دفتر ثبت آزمایشی…');
    try {
      const response = await fetch(
        '/internal/demo/v1/appointment-requests/' + encodeURIComponent(attempt.requestRef),
        { headers: { Accept: 'application/json' } },
      );
      const result = await response.json() as Receipt;
      const commonValid = response.ok &&
        result.contract === 'TRN-TECH-232' &&
        result.outcome === 'Read' &&
        Number.isSafeInteger(result.revision) &&
        Number.isSafeInteger(result.requestCount) &&
        Array.isArray(result.requests) &&
        allFalse(result as Record<string, unknown>, receiptFlags);
      if (!commonValid) {
        setRecovery('UNRESOLVED');
        setMessage('وضعیت درخواست از دفتر ثبت قابل تأیید نیست؛ ارسال مجدد همچنان غیرفعال است.');
        return;
      }
      const requestEvents = result.requests;
      if (result.revision === 1 && result.requestCount === 1 &&
          Array.isArray(requestEvents) && requestEvents.length === 1 &&
          requestEvents[0]?.command &&
          sameCommand(requestEvents[0].command, attempt.body)) {
        setReceipt({ ...result, outcome: 'ReadBackConfirmed' });
        clearStoredAttempt();
        setRecovery(null);
        setMessage('ثبت همان درخواست با بازخوانی تأیید شد؛ نوبت هنوز تأیید نشده است.');
        return;
      }
      if (result.revision === 0 && result.requestCount === 0 &&
          Array.isArray(requestEvents) && requestEvents.length === 0) {
        setRecovery('CONFIRMED_ABSENT');
        setMessage('دفتر ثبت نشان داد این درخواست هنوز ثبت نشده؛ اکنون ارسال دستی همان درخواست ممکن است.');
        return;
      }
      setRecovery('UNRESOLVED');
      setMessage('محتوای دفتر ثبت با درخواست نگه‌داری‌شده منطبق نیست؛ ارسال مجدد غیرفعال ماند.');
    } catch {
      setRecovery('UNRESOLVED');
      setMessage('دفتر ثبت پاسخ نداد؛ وضعیت نامشخص است و ارسال مجدد فعال نمی‌شود.');
    } finally {
      setBusy(false);
    }
  };

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (busy || attempt || !event.currentTarget.reportValidity()) return;
    if (!directory || !roster || !roster.doctors?.some(
      doctor => doctor.doctorRef === requestedDoctorRef,
    )) {
      setMessage('ابتدا فهرست مرکز و فهرست پزشکان همین تخصص را بارگذاری و پزشک را انتخاب کنید.');
      return;
    }
    const body: RequestBody = {
      requestId: crypto.randomUUID(),
      expectedRevision: 0,
      memberRef: memberRef.trim(),
      centerRef: directory.centerRef!,
      departmentRef,
      requestedDoctorRef,
      slotRef: slotRef.trim(),
      // No schedule source is connected in this demo; preserve that absence explicitly.
      unverifiedScheduleSelection: null,
    };
    const candidate = {
      requestRef: 'test:appointment-' + crypto.randomUUID().replaceAll('-', ''),
      body,
    };
    if (!persistAttempt(candidate)) {
      setMessage('وضعیت بازیابی در همین زبانه ذخیره نشد؛ برای جلوگیری از درخواست بدون امکان بازیابی، چیزی ارسال نشد.');
      return;
    }
    setAttempt(candidate);
    void sendExactAttempt(candidate);
  };

  const retry = () => {
    if (!attempt || recovery !== 'CONFIRMED_ABSENT' || busy || receipt) return;
    void sendExactAttempt(attempt);
  };

  return <section className="appointment-request-demo" aria-labelledby="appointment-request-title">
    <h1 id="appointment-request-title">درخواست نوبت — محیط آزمایشی عضو</h1>
    <div role="note" className="notice">
      <strong>فقط شناسه‌های ساختگی — بدون احراز هویت واقعی</strong>
      <p>این فرم فقط قصد درخواست را در پایگاه آزمایشی ثبت می‌کند. ظرفیت پزشک بررسی نمی‌شود و رزرو، نسخه یا اقدام بالینی انجام نمی‌شود.</p>
      <p>پزشک را خود بیمار انتخاب می‌کند؛ ترنم انتخاب نهایی را به‌جای بیمار انجام نمی‌دهد.</p>
    </div>
    <form onSubmit={submit}>
      <fieldset disabled={busy || attempt !== null}>
        <legend>عضو و مرکز درمانی</legend>
        <div className="fields">
          <label>
            شناسه عضو آزمایشی
            <input required pattern={referencePattern} maxLength={128} autoComplete="off" dir="ltr"
              value={memberRef} onChange={event => setMemberRef(event.target.value)} />
            <span className="muted">نمونه: test:member-001</span>
          </label>
          <label>
            شناسه مرکز درمانی آزمایشی
            <input required pattern={referencePattern} maxLength={128} autoComplete="off" dir="ltr"
              value={centerRef}
              onChange={event => {
                setCenterRef(event.target.value);
                setDirectory(null);
                setDepartmentRef('');
                setRoster(null);
                setRequestedDoctorRef('');
                setCatalogMessage('');
              }} />
            <span className="muted">نمونه: test:center-001؛ فهرست واقعی/ملی در این محیط موجود نیست.</span>
          </label>
        </div>
        <div className="actions">
          <button type="button" onClick={() => void loadCenterDirectory()}
            disabled={catalogBusy || !isTestRef(centerRef) || attempt !== null}>
            خواندن تخصص‌های ثبت‌شده مرکز
          </button>
          <button type="button" onClick={() => void loadMemberHistory()}
            disabled={historyBusy || !isTestRef(memberRef.trim()) || attempt !== null}>
            خواندن درخواست‌های قبلی آزمایشی
          </button>
        </div>
      </fieldset>
      {catalogMessage && <p role="status" aria-live="polite" className="status">{catalogMessage}</p>}
      {historyMessage && <p role="status" aria-live="polite" className="status">
        {historyBusy ? 'در حال خواندن تاریخچه…' : historyMessage}
      </p>}
      {history && <section className="panel" aria-labelledby="appointment-history-heading">
        <h2 id="appointment-history-heading">درخواست‌های نوبت آزمایشی قبلی</h2>
        <p>این سوابق با شناسه ساختگی فیلتر شده‌اند؛ عضویت یا مالکیت هویت احراز نشده است.</p>
        {history.requests?.length
          ? <ul>{history.requests.map(row => <li key={row.requestRef}>
              <article className="details">
                <h3>درخواست ثبت‌شده — نوبت تأییدنشده</h3>
                <p>شناسه درخواست: <code>{row.requestRef}</code> | نسخه {row.revision}</p>
                <p>مرکز: <code>{row.centerRef}</code> | بخش: <code>{row.departmentRef}</code></p>
                <p>پزشک انتخاب‌شده توسط بیمار: <code>{row.requestedDoctorRef}</code></p>
                <p>بازه درخواستی: <code>{row.slotRef}</code></p>
                <p>ظرفیت بررسی‌نشده است؛ رزرو انجام نشده است.</p>
              </article>
            </li>)}</ul>
          : <p>درخواستی برای این شناسه آزمایشی پیدا نشد.</p>}
        {history.hasMore && history.nextAfterRequestRef &&
          <button type="button" disabled={historyBusy}
            onClick={() => void loadMemberHistory(history.nextAfterRequestRef!)}>
            صفحه بعد درخواست‌ها
          </button>}
      </section>}
      {directory && <fieldset disabled={catalogBusy || attempt !== null}>
        <legend>انتخاب تخصص و پزشک از فهرست ثبت‌شده مرکز</legend>
        <label>
          تخصص یا بخش
          <select required value={departmentRef}
            onChange={event => {
              setDepartmentRef(event.target.value);
              setRoster(null);
              setRequestedDoctorRef('');
            }}>
            <option value="">انتخاب کنید</option>
            {directory.departments?.map(ref =>
              <option key={ref} value={ref}>{ref}</option>)}
          </select>
        </label>
        <button type="button" onClick={() => void loadDepartmentRoster()}
          disabled={catalogBusy || !departmentRef || attempt !== null}>
          خواندن پزشکان این تخصص در همان نسخه
        </button>
        {roster && <label>
          پزشک انتخاب‌شده توسط بیمار
          <select required value={requestedDoctorRef}
            onChange={event => setRequestedDoctorRef(event.target.value)}>
            <option value="">بیمار پزشک را انتخاب کند</option>
            {roster.doctors?.map(doctor =>
              <option key={doctor.doctorRef} value={doctor.doctorRef}>
                {doctor.doctorRef} — صلاحیت تأیید نشده
              </option>)}
          </select>
        </label>}
        <p className="muted">
          فهرست به نسخه {directory.currentVersion} مرکز مقید است. نمایش پزشک در فهرست، احراز هویت، مجوز طبابت یا دسترسی بالینی را ثابت نمی‌کند.
        </p>
      </fieldset>}
      <fieldset disabled={busy || attempt !== null}>
        <legend>بازه زمانی مورد درخواست</legend>
        <label>
          شناسه بازه زمانی درخواستی آزمایشی
          <input required pattern={referencePattern} maxLength={128} autoComplete="off" dir="ltr"
            value={slotRef} onChange={event => setSlotRef(event.target.value)} />
          <span className="muted">نمونه: test:slot-001؛ زمان‌بندی و ظرفیت در این محیط بررسی نمی‌شود.</span>
        </label>
      </fieldset>
      {!attempt && <div className="actions">
        <button type="submit" disabled={busy}>ارسال درخواست نوبت آزمایشی</button>
      </div>}
    </form>
    {message && <p role="status" aria-live="polite" className="status">{message}</p>}
    {attempt && !receipt && !busy && <div className="actions">
      {recovery !== 'CONFIRMED_ABSENT' && <button type="button" onClick={() => void checkExactAttempt()}>
        بررسی وضعیت همین درخواست
      </button>}
      {recovery === 'CONFIRMED_ABSENT' && <>
        <button type="button" onClick={() => void checkExactAttempt()}>بازخوانی دوباره وضعیت</button>
        <button type="button" onClick={retry}>ارسال مجدد دقیق همان درخواست</button>
      </>}
    </div>}
    {receipt && attempt && <section className="panel" aria-labelledby="appointment-receipt-title">
      <h2 id="appointment-receipt-title">رسید درخواست آزمایشی</h2>
      <p>شناسه درخواست: <code data-testid="appointment-request-ref">{attempt.requestRef}</code></p>
      <p>پزشک انتخاب‌شده توسط بیمار: <code>{attempt.body.requestedDoctorRef}</code></p>
      <p>نتیجه فنی: <code>{receipt.outcome}</code> — نسخه {receipt.revision}</p>
      <strong>نوبت تأیید نشده است؛ پاسخ مرکز و تأیید ظرفیت در این محیط وجود ندارد.</strong>
    </section>}
  </section>;
}
