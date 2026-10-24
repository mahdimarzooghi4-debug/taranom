import {expect,test} from '@playwright/test';
const endpoint='/internal/demo/v1/care-coordination/';
const status='care-status';
async function member(page:import('@playwright/test').Page){
 await page.goto('/member-demo/coordination');
 const ref=await page.getByLabel('شناسه آزمایشی پرونده').inputValue();
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toContainText('نسخه پرونده: 0');
 return ref;
}
test('CARE180-M1 real PostgreSQL accepted lost first ACK, export reload import then GET ONLY',async({page,request})=>{
 const ref=await member(page);
 let sent='',count=0;
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  if(route.request().method()!=='POST')return route.continue();
  count++;sent=route.request().postData()??'';
  const res=await route.fetch();expect(res.status()).toBe(200);
  await route.abort();
 });
 await page.getByRole('button',{name:'ثبت درخواست ادعایی هماهنگی'}).click();
 await expect(page.getByRole('alert')).toBeVisible();
 const receipt=await page.getByTestId('care-recovery-export').inputValue();
 expect(JSON.parse(receipt).surface).toBe('MEMBER');
 expect(JSON.parse(receipt).body).toBe(sent);
 await page.unrouteAll({behavior:'wait'});
 await page.reload();
 await expect(page.getByRole('alert')).toHaveCount(0);
 let nextPosts=0;page.on('request',r=>{if(r.method()==='POST'&&r.url().includes('/care-coordination/'))nextPosts++;});
 await page.getByLabel('متن رسید بازیابی هماهنگی عضو').fill(receipt);
 await page.getByRole('button',{name:'بارگذاری رسید هماهنگی عضو بدون ارسال'}).click();
 await expect(page.getByRole('alert')).toBeVisible();
 await expect(page.getByTestId(status)).toContainText('هیچ POST');
 await expect(page.getByLabel('شناسه آزمایشی پرونده')).toHaveValue(ref);
 expect(nextPosts).toBe(0);
 await page.getByRole('button',{name:'بررسی فقط‌خواندنی فرمان قبلی'}).click();
 await expect(page.getByRole('alert')).toHaveCount(0);
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toContainText('نسخه پرونده: 1');
 expect(count).toBe(1);expect(nextPosts).toBe(0);
 const data=await (await request.get(endpoint+encodeURIComponent(ref))).json();
 expect(data.events[0].requestId).toBe(JSON.parse(sent).requestId);
 expect(data.nationalReleaseAllowed).toBe(false);
});
test('CARE180-M2 forged success not committed: receipt survives reload; GET locks; manual exact replay once',async({page,request})=>{
 const ref=await member(page);
 let sent='';
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  sent=route.request().postData()??'';
  const c=JSON.parse(sent);
  const body=await (await request.get(endpoint+ref)).json();
  body.revision=1;body.outcome='RecordedUnverified';
  body.events=[{revision:1,requestId:c.requestId,action:'RequestNonclinicalCoordination',
   memberRef:c.memberRef,originalMemberRequestRef:c.originalMemberRequestRef,
   originalSubmissionRequestId:c.originalSubmissionRequestId,
   claimedActorRef:null,unverifiedNoteOrDocumentRef:null}];
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(body)});
 });
 await page.getByRole('button',{name:'ثبت درخواست ادعایی هماهنگی'}).click();
 await expect(page.getByRole('alert')).toBeVisible();
 const receipt=await page.getByTestId('care-recovery-export').inputValue();
 await page.unrouteAll({behavior:'wait'});await page.reload();
 await page.getByLabel('متن رسید بازیابی هماهنگی عضو').fill(receipt);
 await page.getByRole('button',{name:'بارگذاری رسید هماهنگی عضو بدون ارسال'}).click();
 await page.getByRole('button',{name:'بررسی فقط‌خواندنی فرمان قبلی'}).click();
 await expect(page.getByRole('alert')).toBeVisible();
 await page.getByRole('button',{name:'بازپخش دستی دقیق فرمان قبلی'}).click();
 await expect(page.getByRole('alert')).toHaveCount(0);
 const data=await (await request.get(endpoint+ref)).json();
 expect(data.events).toHaveLength(1);
 expect(data.events[0].requestId).toBe(JSON.parse(sent).requestId);
});
test('CARE180-M3 malformed or center receipts never import and send no POST',async({page})=>{
 await page.goto('/member-demo/coordination');
 const good={contract:'TRN-TECH-189',syntheticOnly:true,surface:'MEMBER',
 caseRef:'test:care180',body:JSON.stringify({requestId:crypto.randomUUID(),
 expectedRevision:0,action:'REQUEST_NONCLINICAL_COORDINATION',
 memberRef:'test:member',originalMemberRequestRef:'test:request',
 originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
 claimedActorRef:null,unverifiedNoteOrDocumentRef:null})};
 let writes=0;
 page.on('request',r=>{if(r.method()==='POST')writes++;});
 const bad=[
  {...good,caseRef:'real:patient'}, {...good,surface:'CENTER'},
  {...good,syntheticOnly:false}, {...good,extra:'injected'},
  {...good,body:good.body.replace('test:member','real:person')},
  {...good,body:good.body.replace('REQUEST_NONCLINICAL_COORDINATION','ATTACH_UNVERIFIED_CLINICAL_SOURCE')},
  {...good,body:good.body.slice(0,-1)+',"extra":true}'}
 ];
 for(const x of bad){
  await page.getByLabel('متن رسید بازیابی هماهنگی عضو').fill(JSON.stringify(x));
  await page.getByRole('button',{name:'بارگذاری رسید هماهنگی عضو بدون ارسال'}).click();
  await expect(page.getByTestId(status)).toContainText('نامعتبر');
  await expect(page.getByRole('alert')).toHaveCount(0);
 }
 await page.getByLabel('متن رسید بازیابی هماهنگی عضو').fill(JSON.stringify(good));
 await page.getByRole('button',{name:'بارگذاری رسید هماهنگی عضو بدون ارسال'}).click();
 await expect(page.getByRole('alert')).toBeVisible();
 expect(writes).toBe(0);
});
