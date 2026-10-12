import {useRef, useState, type FormEvent} from 'react';
import {SyntheticCostChoiceWorkbench} from './synthetic-cost-choice-workbench';

// TRN-TECH-093: this widget is stripped from ordinary builds by its caller.
// Only literal test references, anonymous local evaluation, no legal choice.
type EvidenceState = 'Missing' | 'ProvisionalClaim' | 'ConflictingClaims';
type Line = {
  component: string; state: EvidenceState; amount: string; currency: string;
  sourceRef: string; evidenceRef: string; sourceRevision: string;
};
export type Draft = {memberRef: string; previewId: string; previewRevision: string; lines: Line[]};
type EvaluatedLine = {
  component: string; state: EvidenceState; amount: string | null;
  currency: string | null; sourceRef: string | null;
};
export type Result = {
  contract: string; syntheticOnly: boolean; state: string; fingerprint: string;
  lines: EvaluatedLine[]; choiceCarriedForward: boolean; walletMutated: boolean;
  clinicalConsentGranted: boolean; financialCommitmentCreated: boolean;
  nationalReleaseAllowed: boolean;
};
const sources = [
  {key: 'ProviderService', label: 'خدمت ارائه‌دهنده'},
  {key: 'PrimaryInsurance', label: 'بیمه پایه'},
  {key: 'SupplementalInsurance', label: 'بیمه تکمیلی'},
  {key: 'TreatmentCredit', label: 'اعتبار درمان'},
  {key: 'MemberRemainder', label: 'سهم عضو'},
] as const;
const newDraft = (): Draft => ({
  memberRef: 'test:member84', previewId: crypto.randomUUID(), previewRevision: '1',
  lines: sources.map(s => ({component:s.key, state:'Missing' as const, amount:'',
    currency:'', sourceRef:'', evidenceRef:'', sourceRevision:'0'})),
});
const localOnly = () => (window.location.hostname === '127.0.0.1' ||
  window.location.hostname === 'localhost') && window.location.port === '4318';

export function SyntheticCostWorkbench() {
  const [draft, setDraft] = useState<Draft>(newDraft);
  const [result, setResult] = useState<Result | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const generation = useRef(0);

  if (!localOnly()) return null;

  function change(update: (previous: Draft) => Draft) {
    generation.current++;
    setDraft(update);
    setResult(null);
    setError('');
    setBusy(false);
  }
  function patchLine(index: number, patch: Partial<Line>) {
    change(old => ({...old, lines:old.lines.map((line,i) => i === index ?
      {...line,...patch} : line)}));
  }
  function changeState(index: number, state: EvidenceState) {
    change(old => ({...old, lines:old.lines.map((line,i) => {
      if (i !== index) return line;
      if (state === 'Missing') return {...line, state, amount:'', currency:'',
        sourceRef:'', evidenceRef:'', sourceRevision:'0'};
      return {...line, state, amount:'', currency:'', sourceRef:'test:'+line.component,
        evidenceRef:crypto.randomUUID(), sourceRevision:'1'};
    })}));
  }
  async function inspect(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const seq = ++generation.current;
    setResult(null);
    setError('');
    setBusy(true);
    const body = {
      memberRef:draft.memberRef, previewId:draft.previewId,
      previewRevision:Number(draft.previewRevision),
      lines:draft.lines.map(line => ({
        component:line.component, state:line.state,
        amount:line.state === 'ProvisionalClaim' ? line.amount : null,
        currency:line.state === 'ProvisionalClaim' ? line.currency : null,
        sourceRef:line.state === 'Missing' ? null : line.sourceRef,
        evidenceRef:line.state === 'Missing' ? null : line.evidenceRef,
        sourceRevision:Number(line.sourceRevision),
      })),
    };
    try {
      const response = await fetch('/internal/ops/v1/synthetic-cost-workbench', {
        method:'POST', credentials:'omit', cache:'no-store',
        headers:{'Content-Type':'application/json'},body:JSON.stringify(body),
      });
      const data: Result = await response.json();
      if (seq !== generation.current) return;
      if (!response.ok || data.contract !== 'TRN-TECH-093' || data.syntheticOnly !== true ||
          data.nationalReleaseAllowed !== false || data.walletMutated !== false ||
          data.choiceCarriedForward !== false || data.clinicalConsentGranted !== false ||
          data.financialCommitmentCreated !== false || !Array.isArray(data.lines) ||
          data.lines.length !== 5 || typeof data.fingerprint !== 'string' ||
          !/^[A-F0-9]{64}$/.test(data.fingerprint)) {
        setError('ورودی یا پاسخ آزمایشی معتبر نیست؛ هیچ نتیجه‌ای پذیرفته نشد.');
        return;
      }
      setResult(data);
    } catch {
      if (seq === generation.current) setError('سرویس محلی پاسخ نداد؛ نتیجه‌ای ثبت نشده است.');
    } finally {
      if (seq === generation.current) setBusy(false);
    }
  }
  return (
    <section aria-labelledby="synthetic-cost-workbench-heading" data-testid="synthetic-cost-workbench">
      <div className="section-header">
        <div>
          <p className="eyebrow">TRN-TECH-093 · فقط آزمایشگاه محلی</p>
          <h2 id="synthetic-cost-workbench-heading">ارزیابی تعاملی پنج منشأ ساختگی در موتور .NET</h2>
        </div>
        <span className="readonly-pill">بدون عضو یا منبع واقعی</span>
      </div>
      <p className="quiet">
        ورود داده فقط با شناسه‌های test: مجاز است. خروجی صرفاً ادعای آزمایشی،
        نه قیمت قطعی، پوشش بیمه، موجودی اعتبار یا تعهد عضو است.
        تغییر هر فیلد یا نسخه، نتیجه قبلی را بی‌اعتبار می‌کند.
      </p>
      <form onSubmit={inspect} aria-label="فرم آزمایش پنج منشأ هزینه">
        <div className="status-grid">
          <label>شناسه عضو ساختگی
            <input aria-label="شناسه عضو ساختگی" value={draft.memberRef}
              onChange={e=>change(p=>({...p,memberRef:e.target.value}))}/>
          </label>
          <label>شناسه نسخه برآورد UUID
            <input aria-label="شناسه نسخه برآورد" value={draft.previewId}
              onChange={e=>change(p=>({...p,previewId:e.target.value}))}/>
          </label>
          <label>شماره نسخه برآورد
            <input aria-label="شماره نسخه برآورد" type="number" min="1" step="1"
              value={draft.previewRevision}
              onChange={e=>change(p=>({...p,previewRevision:e.target.value}))}/>
          </label>
        </div>
        <div className="status-grid" role="list" aria-label="منابع پنج‌گانه آزمایشی">
          {sources.map((source, i) => {
            const line = draft.lines[i];
            return (
              <fieldset className="status-card" role="listitem" key={source.key}>
                <legend>{source.label}</legend>
                <label>وضعیت ادعای منشأ
                  <select aria-label={source.label+' وضعیت'} value={line.state}
                    onChange={e=>changeState(i,e.target.value as EvidenceState)}>
                    <option value="Missing">Missing — نامعلوم</option>
                    <option value="ProvisionalClaim">ProvisionalClaim — صرفاً ادعا</option>
                    <option value="ConflictingClaims">ConflictingClaims — متعارض</option>
                  </select>
                </label>
                {line.state !== 'Missing' ? (
                  <>
                    <label>شناسه منبع آزمایشی
                      <input aria-label={source.label+' مرجع'} value={line.sourceRef}
                        onChange={e=>patchLine(i,{sourceRef:e.target.value})}/>
                    </label>
                    <label>شناسه شاهد ساختگی UUID
                      <input aria-label={source.label+' شاهد'} value={line.evidenceRef}
                        onChange={e=>patchLine(i,{evidenceRef:e.target.value})}/>
                    </label>
                    <label>نسخه منبع
                      <input aria-label={source.label+' نسخه'} type="number" min="1" step="1"
                        value={line.sourceRevision}
                        onChange={e=>patchLine(i,{sourceRevision:e.target.value})}/>
                    </label>
                  </>
                ) : null}
                {line.state === 'ProvisionalClaim' ? (
                  <>
                    <label>مبلغ ادعایی غیرقطعی (متن)
                      <input aria-label={source.label+' مبلغ'} inputMode="decimal"
                        value={line.amount}
                        onChange={e=>patchLine(i,{amount:e.target.value})}/>
                    </label>
                    <label>کد ارز ادعایی
                      <input aria-label={source.label+' ارز'} value={line.currency}
                        onChange={e=>patchLine(i,{currency:e.target.value})}/>
                    </label>
                  </>
                ) : null}
                {line.state === 'ConflictingClaims' ? (
                  <p className="quiet">مبلغ متعارض نمایش یا وارد نمی‌شود.</p>
                ) : null}
              </fieldset>
            );
          })}
        </div>
        <button type="submit" disabled={busy}>ارزیابی دستی با موتور .NET</button>
      </form>
      {error ? <p role="alert">{error}</p> : null}
      <SyntheticCostChoiceWorkbench draft={draft} preview={result} />
      {result ? (
        <div role="status" data-testid="synthetic-cost-result">
          <p><strong>وضعیت موتور .NET: {result.state}</strong></p>
          <p>اثر انگشت ادعاهای همین نسخه: <code>{result.fingerprint}</code></p>
          <ul>
            {result.lines.map(line=>(
              <li key={line.component}>{line.component}: {line.state}
                {line.state === 'ProvisionalClaim' && line.amount !== null
                  ? ' · ادعای تأییدنشده: '+line.amount+' '+line.currency : ' · مبلغ معتبر موجود نیست'}
                {line.sourceRef ? ' · '+line.sourceRef : ''}
              </li>
            ))}
          </ul>
          <p>آگاهی/انتخاب قبلی منتقل نشد؛ رضایت پزشکی، تعهد مالی و تغییر کیف پول ایجاد نشد.</p>
        </div>
      ) : null}
      <p className="emergency-note">
        در فوریت پزشکی، دریافت کمک فوری را به تکمیل این آزمایش یا اطلاع‌رسانی هزینه موکول نکنید.
      </p>
    </section>
  );
}
