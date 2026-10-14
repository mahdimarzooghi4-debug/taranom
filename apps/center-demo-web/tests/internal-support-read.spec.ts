import {expect,test} from '@playwright/test';

test('SUPPORT-115-01 existing .NET HTTP returns unverified PostgreSQL snapshot',async({page})=>{
 await page.goto('/');
 const panel=page.getByTestId('internal-support-read');
 await expect(panel).toContainText('ژورنال PostgreSQL');
 await panel.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 await expect(panel.getByTestId('support-read-result')).toContainText('test:local-support');
 await expect(panel).toContainText('فاقد اعتبار رسمی');
 await expect(panel).toContainText('انتشار ملی ممنوع');
});
test('SUPPORT-115-02 corrupted backend reply never renders claimed evidence',async({page})=>{
 await page.route('**/internal/demo/v1/support/**',route=>route.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({contract:'TRN-TECH-075',caseRef:'test:local-support',
   syntheticOnly:true,memberAuthenticated:true,nationalReleaseAllowed:true})
 }));
 await page.goto('/');
 const panel=page.getByTestId('internal-support-read');
 await panel.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 await expect(panel.getByRole('status')).toContainText('قابل تأیید نیست');
 await expect(panel.getByTestId('support-read-result')).toHaveCount(0);
});
test('SUPPORT-115-03 invalid ref cannot make a request',async({page})=>{
 let calls=0;
 page.on('request',request=>{if(request.url().includes('/internal/demo/v1/support/'))calls++;});
 await page.goto('/');
 const panel=page.getByTestId('internal-support-read');
 await panel.getByRole('textbox',{name:'شناسه پرونده پیگیری آزمایشی'}).fill('untrusted');
 await expect(panel.getByRole('button',{name:'خواندن از پایگاه داده محلی'})).toBeDisabled();
 expect(calls).toBe(0);
});
test('SUPPORT-115-04 source outage clears prior results',async({page})=>{
 await page.goto('/');
 const panel=page.getByTestId('internal-support-read');
 await panel.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 await expect(panel.getByTestId('support-read-result')).toBeVisible();
 await page.route('**/internal/demo/v1/support/**',route=>route.abort());
 await panel.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 await expect(panel.getByTestId('support-read-result')).toHaveCount(0);
 await expect(panel.getByRole('status')).toContainText('اطلاعات قبلی پاک شد');
});
