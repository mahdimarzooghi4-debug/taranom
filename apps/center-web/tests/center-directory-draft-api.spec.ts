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
