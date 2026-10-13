import {expect,test} from '@playwright/test';

test('COST-88-01 two genuine PG journals link manually, survive reload without implicit POST',async({page})=>{
  const writes:string[]=[];
  page.on('request',r=>{
    if(r.method()==='POST'&&r.url().includes('/internal/demo/v1/support/'))
      writes.push(r.url());
  });
  await page.goto('/member-demo/costs');
  const id=await page.getByLabel('UUID پیش‌برآورد').inputValue();
  expect(writes).toEqual([]);
  await page.getByRole('button',{name:'ثبت دستی پیش‌برآورد در PostgreSQL'}).click();
  const widget=page.getByTestId('cost-support-handoff');
  await expect(widget).toBeVisible();
  const caseRef='test:handoff-e2e-'+Date.now();
  await widget.getByLabel('پرونده اختلاف بیمه').fill(caseRef);
  expect(writes).toEqual([]);
  await widget.getByRole('button',{name:'ثبت دستی اعتراض آزمایشی در Journal پشتیبانی'}).click();
  await expect(widget.getByTestId('cost-support-message'))
    .toContainText('فقط درخواست ساختگی');
  expect(writes).toHaveLength(1);
  await widget.getByRole('button',{name:'راستی‌آزمایی مستقل رابطه دو Journal'}).click();
  await expect(widget.getByTestId('cost-support-linked-result'))
    .toContainText('UNVERIFIED_CROSS_JOURNAL_REFERENCE');
  await expect(widget.getByTestId('cost-support-linked-result'))
    .toContainText('نسخه اعتراض: 1');
  await page.reload();
  await page.getByLabel('UUID پیش‌برآورد').fill(id);
  await page.getByRole('button',{name:'بازیابی با UUID پس از Reload'}).click();
  await widget.getByLabel('پرونده اختلاف بیمه').fill(caseRef);
  await widget.getByRole('button',{name:'راستی‌آزمایی مستقل رابطه دو Journal'}).click();
  await expect(widget.getByTestId('cost-support-linked-result'))
    .toContainText('نسخه اعتراض: 1');
  expect(writes).toHaveLength(1);
  await expect(page.getByText(/در فوریت پزشکی، کمک فوری/)).toBeVisible();
});
test('COST-88-02 unrelated and real identifiers cannot imply linked insurance or authority',async({page})=>{
  await page.goto('/member-demo/costs');
  await page.getByRole('button',{name:'ثبت دستی پیش‌برآورد در PostgreSQL'}).click();
  const widget=page.getByTestId('cost-support-handoff');
  await widget.getByLabel('پرونده اختلاف بیمه').fill('real:patient-case');
  await expect(widget.getByRole('button',{name:'ثبت دستی اعتراض آزمایشی در Journal پشتیبانی'}))
    .toBeDisabled();
  await expect(widget.getByRole('button',{name:'راستی‌آزمایی مستقل رابطه دو Journal'}))
    .toBeDisabled();
  await widget.getByLabel('پرونده اختلاف بیمه').fill('test:not-created-'+Date.now());
  await widget.getByRole('button',{name:'راستی‌آزمایی مستقل رابطه دو Journal'}).click();
  await expect(widget.getByTestId('cost-support-message'))
    .toContainText('رابطه دو پرونده قابل تأیید نیست');
  await expect(widget.getByTestId('cost-support-linked-result')).toHaveCount(0);
});
test('COST-88-03 lost support POST ACK freezes the original UUID for exact replay',async({page})=>{
  await page.goto('/member-demo/costs');
  await page.getByRole('button',{name:'ثبت دستی پیش‌برآورد در PostgreSQL'}).click();
  const widget=page.getByTestId('cost-support-handoff');
  let intercepted=false;
  const ids:string[]=[];
  await page.route('**/internal/demo/v1/support/*/commands',async route=>{
    const payload=route.request().postDataJSON() as {requestId:string};
    ids.push(payload.requestId);
    if(!intercepted){
      intercepted=true;
      const real=await route.fetch();
      expect(real.status()).toBe(200);
      await route.abort();
    } else await route.continue();
  });
  await widget.getByRole('button',{name:'ثبت دستی اعتراض آزمایشی در Journal پشتیبانی'}).click();
  await expect(widget.getByTestId('cost-support-message')).toContainText('نتیجه ارسال نامعلوم');
  await expect(widget.getByRole('button',{name:'ثبت دستی اعتراض آزمایشی در Journal پشتیبانی'}))
    .toBeDisabled();
  await widget.getByRole('button',{name:'تکرار دقیق UUID درخواست نامطمئن'}).click();
  await expect(widget.getByTestId('cost-support-message'))
    .toContainText('بازپخش دقیق بدون رویداد دوم');
  expect(ids).toHaveLength(2);
  expect(ids[0]).toBe(ids[1]);
  await widget.getByRole('button',{name:'راستی‌آزمایی مستقل رابطه دو Journal'}).click();
  await expect(widget.getByTestId('cost-support-linked-result')).toContainText('نسخه اعتراض: 1');
});
