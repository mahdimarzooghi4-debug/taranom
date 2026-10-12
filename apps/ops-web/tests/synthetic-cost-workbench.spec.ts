import {expect,test} from '@playwright/test';

test('COST-84-01 real built member UI calls .NET only after explicit human action', async({page})=>{
  const posts: string[] = [];
  page.on('request', request => {
    if (request.url().includes('/synthetic-cost-workbench')) posts.push(request.method());
  });
  await page.goto('/member/costs');
  const workbench = page.getByTestId('synthetic-cost-workbench');
  await expect(workbench.getByRole('heading', {name:/ارزیابی تعاملی پنج منشأ ساختگی/})).toBeVisible();
  await expect(workbench.getByRole('listitem')).toHaveCount(5);
  expect(posts).toEqual([]);
  await workbench.getByRole('button', {name:'ارزیابی دستی با موتور .NET'}).click();
  await expect(page.getByTestId('synthetic-cost-result')).toContainText('MissingOrIncomplete');
  await expect(page.getByTestId('synthetic-cost-result')).toContainText('مبلغ معتبر موجود نیست');
  expect(posts).toEqual(['POST']);
  await expect(page.getByText(/دریافت کمک درمانی را به بررسی برآورد/)).toBeVisible();
  await expect(page.getByRole('button',{name:'ثبت آگاهی و انتخاب — نیازمند اتصال امن'})).toBeDisabled();
});

test('COST-84-02 all five provisional sources go through canonical .NET, not frontend arithmetic',async({page})=>{
  await page.goto('/member/costs');
  const widget = page.getByTestId('synthetic-cost-workbench');
  const names=['خدمت ارائه‌دهنده','بیمه پایه','بیمه تکمیلی','اعتبار درمان','سهم عضو'];
  for(const name of names){
    await widget.getByLabel(name+' وضعیت').selectOption('ProvisionalClaim');
    await widget.getByLabel(name+' مبلغ').fill('123.45');
    await widget.getByLabel(name+' ارز').fill('IRR');
  }
  await widget.getByRole('button',{name:'ارزیابی دستی با موتور .NET'}).click();
  const result=page.getByTestId('synthetic-cost-result');
  await expect(result).toContainText('AllComponentsProvisionalOnly');
  await expect(result.locator('li')).toHaveCount(5);
  await expect(result.locator('li').first()).toContainText('123.45 IRR');
  await expect(result).not.toContainText('مجموع');
  await widget.getByLabel('بیمه تکمیلی وضعیت').selectOption('ConflictingClaims');
  await expect(result).toHaveCount(0); // editing invalidates the result immediately
  await widget.getByRole('button',{name:'ارزیابی دستی با موتور .NET'}).click();
  const next=page.getByTestId('synthetic-cost-result');
  await expect(next).toContainText('Conflicted');
  await expect(next.locator('li').nth(2)).toContainText('مبلغ معتبر موجود نیست');
  await expect(next).toContainText('رضایت پزشکی، تعهد مالی و تغییر کیف پول ایجاد نشد');
});

test('COST-84-03 revising preview invalidates prior result; invalid test ref fails closed',async({page})=>{
  await page.goto('/member/costs');
  const widget=page.getByTestId('synthetic-cost-workbench');
  await widget.getByRole('button',{name:'ارزیابی دستی با موتور .NET'}).click();
  const before=await page.getByTestId('synthetic-cost-result').locator('code').innerText();
  await widget.getByLabel('شماره نسخه برآورد',{exact:true}).fill('2');
  await expect(page.getByTestId('synthetic-cost-result')).toHaveCount(0);
  await widget.getByRole('button',{name:'ارزیابی دستی با موتور .NET'}).click();
  const after=await page.getByTestId('synthetic-cost-result').locator('code').innerText();
  expect(after).not.toBe(before);
  await widget.getByLabel('شناسه عضو ساختگی').fill('real:patient');
  await widget.getByRole('button',{name:'ارزیابی دستی با موتور .NET'}).click();
  await expect(widget.getByRole('alert')).toContainText('هیچ نتیجه‌ای پذیرفته نشد');
  await expect(page.getByTestId('synthetic-cost-result')).toHaveCount(0);
  await expect(page.getByRole('button',{name:'ثبت انتخاب جدید — نیازمند نمایش معتبر و احراز هویت'}))
    .toBeDisabled();
});
