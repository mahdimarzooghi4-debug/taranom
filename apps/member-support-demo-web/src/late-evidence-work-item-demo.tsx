import {useState} from 'react';

type Action='SUBMIT_UNVERIFIED_EVIDENCE'|'RECORD_UNVERIFIED_HUMAN_NOTE';
type WorkCommand={
 requestId:string;expectedRevision:number;action:Action;
 memberRef:string;periodStartInclusive:string;periodEndExclusive:string;
 evidenceRef:string;submittedByClaimRef:string;originalSubmissionRequestId:string;
 reviewerClaimRef:string|null;reviewNoteEvidenceRef:string|null;
};
type Event={revision:number;action:string;requestId:string;
 memberRef:string;evidenceRef:string;submittedByClaimRef:string;
 originalSubmissionRequestId:string;reviewerClaimRef:string|null;
 reviewNoteEvidenceRef:string|null;periodStartInclusive:string;
 periodEndExclusive:string};
type Snapshot={contract:'TRN-TECH-114';syntheticOnly:true;untrustedClaimsOnly:true;
 caseRef:string;outcome:string;revision:number;events:Event[];
 realMemberAuthenticated:false;realReviewerAuthenticated:false;
 officialMembershipVerified:false;legalEvidenceVerified:false;
 evidenceSourceAuthenticated:false;entitlementApproved:false;
 benefitAmountCalculated:false;walletCredited:false;
 financialLedgerMutated:false;clinicalAccessGranted:false;nationalReleaseAllowed:false};
const z='00000000-0000-0000-0000-000000000000';
const newId=()=> 'test:late-'+crypto.randomUUID().replaceAll('-','');
function validated(v:unknown,id:string):v is Snapshot{
 if(!v||typeof v!=='object')return false;
 const x=v as Partial<Snapshot>;
 return x.contract==='TRN-TECH-114'&&x.syntheticOnly===true&&
 x.untrustedClaimsOnly===true&&x.caseRef===id&&
 ['READ','RecordedUnverified','Replayed'].includes(String(x.outcome))&&
 Number.isSafeInteger(x.revision)&&x.revision!>=0&&
 Array.isArray(x.events)&&x.events.length===x.revision&&
 x.events.every((e,i)=>e.revision===i+1&&
 (e.action==='SubmitUnverifiedEvidence'||e.action==='RecordUnverifiedHumanNote'))&&
 x.realMemberAuthenticated===false&&x.realReviewerAuthenticated===false&&
 x.officialMembershipVerified===false&&x.legalEvidenceVerified===false&&
 x.evidenceSourceAuthenticated===false&&x.entitlementApproved===false&&
 x.benefitAmountCalculated===false&&x.walletCredited===false&&
 x.financialLedgerMutated===false&&x.clinicalAccessGranted===false&&
 x.nationalReleaseAllowed===false;
}
export function LateEvidenceWorkItemDemo(){
 const [caseRef,setCaseRef]=useState(newId);
 const [memberRef,setMemberRef]=useState('test:member-claim');
 const [evidenceRef,setEvidenceRef]=useState('test:opaque-document-claim');
 const [submitter,setSubmitter]=useState('test:submitter-claim');
 const [reviewer,setReviewer]=useState('test:reviewer-claim');
 const [note,setNote]=useState('test:opaque-review-note');
 const [snapshot,setSnapshot]=useState<Snapshot|null>(null);
 const [unknown,setUnknown]=useState<{ref:string;original:string}|null>(null);
 const [busy,setBusy]=useState(false);
 const [message,setMessage]=useState('هیچ سند واقعی یا استحقاقی بررسی نشده است.');
 function change(set:(x:string)=>void,x:string){set(x);setSnapshot(null);setMessage('ورودی تغییر کرد؛ برای نسخه جاری خواندن دستی لازم است.');}
 async function load(){
  if(busy||unknown)return;
  setBusy(true);setSnapshot(null);
  try{
   const res=await fetch('/internal/demo/v1/late-evidence/'+encodeURIComponent(caseRef),
    {credentials:'omit',cache:'no-store'});
   if(!res.ok)throw Error('UNREADABLE');
   const value:unknown=await res.json();
   if(!validated(value,caseRef))throw Error('SOURCE_INVALID');
   setSnapshot(value);setMessage('نسخه ثبت‌شده و رویدادهای ممیزی از منبع PostgreSQL خوانده شدند.');
  }catch{setMessage('خواندن یا صحت منبع نامشخص است؛ هیچ داده قبلی معتبر نیست.');}
  finally{setBusy(false);}
 }
 async function send(pending:{ref:string;original:string}){
  if(busy)return;
  setBusy(true);setSnapshot(null);
  try{
   const resp=await fetch('/internal/demo/v1/late-evidence/'+encodeURIComponent(pending.ref)+'/commands',
    {method:'POST',headers:{'Content-Type':'application/json'},
      credentials:'omit',cache:'no-store',body:pending.original});
   if(!resp.ok)throw Error('FAILED_OR_UNKNOWN');
   const value:unknown=await resp.json();
   if(!validated(value,pending.ref)||!['RecordedUnverified','Replayed'].includes(value.outcome))
    throw Error('INVALID_RESPONSE');
   const command=JSON.parse(pending.original) as WorkCommand;
   if(value.revision<command.expectedRevision+1)throw Error('VERSION_INVALID');
   setSnapshot(value);setUnknown(null);
   setMessage(value.outcome==='Replayed'?
    'همان درخواست قبلی بدون ثبت رویداد تازه بازیابی شد.':
    'رویداد ساختگی با ثبت ممیزی پذیرفته شد؛ هیچ استحقاق مالی تأیید نشد.');
  }catch{
   setUnknown(pending);
   setMessage('نتیجه ثبت نامطمئن است. برای جلوگیری از تکرار، فقط همان فرمان را دستی بازپخش کنید.');
  }finally{setBusy(false);}
 }
 function submit(action:Action){
  if(!snapshot||unknown||busy)return;
  const root=snapshot.events[0];
  if(action==='RECORD_UNVERIFIED_HUMAN_NOTE'&&!root)return;
  const noteAction=action==='RECORD_UNVERIFIED_HUMAN_NOTE';
  const command:WorkCommand={
   requestId:crypto.randomUUID(),expectedRevision:snapshot.revision,action,
   memberRef:noteAction?root.memberRef:memberRef,
   periodStartInclusive:noteAction?root.periodStartInclusive:'2026-01-01T00:00:00Z',
   periodEndExclusive:noteAction?root.periodEndExclusive:'2026-02-01T00:00:00Z',
   evidenceRef:noteAction?root.evidenceRef:evidenceRef,
   submittedByClaimRef:noteAction?root.submittedByClaimRef:submitter,
   originalSubmissionRequestId:noteAction?root.requestId:z,
   reviewerClaimRef:noteAction?reviewer:null,
   reviewNoteEvidenceRef:noteAction?note:null
  };
  void send({ref:caseRef,original:JSON.stringify(command)});
 }
 return <main dir="rtl" className="late-workbench">
  <h1>ثبت آزمایشی مدرک دوره گذشته و یادداشت بررسی انسانی</h1>
  <p className="safety">صرفاً شناسه‌های تصادفیِ test: و ادعای اشخاص؛ نه مدرک معتبر،
   نه بررسی‌کننده احرازشده، نه تعیین استحقاق، نه مبلغ و نه تخصیص کیف پول.
   دوره نمایشی ثابت [۱ ژانویه، ۱ فوریه ۲۰۲۶) است و حقی از تاریخ استنتاج نمی‌شود.</p>
  <fieldset><legend>ثبت مدرک تأییدنشده</legend>
   <label>پرونده آزمایشی
    <input dir="ltr" value={caseRef} disabled={busy||!!unknown}
      onChange={e=>change(setCaseRef,e.target.value)}/></label>
   <label>شناسه عضوِ ادعایی
    <input dir="ltr" value={memberRef} disabled={busy||!!unknown}
     onChange={e=>change(setMemberRef,e.target.value)}/></label>
   <label>ارجاع مدرک تأییدنشده
    <input dir="ltr" value={evidenceRef} disabled={busy||!!unknown}
     onChange={e=>change(setEvidenceRef,e.target.value)}/></label>
   <label>شناسه مدعی ثبت
    <input dir="ltr" value={submitter} disabled={busy||!!unknown}
     onChange={e=>change(setSubmitter,e.target.value)}/></label>
   <button disabled={busy||!!unknown} onClick={()=>void load()}>
     خواندن پرونده از PostgreSQL
   </button>
   <button disabled={busy||!!unknown||!snapshot||snapshot.revision!==0}
    onClick={()=>submit('SUBMIT_UNVERIFIED_EVIDENCE')}>
     ثبت مدرک تأییدنشده
   </button>
  </fieldset>
  <fieldset><legend>یادداشت انسانیِ صرفاً ادعایی، بدون تأیید تصمیم</legend>
   <label>شناسه بررسی‌کننده ادعایی
    <input dir="ltr" value={reviewer} disabled={busy||!!unknown}
     onChange={e=>change(setReviewer,e.target.value)}/></label>
   <label>ارجاع یادداشت بدون محتوای شخصی
    <input dir="ltr" value={note} disabled={busy||!!unknown}
     onChange={e=>change(setNote,e.target.value)}/></label>
   <button disabled={busy||!!unknown||!snapshot||snapshot.revision===0}
    onClick={()=>submit('RECORD_UNVERIFIED_HUMAN_NOTE')}>
    ثبت یادداشت بررسی تأییدنشده
   </button>
  </fieldset>
  {unknown&&<section role="alert">
   <p>ACK ثبت نامعلوم است. فرمان اولیه در حافظه موقت صفحه قفل شده؛
     بدون درخواست تازه فقط همان UUID و متن اصلی را بازپخش کنید.</p>
   <button disabled={busy} onClick={()=>void send(unknown)}>
     بازپخش دستی دقیق فرمان اولیه
   </button>
  </section>}
  <p role="status" data-testid="late-status">{message}</p>
  {snapshot&&<section role="region" aria-label="تاریخچه رویدادهای آزمایشی"
    className="events">
   <p>نسخه پرونده: <strong>{snapshot.revision}</strong></p>
   <p>استحقاق تأییدشده، اعتبار درمانی و پرداخت: <strong>هیچ‌کدام</strong></p>
   <ol>{snapshot.events.map(e=><li key={e.revision}>
    نسخه {e.revision}: {e.action} — مدرک {e.evidenceRef} —
    ارجاع بررسی {e.reviewNoteEvidenceRef??'ثبت نشده'}
   </li>)}</ol>
  </section>}
 </main>;
}
