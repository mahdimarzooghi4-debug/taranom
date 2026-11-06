import {test,expect,type Page} from '@playwright/test';
const ref='center-'+'b'.repeat(32);
const id='11111111-1111-4111-8111-111111111111';
const path='**/api/v1/operations/center-manager-requests**';
const request=(state='PENDING_REVIEW',reviewReason:string|null=null)=>({
 requestId:id,centerRef:ref,applicantIssuer:'https://test.invalid/realms/taranom',
 applicantSubject:'claimant-oidc-subject',reason:'I claim authorization to manage this center',
 submittedAt:'2026-11-06T11:00:00Z',state,
 reviewReason,reviewedAt:reviewReason?'2026-11-06T12:00:00Z':null,managerAssigned:false
});
async function adminSession(page:Page){
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'reviewing-admin-test-token'}});
}
test('Admin reviews manager request but grants no authority',async({page})=>{
 await adminSession(page);let state='PENDING_REVIEW',reason:string|null=null,posts=0;
 await page.route(path,route=>{
  const req=route.request();
  expect(req.headers().authorization).toBe('Bearer reviewing-admin-test-token');
  if(req.method()==='GET')return route.fulfill({json:{
   requests:[request(state,reason)],nextAfter:null,associationGateRequired:true
  }});
  if(req.method()==='POST'){
   posts++;const command=req.postDataJSON();
   expect(command.commandId).toMatch(/^[a-f0-9-]{36}$/);
   expect(command.decision).toBe('REVIEWED_PENDING_VERIFICATION');
   expect(command.reason).toBe('Waiting for independent proof of identity and authority');
   expect(command.managerAssigned).toBeUndefined();
   state=command.decision;reason=command.reason;
   return route.fulfill({json:{outcome:'Applied',requestId:id,state,managerAssigned:false}});
  }
  return route.fulfill({status:404});
 });
 await page.goto('/admin/center-manager-requests');
 await expect(page.getByText('I claim authorization to manage this center')).toBeVisible();
 await page.getByRole('button',{name:'بررسی درخواست'}).click();
 await page.getByLabel('علت ثبت‌شده توسط ادمین')
  .fill('Waiting for independent proof of identity and authority');
 await page.getByRole('button',{name:'ثبت بررسی انسانی بدون صدور دسترسی'}).click();
 await expect(page.getByRole('status').filter({hasText:'بررسی انسانی ثبت شد'})).toBeVisible();
 await expect(page.getByText('بررسی‌شده؛ نیازمند احراز اختیار')).toBeVisible();
 await expect(page.getByRole('button',{name:'بررسی درخواست'})).toHaveCount(0);
 expect(posts).toBe(1);
 expect(await page.evaluate(()=>localStorage.length+sessionStorage.length)).toBe(0);
});
test('Admin may decline manager claim with accountable reason',async({page})=>{
 await adminSession(page);let decision='';
 await page.route(path,route=>{
  if(route.request().method()==='POST'){
   decision=route.request().postDataJSON().decision;
   return route.fulfill({json:{outcome:'Applied',requestId:id,
    state:'DECLINED',managerAssigned:false}});
  }
  return route.fulfill({json:{requests:[request()],nextAfter:null,associationGateRequired:true}});
 });
 await page.goto('/admin/center-manager-requests');
 await page.getByRole('button',{name:'بررسی درخواست'}).click();
 await page.getByLabel('نتیجه بررسی').selectOption('DECLINED');
 await page.getByLabel('علت ثبت‌شده توسط ادمین').fill('Insufficient evidence of the claimed role');
 await page.getByRole('button',{name:'ثبت بررسی انسانی بدون صدور دسترسی'}).click();
 expect(decision).toBe('DECLINED');
});
test('denied Admin cannot review manager requests',async({page})=>{
 await adminSession(page);let posts=0;
 await page.route(path,route=>{
  if(route.request().method()==='POST')posts++;
  return route.fulfill({status:403,json:{code:'FORBIDDEN'}});
 });
 await page.goto('/admin/center-manager-requests');
 await expect(page.getByRole('alert')).toContainText('مجوز بررسی درخواست');
 await expect(page.getByRole('button',{name:'بررسی درخواست'})).toHaveCount(0);
 expect(posts).toBe(0);
});
test('no session causes no privileged review read',async({page})=>{
 let requests=0;page.on('request',r=>{if(r.url().includes('/api/v1/'))requests++});
 await page.goto('/admin/center-manager-requests');
 await expect(page.getByRole('alert')).toContainText('نشست ورود معتبر');
 expect(requests).toBe(0);
});
test('forged manager assigned status is rejected and mobile page does not overflow',async({page})=>{
 await page.setViewportSize({width:390,height:844});await adminSession(page);
 await page.route(path,route=>route.fulfill({json:{
  requests:[{...request(),managerAssigned:true}],nextAfter:null,associationGateRequired:true
 }}));
 await page.goto('/admin/center-manager-requests');
 await expect(page.getByRole('alert')).toContainText('پاسخ صف بررسی');
 await expect(page.getByRole('button',{name:'بررسی درخواست'})).toHaveCount(0);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});

test('lost review response recovers exact reviewer command, not a matching list row',async({page})=>{
 await adminSession(page);let posts=0,requestCommand='',reads=0;
 await page.route(path,route=>{
  const req=route.request(),url=new URL(req.url());
  if(req.method()==='POST'){
   posts++;requestCommand=req.postDataJSON().commandId;
   return route.abort('failed');
  }
  if(url.pathname.includes('/review-receipts/')){
   reads++;
   expect(url.pathname).toContain('/'+id+'/review-receipts/'+requestCommand);
   return route.fulfill({json:{outcome:'Observed',requestId:id,commandId:requestCommand,
    decision:'REVIEWED_PENDING_VERIFICATION',reason:'Submitted review with no delivery acknowledgement',
    managerAssigned:false}});
  }
  return route.fulfill({json:{requests:[request()],nextAfter:null,
   associationGateRequired:true}});
 });
 await page.goto('/admin/center-manager-requests');
 await page.getByRole('button',{name:'بررسی درخواست'}).click();
 await page.getByLabel('علت ثبت‌شده توسط ادمین')
  .fill('Submitted review with no delivery acknowledgement');
 await page.getByRole('button',{name:'ثبت بررسی انسانی بدون صدور دسترسی'}).click();
 await expect(page.getByRole('button',{name:'بررسی نتیجه قبلی بدون ارسال دوباره'})).toBeVisible();
 await page.getByRole('button',{name:'بررسی نتیجه قبلی بدون ارسال دوباره'}).click();
 await expect(page.getByRole('status').filter({hasText:'رسید همان بررسی'})).toBeVisible();
 expect(posts).toBe(1);expect(reads).toBe(1);
 expect(await page.evaluate(()=>localStorage.length+sessionStorage.length)).toBe(0);
});
