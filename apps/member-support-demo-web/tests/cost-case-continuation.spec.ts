import {expect,test} from '@playwright/test';

// TRN-TECH-179 — portable locator, never a session token or authorization.
async function newCase(page:import('@playwright/test').Page){
 await page.goto('/member-demo/costs');
 const id=await page.getByLabel('UUID پیش‌برآورد').inputValue();
 await page.getByRole('button',{name:'ثبت دستی پیش‌برآورد در PostgreSQL'}).click();
 const panel=page.getByTestId('cost-support-handoff');
 const caseRef='test:continuation-'+crypto.randomUUID().replaceAll('-','');
 await panel.getByLabel('پرونده اختلاف بیمه').fill(caseRef);
 await panel.getByRole('button',{name:'ثبت دستی اعتراض آزمایشی در Journal پشتیبانی'}).click();
 await expect(panel.getByTestId('cost-support-message')).toContainText('فقط درخواست ساختگی');
 await panel.getByRole('button',{name:'راستی‌آزمایی مستقل رابطه دو Journal'}).click();
 await expect(panel.getByTestId('cost-support-linked-result')).toContainText('نسخه اعتراض: 1');
 const ticket=await panel.getByLabel('رسید ادامه پرونده آزمایشی',{exact:true}).inputValue();
 return {id,caseRef,ticket,panel};
}
async function loadPreview(page:import('@playwright/test').Page,id:string){
 await page.reload();
 await page.getByLabel('UUID پیش‌برآورد').fill(id);
 await page.getByRole('button',{name:'بازیابی با UUID پس از Reload'}).click();
 await expect(page.getByTestId('cost-journal-result')).toBeVisible();
 return page.getByTestId('cost-support-handoff');
}
test('CONT170-01 manually resume two-journal existing case after reload without any POST',async({page,request})=>{
 const {id,caseRef,panel}=await newCase(page);
 const follow=panel.getByTestId('cost-followup-panel');
 await follow.getByLabel('مرجع یادداشت پیگیری').fill('test:manual-continuation-followup');
 await follow.getByRole('button',{name:'ثبت دستی پیگیری آزمایشی بیمه'}).click();
 await expect(panel.getByTestId('cost-support-message')).toContainText('یک پیگیری ساختگی');
 await panel.getByRole('button',{name:'راستی‌آزمایی مستقل رابطه دو Journal'}).click();
 await expect(panel.getByTestId('cost-support-linked-result')).toContainText('نسخه اعتراض: 2');
 const ticket=await panel.getByLabel('رسید ادامه پرونده آزمایشی',{exact:true}).inputValue();
 const parsed=JSON.parse(ticket);
 expect(parsed.previewId).toBe(id);
 expect(parsed.caseRef).toBe(caseRef);
 expect(parsed.version).toBe(1);
 const reopened=await loadPreview(page,id);
 let writes=0;
 page.on('request',req=>{
  if(req.method()==='POST')writes++;
 });
 await reopened.getByLabel('ورود رسید ادامه پرونده آزمایشی').fill(ticket);
 await reopened.getByRole('button',{name:'بازیابی مستقل پرونده از روی رسید'}).click();
 await expect(reopened.getByTestId('cost-support-linked-result')).toContainText('نسخه اعتراض: 2');
 await expect(reopened.getByTestId('cost-support-message')).toContainText('بدون ارسال فرمان جدید');
 expect(await reopened.getByLabel('پرونده اختلاف بیمه').inputValue()).toBe(caseRef);
 await reopened.getByRole('button',{name:'خواندن دستی صفحه اول اعتراض'}).click();
 await expect(reopened.getByTestId('cost-support-timeline-events')).toContainText(
  'test:manual-continuation-followup');
 expect(writes).toBe(0);
 const saved=await (await request.get('/internal/demo/v1/support/'+encodeURIComponent(caseRef))).json();
 expect(saved.revision).toBe(2);
 expect(saved.memberAuthenticated).toBe(false);
 expect(saved.insurerContacted).toBe(false);
 expect(saved.nationalReleaseAllowed).toBe(false);
});
test('CONT170-02 forged or unrelated locator is rejected without adopting a case',async({page})=>{
 const {id,ticket}=await newCase(page);
 const reopened=await loadPreview(page,id);
 const forged=JSON.parse(ticket);
 forged.caseRef='test:unrelated-'+crypto.randomUUID().replaceAll('-','');
 await reopened.getByLabel('ورود رسید ادامه پرونده آزمایشی').fill(JSON.stringify(forged));
 let post=0;
 page.on('request',req=>{if(req.method()==='POST')post++;});
 await reopened.getByRole('button',{name:'بازیابی مستقل پرونده از روی رسید'}).click();
 await expect(reopened.getByTestId('cost-support-message')).toContainText('تطبیق نداشت');
 await expect(reopened.getByTestId('cost-support-linked-result')).toHaveCount(0);
 await expect(reopened.getByTestId('cost-continuation-ticket')).toHaveCount(0);
 expect(await reopened.getByLabel('پرونده اختلاف بیمه').inputValue()).not.toBe(forged.caseRef);
 expect(post).toBe(0);
 const tampered=JSON.parse(ticket);
 tampered.memberRef='test:different-person-claim';
 await reopened.getByLabel('ورود رسید ادامه پرونده آزمایشی').fill(JSON.stringify(tampered));
 await reopened.getByRole('button',{name:'بازیابی مستقل پرونده از روی رسید'}).click();
 await expect(reopened.getByTestId('cost-support-message')).toContainText('تطبیق نداشت');
 expect(post).toBe(0);
});
test('CONT170-03 a changed source choice revision invalidates old locator before linking',async({page})=>{
 const {ticket}=await newCase(page);
 await page.getByRole('button',{name:'ثبت انتخاب آزمایشی با Journal'}).click();
 await expect(page.getByTestId('cost-journal-result')).toContainText('نسخه انتخاب: 1');
 const newPanel=page.getByTestId('cost-support-handoff');
 await newPanel.getByLabel('ورود رسید ادامه پرونده آزمایشی').fill(ticket);
 let gets=0,posts=0;
 page.on('request',req=>{
  if(req.method()==='POST')posts++;
  if(req.url().includes('/support-link/'))gets++;
 });
 await newPanel.getByRole('button',{name:'بازیابی مستقل پرونده از روی رسید'}).click();
 await expect(newPanel.getByTestId('cost-support-message')).toContainText('تطبیق نداشت');
 await expect(newPanel.getByTestId('cost-support-linked-result')).toHaveCount(0);
 expect(posts).toBe(0);expect(gets).toBe(0);
});
