import {expect,test} from '@playwright/test';
test('SUPPORT-116-01 history reads .NET PostgreSQL, stays unverified',async({page})=>{
 await page.goto('/');
 const panel=page.getByTestId('internal-support-read');
 await panel.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 const history=panel.getByTestId('support-history');
 await history.getByRole('button',{name:'بازخوانی تاریخچه پیگیری'}).click();
 await expect(history.getByRole('status')).toContainText('از پایگاه داده بازخوانی شد');
 await expect(history).toContainText('بدون احراز هویت، تأیید بیمه');
 await expect(history.getByRole('status')).toContainText('از پایگاه داده بازخوانی شد');
 await expect(history.getByTestId('support-history-events').locator('li')).toHaveCount(0);
});
test('SUPPORT-116-02 tampered authority flags deny entire history',async({page})=>{
 await page.route('**/internal/demo/v1/support/**/history?*',route=>route.fulfill({
 status:200,contentType:'application/json',body:JSON.stringify({
 contract:'TRN-TECH-084',syntheticOnly:true,memberAuthenticated:true,
 complaintLegallySubmitted:false,insurerContacted:false,clinicalReviewCompleted:false,
 nationalReleaseAllowed:false,caseRef:'test:local-support',currentRevision:0,
 afterRevision:0,hasMore:false,nextAfterRevision:null,events:[]})}));
 await page.goto('/');
 const panel=page.getByTestId('internal-support-read');
 await panel.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 const history=panel.getByTestId('support-history');
 await history.getByRole('button',{name:'بازخوانی تاریخچه پیگیری'}).click();
 await expect(history.getByRole('status')).toContainText('داده پاک شد');
 await expect(history.getByTestId('support-history-events')).toHaveCount(0);
});
test('SUPPORT-116-03 outage clears last read history',async({page})=>{
 await page.goto('/');
 const panel=page.getByTestId('internal-support-read');
 await panel.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 const history=panel.getByTestId('support-history');
 await history.getByRole('button',{name:'بازخوانی تاریخچه پیگیری'}).click();
 await expect(history.getByTestId('support-history-events')).toBeVisible();
 await page.route('**/internal/demo/v1/support/**/history?*',route=>route.abort());
 await history.getByRole('button',{name:'بازخوانی تاریخچه پیگیری'}).click();
 await expect(history.getByTestId('support-history-events')).toHaveCount(0);
 await expect(history.getByRole('status')).toContainText('داده پاک شد');
});
