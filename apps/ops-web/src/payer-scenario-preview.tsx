import {useEffect,useRef,useState} from 'react';
type MarketId='MARKET-PUBLIC'|'MARKET-SUPPORT'|'MARKET-CORPORATE';
type PayerMode='MEMBER'|'SPONSOR';
type Market={id:MarketId;label:string;missingEvidence:string[]};
type Output={
 contract:'TRN-TECH-090';outcome:'HYPOTHETICAL_POLICY_ONLY';
 hypotheticalOnly:true;marketId:MarketId;payerMode:PayerMode;
 structurallyDescribedByBusinessPolicy:true;
 sponsorContractRequired:boolean;sponsorContractVerified:false;
 payerVerified:false;individualOwnsMembership:true;individualOwnsHealthcareWallet:true;
 sponsorClinicalAccessAuthorized:false;sponsorWalletControlAuthorized:false;
 splitPaymentTermsDefined:false;membershipActivated:false;paymentEnabled:false;
 onboardingEnabled:false;nationalReleaseAllowed:false;missingEvidence:string[];
};
type State={kind:'IDLE'|'CHECKING'|'UNVERIFIABLE'}|{kind:'HYPOTHETICAL';data:Output};
function valid(x:unknown,market:Market,payer:PayerMode):x is Output{
 if(!x||typeof x!=='object')return false;
 const v=x as Partial<Output>;
 return v.contract==='TRN-TECH-090'&&v.outcome==='HYPOTHETICAL_POLICY_ONLY'&&
  v.hypotheticalOnly===true&&v.marketId===market.id&&v.payerMode===payer&&
  v.structurallyDescribedByBusinessPolicy===true&&
  v.sponsorContractRequired===(payer==='SPONSOR')&&
  v.sponsorContractVerified===false&&v.payerVerified===false&&
  v.individualOwnsMembership===true&&v.individualOwnsHealthcareWallet===true&&
  v.sponsorClinicalAccessAuthorized===false&&v.sponsorWalletControlAuthorized===false&&
  v.splitPaymentTermsDefined===false&&v.membershipActivated===false&&
  v.paymentEnabled===false&&v.onboardingEnabled===false&&
  v.nationalReleaseAllowed===false&&Array.isArray(v.missingEvidence)&&
  v.missingEvidence.length===market.missingEvidence.length&&
  v.missingEvidence.every((s,i)=>s===market.missingEvidence[i]);
}
export function PayerScenarioPreview({markets,initial}:{markets:Market[];initial?:MarketId}){
 const [marketId,setMarketId]=useState<MarketId>(initial??'MARKET-PUBLIC');
 const [payer,setPayer]=useState<PayerMode>('MEMBER');
 const [state,setState]=useState<State>({kind:'IDLE'});
 const generation=useRef(0);
 const actual=markets.find(m=>m.id===marketId);
 const sponsorAvailable=marketId!=='MARKET-PUBLIC';
 useEffect(()=>setState({kind:'IDLE'}),[marketId,payer]);
 async function check(){
  const ticket=++generation.current;
  if(!actual||(!sponsorAvailable&&payer==='SPONSOR')){
   setState({kind:'UNVERIFIABLE'});return;
  }
  setState({kind:'CHECKING'});
  try{
   const response=await fetch('/internal/ops/v1/payer-scenario-preview',{
    method:'POST',credentials:'omit',cache:'no-store',
    headers:{'Content-Type':'application/json'},
    body:JSON.stringify({marketId:actual.id,payerMode:payer})
   });
   if(!response.ok)throw Error('NOT_AVAILABLE');
   const data:unknown=await response.json();
   if(!valid(data,actual,payer))throw Error('INVALID');
   if(ticket===generation.current)setState({kind:'HYPOTHETICAL',data});
  }catch{if(ticket===generation.current)setState({kind:'UNVERIFIABLE'});}
 }
 return <section className="panel payer-scenario" aria-labelledby="payer-title">
  <h2 id="payer-title">بررسی فرضی مسئولیت پرداخت حق اشتراک</h2>
  <p>فقط مقایسه سیاست مصوب است؛ این صفحه نه قرارداد و قیمت می‌پذیرد، نه عضو ثبت می‌کند، نه پول دریافت می‌کند.</p>
  <div className="payer-grid">
   <label>بازار مورد بررسی
    <select aria-label="بازار مورد بررسی" value={marketId} onChange={e=>{
     generation.current++;setMarketId(e.target.value as MarketId);setPayer('MEMBER');setState({kind:'IDLE'});
    }}>{markets.map(m=><option key={m.id} value={m.id}>{m.label}</option>)}</select>
   </label>
   <label>پرداخت‌کننده فرضی حق اشتراک
    <select aria-label="پرداخت‌کننده فرضی حق اشتراک" value={payer} onChange={e=>{
     generation.current++;setPayer(e.target.value as PayerMode);setState({kind:'IDLE'});
    }}>
     <option value="MEMBER">خود عضو</option>
     {sponsorAvailable&&<option value="SPONSOR">حامی صرفاً مشروط به قرارداد معتبر</option>}
    </select>
   </label>
  </div>
  <button type="button" disabled={state.kind==='CHECKING'} onClick={()=>void check()}>
   مقایسه فرضی مسئولیت‌ها
  </button>
  <p aria-live="polite" data-testid="payer-preview-status">
   {state.kind==='IDLE'&&'هنوز سناریویی بررسی نشده؛ هیچ عضویت یا پرداختی فعال نیست.'}
   {state.kind==='CHECKING'&&'در حال بررسی سیاست از منبع .NET؛ بدون پرداخت واقعی.'}
   {state.kind==='UNVERIFIABLE'&&'نتیجه قابل تأیید نیست؛ هیچ وضعیت یا مجوزی فعال نشده است.'}
   {state.kind==='HYPOTHETICAL'&&'فقط سناریوی فرضی مجاز در سیاست Business؛ هیچ قرارداد یا پرداختی احراز نشده است.'}
  </p>
  {state.kind==='HYPOTHETICAL'&&<div className="payer-result" data-testid="payer-preview-result">
   <h3>تفکیک مسئولیت‌ها در سناریوی انتخابی</h3>
   <p>پرداخت‌کننده فرضی: {payer==='MEMBER'?'خود عضو':'حامی صرفاً با قرارداد معتبر'}</p>
   <p>مالک عضویت و کیف پول درمانی: <strong>فقط خود فرد</strong></p>
   <p>حق دسترسی حامی به اطلاعات درمانی: <strong>ندارد</strong></p>
   <p>کنترل کیف پول توسط حامی: <strong>مجاز نیست</strong></p>
   <p>وضعیت قرارداد واقعی حامی: <strong>احراز نشده</strong></p>
   <p>عضویت، پرداخت، فعال‌سازی و انتشار ملی: <strong>غیرفعال / DENIED_BY_DEFAULT</strong></p>
   <p>شواهد تکمیل‌نشده بازار: {state.data.missingEvidence.join('، ')}</p>
   <p>تقسیم هزینه بین پرداخت‌کنندگان، نرخ و تخفیف همچنان تعیین نشده‌اند.</p>
  </div>}
 </section>;
}
