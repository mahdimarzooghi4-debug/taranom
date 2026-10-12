import {useEffect,useState} from 'react';
type Dimension={id:string;label:string;status:'BLOCKED_UNVERIFIED';missing_evidence:string[]};
type Payload={contract:'TRN-TECH-071';runtime:'LOOPBACK_PROCESS_ALIVE';inventory:'SOURCE_BLOCKED_UNVERIFIED';releaseAllowed:false;verifiedIntegrations:false;dimensions:Dimension[]};
type State={kind:'OFFLINE'|'LOADING'|'UNVERIFIABLE'}|{kind:'BLOCKED';data:Payload};
const expected=new Set([
 ...Array.from({length:13},(_,i)=>'TRN-EP-'+String(i+1).padStart(2,'0')),
 'MARKET-PUBLIC','MARKET-SUPPORT','MARKET-CORPORATE','NATIONAL-COVERAGE'
]);
const onLocalHost=()=>window.location.port==='4318'&&
 (window.location.hostname==='127.0.0.1'||window.location.hostname==='localhost');
function validated(v:unknown):v is Payload{
 if(!v||typeof v!=='object')return false;
 const x=v as Partial<Payload>;
 if(x.contract!=='TRN-TECH-071'||x.runtime!=='LOOPBACK_PROCESS_ALIVE'||
  x.inventory!=='SOURCE_BLOCKED_UNVERIFIED'||x.releaseAllowed!==false||
  x.verifiedIntegrations!==false||!Array.isArray(x.dimensions)||x.dimensions.length!==17)return false;
 const seen=new Set<string>();
 for(const d of x.dimensions){
  if(!d||typeof d!=='object'||!expected.has(d.id)||seen.has(d.id)||
   d.status!=='BLOCKED_UNVERIFIED'||typeof d.label!=='string'||
   d.label.trim().length===0||d.label.length>120||
   !Array.isArray(d.missing_evidence)||d.missing_evidence.length<1||
   d.missing_evidence.length>20||
   new Set(d.missing_evidence).size!==d.missing_evidence.length||
   !d.missing_evidence.every(s=>typeof s==='string'&&s.length<=100&&/^[A-Z0-9_]+$/.test(s)))return false;
  seen.add(d.id);
 }
 return seen.size===expected.size;
}
export function ReleaseSourcePanel({audience}:{audience:'MEMBER'|'CENTER'}){
 const [state,setState]=useState<State>({kind:'OFFLINE'});
 const [revision,setRevision]=useState(0);
 const [details,setDetails]=useState(false);
 useEffect(()=>{
  if(!onLocalHost()){setState({kind:'OFFLINE'});return;}
  const controller=new AbortController();
  setState({kind:'LOADING'});
  async function read(){
   try{
    const response=await fetch('/internal/ops/v1/status',{method:'GET',
     cache:'no-store',credentials:'omit',signal:controller.signal});
    if(!response.ok)throw Error('NO_SOURCE');
    const value:unknown=await response.json();
    if(!validated(value))throw Error('UNVERIFIED_SOURCE');
    if(!controller.signal.aborted)setState({kind:'BLOCKED',data:value});
   }catch{if(!controller.signal.aborted)setState({kind:'UNVERIFIABLE'});}
  }
  void read();return ()=>controller.abort();
 },[revision]);
 const data=state.kind==='BLOCKED'?state.data:null;
 return <section aria-label="وضعیت مستند انتشار ملی" role="region" data-testid="release-source-panel"
  style={{border:'1px solid currentColor',borderRadius:'0.5rem',marginBlock:'1rem',padding:'1rem'}}>
  <h2>کنترل وضعیت انتشار از منبع واقعی .NET</h2>
  <p>فضای {audience==='MEMBER'?'عضو':'مرکز'} فاقد احراز هویت و فعال‌سازی خدمات واقعی است.</p>
  <p role="status" aria-live="polite" data-testid="release-source-state">
   {state.kind==='OFFLINE'&&'منبع ملی متصل نیست؛ هیچ خدمت یا مجوزی تأیید نشده است.'}
   {state.kind==='LOADING'&&'بررسی منبع ۱۷گانه؛ هیچ مجوزی فعال نیست.'}
   {state.kind==='UNVERIFIABLE'&&'منبع ملی قابل تأیید نیست؛ هیچ خدمت یا مجوزی فعال نیست.'}
   {data&&'۱۷/۱۷ الزام ملی: BLOCKED_UNVERIFIED — DENIED_BY_DEFAULT'}
  </p>
  {onLocalHost()&&<button type="button" onClick={()=>{setDetails(false);setRevision(v=>v+1);}}>
   بازخوانی منبع ۱۷گانه</button>}
  {data&&<>
   <button type="button" aria-expanded={details} onClick={()=>setDetails(v=>!v)}>
    {details?'بستن جزئیات موانع':'مشاهده جزئیات ۱۷ مانع'}</button>
   {details&&<ul data-testid="release-source-blockers">{data.dimensions.map(d=>
    <li key={d.id}><strong>{d.id} — {d.label}</strong>: BLOCKED_UNVERIFIED — {d.missing_evidence.join('، ')}</li>)}</ul>}
  </>}
  <p>عضویت، داده سلامت، پزشک، اعتبار درمان، بیمه و پرداخت واقعی تأیید نشده‌اند.</p>
 </section>;
}
