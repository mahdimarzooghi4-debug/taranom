import {test,expect,type Page} from '@playwright/test';

const path='**/api/v1/me/center-manager-requests**';
const ref='center-'+'a'.repeat(32);
async function login(page:Page){
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'manager-applicant-test-token'}});
}
const claim=(id:string,state='PENDING_REVIEW')=>({
 requestId:id,centerRef:ref,applicantIssuer:'https://test.invalid/realms/taranom',
 applicantSubject:'applicant-synthetic',reason:'Request for center manager consideration',
 submittedAt:'2026-11-06T10:00:00Z',state,reviewReason:null,reviewedAt:null,
 managerAssigned:false
});
test('authenticated applicant submits own identity-bound unverified request',async({page})=>{
 await login(page);
 let created:string|null=null,posts=0;
 await page.route(path,route=>{
  const req=route.request(),url=new URL(req.url());
  expect(req.headers().authorization).toBe('Bearer manager-applicant-test-token');
  if(req.method()==='GET')return route.fulfill({json:{
   requests:created?[claim(created)]:[],nextAfter:null,associationGateRequired:true
  }});
  if(req.method()==='POST'){
   posts++;const body=req.postDataJSON();
   expect(body.centerRef).toBe(ref);
   expect(body.reason).toBe('I represent this clinical center for management review');
   expect(body.applicantIssuer).toBeUndefined();
   expect(body.applicantSubject).toBeUndefined();
   expect(body.managerAssigned).toBeUndefined();
   created=body.requestId;
   return route.fulfill({json:{outcome:'Applied',requestId:body.requestId,
    centerRef:ref,state:'PENDING_REVIEW',managerAssigned:false}});
  }
  return route.fulfill({status:404});
 });
 await page.goto('/center-portal/manager-request');
 await expect(page.getByRole('heading',{name:'درخواست مدیریت مرکز درمانی'})).toBeVisible();
 await page.getByLabel('شناسه مرجع مرکز تعریف‌شده توسط ادمین').fill(ref);
 await page.getByLabel('توضیح نسبت و اختیار ادعایی شما در مرکز')
  .fill('I represent this clinical center for management review');
 await page.getByRole('button',{name:'ارسال درخواست برای بررسی ادمین'}).click();
 await expect(page.getByText('درخواست ثبت شد و در انتظار بررسی ادمین است؛ هنوز مجوز مدیریت صادر نشده است.')).toBeVisible();
 await expect(page.getByText('در انتظار بررسی ادمین',{exact:true})).toBeVisible();
 expect(posts).toBe(1);
 expect(await page.evaluate(()=>localStorage.length+sessionStorage.length)).toBe(0);
});
test('missing authenticated host session never sends manager application API request',async({page})=>{
 let requests=0;page.on('request',r=>{if(r.url().includes('/api/v1/'))requests++});
 await page.goto('/center-portal/manager-request');
 await expect(page.getByRole('alert')).toContainText('نشست ورود معتبر');
 expect(requests).toBe(0);
});
test('uncertain POST only reconciles by exact original request ID',async({page})=>{
 await login(page);let id='',posts=0,reads=0;
 await page.route(path,route=>{
  const req=route.request(),url=new URL(req.url());
  if(req.method()==='POST'){posts++;id=req.postDataJSON().requestId;return route.abort('failed')}
  if(req.method()==='GET'&&url.pathname.endsWith('/'+id)&&id){
   reads++;return route.fulfill({json:{outcome:'Observed',requestId:id,centerRef:ref,
    state:'PENDING_REVIEW',managerAssigned:false,associationGateRequired:true}});
  }
  return route.fulfill({json:{requests:[],nextAfter:null,associationGateRequired:true}});
 });
 await page.goto('/center-portal/manager-request');
 await page.getByLabel('شناسه مرجع مرکز تعریف‌شده توسط ادمین').fill(ref);
 await page.getByLabel('توضیح نسبت و اختیار ادعایی شما در مرکز')
  .fill('We need review of delegated management authority');
 await page.getByRole('button',{name:'ارسال درخواست برای بررسی ادمین'}).click();
 await expect(page.getByRole('button',{name:'بررسی نتیجه درخواست قبلی'})).toBeVisible();
 await page.getByRole('button',{name:'بررسی نتیجه درخواست قبلی'}).click();
 await expect(page.getByRole('status').filter({hasText:'نتیجه درخواست قبلی بازیابی شد'})).toBeVisible();
 expect(posts).toBe(1);expect(reads).toBe(1);
});
test('bad center reference and mobile layout remain safe',async({page})=>{
 await page.setViewportSize({width:390,height:844});await login(page);
 let posts=0;
 await page.route(path,route=>{
  if(route.request().method()==='POST')posts++;
  return route.fulfill({json:{requests:[],nextAfter:null,associationGateRequired:true}});
 });
 await page.goto('/center-portal/manager-request');
 await page.getByLabel('شناسه مرجع مرکز تعریف‌شده توسط ادمین')
  .fill('center-does-not-exist');
 await page.getByLabel('توضیح نسبت و اختیار ادعایی شما در مرکز')
  .fill('A claim that should never be sent with malformed ID');
 await expect(page.getByRole('button',{name:'ارسال درخواست برای بررسی ادمین'})).toBeDisabled();
 expect(posts).toBe(0);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});
