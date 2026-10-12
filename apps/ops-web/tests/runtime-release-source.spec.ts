import {expect,test} from '@playwright/test';

test('SOURCE-80-00 diagnostic real API shape vs browser transport',async({page,request})=>{
 const raw=await request.get('/internal/ops/v1/status');
 const body=await raw.json();
 const browser=await page.goto('/member').then(async()=>await page.evaluate(async()=>{
  try{
   const res=await fetch('/internal/ops/v1/status',{method:'GET',cache:'no-store',credentials:'omit'});
   const json=await res.json();
   return {code:res.status,keys:Object.keys(json),dimensionKeys:Object.keys(json.dimensions?.[0]||{})};
  }catch(error){return {error:String(error)};}
 }));
 console.log('SOURCE80_DEBUG',JSON.stringify({
   http:raw.status(),keys:Object.keys(body),dimensionKeys:Object.keys(body.dimensions?.[0]||{}),
   dimensions:body.dimensions?.length,firstId:body.dimensions?.[0]?.id,
   inventory:body.inventory,runtime:body.runtime,
   availableValues:body.dimensions?.map((d:any)=>({id:d.id,status:d.status,labelLength:d.label?.length,
    evidenceCount:d.missingEvidence?.length,evidenceUnique:new Set(d.missingEvidence||[]).size})),
   browser
 }));
 expect(raw.status()).toBe(200);
});

test('SOURCE-80-01 real Member and Center pages show complete .NET release blockers',async({page})=>{
 for(const path of ['/member','/member/support','/member/consents','/center','/center/doctors','/center/audit']){
  await page.goto(path);
  await expect(page.getByTestId('release-source-state')).toContainText('۱۷/۱۷ الزام ملی');
  await expect(page.getByTestId('release-source-state')).toContainText('DENIED_BY_DEFAULT');
  await page.getByRole('button',{name:'مشاهده جزئیات ۱۷ مانع'}).click();
  await expect(page.getByTestId('release-source-blockers').locator('li')).toHaveCount(17);
  await page.getByRole('button',{name:'بستن جزئیات موانع'}).click();
  await expect(page.getByTestId('release-source-blockers')).toHaveCount(0);
 }
});
test('SOURCE-80-02 unauthorized READY flags do not pass validation',async({page})=>{
 await page.route('**/internal/ops/v1/status',async route=>{
  const result=await route.fetch();const x=await result.json();
  x.releaseAllowed=true;x.dimensions[0].status='READY';
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(x)});
 });
 await page.goto('/member/credit');
 await expect(page.getByTestId('release-source-state')).toContainText('قابل تأیید نیست');
 await expect(page.getByTestId('release-source-blockers')).toHaveCount(0);
});
test('SOURCE-80-03 duplicate blocked ID fails Center source validation',async({page})=>{
 await page.route('**/internal/ops/v1/status',async route=>{
  const result=await route.fetch();const x=await result.json();x.dimensions[16]=x.dimensions[0];
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(x)});
 });
 await page.goto('/center/qualifications');
 await expect(page.getByTestId('release-source-state')).toContainText('قابل تأیید نیست');
 await expect(page.getByText('مدیر مرکز هنوز احراز نشده است')).toBeVisible();
});
test('SOURCE-80-04 retry after network failure clears previously rendered blockers',async({page})=>{
 await page.goto('/member/records');
 await expect(page.getByTestId('release-source-state')).toContainText('۱۷/۱۷ الزام ملی');
 await page.getByRole('button',{name:'مشاهده جزئیات ۱۷ مانع'}).click();
 await expect(page.getByTestId('release-source-blockers').locator('li')).toHaveCount(17);
 await page.route('**/internal/ops/v1/status',route=>route.abort());
 await page.getByRole('button',{name:'بازخوانی منبع ۱۷گانه'}).click();
 await expect(page.getByTestId('release-source-state')).toContainText('قابل تأیید نیست');
 await expect(page.getByTestId('release-source-blockers')).toHaveCount(0);
});
test('SOURCE-80-05 only credentialless GET and mutation remains denied',async({page,request})=>{
 const methods:string[]=[];
 page.on('request',r=>{if(r.url().includes('/internal/ops/v1/status')){
  methods.push(r.method());expect(r.headers()['authorization']).toBeUndefined();
  expect(r.headers()['cookie']).toBeUndefined();
 }});
 await page.goto('/member');await expect(page.getByTestId('release-source-state')).toContainText('BLOCKED_UNVERIFIED');
 await page.goto('/center');await expect(page.getByTestId('release-source-state')).toContainText('BLOCKED_UNVERIFIED');
 expect(methods).toEqual(['GET','GET']);
 expect((await request.post('/internal/ops/v1/status',{data:{releaseAllowed:true}})).status()).toBe(405);
});
