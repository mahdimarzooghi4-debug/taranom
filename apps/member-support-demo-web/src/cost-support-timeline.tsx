import {useState} from 'react';

// TRN-TECH-100 — never trusts a stand-alone support timeline without
// replayed, exact cost+support journal identities and caller-pinned revisions.
type Source={previewId:string;previewRevision:number;previewFingerprint:string;
  choiceRevision:number};
type Link={supportRevision:number;costChoiceRevision:number;
  previewFingerprint:string;independentlyReattested:boolean;
  atomicCrossJournalSnapshot:boolean};
type Event={revision:number;action:string;noteRef:string|null;
  hypotheticalOnly:boolean;insurerVerified:boolean};
type Timeline={contract:string;syntheticOnly:boolean;outcome:string;
  previewId:string;caseRef:string;previewRevision:number;
  previewFingerprint:string;costChoiceRevision:number;supportRevision:number;
  expectedCostChoiceRevision:number;expectedSupportRevision:number;
  originalSubmissionRequestId:string;afterRevision:number;hasMore:boolean;
  nextAfterRevision:number|null;events:Event[];
  independentlyReattested:boolean;atomicCrossJournalSnapshot:boolean;
  memberAuthenticated:boolean;insurerContacted:boolean;coverageAdjudicated:boolean;
  complaintLegallySubmitted:boolean;officialChoiceRecorded:boolean;
  walletMutated:boolean;financialCommitmentCreated:boolean;
  clinicalConsentGranted:boolean;nationalReleaseAllowed:boolean};

const denied=(v:Timeline)=>v.memberAuthenticated===false&&v.insurerContacted===false&&
  v.coverageAdjudicated===false&&v.complaintLegallySubmitted===false&&
  v.officialChoiceRecorded===false&&v.walletMutated===false&&
  v.financialCommitmentCreated===false&&v.clinicalConsentGranted===false&&
  v.nationalReleaseAllowed===false;
function valid(raw:unknown,source:Source,link:Link,caseRef:string,after:number):raw is Timeline{
  if(!raw||typeof raw!=='object')return false;
  const v=raw as Timeline;
  if(v.contract!=='TRN-TECH-100'||v.syntheticOnly!==true||
    v.outcome!=='VERIFIED_UNVERIFIED_INSURANCE_TIMELINE'||
    v.previewId!==source.previewId||v.caseRef!==caseRef||
    v.previewRevision!==source.previewRevision||
    v.previewFingerprint!==source.previewFingerprint||
    v.previewFingerprint!==link.previewFingerprint||
    v.costChoiceRevision!==source.choiceRevision||
    v.costChoiceRevision!==link.costChoiceRevision||
    v.expectedCostChoiceRevision!==source.choiceRevision||
    v.supportRevision!==link.supportRevision||
    v.expectedSupportRevision!==link.supportRevision||
    v.afterRevision!==after||!Array.isArray(v.events)||v.events.length>3||
    v.independentlyReattested!==true||v.atomicCrossJournalSnapshot!==false||
    typeof v.hasMore!=='boolean'||!denied(v))return false;
  for(let i=0;i<v.events.length;i++){
    const e=v.events[i];
    if(e.revision!==after+i+1||
       e.action!==(e.revision===1?'SUBMIT_UNVERIFIED':'FOLLOW_UP_UNVERIFIED')||
       e.hypotheticalOnly!==true||e.insurerVerified!==false||
       (e.noteRef!==null&&
         (typeof e.noteRef!=='string'||!/^test:[A-Za-z0-9_:-]+$/.test(e.noteRef))))
      return false;
  }
  return v.hasMore
    ? v.events.length===3&&v.nextAfterRevision===after+3
    : v.nextAfterRevision===null;
}
export function CostSupportTimeline({source,caseRef,link}:{
  source:Source;caseRef:string;link:Link;
}){
  const [events,setEvents]=useState<Event[]>([]);
  const [cursor,setCursor]=useState(0);
  const [hasMore,setHasMore]=useState(false);
  const [busy,setBusy]=useState(false);
  const [message,setMessage]=useState('تاریخچه مربوط به دو Journal هنوز بازخوانی نشده است.');
  async function page(after:number){
    if(busy)return;
    setBusy(true);
    try{
      const url='/internal/demo/v1/cost-previews/'+
        encodeURIComponent(source.previewId)+'/support-link/'+encodeURIComponent(caseRef)+
        '/timeline?afterRevision='+after+'&limit=3&expectedCostChoiceRevision='+
        source.choiceRevision+'&expectedSupportRevision='+link.supportRevision;
      const response=await fetch(url,{credentials:'omit',cache:'no-store'});
      const raw:unknown=await response.json();
      if(!response.ok||!valid(raw,source,link,caseRef,after))throw Error('UNVERIFIED');
      setEvents(previous=>after===0?raw.events:[...previous,...raw.events]);
      setCursor(raw.hasMore?raw.nextAfterRevision!:after+raw.events.length);
      setHasMore(raw.hasMore);
      setMessage('این صفحه با همان نسخه دو Journal مستقل بازخوانی شد؛ فاقد تأیید بیمه است.');
    }catch{
      // Never retain a truncated or mixed-version historical timeline.
      setEvents([]);setCursor(0);setHasMore(false);
      setMessage('نسخه یا مرجع تغییر کرده است؛ تاریخچه قبلی معتبر فرض نمی‌شود.');
    }finally{setBusy(false);}
  }
  return <section data-testid="cost-support-timeline" aria-label="تاریخچه اعتراض با نسخه ثابت">
    <h4>تاریخچه اعتراض و پیگیری‌ها — فقط خواندنی</h4>
    <p>نسخه انتخاب هزینه: {source.choiceRevision} · نسخه پرونده اعتراض: {link.supportRevision}.
      مرور هر صفحه فقط با همین دو نسخه انجام می‌شود.</p>
    <button type="button" disabled={busy}
      onClick={()=>void page(0)}>خواندن دستی صفحه اول اعتراض</button>
    {hasMore?<button type="button" disabled={busy}
      onClick={()=>void page(cursor)}>صفحه بعد با همان دو نسخه</button>:null}
    <ol data-testid="cost-support-timeline-events">
      {events.map(e=><li key={e.revision}>نسخه {e.revision}: {e.action}
        {e.noteRef?' — '+e.noteRef:''} (تأییدنشده)</li>)}
    </ol>
    <p role="status" data-testid="cost-support-timeline-message">{message}</p>
  </section>;
}
