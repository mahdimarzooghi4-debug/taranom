'use strict';
// Real same-origin calls to loopback synthetic SQLite backend; never local
// mocked success. Only textContent; no HTML injection or saved credentials.
const root='/internal/qa/v1/ai';
const byId=(id)=>document.getElementById(id);
const output=byId('result');
const show=(target,value)=>{
  target.textContent=JSON.stringify(value,null,2);
};
async function read(){
  const tenant=byId('tenant').value;
  byId('inventory').textContent='در حال خواندن...';
  try{
    const response=await fetch(root+'/inventory?tenant='+encodeURIComponent(tenant),{
      credentials:'omit',cache:'no-store'});
    const result=await response.json();
    if(!response.ok||result.contract!=='TRN-TECH-142'||
       result.operatorAuthenticated!==false||
       result.adminAuthorized!==false||
       result.productionAllowed!==false||
       result.modelActivated!==false)throw Error('UNVERIFIED_AUTHORITY');
    show(byId('inventory'),result);
  }catch{
    byId('inventory').textContent='خواندن قابل تأیید نیست.';
  }
}
async function command(id,path){
  output.textContent='در حال ارسال درخواست آزمایشی...';
  try{
    const content=JSON.parse(byId(id).value);
    const response=await fetch(root+'/'+path,{
      method:'POST',headers:{'Content-Type':'application/json'},
      credentials:'omit',body:JSON.stringify(content),cache:'no-store'});
    const result=await response.json();
    if(result.contract!=='TRN-TECH-142'||
       result.operatorAuthenticated!==false||
       result.adminAuthorized!==false||
       result.productionAllowed!==false||
       result.trainingAllowed!==false||
       result.modelActivated!==false)throw Error('UNTRUSTED_AUTHORITY');
    show(output,result);
    if(response.ok)await read();
  }catch{
    output.textContent='فرمان یا نتیجه قابل تأیید نیست؛ هیچ تأییدی صادر نشده است.';
  }
}
byId('read').addEventListener('click',()=>void read());
byId('freeze').addEventListener('click',()=>void command('dataset','datasets'));
byId('register').addEventListener('click',()=>void command('model','models'));
byId('evaluate').addEventListener('click',()=>void command('evaluation','evaluations'));
