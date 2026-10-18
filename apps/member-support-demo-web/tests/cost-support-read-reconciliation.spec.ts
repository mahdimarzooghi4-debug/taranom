import {expect,test} from '@playwright/test';

const caseRoot='/internal/demo/v1/support/';
async function start(page:import('@playwright/test').Page){
 await page.goto('/member-demo/costs');
 await page.getByRole('button',{name:'ثبت دستی پیش‌برآورد در PostgreSQL'}).click();
 const panel=page.getByTestId('cost-support-handoff');
 const caseRef='test:cost169-'+crypto.randomUUID().replaceAll('-','');
 await panel.getByLabel('پرونده اختلاف بیمه').fill(caseRef);
 await panel.getByRole('button',{name:'ثبت دستی اعتراض آزمایشی در Journal پشتیبانی'}).click();
 await expect(panel.getByTestId('cost-support-message')).toContainText('فقط درخواست ساختگی');
 await panel.getByRole('button',{name:'راستی‌آزمایی مستقل رابطه دو Journal'}).click();
 await expect(panel.getByTestId('cost-support-linked-result')).toContainText('نسخه اعتراض: 1');
 return {caseRef,panel,follow:page.getByTestId('cost-followup-panel')};
}

test('COST169-01 accepted follow-up with lost ACK recovers by GET without extra POST',async({page,request})=>{
 const {caseRef,panel,follow}=await start(page);
 const note='test:cost169-one';
 await follow.getByLabel('مرجع یادداشت پیگیری').fill(note);
 let sent='',count=0;
 await page.route('**/internal/demo/v1/support/*/commands',async route=>{
  if(route.request().method()!=='POST')return route.continue();
  const json=route.request().postDataJSON() as {action:string};
  if(json.action!=='FOLLOW_UP_UNVERIFIED')return route.continue();
  sent=route.request().postData()??'';count++;
  const result=await route.fetch();expect(result.status()).toBe(200);
  await route.abort();
 });
 await follow.getByRole('button',{name:'ثبت دستی پیگیری آزمایشی بیمه'}).click();
 await expect(follow.getByTestId('cost-followup-message')).toContainText('نتیجه ثبت پیگیری نامطمئن');
 await expect(panel.getByLabel('پرونده اختلاف بیمه')).toBeDisabled();
 await page.unrouteAll({behavior:'wait'});
 let retries=0;
 page.on('request',r=>{
  if(r.method()==='POST'&&r.url().includes('/support/')&&r.url().endsWith('/commands'))retries++;
 });
 await follow.getByRole('button',{name:'بررسی فقط‌خواندنی پیگیری ثبت‌شده'}).click();
 await expect(panel.getByTestId('cost-support-message')).toContainText('بدون POST مجدد تأیید شد');
 await expect(follow).toHaveCount(0);
 expect(count).toBe(1);expect(retries).toBe(0);
 const response=await (await request.get(caseRoot+encodeURIComponent(caseRef))).json();
 expect(response.revision).toBe(2);
 expect(response.followUps).toHaveLength(1);
 expect(response.followUps[0].requestId).toBe(JSON.parse(sent).requestId);
 expect(response.followUps[0].noteRef).toBe(note);
 expect(response.insurerContacted).toBe(false);
 expect(response.complaintLegallySubmitted).toBe(false);
 expect(response.nationalReleaseAllowed).toBe(false);
});

test('COST169-02 forged success without journal event retains UUID until original POST',async({page,request})=>{
 const {caseRef,panel,follow}=await start(page);
 const note='test:cost169-forged';
 await follow.getByLabel('مرجع یادداشت پیگیری').fill(note);
 let requestId='';
 await page.route('**/internal/demo/v1/support/*/commands',async route=>{
  if(route.request().method()!=='POST')return route.continue();
  const command=route.request().postDataJSON() as {action:string;requestId:string};
  if(command.action!=='FOLLOW_UP_UNVERIFIED')return route.continue();
  requestId=command.requestId;
  const existing=await (await request.get(caseRoot+encodeURIComponent(caseRef))).json();
  existing.outcome='RecordedUnverified';existing.revision=2;
  existing.followUpCount=1;
  existing.followUps=[{revision:2,requestId,noteRef:note,
    claimedActorRef:'test:cost-support-followup-operator'}];
  await route.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify(existing)});
 });
 await follow.getByRole('button',{name:'ثبت دستی پیگیری آزمایشی بیمه'}).click();
 await expect(follow.getByTestId('cost-followup-message')).toContainText('نتیجه ثبت پیگیری نامطمئن');
 await expect(panel.getByLabel('پرونده اختلاف بیمه')).toBeDisabled();
 const uncommitted=await (await request.get(caseRoot+encodeURIComponent(caseRef))).json();
 expect(uncommitted.revision).toBe(1);expect(uncommitted.followUps).toHaveLength(0);
 await page.unrouteAll({behavior:'wait'});
 await follow.getByRole('button',{name:'تکرار دقیق UUID پیگیری نامطمئن'}).click();
 await expect(panel.getByTestId('cost-support-message')).toContainText('یک پیگیری ساختگی');
 const saved=await (await request.get(caseRoot+encodeURIComponent(caseRef))).json();
 expect(saved.revision).toBe(2);
 expect(saved.followUps[0].requestId).toBe(requestId);
 expect(saved.followUps[0].noteRef).toBe(note);
 expect(saved.insurerContacted).toBe(false);
});

test('COST169-03 forged read of committed follow-up cannot clear pending latch',async({page,request})=>{
 const {caseRef,panel,follow}=await start(page);
 const note='test:cost169-read';
 await follow.getByLabel('مرجع یادداشت پیگیری').fill(note);
 await page.route('**/internal/demo/v1/support/*',async route=>{
  const req=route.request();
  if(req.method()!=='GET'||req.url().endsWith('/commands'))return route.continue();
  const upstream=await route.fetch();const x=await upstream.json();
  if(x.followUps?.length){
   x.followUps[0].requestId=crypto.randomUUID();
  }
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(x)});
 });
 await follow.getByRole('button',{name:'ثبت دستی پیگیری آزمایشی بیمه'}).click();
 await expect(follow.getByTestId('cost-followup-message')).toContainText('نتیجه ثبت پیگیری نامطمئن');
 await follow.getByRole('button',{name:'بررسی فقط‌خواندنی پیگیری ثبت‌شده'}).click();
 await expect(follow.getByTestId('cost-followup-message')).toContainText('قفل و بازپخش دستی حفظ شد');
 await expect(panel.getByLabel('پرونده اختلاف بیمه')).toBeDisabled();
 await page.unrouteAll({behavior:'wait'});
 let morePosts=0;
 page.on('request',r=>{
  if(r.method()==='POST'&&r.url().includes('/support/')&&r.url().endsWith('/commands'))morePosts++;
 });
 await follow.getByRole('button',{name:'بررسی فقط‌خواندنی پیگیری ثبت‌شده'}).click();
 await expect(panel.getByTestId('cost-support-message')).toContainText('بدون POST مجدد تأیید شد');
 expect(morePosts).toBe(0);
 const saved=await (await request.get(caseRoot+encodeURIComponent(caseRef))).json();
 expect(saved.revision).toBe(2);
 expect(saved.followUps[0].noteRef).toBe(note);
 expect(saved.nationalReleaseAllowed).toBe(false);
});
