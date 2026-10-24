import {expect,test} from '@playwright/test';
const ref=()=>('test:orgweb-'+crypto.randomUUID().replaceAll('-',''));
const base='/internal/demo/v1/org-drafts';

test('ORG126-01 real React -> localhost ASP.NET -> restricted PostgreSQL',async({page,request})=>{
 const item=ref(),source='test:manual-source',note='test:manual-note';
 await page.goto('/org-drafts-qa');
 const root=page.getByTestId('org-draft-qa');
 await expect(root).toContainText('DENIED_BY_DEFAULT');
 await root.getByRole('textbox',{name:'شناسه پیش‌نویس آزمایشی'}).fill(item);
 await root.getByRole('textbox',{name:'ارجاع منبع تأییدنشده'}).fill(source);
 await root.getByRole('button',{name:'ایجاد پیش‌نویس آزمایشی'}).click();
 await expect(root.getByTestId('org-draft-source')).toContainText(item);
 await root.getByRole('textbox',{name:'ارجاع یادداشت فنی آزمایشی'}).fill(note);
 await root.getByRole('button',{name:'ثبت فعالیت نسخه‌دار'}).click();
 await expect(root.getByRole('status').first()).toContainText('فعالیت فنی ثبت شد');
 await root.getByRole('button',{name:'بازخوانی تاریخچه فعالیت'}).click();
 await expect(root.getByTestId('org-activity-history')).toContainText('نسخه جاری فعالیت: 1');
 await expect(root.getByTestId('org-activity-history')).toContainText(note);
 await page.reload();
 await root.getByRole('textbox',{name:'شناسه پیش‌نویس آزمایشی'}).fill(item);
 await root.getByRole('button',{name:'خواندن پیش‌نویس از PostgreSQL'}).click();
 await expect(root.getByTestId('org-activity-history')).toContainText('نسخه جاری فعالیت: 1');
 const response=await request.get(base+'/'+encodeURIComponent(item)+'/activity');
 expect(response.status()).toBe(200);
 const h=await response.json();
 expect(h.organizationAuthenticated).toBe(false);
 expect(h.operatorAuthorized).toBe(false);
 expect(h.nationalReleaseAllowed).toBe(false);
 expect(h.data.currentNo).toBe(1);
});
test('ORG126-02 forged authentication response cannot become accepted draft',async({page})=>{
 await page.route('**/internal/demo/v1/org-drafts/*',route=>route.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   contract:'TRN-TECH-135',source:'SUPPORT_SCOPE_EPHEMERAL_POSTGRES_ONLY',
   syntheticOnly:true,organizationAuthenticated:true,operatorAuthorized:false,
   businessStatusChanged:false,nationalReleaseAllowed:false,outcome:'Read',
   data:{itemRef:'test:forged',sourceRef:'test:unverified'}
  })
 }));
 await page.goto('/org-drafts-qa');
 const root=page.getByTestId('org-draft-qa');
 await root.getByRole('textbox',{name:'شناسه پیش‌نویس آزمایشی'}).fill('test:forged');
 await root.getByRole('button',{name:'خواندن پیش‌نویس از PostgreSQL'}).click();
 await expect(root.getByRole('status').first()).toContainText('قابل تأیید نیست');
 await expect(root.getByTestId('org-draft-source')).toHaveCount(0);
});
test('ORG126-03 invalid refs and browser authority hints are rejected',async({page,request})=>{
 await page.goto('/org-drafts-qa');
 const root=page.getByTestId('org-draft-qa');
 await root.getByRole('textbox',{name:'شناسه پیش‌نویس آزمایشی'}).fill('person:real');
 await expect(root.getByRole('button',{name:'ایجاد پیش‌نویس آزمایشی'})).toBeDisabled();
 const extra=await request.get(base+'?afterItemRef=&limit=5&scopeRef=test:scope-corporate');
 expect(extra.status()).toBe(400);
 const fake=await request.get(base+'?afterItemRef=&limit=5',{headers:{Authorization:'Bearer fake'}});
 expect(fake.status()).toBe(403);
 const data=await fake.json();
 expect(data.organizationAuthenticated).toBe(false);
 expect(data.nationalReleaseAllowed).toBe(false);
});


test('ORG126-04 lost accepted acknowledgement recovers exact request once',async({page,request})=>{
 const item=ref();
 await page.goto('/org-drafts-qa');
 const root=page.getByTestId('org-draft-qa');
 await root.getByRole('textbox',{name:'شناسه پیش‌نویس آزمایشی'}).fill(item);
 await root.getByRole('button',{name:'ایجاد پیش‌نویس آزمایشی'}).click();
 await expect(root.getByTestId('org-draft-source')).toContainText(item);
 let dropped=false;
 await page.route('**/internal/demo/v1/org-drafts/*/activity',async route=>{
  if(route.request().method()==='POST'&&!dropped){
   dropped=true;
   const accepted=await route.fetch();
   expect(accepted.status()).toBe(200);
   await route.fulfill({status:503,contentType:'application/json',body:'{"contract":"UNTRUSTED_ACK"}'});
  }else await route.continue();
 });
 await root.getByRole('button',{name:'ثبت فعالیت نسخه‌دار'}).click();
 await expect(root.getByTestId('org-draft-uncertain')).toContainText('نتیجه ثبت نامعلوم است');
 await expect(root.getByRole('button',{name:'ثبت فعالیت نسخه‌دار'})).toBeDisabled();
 await root.getByRole('button',{name:'بازپخش دقیق درخواست قبلی'}).click();
 await expect(root.getByTestId('org-draft-uncertain')).toHaveCount(0);
 await expect(root.getByRole('status').first()).toContainText('بازپخش شد');
 await root.getByRole('button',{name:'بازخوانی تاریخچه فعالیت'}).click();
 await expect(root.getByTestId('org-activity-history')).toContainText('نسخه جاری فعالیت: 1');
 const response=await request.get(base+'/'+encodeURIComponent(item)+'/activity');
 expect(response.status()).toBe(200);
 const data=await response.json();
 expect(data.data.events).toHaveLength(1);
 expect(dropped).toBe(true);
});


test('ORG127-01 verified workqueue selects genuine PostgreSQL draft and activity',async({page,request})=>{
 const item=ref(),source='test:queue-source',note='test:queue-note';
 const created=await request.post(base,{data:{itemRef:item,sourceRef:source}});
 expect(created.status()).toBe(200);
 const first=await request.post(base+'/'+encodeURIComponent(item)+'/activity',{
  data:{requestRef:'test:queue-request',noteRef:note,expectedPreviousNo:0}});
 expect(first.status()).toBe(200);
 await page.goto('/org-drafts-qa');
 const q=page.getByTestId('org-draft-queue');
 await q.getByRole('textbox',{name:'شروع کارپوشه پس از شناسه'}).fill(item.slice(0,-1));
 await q.getByRole('button',{name:'خواندن کارپوشه PostgreSQL'}).click();
 await expect(q.getByRole('status')).toContainText('از یک نمای معتبر PostgreSQL');
 const row=q.getByTestId('org-queue-rows').locator('li').filter({hasText:item});
 await expect(row).toContainText('تعداد فعالیت: 1');
 await expect(row).toContainText(note);
 await row.getByRole('button',{name:'انتخاب و بازخوانی پیش‌نویس'}).click();
 const root=page.getByTestId('org-draft-qa');
 await expect(root.getByTestId('org-draft-source')).toContainText(source);
 await expect(root.getByTestId('org-activity-history')).toContainText('نسخه جاری فعالیت: 1');
 await root.getByRole('textbox',{name:'ارجاع یادداشت فنی آزمایشی'}).fill('test:second-queue-note');
 await root.getByRole('button',{name:'ثبت فعالیت نسخه‌دار'}).click();
 await expect(root.getByRole('status').first()).toContainText('فعالیت فنی ثبت شد');
 await q.getByRole('button',{name:'خواندن کارپوشه PostgreSQL'}).click();
 await expect(q.getByTestId('org-queue-rows').locator('li').filter({hasText:item}))
  .toContainText('تعداد فعالیت: 2');
});
test('ORG127-02 forged workqueue envelope and revision cannot display',async({page})=>{
 await page.route('**/internal/demo/v1/org-draft-queue?*',route=>route.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   contract:'TRN-TECH-135',source:'SUPPORT_SCOPE_EPHEMERAL_POSTGRES_ONLY',
   syntheticOnly:true,organizationAuthenticated:true,operatorAuthorized:false,
   businessStatusChanged:false,nationalReleaseAllowed:false,outcome:'Read',
   data:{afterItemRef:'',limit:5,nextAfterItemRef:null,items:[
    {itemRef:'test:forged',sourceRef:'test:untrusted',activityCount:99,
     lastNoteRef:'test:fake',lastRequestRef:'test:fake'}]}
  })
 }));
 await page.goto('/org-drafts-qa');
 const q=page.getByTestId('org-draft-queue');
 await q.getByRole('button',{name:'خواندن کارپوشه PostgreSQL'}).click();
 await expect(q.getByRole('status')).toContainText('معتبر نیست');
 await expect(q.getByTestId('org-queue-rows')).toHaveCount(0);
});


test('ORG128-01 pinned as-of timeline pages and rejects concurrent append',async({page,request})=>{
 const item=ref();
 const create=await request.post(base,{data:{itemRef:item,sourceRef:'test:timeline-source'}});
 expect(create.status()).toBe(200);
 for(let n=0;n<3;n++){
  const reply=await request.post(base+'/'+encodeURIComponent(item)+'/activity',{
   data:{requestRef:'test:req128-'+n,noteRef:'test:note128-'+n,expectedPreviousNo:n}});
  expect(reply.status()).toBe(200);
 }
 await page.goto('/org-drafts-qa');
 const root=page.getByTestId('org-draft-qa');
 await root.getByRole('textbox',{name:'شناسه پیش‌نویس آزمایشی'}).fill(item);
 await root.getByRole('button',{name:'خواندن پیش‌نویس از PostgreSQL'}).click();
 const timeline=root.getByTestId('org-draft-timeline');
 await expect(timeline).toContainText('نسخه ثبت‌شده هنگام آخرین خواندن: 3');
 const asOf=timeline.getByRole('textbox',{name:'نمایش فعالیت‌ها تا نسخه'});
 await asOf.fill('2');
 await timeline.getByRole('button',{name:'مشاهده نسخه تاریخی'}).click();
 await expect(timeline.getByTestId('org-timeline-events')).toContainText('test:note128-0');
 await expect(timeline.getByTestId('org-timeline-events')).toContainText('test:note128-1');
 await expect(timeline.getByTestId('org-timeline-events')).not.toContainText('test:note128-2');
 await expect(timeline.getByRole('button',{name:'صفحه بعد تاریخچه'})).toHaveCount(0);
 await asOf.fill('3');
 await timeline.getByRole('button',{name:'مشاهده نسخه تاریخی'}).click();
 await expect(timeline.getByRole('button',{name:'صفحه بعد تاریخچه'})).toBeVisible();
 await timeline.getByRole('button',{name:'صفحه بعد تاریخچه'}).click();
 await expect(timeline.getByTestId('org-timeline-events')).toContainText('test:note128-2');
 const concurrent=await request.post(base+'/'+encodeURIComponent(item)+'/activity',{
  data:{requestRef:'test:req128-next',noteRef:'test:note128-next',expectedPreviousNo:3}});
 expect(concurrent.status()).toBe(200);
 await timeline.getByRole('button',{name:'مشاهده نسخه تاریخی'}).click();
 await expect(timeline.getByRole('status')).toContainText('نسخه فعلی تغییر کرده است');
 await expect(timeline.getByTestId('org-timeline-events')).toHaveCount(0);
});
test('ORG128-02 forged timeline response and invalid cutoff fail closed',async({page,request})=>{
 const item=ref();
 const saved=await request.post(base,{data:{itemRef:item,sourceRef:'test:timeline'}});
 expect(saved.status()).toBe(200);
 await page.goto('/org-drafts-qa');
 const root=page.getByTestId('org-draft-qa');
 await root.getByRole('textbox',{name:'شناسه پیش‌نویس آزمایشی'}).fill(item);
 await root.getByRole('button',{name:'خواندن پیش‌نویس از PostgreSQL'}).click();
 const t=root.getByTestId('org-draft-timeline');
 const input=t.getByRole('textbox',{name:'نمایش فعالیت‌ها تا نسخه'});
 await input.fill('01');
 await expect(t.getByRole('button',{name:'مشاهده نسخه تاریخی'})).toBeDisabled();
 await input.fill('0');
 await page.route('**/internal/demo/v1/org-drafts/*/activity-page?*',route=>
  route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   contract:'TRN-TECH-135',source:'SUPPORT_SCOPE_EPHEMERAL_POSTGRES_ONLY',
   syntheticOnly:true,organizationAuthenticated:true,operatorAuthorized:false,
   businessStatusChanged:false,nationalReleaseAllowed:false,outcome:'Read',
   data:{itemRef:item,currentNo:0,expectedCurrentNo:0,asOfNo:0,afterNo:0,
    limit:2,hasMore:false,nextAfterNo:null,events:[]}
  })}));
 await t.getByRole('button',{name:'مشاهده نسخه تاریخی'}).click();
 await expect(t.getByRole('status')).toContainText('قابل تأیید نیست');
 await expect(t.getByTestId('org-timeline-events')).toHaveCount(0);
});
