import {useEffect,useState} from 'react';
import {AppointmentPage} from './figma-booking';
import {patientServices,type PatientServiceKey} from './patient-services';
import './patient-all-services.css';
const sections:{title:string;keys:PatientServiceKey[]}[]=[{title:'حساب و عضویت',keys:['membership','finance']},{title:'خدمات درمانی',keys:['guide','booking','records','referrals']}];
const accountKeys:PatientServiceKey[]=['account','profile','appointments','notifications','support'];
export function PatientAllServices(){
 const [expanded,setExpanded]=useState(false);
 useEffect(()=>{document.title='ترنم | همهٔ خدمات'},[]);
 return <AppointmentPage title="همهٔ خدمات" subtitle="خدمات و امکانات اپ ترنم" activeSection="home"><div className="patient-all-services">
 {sections.map(section=><section className="all-services-group" key={section.title}><h2>{section.title}</h2><div className="all-services-list">{section.keys.map(key=>{
  const item=patientServices[key];const copy=<><span className="all-service-copy"><strong>{item.title}</strong><small>{item.description}</small></span>{item.href?<span aria-hidden="true" className="all-service-arrow">‹</span>:<span className="all-service-pending">آماده‌سازی</span>}</>;
  return item.href?<a className="all-service-row" key={key} href={item.href} aria-label={item.title}>{copy}</a>:<button className="all-service-row" key={key} disabled aria-label={item.title} title="این خدمت هنوز آمادهٔ استفاده نیست.">{copy}</button>;
 })}</div></section>)}
 <p className="all-services-note">عضویت در ترنم به‌تنهایی به‌معنای بیمار بودن یا ایجاد سابقهٔ درمانی نیست.</p>
 <button className="all-services-toggle" aria-expanded={expanded} aria-controls="all-services-account" onClick={()=>setExpanded(x=>!x)}>خدمات حساب و پیگیری</button>
 {expanded&&<section id="all-services-account" className="all-services-account"><h2>خدمات حساب و پیگیری</h2>{accountKeys.map(key=><a key={key} href={patientServices[key].href!}>{patientServices[key].title}<span className="all-service-arrow" aria-hidden="true">‹</span></a>)}<p>نمایش خدمات در این فهرست، جایگزین بررسی دسترسی حساب نیست.</p></section>}
 </div></AppointmentPage>;
}
