import {expect,test} from '@playwright/test';

const panel=(page:import('@playwright/test').Page)=>page.getByTestId('synthetic-choice-workbench');
const widget=(page:import('@playwright/test').Page)=>page.getByTestId('synthetic-cost-workbench');

test('COST-85-01 no synthetic choice posted until separate explicit action; replay is idempotent',async({page})=>{
  const calls:string[]=[];
  page.on('request',r=>{if(r.url().includes('/synthetic-cost-choice'))calls.push(r.method());});
  await page.goto('/member/costs');
  await expect(panel(page).getByRole('button',{name:'انتخاب آزمایشی برای همین نسخه برآورد'})).toBeDisabled();
  await widget(page).getByRole('button',{name:'ارزیابی دستی با موتور .NET'}).click();
  await panel(page).getByRole('button',{name:'انتخاب آزمایشی برای همین نسخه برآورد'}).click();
  expect(calls).toEqual([]);
  await panel(page).getByRole('button',{name:'شبیه‌سازی ثبت انتخاب در .NET'}).click();
  await expect(page.getByTestId('synthetic-choice-result')).toContainText('RecordedUnverified');
  await expect(panel(page)).toContainText('رویدادهای آزمایشی: 1');
  await panel(page).getByRole('button',{name:'بازپخش دقیق آخرین فرمان'}).click();
  await expect(page.getByTestId('synthetic-choice-result')).toContainText('Replayed');
  await expect(panel(page)).toContainText('رویدادهای آزمایشی: 1');
  expect(calls).toEqual(['POST','POST']);
  await expect(page.getByRole('button',{name:'ثبت آگاهی و انتخاب — نیازمند اتصال امن'})).toBeDisabled();
});

test('COST-85-02 changing preview version requires manual continuity review and never carries choice',async({page})=>{
  await page.goto('/member/costs');
  await widget(page).getByRole('button',{name:'ارزیابی دستی با موتور .NET'}).click();
  await panel(page).getByRole('button',{name:'انتخاب آزمایشی برای همین نسخه برآورد'}).click();
  await panel(page).getByLabel('نوع انتخاب آزمایشی').selectOption('RequestedClarification');
  await panel(page).getByRole('button',{name:'شبیه‌سازی ثبت انتخاب در .NET'}).click();
  await expect(page.getByTestId('synthetic-choice-result')).toContainText('RecordedUnverified');
  await widget(page).getByLabel('شماره نسخه برآورد',{exact:true}).fill('2');
  await expect(page.getByTestId('synthetic-cost-result')).toHaveCount(0);
  await expect(panel(page).getByRole('button',{name:'شبیه‌سازی ثبت انتخاب در .NET'})).toBeDisabled();
  await widget(page).getByRole('button',{name:'ارزیابی دستی با موتور .NET'}).click();
  await panel(page).getByRole('button',{name:'بررسی تغییر نسخه با تاریخچه قبلی'}).click();
  await expect(page.getByTestId('synthetic-choice-result'))
    .toContainText('CurrentMissingRequiresSeparateReview');
  await expect(page.getByTestId('synthetic-choice-result')).toContainText('آگاهی قبلی منتقل نشد');
  await panel(page).getByRole('button',{name:'شروع نسخه مستقل بدون انتقال انتخاب'}).click();
  await expect(panel(page).getByRole('button',{name:'انتخاب آزمایشی برای همین نسخه برآورد'})).toBeEnabled();
  await expect(page.getByRole('button',{name:'ثبت انتخاب جدید — نیازمند نمایش معتبر و احراز هویت'})).toBeDisabled();
});

test('COST-85-03 conflicted lane never authorizes a cost; urgent help independent',async({page})=>{
  await page.goto('/member/costs');
  await widget(page).getByRole('button',{name:'ارزیابی دستی با موتور .NET'}).click();
  await panel(page).getByRole('button',{name:'انتخاب آزمایشی برای همین نسخه برآورد'}).click();
  await panel(page).getByRole('button',{name:'شبیه‌سازی ثبت انتخاب در .NET'}).click();
  await widget(page).getByLabel('بیمه پایه وضعیت').selectOption('ConflictingClaims');
  await widget(page).getByRole('button',{name:'ارزیابی دستی با موتور .NET'}).click();
  await panel(page).getByRole('button',{name:'بررسی تغییر نسخه با تاریخچه قبلی'}).click();
  await expect(page.getByTestId('synthetic-choice-result')).toContainText('CurrentConflictRequiresSeparateReview');
  await expect(page.getByTestId('synthetic-cost-result')).toContainText('Conflicted');
  await expect(page.getByText(/در فوریت پزشکی، دریافت کمک فوری را به تکمیل این آزمایش/)).toBeVisible();
});
