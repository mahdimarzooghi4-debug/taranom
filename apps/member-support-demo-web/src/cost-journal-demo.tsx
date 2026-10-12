import {useState} from 'react';
import {CostSupportHandoff} from './cost-support-handoff';

// TRN-TECH-095: CI-only, exact test: fixtures, existing append-only PG journal.
// No verified estimate, identity, legal choice or medical/financial authority.
const names = [
  ['ProviderService','خدمت ارائه‌دهنده'],
  ['PrimaryInsurance','بیمه پایه'],
  ['SupplementalInsurance','بیمه تکمیلی'],
  ['TreatmentCredit','اعتبار درمان'],
  ['MemberRemainder','سهم عضو'],
] as const;
type LineState='Missing'|'ProvisionalClaim'|'ConflictingClaims';
type Line={component:string;state:LineState;amount:string;currency:string;sourceRef:string;evidenceRef:string;sourceRevision:number};
type State={previewId:string;memberRef:string;previewRevision:number;previewFingerprint:string;choiceRevision:number;historyCount:number;outcome:string;lastUnverifiedChoice:string|null;contract:string;syntheticOnly:boolean;memberAuthenticated:boolean;officialChoiceRecorded:boolean;choiceSignatureValid:boolean;financialCommitmentCreated:boolean;clinicalConsentGranted:boolean;walletMutated:boolean;insurerEntitlementVerified:boolean;officialEstimateVerified:boolean;nationalReleaseAllowed:boolean};
type Command={requestId:string;expectedRevision:number;memberRef:string;previewId:string;previewRevision:number;previewFingerprint:string;choice:string};
const testRef=(s:string)=>/^test:[A-Za-z0-9_:-]{1,123}$/.test(s);
const clean=()=>names.map(([component]):Line=>({component,state:'Missing',amount:'',currency:'',sourceRef:'',evidenceRef:'',sourceRevision:0}));
const good=(raw:unknown):raw is State=>{
 if(!raw||typeof raw!=='object')return false;
 const v=raw as Partial<State>;
 return v.contract==='TRN-TECH-095'&&v.syntheticOnly===true &&
  v.memberAuthenticated===false&&v.officialChoiceRecorded===false &&
  v.choiceSignatureValid===false&&v.financialCommitmentCreated===false &&
  v.clinicalConsentGranted===false&&v.walletMutated===false &&
  v.insurerEntitlementVerified===false&&v.officialEstimateVerified===false &&
  v.nationalReleaseAllowed===false&&typeof v.previewId==='string' &&
  testRef(v.memberRef||'')&&typeof v.previewFingerprint==='string'&&
  /^[A-F0-9]{64}$/.test(v.previewFingerprint)&&
  Number.isSafeInteger(v.choiceRevision)&&v.choiceRevision!>=0 &&
  v.historyCount===v.choiceRevision&&typeof v.outcome==='string';
};
export function CostJournalDemo() {
  const [previewId,setPreviewId]=useState<string>(crypto.randomUUID());
  const [memberRef,setMemberRef]=useState('test:cost-member');
  const [revision,setRevision]=useState('1');
  const [lines,setLines]=useState<Line[]>(clean);
  const [current,setCurrent]=useState<State|null>(null);
  const [pending,setPending]=useState<Command|null>(null);
  const [last,setLast]=useState<Command|null>(null);
  const [choice,setChoice]=useState('AcknowledgedUncertainty');
  const [message,setMessage]=useState('داده‌های آزمایشی هنوز در PostgreSQL ثبت نشده‌اند.');
  const [busy,setBusy]=useState(false);
  function changed(){
    setCurrent(null);setPending(null);setLast(null);
    setMessage('ورودی تغییر کرده است؛ نتیجه قبلی معتبر نیست.');
  }
  function patchLine(i:number,p:Partial<Line>){
    changed();
    setLines(old=>old.map((x,n)=>n===i?{...x,...p}:x));
  }
  function selectState(i:number,v:LineState){
    changed();
    setLines(old=>old.map((line,n)=>{
      if(n!==i)return line;
      if(v==='Missing')return {...line,state:v,amount:'',currency:'',sourceRef:'',evidenceRef:'',sourceRevision:0};
      return {...line,state:v,amount:'',currency:'',sourceRef:'test:'+line.component,
        evidenceRef:crypto.randomUUID(),sourceRevision:1};
    }));
  }
  function payload(){
    return {memberRef,previewId,previewRevision:Number(revision),lines:lines.map(x=>({
      component:x.component,state:x.state,
      amount:x.state==='ProvisionalClaim'?x.amount:null,
      currency:x.state==='ProvisionalClaim'?x.currency:null,
      sourceRef:x.state==='Missing'?null:x.sourceRef,
      evidenceRef:x.state==='Missing'?null:x.evidenceRef,
      sourceRevision:x.sourceRevision,
    }))};
  }
  async function request(url:string,method:string,data?:unknown) {
    const response=await fetch(url,{method,cache:'no-store',credentials:'omit',
      headers:method==='POST'?{'Content-Type':'application/json'}:undefined,
      body:data===undefined?undefined:JSON.stringify(data)});
    const value:unknown=await response.json();
    if(!good(value))throw Error('SERVER_RESULT_UNVERIFIABLE');
    return {status:response.status,value};
  }
  async function register(){
    if(busy||pending)return;
    setBusy(true);setCurrent(null);
    try{
      if(!testRef(memberRef))throw Error('TEST_MEMBER_REQUIRED');
      const result=await request('/internal/demo/v1/cost-previews','POST',payload());
      if(result.status!==200||!['Registered','Replayed'].includes(result.value.outcome)||
          result.value.previewId!==previewId||result.value.memberRef!==memberRef||
          result.value.previewRevision!==Number(revision))throw Error('REGISTRATION_DENIED');
      setCurrent(result.value);
      setMessage('پیش‌برآورد ساختگی به‌شکل تغییرناپذیر در PostgreSQL ثبت یا بازپخش شد.');
    }catch{
      setMessage('ثبت قابل تأیید نیست؛ عدد/رضایت یا پوشش واقعی استنباط نشد.');
    }finally{setBusy(false);}
  }
  async function read(){
    if(busy||pending)return;
    setBusy(true);setCurrent(null);setLast(null);
    try{
      const result=await request('/internal/demo/v1/cost-previews/'+encodeURIComponent(previewId),'GET');
      if(result.status!==200||result.value.previewId!==previewId)throw Error('READ_DENIED');
      setCurrent(result.value);
      setMessage('وضعیت واقعی Journal آزمایشی از PostgreSQL بازخوانی شد.');
    }catch{setMessage('داده آزمایشی پیدا نشد یا اصالت داخلی Journal قابل بررسی نیست.');}
    finally{setBusy(false);}
  }
  async function send(command:Command){
    if(busy)return;
    setBusy(true);setPending(command);setCurrent(null);
    try{
      const {status,value}=await request('/internal/demo/v1/cost-previews/'+
        encodeURIComponent(command.previewId)+'/choices','POST',command);
      if(status!==200||!['RecordedUnverified','Replayed'].includes(value.outcome)||
         value.previewId!==command.previewId ||
         value.previewFingerprint!==command.previewFingerprint ||
         value.choiceRevision<command.expectedRevision+1)throw Error('WRITE_UNVERIFIABLE');
      setCurrent(value);setLast(command);setPending(null);
      setMessage(value.outcome==='Replayed'?'همان درخواست بدون رویداد اضافی بازپخش شد.':
        'فقط انتخاب ساختگی به Journal اضافه شد؛ نه رضایت قانونی.');
    }catch{
      // ACK may have been lost AFTER the transaction committed. Freeze EXACT
      // command and do not invent a second UUID until reconciled.
      setMessage('نتیجه نامطمئن است؛ فقط تکرار دقیق همین UUID مجاز است.');
    }finally{setBusy(false);}
  }
  function choose(){
    if(!current||pending||busy||!['AcknowledgedUncertainty','Deferred','RequestedClarification'].includes(choice))return;
    void send({requestId:crypto.randomUUID(),expectedRevision:current.choiceRevision,
      memberRef:current.memberRef,previewId:current.previewId,
      previewRevision:current.previewRevision,
      previewFingerprint:current.previewFingerprint,choice});
  }
  const editingBlocked=busy||pending!==null;
  return <section className="panel" aria-labelledby="cost-journal-title">
    <h2 id="cost-journal-title">گردش‌کار واقعی PostgreSQL برای پنج منشأ آزمایشی</h2>
    <p className="muted">TRN-TECH-095 · تمام داده‌ها فقط test:، بدون محاسبه جمع یا تعهد مالی.
      ذخیره در پایگاه داده صرفاً آزمایشی است و اعتبار هویت یا انتخاب قانونی ندارد.</p>
    <div className="fields">
      <label>UUID پیش‌برآورد
        <input aria-label="UUID پیش‌برآورد" dir="ltr" disabled={editingBlocked} value={previewId}
          onChange={e=>{changed();setPreviewId(e.target.value);}}/>
      </label>
      <label>شناسه عضو ساختگی
        <input aria-label="عضو ساختگی" dir="ltr" disabled={editingBlocked} value={memberRef}
          onChange={e=>{changed();setMemberRef(e.target.value);}}/>
      </label>
      <label>نسخه پیش‌برآورد
        <input aria-label="نسخه برآورد" type="number" min="1" disabled={editingBlocked}
          value={revision} onChange={e=>{changed();setRevision(e.target.value);}}/>
      </label>
    </div>
    <div className="details">
      {names.map(([key,label],i)=>{const line=lines[i];return <fieldset key={key}>
        <legend>{label}</legend>
        <label>وضعیت منشأ <select aria-label={label+' وضعیت'} disabled={editingBlocked}
          value={line.state} onChange={e=>selectState(i,e.target.value as LineState)}>
          <option value="Missing">نامعلوم</option>
          <option value="ProvisionalClaim">ادعای غیرقطعی</option>
          <option value="ConflictingClaims">متعارض</option>
        </select></label>
        {line.state!=='Missing'?<p>مرجع: {line.sourceRef} · شاهد UUID: {line.evidenceRef}</p>:null}
        {line.state==='ProvisionalClaim'?<>
          <label>مبلغ ادعایی <input aria-label={label+' مبلغ'} disabled={editingBlocked}
            value={line.amount} onChange={e=>patchLine(i,{amount:e.target.value})}/></label>
          <label>ارز ادعایی <input aria-label={label+' ارز'} disabled={editingBlocked}
            value={line.currency} onChange={e=>patchLine(i,{currency:e.target.value})}/></label>
        </>:null}
        {line.state==='ConflictingClaims'?<p>برای تعارض، مبلغ و ارز ثبت نمی‌شود.</p>:null}
      </fieldset>;})}
    </div>
    <div className="actions">
      <button type="button" disabled={editingBlocked} onClick={()=>void register()}>ثبت دستی پیش‌برآورد در PostgreSQL</button>
      <button type="button" disabled={editingBlocked} onClick={()=>void read()}>بازیابی با UUID پس از Reload</button>
    </div>
    {current?<section data-testid="cost-journal-result" className="status">
      <p>نتیجه Journal: {current.outcome} · نسخه انتخاب: {current.choiceRevision}
        · شمارش رویداد: {current.historyCount}</p>
      <p>اثر انگشت همان نسخه: <code>{current.previewFingerprint}</code></p>
      <p>آخرین انتخاب ساختگی: {current.lastUnverifiedChoice??'ناموجود'}</p>
      <label>نوع انتخاب آزمایشی
        <select aria-label="نوع انتخاب" value={choice} onChange={e=>setChoice(e.target.value)}>
          <option value="AcknowledgedUncertainty">اطلاع از عدم‌قطعیت</option>
          <option value="Deferred">تعویق</option>
          <option value="RequestedClarification">درخواست توضیح</option>
        </select>
      </label>
      <button disabled={busy||pending!==null} onClick={choose}>ثبت انتخاب آزمایشی با Journal</button>
      {last?<button disabled={busy||pending!==null} onClick={()=>void send(last)}>
        بازپخش دقیق آخرین انتخاب
      </button>:null}
    </section>:null}
    {current&&!pending?<CostSupportHandoff source={current}/>:null}
    {pending?<button type="button" disabled={busy} onClick={()=>void send(pending)}>
      تکرار امن همان UUID نامطمئن
    </button>:null}
    <p role="status">{message}</p>
    <p className="notice">در فوریت پزشکی، کمک فوری را به ثبت هزینه یا انتخاب آزمایشی موکول نکنید.
      هیچ بدهی، کیف پول، بیمه یا رضایت درمانی در این محیط فعال نیست.</p>
  </section>;
}
