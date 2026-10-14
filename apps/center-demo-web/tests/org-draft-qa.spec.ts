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
