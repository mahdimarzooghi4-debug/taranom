import {expect,test} from '@playwright/test';

const panel=(p:import('@playwright/test').Page)=>p.getByTestId('cost-history-panel');

test('COST-89-01 verified PG timeline and historical versions after manual choice and reload',async({page})=>{
  const readRequests:string[]=[];
  page.on('request',r=>{
    if(r.url().includes('/cost-previews/')&&
       (r.url().includes('/history?')||r.url().includes('/versions/')))
      readRequests.push(r.method());
  });
  await page.goto('/member-demo/costs');
  const uuid=await page.getByLabel('UUID پیش‌برآورد').inputValue();
  await page.getByRole('button',{name:'ثبت دستی پیش‌برآورد در PostgreSQL'}).click();
  await expect(panel(page)).toBeVisible();
  expect(readRequests).toEqual([]);
  await panel(page).getByRole('button',{name:'خواندن دستی صفحه اول تاریخچه'}).click();
  await expect(panel(page).getByTestId('cost-history-events').locator('li')).toHaveCount(0);
  await panel(page).getByRole('button',{name:'بازخوانی دستی وضعیت آن نسخه'}).click();
  await expect(panel(page).getByTestId('cost-history-asof')).toContainText('نسخه تاریخی: 0');
  await page.getByRole('button',{name:'ثبت انتخاب آزمایشی با Journal'}).click();
  await expect(page.getByTestId('cost-journal-result')).toContainText('نسخه انتخاب: 1');
  await expect(panel(page).getByTestId('cost-history-asof')).toHaveCount(0);
  await panel(page).getByRole('button',{name:'خواندن دستی صفحه اول تاریخچه'}).click();
  await expect(panel(page).getByTestId('cost-history-events').locator('li')).toHaveCount(1);
  await expect(panel(page).getByTestId('cost-history-events')).toContainText('AcknowledgedUncertainty');
  await panel(page).getByLabel('نسخه تاریخی هزینه').fill('0');
  await panel(page).getByRole('button',{name:'بازخوانی دستی وضعیت آن نسخه'}).click();
  await expect(panel(page).getByTestId('cost-history-asof')).toContainText('شمار انتخاب: 0');
  await panel(page).getByLabel('نسخه تاریخی هزینه').fill('1');
  await panel(page).getByRole('button',{name:'بازخوانی دستی وضعیت آن نسخه'}).click();
  await expect(panel(page).getByTestId('cost-history-asof')).toContainText('شمار انتخاب: 1');
  await page.reload();
  await page.getByLabel('UUID پیش‌برآورد').fill(uuid);
  await page.getByRole('button',{name:'بازیابی با UUID پس از Reload'}).click();
  await panel(page).getByRole('button',{name:'خواندن دستی صفحه اول تاریخچه'}).click();
  await expect(panel(page).getByTestId('cost-history-events')).toContainText('AcknowledgedUncertainty');
  expect(readRequests).toEqual(['GET','GET','GET','GET','GET','GET']);
  await expect(page.getByText(/در فوریت پزشکی، کمک فوری/)).toBeVisible();
});

test('COST-89-02 no new history query or official consent on unregistered test or invalid versions',async({page})=>{
  await page.goto('/member-demo/costs');
  await expect(panel(page)).toHaveCount(0);
  await page.getByRole('button',{name:'ثبت دستی پیش‌برآورد در PostgreSQL'}).click();
  await expect(panel(page)).toBeVisible();
  await panel(page).getByLabel('نسخه تاریخی هزینه').fill('01');
  await expect(panel(page).getByRole('button',{name:'بازخوانی دستی وضعیت آن نسخه'})).toBeDisabled();
  await panel(page).getByLabel('نسخه تاریخی هزینه').fill('5');
  await expect(panel(page).getByRole('button',{name:'بازخوانی دستی وضعیت آن نسخه'})).toBeDisabled();
  await expect(page.getByText(/هیچ بدهی، کیف پول، بیمه یا رضایت درمانی/)).toBeVisible();
});
