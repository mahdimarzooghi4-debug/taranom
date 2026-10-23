import {test,expect} from '@playwright/test';
const id=(name:string)=>'test:193-'+name+'-'+crypto.randomUUID().replaceAll('-','');
async function member(page:import('@playwright/test').Page){
 const name=id('member');await page.goto('/member-demo/');
 await page.getByLabel('پرونده آزمایشی').fill(name);
 await page.getByRole('button',{name:'بازخوانی پرونده پشتیبانی آزمایشی'}).click();
 await expect(page.getByTestId('support-status')).toContainText('PostgreSQL خوانده شد');
 return name;
}
test('SUP193-M1 forged fully matching 200 without persisted event cannot unlock',async({page,request})=>{
 const name=await member(page);let posts=0;
 await page.route('**/internal/demo/v1/support/*/commands',async route=>{
  posts++;const x=route.request().postDataJSON();
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   contract:'TRN-TECH-075',syntheticOnly:true,memberAuthenticated:false,complaintLegallySubmitted:false,
   insurerContacted:false,clinicalReviewCompleted:false,nationalReleaseAllowed:false,caseRef:name,
   revision:1,outcome:'RecordedUnverified',topic:'MedicalServiceComplaint',memberRef:x.memberRef,
   evidenceRef:x.unverifiedEvidenceRef,claimedSubmitterRef:x.claimedSubmitterRef,
   originalRequestId:x.requestId,followUpCount:0,followUps:[]})});
 });
 await page.getByRole('button',{name:'ثبت درخواست اولیه آزمایشی'}).click();
 await expect(page.getByTestId('pending-support-write')).toBeVisible();
 await page.getByRole('button',{name:'بررسی فقط‌خواندنی همان UUID در PostgreSQL'}).click();
 await expect(page.getByTestId('pending-support-write')).toBeVisible();
 expect(posts).toBe(1);
 expect((await (await request.get('/internal/demo/v1/support/'+name)).json()).revision).toBe(0);
});
test('SUP193-M2 lost committed ACK recovered with exactly one manual GET no POST replay',async({page,request})=>{
 const name=await member(page);let posts=0,uuid='';
 await page.route('**/internal/demo/v1/support/*/commands',async route=>{
  posts++;uuid=route.request().postDataJSON().requestId;
  const real=await route.fetch();expect(real.status()).toBe(200);await route.abort();
 });
 await page.getByRole('button',{name:'ثبت درخواست اولیه آزمایشی'}).click();
 await expect(page.getByTestId('pending-support-write')).toBeVisible();
 await page.getByRole('button',{name:'بررسی فقط‌خواندنی همان UUID در PostgreSQL'}).click();
 await expect(page.getByTestId('pending-support-write')).toHaveCount(0);
 await expect(page.getByTestId('support-status')).toContainText('هیچ POST جدیدی');
 expect(posts).toBe(1);
 const src=await (await request.get('/internal/demo/v1/support/'+name)).json();
 expect(src.revision).toBe(1);expect(src.originalRequestId).toBe(uuid);
});
test('SUP193-M3 successful committed ACK with blocked GET keeps the original command',async({page})=>{
 await member(page);let posts=0;
 await page.route('**/internal/demo/v1/support/*/commands',async route=>{
  posts++;const reply=await route.fetch();expect(reply.status()).toBe(200);await route.fulfill({response:reply});
 });
 await page.route('**/internal/demo/v1/support/*',async route=>{
  if(route.request().method()==='GET')return route.fulfill({status:503,body:'{}'});
  await route.continue();
 });
 await page.getByRole('button',{name:'ثبت درخواست اولیه آزمایشی'}).click();
 await expect(page.getByTestId('pending-support-write')).toBeVisible();
 await page.getByRole('button',{name:'بررسی فقط‌خواندنی همان UUID در PostgreSQL'}).click();
 await expect(page.getByTestId('pending-support-write')).toBeVisible();
 expect(posts).toBe(1);
});
