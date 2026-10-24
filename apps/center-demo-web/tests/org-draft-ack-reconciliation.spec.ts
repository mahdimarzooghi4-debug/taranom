import {expect,test} from '@playwright/test';
const base='/internal/demo/v1/org-drafts';
const ref=()=> 'test:org-ack-'+crypto.randomUUID().replaceAll('-','');
const open=async(page:import('@playwright/test').Page,item:string)=>{
 await page.goto('/org-drafts-qa');
 const ui=page.getByTestId('org-draft-qa');
 await ui.getByRole('textbox',{name:'شناسه پیش‌نویس آزمایشی'}).fill(item);
 await ui.getByRole('button',{name:'ایجاد پیش‌نویس آزمایشی'}).click();
 await expect(ui.getByTestId('org-draft-source')).toContainText(item);
 return ui;
};
test('ORG168-01 committed but lost ACK resolved by exact journal GET with no additional POST',async({page,request})=>{
 const item=ref(),note='test:lost-ack-evidence';
 const ui=await open(page,item);
 await ui.getByRole('textbox',{name:'ارجاع یادداشت فنی آزمایشی'}).fill(note);
 let sent='',postCount=0;
 await page.route('**/internal/demo/v1/org-drafts/*/activity',async route=>{
  if(route.request().method()!=='POST')return route.continue();
  postCount++;sent=route.request().postData()??'';
  const accepted=await route.fetch();
  expect(accepted.status()).toBe(200);
  await route.abort();
 });
 await ui.getByRole('button',{name:'ثبت فعالیت نسخه‌دار'}).click();
 await expect(ui.getByTestId('org-draft-uncertain')).toContainText('نتیجه ثبت نامعلوم');
 await expect(ui.getByRole('button',{name:'ثبت فعالیت نسخه‌دار'})).toBeDisabled();
 await page.unrouteAll({behavior:'wait'});
 let nextPosts=0;
 page.on('request',r=>{if(r.method()==='POST'&&r.url().includes('/org-drafts/')&&r.url().endsWith('/activity'))nextPosts++;});
 await ui.getByRole('button',{name:'بررسی فقط‌خواندنی ثبت قبلی'}).click();
 await expect(ui.getByTestId('org-draft-uncertain')).toHaveCount(0);
 await expect(ui.getByRole('status').first()).toContainText('بدون ارسال POST بازیابی شد');
 await expect(ui.getByTestId('org-activity-history')).toContainText('نسخه جاری فعالیت: 1');
 await expect(ui.getByTestId('org-activity-history')).toContainText(note);
 expect(postCount).toBe(1);expect(nextPosts).toBe(0);
 const saved=await (await request.get(base+'/'+encodeURIComponent(item)+'/activity')).json();
 expect(saved.data.events).toHaveLength(1);
 expect(saved.data.events[0].requestRef).toBe(JSON.parse(sent).requestRef);
 expect(saved.data.events[0].noteRef).toBe(note);
 expect(saved.organizationAuthenticated).toBe(false);
 expect(saved.operatorAuthorized).toBe(false);
 expect(saved.nationalReleaseAllowed).toBe(false);
});
test('ORG168-02 forged 200 ACK without matching persisted event keeps same pending payload',async({page,request})=>{
 const item=ref(),note='test:forged-ack-note';
 const ui=await open(page,item);
 await ui.getByRole('textbox',{name:'ارجاع یادداشت فنی آزمایشی'}).fill(note);
 let sent='';
 await page.route('**/internal/demo/v1/org-drafts/*/activity',async route=>{
  if(route.request().method()!=='POST')return route.continue();
  sent=route.request().postData()??'';
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   contract:'TRN-TECH-135',source:'SUPPORT_SCOPE_EPHEMERAL_POSTGRES_ONLY',
   syntheticOnly:true,organizationAuthenticated:false,operatorAuthorized:false,
   businessStatusChanged:false,nationalReleaseAllowed:false,outcome:'Recorded',
   data:{itemRef:item,currentNo:1}
  })});
 });
 await ui.getByRole('button',{name:'ثبت فعالیت نسخه‌دار'}).click();
 await expect(ui.getByTestId('org-draft-uncertain')).toContainText('نتیجه ثبت نامعلوم');
 await expect(ui.getByRole('button',{name:'ثبت فعالیت نسخه‌دار'})).toBeDisabled();
 const empty=await (await request.get(base+'/'+encodeURIComponent(item)+'/activity')).json();
 expect(empty.data.currentNo).toBe(0);
 await page.unrouteAll({behavior:'wait'});
 await ui.getByRole('button',{name:'بازپخش دقیق درخواست قبلی'}).click();
 await expect(ui.getByTestId('org-draft-uncertain')).toHaveCount(0);
 await expect(ui.getByRole('status').first()).toContainText('رویداد دقیق');
 const saved=await (await request.get(base+'/'+encodeURIComponent(item)+'/activity')).json();
 expect(saved.data.events).toHaveLength(1);
 expect(saved.data.events[0].requestRef).toBe(JSON.parse(sent).requestRef);
 expect(saved.data.events[0].noteRef).toBe(note);
 expect(saved.nationalReleaseAllowed).toBe(false);
});
test('ORG168-03 tampered activity GET cannot clear uncertain command, genuine GET restores it',async({page,request})=>{
 const item=ref(),note='test:verified-note';
 const ui=await open(page,item);
 await ui.getByRole('textbox',{name:'ارجاع یادداشت فنی آزمایشی'}).fill(note);
 await page.route('**/internal/demo/v1/org-drafts/*/activity',async route=>{
  if(route.request().method()!=='POST')return route.continue();
  const accepted=await route.fetch();expect(accepted.status()).toBe(200);
  await route.abort();
 });
 await ui.getByRole('button',{name:'ثبت فعالیت نسخه‌دار'}).click();
 // The pending control appears before the simulated network failure is
 // fully handled. Wait for the original POST to finish and be frozen.
 await expect(ui.getByTestId('org-draft-uncertain')).toBeVisible();
 await expect(ui.getByRole('status').first()).toContainText('نتیجه ثبت نامعلوم');
 await page.unrouteAll({behavior:'wait'});
 const actual=await request.get(base+'/'+encodeURIComponent(item)+'/activity');
 expect(actual.status()).toBe(200);
 const forged=await actual.json();
 expect(forged.data.events[0].noteRef).toBe(note);
 forged.data.events[0].noteRef='test:forged-different-note';
 let forgedReads=0;
 // Fulfill with a pinned forged response instead of route.fetch(), which
 // could race the original ACK setup and accidentally serve a real GET.
 await page.route('**/internal/demo/v1/org-drafts/**',async route=>{
  if(route.request().method()!=='GET'||
     !new URL(route.request().url()).pathname.endsWith('/activity'))
     return route.continue();
  forgedReads++;
  await route.fulfill({status:200,contentType:'application/json',
    body:JSON.stringify(forged)});
 });
 await ui.getByRole('button',{name:'بررسی فقط‌خواندنی ثبت قبلی'}).click();
 await expect(ui.getByTestId('org-draft-uncertain')).toBeVisible();
 await expect(ui.getByRole('status').first()).toContainText('قفل و بازپخش دستی حفظ شد');
 expect(forgedReads).toBe(1);
 await expect(ui.getByRole('button',{name:'ثبت فعالیت نسخه‌دار'})).toBeDisabled();
 await expect(ui.getByTestId('org-activity-history')).toHaveCount(0);
 await page.unrouteAll({behavior:'wait'});
 let posts=0;
 page.on('request',r=>{if(r.method()==='POST'&&r.url().includes('/org-drafts/')&&r.url().endsWith('/activity'))posts++;});
 await ui.getByRole('button',{name:'بررسی فقط‌خواندنی ثبت قبلی'}).click();
 await expect(ui.getByTestId('org-draft-uncertain')).toHaveCount(0);
 await expect(ui.getByTestId('org-activity-history')).toContainText(note);
 expect(posts).toBe(0);
 const saved=await (await request.get(base+'/'+encodeURIComponent(item)+'/activity')).json();
 expect(saved.data.currentNo).toBe(1);
 expect(saved.nationalReleaseAllowed).toBe(false);
});
