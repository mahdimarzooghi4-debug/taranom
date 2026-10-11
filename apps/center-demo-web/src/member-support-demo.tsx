import { useState } from 'react';

type FollowUp = { revision: number; noteRef: string; claimedActorRef: string };
type Support = {
  contract: 'TRN-TECH-075'; syntheticOnly: true;
  memberAuthenticated: false; complaintLegallySubmitted: false;
  insurerContacted: false; clinicalReviewCompleted: false; nationalReleaseAllowed: false;
  caseRef: string; revision: number; outcome: string;
  topic: 'MedicalServiceComplaint' | 'InsuranceCoverageDispute' | null;
  memberRef: string | null; originalRequestId: string | null;
  evidenceRef: string | null; claimedSubmitterRef: string | null;
  followUpCount: number; followUps: FollowUp[];
};
const testRef = (s: string): boolean =>
  s.length <= 128 && /^test:[A-Za-z0-9_:-]+$/.test(s);
const zero = '00000000-0000-0000-0000-000000000000';
function verified(value: unknown, caseRef: string): value is Support {
  if (!value || typeof value !== 'object') return false;
  const x = value as Partial<Support>;
  if (x.contract !== 'TRN-TECH-075' || x.syntheticOnly !== true ||
      x.memberAuthenticated !== false || x.complaintLegallySubmitted !== false ||
      x.insurerContacted !== false || x.clinicalReviewCompleted !== false ||
      x.nationalReleaseAllowed !== false || x.caseRef !== caseRef ||
      !Number.isSafeInteger(x.revision) || x.revision! < 0 ||
      typeof x.outcome !== 'string' || !Array.isArray(x.followUps) ||
      !Number.isSafeInteger(x.followUpCount) || x.followUpCount !== x.followUps.length) return false;
  if (x.revision === 0)
    return x.originalRequestId === null && x.memberRef === null &&
      x.topic === null && x.followUpCount === 0 && x.followUps.length === 0;
  return x.revision === x.followUpCount! + 1 &&
    (x.topic === 'MedicalServiceComplaint' || x.topic === 'InsuranceCoverageDispute') &&
    typeof x.originalRequestId === 'string' &&
    typeof x.memberRef === 'string' && testRef(x.memberRef) &&
    typeof x.evidenceRef === 'string' && testRef(x.evidenceRef) &&
    typeof x.claimedSubmitterRef === 'string' && testRef(x.claimedSubmitterRef) &&
    x.followUps.every(v => v && Number.isSafeInteger(v.revision) && v.revision >= 2 &&
      testRef(v.noteRef) && testRef(v.claimedActorRef));
}

export function MemberSupportDemo() {
  const [caseRef, setCaseRef] = useState('test:support-demo');
  const [memberRef, setMemberRef] = useState('test:member-demo');
  const [evidenceRef, setEvidenceRef] = useState('test:evidence-demo');
  const [submitter, setSubmitter] = useState('test:submitter-demo');
  const [actor, setActor] = useState('test:followup-actor-demo');
  const [note, setNote] = useState('test:note-demo');
  const [topic, setTopic] = useState<'MEDICAL_SERVICE_COMPLAINT' | 'INSURANCE_COVERAGE_DISPUTE'>('MEDICAL_SERVICE_COMPLAINT');
  const [current, setCurrent] = useState<Support | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('این فرم فقط درخواست‌ها و ارجاعات آزمایشی را ذخیره می‌کند.');

  async function read() {
    setBusy(true); setCurrent(null);
    if (!testRef(caseRef)) {
      setMessage('فقط شناسه پرونده آزمایشی با پیشوند test: مجاز است.');
      setBusy(false); return;
    }
    try {
      const response = await fetch('/internal/demo/v1/support/' + encodeURIComponent(caseRef),
        { cache: 'no-store', credentials: 'omit' });
      if (!response.ok) throw Error('UNAVAILABLE');
      const data: unknown = await response.json();
      if (!verified(data, caseRef)) throw Error('NOT_VERIFIED');
      setCurrent(data);
      setMessage('نسخه آزمایشی از PostgreSQL خوانده شد؛ هیچ موضوع پزشکی یا بیمه‌ای رسیدگی رسمی نشده است.');
    } catch {
      setMessage('خواندن پرونده آزمایشی ممکن نیست؛ هیچ وضعیت قبلی معتبر فرض نمی‌شود.');
    } finally { setBusy(false); }
  }

  async function write(followUp: boolean) {
    if (!current || busy) return;
    if (![caseRef,memberRef,evidenceRef,submitter].every(testRef) ||
        followUp && (![actor,note].every(testRef)) ||
        current.revision > 0 && !followUp ||
        current.revision === 0 && followUp ||
        current.revision > 0 && (current.memberRef !== memberRef ||
          current.evidenceRef !== evidenceRef || current.claimedSubmitterRef !== submitter))
    {
      setMessage('ارجاعات آزمایشی معتبر نیستند یا به پرونده اولیه تعلق ندارند.');
      return;
    }
    setBusy(true);
    try {
      const payload = {
        requestId: crypto.randomUUID(),
        expectedRevision: current.revision,
        action: followUp ? 'FOLLOW_UP_UNVERIFIED' : 'SUBMIT_UNVERIFIED',
        memberRef, topic: followUp ? (current.topic === 'InsuranceCoverageDispute'
          ? 'INSURANCE_COVERAGE_DISPUTE' : 'MEDICAL_SERVICE_COMPLAINT') : topic,
        unverifiedEvidenceRef: evidenceRef, claimedSubmitterRef: submitter,
        originalSubmissionRequestId: followUp ? current.originalRequestId : zero,
        claimedFollowUpActorRef: followUp ? actor : null,
        unverifiedFollowUpNoteRef: followUp ? note : null
      };
      const response = await fetch('/internal/demo/v1/support/' + encodeURIComponent(caseRef) + '/commands', {
        method: 'POST', cache: 'no-store', credentials: 'omit',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data: unknown = await response.json();
      if (!verified(data, caseRef)) throw Error('INVALID_TEST_RESULT');
      setCurrent(data);
      setMessage(response.ok
        ? 'فقط ارجاع آزمایشی ثبت شد؛ پرونده قانونی، رسیدگی بالینی یا مکاتبه با بیمه‌گر ایجاد نشده است.'
        : 'دستور اعمال نشد: ' + data.outcome + '؛ پرونده را دوباره بخوانید.');
    } catch {
      setCurrent(null);
      setMessage('عملیات ناموفق یا وضعیت نامعتبر بود. پیش از اقدام بعدی بازخوانی کنید.');
    } finally { setBusy(false); }
  }

  return <section className="panel" aria-labelledby="support-demo-title">
    <h2 id="support-demo-title">ثبت و پیگیری درخواست پشتیبانی آزمایشی عضو</h2>
    <p>این گردش مستقل از مدیریت مرکز است. تنها شناسه‌های ساختگی را ثبت می‌کند و هیچ شکایت واقعی، اقدام بیمه‌ای، تشخیص یا تسویه‌ای را انجام نمی‌دهد.</p>
    <div className="fields">
      <label htmlFor="sup-case">پرونده آزمایشی
        <input id="sup-case" dir="ltr" value={caseRef}
          onChange={e => { setCaseRef(e.target.value); setCurrent(null); }} autoComplete="off" />
      </label>
      <label htmlFor="sup-member">شناسه عضو فرضی
        <input id="sup-member" dir="ltr" value={memberRef}
          onChange={e => setMemberRef(e.target.value)} autoComplete="off" />
      </label>
      <label htmlFor="sup-evidence">ارجاع مدرک تأییدنشده
        <input id="sup-evidence" dir="ltr" value={evidenceRef}
          onChange={e => setEvidenceRef(e.target.value)} autoComplete="off" />
      </label>
      <label htmlFor="sup-submitter">ثبت‌کننده ادعایی
        <input id="sup-submitter" dir="ltr" value={submitter}
          onChange={e => setSubmitter(e.target.value)} autoComplete="off" />
      </label>
      <label htmlFor="sup-topic">موضوع آزمایشی
        <select id="sup-topic" value={topic}
          onChange={e => setTopic(e.target.value as typeof topic)}>
          <option value="MEDICAL_SERVICE_COMPLAINT">نارضایتی از خدمت پزشکی (تأییدنشده)</option>
          <option value="INSURANCE_COVERAGE_DISPUTE">اختلاف پوشش بیمه (تأییدنشده)</option>
        </select>
      </label>
      <label htmlFor="sup-actor">ارجاع پیگیر ادعایی
        <input id="sup-actor" dir="ltr" value={actor}
          onChange={e => setActor(e.target.value)} autoComplete="off" />
      </label>
      <label htmlFor="sup-note">ارجاع یادداشت پیگیری تأییدنشده
        <input id="sup-note" dir="ltr" value={note}
          onChange={e => setNote(e.target.value)} autoComplete="off" />
      </label>
    </div>
    <div className="actions">
      <button disabled={busy} type="button" onClick={() => void read()}>بازخوانی پرونده پشتیبانی آزمایشی</button>
      <button disabled={busy || !current || current.revision !== 0} type="button"
        onClick={() => void write(false)}>ثبت درخواست اولیه آزمایشی</button>
      <button disabled={busy || !current || current.revision === 0} type="button"
        onClick={() => void write(true)}>ثبت ارجاع پیگیری آزمایشی</button>
    </div>
    <p data-testid="support-status" aria-live="polite" className="status">
      {busy ? 'در حال ثبت یا خواندن از پایگاه آزمایشی...' : message}
    </p>
    {current && <section aria-label="وضعیت پرونده پشتیبانی آزمایشی">
      <p><strong>نسخه:</strong> {current.revision} | <strong>پیگیری‌های تأییدنشده:</strong> {current.followUpCount}</p>
      <p>{current.topic === 'MedicalServiceComplaint' ? 'موضوع: نارضایتی از خدمت پزشکی' :
        current.topic === 'InsuranceCoverageDispute' ? 'موضوع: اختلاف پوشش بیمه' : 'درخواست اولیه‌ای وجود ندارد'}</p>
      <p>تأیید عضویت: خیر | شکایت رسمی: خیر | تماس با بیمه‌گر: خیر | رسیدگی بالینی: خیر</p>
      <ul>{current.followUps.map(x => <li key={x.revision}>
        نسخه {x.revision}: یادداشت <code>{x.noteRef}</code> — ثبت‌کننده ادعایی <code>{x.claimedActorRef}</code>
      </li>)}</ul>
    </section>}
  </section>;
}
