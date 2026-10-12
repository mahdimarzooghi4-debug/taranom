import {expect,test} from '@playwright/test';

test('COST-86-01 PostgreSQL preview and choice survive real Chromium reload, no automatic writes',async({page})=>{
  const posts:string[]=[];
  page.on('request',r=>{
    if(r.url().includes('/internal/demo/v1/cost-previews')&&r.method()==='POST')posts.push(r.method());
  });
  await page.goto('/member-demo/costs');
  await expect(page.getByRole('heading',{name:/گردش‌کار واقعی PostgreSQL/})).toBeVisible();
  const id=await page.getByLabel('UUID پیش‌برآورد').inputValue();
  expect(posts).toEqual([]);
  await page.getByRole('button',{name:'ثبت دستی پیش‌برآورد در PostgreSQL'}).click();
  const current=page.getByTestId('cost-journal-result');
  await expect(current).toContainText('Registered');
  await expect(current).toContainText('نسخه انتخاب: 0');
  await page.getByRole('button',{name:'ثبت انتخاب آزمایشی با Journal'}).click();
  await expect(current).toContainText('RecordedUnverified');
  await expect(current).toContainText('نسخه انتخاب: 1');
  await page.getByRole('button',{name:'بازپخش دقیق آخرین انتخاب'}).click();
  await expect(current).toContainText('Replayed');
  await expect(current).toContainText('نسخه انتخاب: 1');
  await page.reload();
  await page.getByLabel('UUID پیش‌برآورد').fill(id);
  await page.getByRole('button',{name:'بازیابی با UUID پس از Reload'}).click();
  await expect(current).toContainText('READ');
  await expect(current).toContainText('نسخه انتخاب: 1');
  await expect(current).toContainText('شمارش رویداد: 1');
  await expect(page.getByText(/در فوریت پزشکی، کمک فوری/)).toBeVisible();
  expect(posts).toEqual(['POST','POST','POST']);
});
test('COST-86-02 immutable PostgreSQL preview rejects changed version and fake member',async({page})=>{
  await page.goto('/member-demo/costs');
  await page.getByRole('button',{name:'ثبت دستی پیش‌برآورد در PostgreSQL'}).click();
  await expect(page.getByTestId('cost-journal-result')).toContainText('Registered');
  await page.getByLabel('نسخه برآورد').fill('2');
  await page.getByRole('button',{name:'ثبت دستی پیش‌برآورد در PostgreSQL'}).click();
  await expect(page.getByTestId('cost-journal-result')).toHaveCount(0);
  await expect(page.getByRole('status')).toContainText('ثبت قابل تأیید نیست');
  await page.getByLabel('عضو ساختگی').fill('real:member');
  await page.getByRole('button',{name:'ثبت دستی پیش‌برآورد در PostgreSQL'}).click();
  await expect(page.getByRole('status')).toContainText('ثبت قابل تأیید نیست');
  await expect(page.getByRole('button',{name:'ثبت انتخاب آزمایشی با Journal'})).toHaveCount(0);
});
test('COST-86-03 lost server ACK cannot fabricate success; exact UUID retry is sole action',async({page})=>{
  await page.goto('/member-demo/costs');
  await page.getByRole('button',{name:'ثبت دستی پیش‌برآورد در PostgreSQL'}).click();
  await expect(page.getByTestId('cost-journal-result')).toContainText('Registered');
  let dropped=false;
  await page.route('**/internal/demo/v1/cost-previews/*/choices',async route=>{
    if(dropped){await route.continue();return;}
    dropped=true;
    const accepted=await route.fetch();
    expect(accepted.status()).toBe(200);
    await route.abort();
  });
  await page.getByRole('button',{name:'ثبت انتخاب آزمایشی با Journal'}).click();
  await expect(page.getByRole('status')).toContainText('نتیجه نامطمئن است');
  await expect(page.getByTestId('cost-journal-result')).toHaveCount(0);
  await expect(page.getByRole('button',{name:'ثبت دستی پیش‌برآورد در PostgreSQL'})).toBeDisabled();
  await page.getByRole('button',{name:'تکرار امن همان UUID نامطمئن'}).click();
  await expect(page.getByTestId('cost-journal-result')).toContainText('Replayed');
  await expect(page.getByTestId('cost-journal-result')).toContainText('شمارش رویداد: 1');
});
