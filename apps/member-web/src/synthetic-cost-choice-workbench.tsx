import {useRef, useState} from 'react';
import type {Draft, Result} from './synthetic-cost-workbench';

// TRN-TECH-094: in-memory, ephemeral, unsigned paper-choice replay.
// Never member authentication, real consent, payable debt or a signed record.
type Choice = 'AcknowledgedUncertainty' | 'Deferred' | 'RequestedClarification';
type Command = {
  requestId: string; expectedRevision: number; memberRef: string;
  previewId: string; previewRevision: number; previewFingerprint: string; choice: Choice;
};
type Pinned = {input: ReturnType<typeof prepare>; fingerprint: string};
type Inspection = {
  contract: string; syntheticOnly: boolean; outcome: string; continuity: string;
  historyRevision: number; lastUnverifiedChoice: Choice | null;
  changedComponents: string[]; choiceCarriedForward: boolean;
  financialCommitmentCreated: boolean; clinicalConsentGranted: boolean;
  walletMutated: boolean; memberAuthenticated: boolean;
  nationalReleaseAllowed: boolean; officialChoiceRecorded: boolean;
  choiceSignatureValid: boolean;
};
function prepare(draft: Draft) {
  return {
    memberRef: draft.memberRef, previewId: draft.previewId,
    previewRevision: Number(draft.previewRevision),
    lines: draft.lines.map(line => ({
      component: line.component, state: line.state,
      amount: line.state === 'ProvisionalClaim' ? line.amount : null,
      currency: line.state === 'ProvisionalClaim' ? line.currency : null,
      sourceRef: line.state === 'Missing' ? null : line.sourceRef,
      evidenceRef: line.state === 'Missing' ? null : line.evidenceRef,
      sourceRevision: Number(line.sourceRevision),
    }))
  };
}
function validResult(v: Inspection): boolean {
  return v.contract === 'TRN-TECH-094' && v.syntheticOnly === true &&
    v.choiceCarriedForward === false && v.financialCommitmentCreated === false &&
    v.clinicalConsentGranted === false && v.walletMutated === false &&
    v.memberAuthenticated === false && v.nationalReleaseAllowed === false &&
    v.officialChoiceRecorded === false && v.choiceSignatureValid === false &&
    Number.isInteger(v.historyRevision) && v.historyRevision >= 0 &&
    Array.isArray(v.changedComponents) && typeof v.outcome === 'string' &&
    typeof v.continuity === 'string';
}
export function SyntheticCostChoiceWorkbench(
  {draft, preview}: {draft: Draft; preview: Result | null}
) {
  const [pinned, setPinned] = useState<Pinned | null>(null);
  const [history, setHistory] = useState<Command[]>([]);
  const [choice, setChoice] = useState<Choice>('AcknowledgedUncertainty');
  const [last, setLast] = useState<Command | null>(null);
  const [inspection, setInspection] = useState<Inspection | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const generation = useRef(0);
  const same = pinned !== null && preview?.fingerprint === pinned.fingerprint &&
    draft.previewId === pinned.input.previewId &&
    Number(draft.previewRevision) === pinned.input.previewRevision;
  function start() {
    if (!preview || pinned || !/^[A-F0-9]{64}$/.test(preview.fingerprint)) return;
    generation.current++;
    setPinned({input:prepare(draft),fingerprint:preview.fingerprint});
    setHistory([]); setLast(null); setInspection(null); setError('');
  }
  function clear() {
    generation.current++;
    setPinned(null); setHistory([]); setLast(null); setInspection(null);
    setError(''); setBusy(false);
  }
  async function run(attempt: Command | null, compareCurrent: boolean) {
    if (!pinned || busy || (compareCurrent && !preview)) return;
    const seq = ++generation.current;
    setBusy(true); setError(''); setInspection(null);
    try {
      const res = await fetch('/internal/ops/v1/synthetic-cost-choice', {
        method:'POST',credentials:'omit',cache:'no-store',
        headers:{'Content-Type':'application/json'},
        body:JSON.stringify({
          previous:pinned.input,
          current:compareCurrent ? prepare(draft) : pinned.input,
          history,attempt
        })
      });
      const data: Inspection = await res.json();
      if (generation.current !== seq) return;
      if (!res.ok || !validResult(data)) {
        setError('توالی نسخه، شاهد یا فرمان آزمایشی معتبر نیست؛ هیچ انتخابی ثبت نشد.');
        return;
      }
      if (attempt && data.outcome === 'RecordedUnverified' &&
          data.historyRevision === history.length + 1) {
        setHistory(list => [...list,attempt]);
        setLast(attempt);
      } else if (attempt && data.outcome === 'Replayed' &&
                 data.historyRevision !== history.length) {
        setError('بازپخش ناسازگار رد شد.'); return;
      }
      setInspection(data);
    } catch {
      if (generation.current === seq)
        setError('اتصال به موتور محلی برقرار نشد. نتیجه‌ای ثبت نشده است.');
    } finally {
      if (generation.current === seq) setBusy(false);
    }
  }
  function choose() {
    if (!pinned || !same) return;
    void run({
      requestId:crypto.randomUUID(), expectedRevision:history.length,
      memberRef:pinned.input.memberRef,previewId:pinned.input.previewId,
      previewRevision:pinned.input.previewRevision,
      previewFingerprint:pinned.fingerprint,choice
    },false);
  }
  return (
    <section data-testid="synthetic-choice-workbench" aria-label="آزمایش پیوستگی انتخاب غیرقطعی">
      <h3>آزمایش انتخاب آگاهانه و پیوستگی نسخه · TRN-TECH-094</h3>
      <p className="quiet">فرمان و سابقه فقط در حافظه همین صفحه نگهداری می‌شود؛ هر بار موتور
        .NET کل تاریخچه ادعایی را دوباره بررسی می‌کند. این رسید قانونی، احراز هویت، رضایت
        درمان یا تعهد مالی نیست. هیچ انتخابی به نسخه جدید منتقل نمی‌شود.</p>
      {!pinned ? (
        <button type="button" disabled={!preview} onClick={start}>
          انتخاب آزمایشی برای همین نسخه برآورد
        </button>
      ) : (
        <>
          <p>نسخه سنجاق‌شده: {pinned.input.previewRevision} · رویدادهای آزمایشی: {history.length}</p>
          <label>پاسخ آزمایشی به هزینه غیرقطعی
            <select aria-label="نوع انتخاب آزمایشی" value={choice}
              onChange={e=>setChoice(e.target.value as Choice)}>
              <option value="AcknowledgedUncertainty">آگاهی از عدم قطعیت</option>
              <option value="Deferred">تعویق انتخاب</option>
              <option value="RequestedClarification">درخواست توضیح</option>
            </select>
          </label>
          <button type="button" disabled={!same || busy} onClick={choose}>
            شبیه‌سازی ثبت انتخاب در .NET
          </button>
          <button type="button" disabled={!last || busy}
            onClick={()=>{if(last)void run(last,false);}}>
            بازپخش دقیق آخرین فرمان
          </button>
          <button type="button" disabled={!preview || busy || same || history.length === 0}
            onClick={()=>void run(null,true)}>
            بررسی تغییر نسخه با تاریخچه قبلی
          </button>
          <button type="button" disabled={busy} onClick={clear}>
            شروع نسخه مستقل بدون انتقال انتخاب
          </button>
        </>
      )}
      {error ? <p role="alert">{error}</p> : null}
      {inspection ? (
        <div role="status" data-testid="synthetic-choice-result">
          <p>نتیجه موتور: <strong>{inspection.outcome}</strong></p>
          <p>پیوستگی: <strong>{inspection.continuity}</strong></p>
          <p>نسخه تاریخچه: {inspection.historyRevision}</p>
          <p>منشأهای تغییریافته: {inspection.changedComponents.join('، ') || 'هیچ'}</p>
          <p>آگاهی قبلی منتقل نشد. تعهد مالی، رضایت درمانی و عملیات کیف پول ایجاد نشد.</p>
        </div>
      ) : null}
    </section>
  );
}
