import {expect,test} from '@playwright/test';

async function start(page:import('@playwright/test').Page){
  await page.goto('/member-demo/costs');
  const preview=await page.getByLabel('UUID پیش‌برآورد').inputValue();
  await page.getByRole('button',{name:'ثبت دستی پیش‌برآورد در PostgreSQL'}).click();
  const handoff=page.getByTestId('cost-support-handoff');
  const ref='test:followup-e2e-'+Date.now();
  await handoff.getByLabel('پرونده اختلاف بیمه').fill(ref);
  await handoff.getByRole('button',{name:'ثبت دستی اعتراض آزمایشی در Journal پشتیبانی'}).click();
  await expect(handoff.getByTestId('cost-support-message')).toContainText('فقط درخواست ساختگی');
  await handoff.getByRole('button',{name:'راستی‌آزمایی مستقل رابطه دو Journal'}).click();
  await expect(handoff.getByTestId('cost-support-linked-result')).toContainText('نسخه اعتراض: 1');
  return {preview,ref,handoff,followup:page.getByTestId('cost-followup-panel')};
}

test('COST-90-01 persistent explicit follow-up and read-after-reload',async({page})=>{
  const writes:string[]=[];
  page.on('request',request=>{
    if(request.method()==='POST'&&request.url().includes('/support/'))
      writes.push(request.url());
  });
  const {preview,ref,handoff,followup}=await start(page);
  await expect(followup).toBeVisible();
  expect(writes).toHaveLength(1);
  await followup.getByRole('button',{name:'ثبت دستی پیگیری آزمایشی بیمه'}).click();
  await expect(handoff.getByTestId('cost-support-message')).toContainText('یک پیگیری ساختگی');
  expect(writes).toHaveLength(2);
  await handoff.getByRole('button',{name:'راستی‌آزمایی مستقل رابطه دو Journal'}).click();
  await expect(handoff.getByTestId('cost-support-linked-result')).toContainText('نسخه اعتراض: 2');
  await page.reload();
  await page.getByLabel('UUID پیش‌برآورد').fill(preview);
  await page.getByRole('button',{name:'بازیابی با UUID پس از Reload'}).click();
  await page.getByTestId('cost-support-handoff').getByLabel('پرونده اختلاف بیمه').fill(ref);
  await page.getByRole('button',{name:'راستی‌آزمایی مستقل رابطه دو Journal'}).click();
  await expect(page.getByTestId('cost-support-linked-result')).toContainText('نسخه اعتراض: 2');
  expect(writes).toHaveLength(2);
});

test('COST-90-02 network ACK loss retries identical follow-up UUID',async({page})=>{
  const {handoff,followup}=await start(page);
  let dropped=false;
  const ids:string[]=[];
  await page.route('**/internal/demo/v1/support/*/commands',async route=>{
    const command=route.request().postDataJSON() as {requestId:string;action:string};
    if(command.action==='FOLLOW_UP_UNVERIFIED'){
      ids.push(command.requestId);
      if(!dropped){
        dropped=true;
        const response=await route.fetch();
        expect(response.status()).toBe(200);
        await route.abort();
        return;
      }
    }
    await route.continue();
  });
  await followup.getByRole('button',{name:'ثبت دستی پیگیری آزمایشی بیمه'}).click();
  await expect(followup.getByTestId('cost-followup-message')).toContainText('نتیجه ثبت پیگیری نامطمئن');
  await expect(handoff.getByLabel('پرونده اختلاف بیمه')).toBeDisabled();
  await followup.getByRole('button',{name:'تکرار دقیق UUID پیگیری نامطمئن'}).click();
  await expect(handoff.getByTestId('cost-support-message')).toContainText('بدون ایجاد رویداد دوم بازپخش شد');
  expect(ids).toHaveLength(2);
  expect(ids[0]).toEqual(ids[1]);
  await handoff.getByRole('button',{name:'راستی‌آزمایی مستقل رابطه دو Journal'}).click();
  await expect(handoff.getByTestId('cost-support-linked-result')).toContainText('نسخه اعتراض: 2');
});

test('COST-90-03 real source refs cannot impersonate insurer follow-up',async({page})=>{
  const {followup}=await start(page);
  await followup.getByLabel('مرجع یادداشت پیگیری').fill('real:insurer-reply');
  await expect(followup.getByRole('button',{name:'ثبت دستی پیگیری آزمایشی بیمه'})).toBeDisabled();
  await followup.getByLabel('مرجع یادداشت پیگیری').fill('test:followup-plain');
  await expect(followup.getByRole('button',{name:'ثبت دستی پیگیری آزمایشی بیمه'})).toBeEnabled();
});
