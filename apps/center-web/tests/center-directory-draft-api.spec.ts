import {test,expect,type Page} from '@playwright/test';

async function login(page:Page){
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'scoped-center-test-token'}});
}
test('authorized center manager saves and reloads an unverified center draft',async({page})=>{
 await login(page);
 let revision=0,displayName='',city='',updates=0;
 await page.route('**/api/v1/**',route=>{
  const req=route.request(),path=new URL(req.url()).pathname;
  expect(req.headers().authorization).toBe('Bearer scoped-center-test-token');
  if(path.endsWith('/center-directory-draft-scopes'))return route.fulfill({json:{scopes:[{capability:'CenterAccessGrantsManage',scopeRef:'center-abc'}]}});
  if(path.endsWith('/directory-draft')&&req.method()==='GET')return route.fulfill({json:{centerRef:'center-abc',profile:{revision,displayName,city},verificationState:'UNVERIFIED_DRAFT'}});
  if(path.endsWith('/directory-draft')&&req.method()==='PUT'){
   const input=req.postDataJSON();expect(input.expectedRevision).toBe(revision);
   expect(input.requestId).toMatch(/^[a-f0-9-]{36}$/);
   revision++;displayName=input.displayName;city=input.city;updates++;
   return route.fulfill({json:{outcome:'Applied',revision,verificationState:'UNVERIFIED_DRAFT'}});
  }
  return route.fulfill({status:404});
 });
 await page.goto('/center-portal/profile');
 await expect(page.getByText('مرجع مرکز:')).toBeVisible();
 await page.getByLabel('نام اعلامی مرکز').fill('مرکز آزمایشی');
 await page.getByLabel('شهر اعلامی').fill('شیراز');
 await page.getByRole('button',{name:'ثبت پیش‌نویس با کنترل نسخه'}).click();
 await expect.poll(()=>updates).toBe(1);
 await expect(page.getByLabel('نام اعلامی مرکز')).toHaveValue('مرکز آزمایشی');
 await expect(page.getByText('نسخه فعلی: ۱')).toBeVisible();
 await expect(page.getByText('اطلاعات اعلامی با رسید ثبت شد؛ هویت و صلاحیت همچنان تأیید نشده‌اند.')).toBeVisible();
 await expect(page.getByText('تأیید صلاحیت پزشکی')).toHaveCount(0);
 expect(await page.evaluate(()=>Object.keys(localStorage).length+Object.keys(sessionStorage).length)).toBe(0);
});

test('roster scope can create and edit only draft doctors',async({page})=>{
 await login(page);
 let doc:null|{doctorRef:string;revision:number;displayName:string;specialty:string;verificationState:string}=null;
 let writes=0;
 await page.route('**/api/v1/**',route=>{
  const req=route.request(),path=new URL(req.url()).pathname;
  if(path.endsWith('/center-directory-draft-scopes'))return route.fulfill({json:{scopes:[{capability:'CenterRosterManage',scopeRef:'center-roster'}]}});
  if(path.endsWith('/doctor-drafts')&&req.method()==='GET')return route.fulfill({json:{centerRef:'center-roster',doctors:doc?[doc]:[],nextAfter:null}});
  if(path.endsWith('/doctor-drafts/doctor-one')&&req.method()==='PUT'){
   const command=req.postDataJSON();expect(command.expectedRevision).toBe(doc?.revision??0);
   doc={doctorRef:'doctor-one',revision:(doc?.revision??0)+1,displayName:command.displayName,specialty:command.specialty,verificationState:'UNVERIFIED_DRAFT'};
   writes++;return route.fulfill({json:{outcome:'Applied',revision:doc.revision,verificationState:'UNVERIFIED_DRAFT'}});
  }
  return route.fulfill({status:404});
 });
 await page.goto('/center-portal/doctors');
 await page.getByLabel('شناسه مرجع پزشک در مرکز').fill('doctor-one');
 await page.getByLabel('نام اعلامی پزشک').fill('پزشک معرفی‌شده');
 await page.getByLabel('تخصص اعلامی').fill('تخصص اعلام‌شده');
 await page.getByRole('button',{name:'ثبت پیش‌نویس با کنترل نسخه'}).click();
 await expect.poll(()=>writes).toBe(1);
 await expect(page.getByRole('cell',{name:'doctor-one'})).toBeVisible();
 await expect(page.getByRole('cell',{name:'تأییدنشده'})).toBeVisible();
 await page.getByRole('button',{name:'ویرایش پیش‌نویس'}).click();
 await expect(page.getByLabel('شناسه مرجع پزشک در مرکز')).toBeDisabled();
 await page.getByLabel('تخصص اعلامی').fill('تخصص ویرایش‌شده');
 await page.getByRole('button',{name:'ثبت پیش‌نویس با کنترل نسخه'}).click();
 await expect.poll(()=>writes).toBe(2);
 await expect(page.getByRole('cell',{name:'تخصص ویرایش‌شده'})).toBeVisible();
});
test('missing current grant shows no draft content or writable form',async({page})=>{
 await login(page);
 let otherCalls=0;
 await page.route('**/api/v1/**',route=>{
  if(route.request().url().includes('center-directory-draft-scopes'))return route.fulfill({json:{scopes:[]}});
  otherCalls++;return route.fulfill({status:403});
 });
 await page.goto('/center-portal/doctors');
 await expect(page.getByText('مجوز فعال برای این بخش در دسترس نیست.')).toBeVisible();
 await expect(page.getByRole('button',{name:'ثبت پیش‌نویس با کنترل نسخه'})).toHaveCount(0);
 expect(otherCalls).toBe(0);
});

test('late doctor page from a previous center is ignored after switching scope',async({page})=>{
 await login(page);
 let releaseOld!:()=>void;
 const delayedOldPage=new Promise<void>(resolve=>{releaseOld=resolve});
 await page.route('**/api/v1/**',async route=>{
  const request=route.request(),url=new URL(request.url()),path=url.pathname;
  if(path.endsWith('/center-directory-draft-scopes'))return route.fulfill({json:{scopes:[
   {capability:'CenterRosterManage',scopeRef:'center-a'},
   {capability:'CenterRosterManage',scopeRef:'center-b'}]}});
  if(path.endsWith('/doctor-drafts')&&request.method()==='GET'){
   const center=path.split('/')[4];
   if(center==='center-a'&&url.searchParams.has('after')){
    await delayedOldPage;
    return route.fulfill({json:{centerRef:'center-a',doctors:[{
     doctorRef:'late-old-page',revision:1,displayName:'پزشک مرکز قبلی',
     specialty:'تخصص اعلامی',verificationState:'UNVERIFIED_DRAFT'}],nextAfter:null}});
   }
   if(center==='center-a')return route.fulfill({json:{centerRef:'center-a',doctors:[{
    doctorRef:'first-old-page',revision:1,displayName:'پزشک اول',
    specialty:'تخصص اعلامی',verificationState:'UNVERIFIED_DRAFT'}],nextAfter:'first-old-page'}});
   if(center==='center-b')return route.fulfill({json:{centerRef:'center-b',doctors:[{
    doctorRef:'current-center-doctor',revision:1,displayName:'پزشک مرکز جاری',
    specialty:'تخصص اعلامی',verificationState:'UNVERIFIED_DRAFT'}],nextAfter:null}});
  }
  return route.fulfill({status:404});
 });
 await page.goto('/center-portal/doctors');
 await page.getByLabel('مرکز دارای مجوز').selectOption('center-a');
 await expect(page.getByRole('cell',{name:'first-old-page'})).toBeVisible();
 await page.getByRole('button',{name:'صفحه بعد فهرست'}).click();
 await page.getByLabel('مرکز دارای مجوز').selectOption('center-b');
 await expect(page.getByRole('cell',{name:'current-center-doctor'})).toBeVisible();
 releaseOld();
 await expect(page.getByRole('cell',{name:'late-old-page'})).toHaveCount(0);
});

test('deterministic stale revision response blocks resubmission until re-read',async({page})=>{
 await login(page);
 let puts=0,reads=0;
 await page.route('**/api/v1/**',route=>{
  const request=route.request(),path=new URL(request.url()).pathname;
  if(path.endsWith('/center-directory-draft-scopes'))return route.fulfill({json:{scopes:[{capability:'CenterAccessGrantsManage',scopeRef:'center-stale'}]}});
  if(path.endsWith('/directory-draft')&&request.method()==='GET'){
   reads++;return route.fulfill({json:{centerRef:'center-stale',profile:{revision:reads>1?1:0,displayName:reads>1?'مرکز نسخه جدید':'',city:reads>1?'قم':''},verificationState:'UNVERIFIED_DRAFT'}});
  }
  if(path.endsWith('/directory-draft')&&request.method()==='PUT'){puts++;return route.fulfill({status:409,json:{code:'DIRECTORY_DRAFT_STALE_REVISION'}})}
  return route.fulfill({status:404});
 });
 await page.goto('/center-portal/profile');
 await page.getByLabel('نام اعلامی مرکز').fill('نام قدیمی');
 await page.getByLabel('شهر اعلامی').fill('تهران');
 await page.getByRole('button',{name:'ثبت پیش‌نویس با کنترل نسخه'}).click();
 await expect(page.getByRole('alert')).toContainText('اطلاعات برنامه تغییر کرده');
 await expect(page.getByRole('button',{name:'ثبت پیش‌نویس با کنترل نسخه'})).toHaveCount(0);
 await page.getByRole('button',{name:'بازخوانی از پایگاه داده'}).click();
 await expect(page.getByLabel('نام اعلامی مرکز')).toHaveValue('مرکز نسخه جدید');
 expect(puts).toBe(1);
});

test('uncertain save is not automatically repeated',async({page})=>{
 await login(page);
 let puts=0;
 await page.route('**/api/v1/**',route=>{
  const req=route.request(),path=new URL(req.url()).pathname;
  if(path.endsWith('/center-directory-draft-scopes'))return route.fulfill({json:{scopes:[{capability:'CenterAccessGrantsManage',scopeRef:'center-uncertain'}]}});
  if(path.endsWith('/directory-draft')&&req.method()==='GET')return route.fulfill({json:{centerRef:'center-uncertain',profile:{revision:0,displayName:'',city:''},verificationState:'UNVERIFIED_DRAFT'}});
  if(path.endsWith('/directory-draft')&&req.method()==='PUT'){puts++;return route.abort('failed')}
  return route.fulfill({status:404});
 });
 await page.goto('/center-portal/profile');
 await page.getByLabel('نام اعلامی مرکز').fill('مرکز تست');
 await page.getByLabel('شهر اعلامی').fill('یزد');
 await page.getByRole('button',{name:'ثبت پیش‌نویس با کنترل نسخه'}).click();
 await expect(page.getByRole('alert')).toContainText('نتیجه ذخیره نامطمئن');
 expect(puts).toBe(1);
 await expect(page.getByRole('button',{name:'ثبت پیش‌نویس با کنترل نسخه'})).toBeDisabled();
});
