import {expect,test} from '@playwright/test';
const unique=()=>('test:write-'+crypto.randomUUID().replaceAll('-',''));
test('SUPPORT-117-01 full browser -> .NET -> PostgreSQL submit and follow-up',async({page})=>{
 await page.goto('/');
 const panel=page.getByTestId('internal-support-read');
 const ref=unique();
 await panel.getByRole('textbox',{name:'شناسه پرونده پیگیری آزمایشی'}).fill(ref);
 await panel.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 await expect(panel.getByTestId('support-read-result')).toContainText('نسخه 0');
 await panel.getByRole('button',{name:'ثبت اولیه تأییدنشده'}).click();
 await expect(panel.getByTestId('support-read-result')).toContainText('نسخه 1');
 await panel.getByRole('button',{name:'ثبت پیگیری تأییدنشده'}).click();
 await expect(panel.getByTestId('support-read-result')).toContainText('نسخه 2');
 await expect(panel.getByTestId('support-read-result')).toContainText('پیگیری‌ها: 1');
 await panel.getByRole('button',{name:'بازخوانی تاریخچه پیگیری'}).click();
 await expect(panel.getByTestId('support-history-events').locator('li')).toHaveCount(2);
 await expect(panel).toContainText('انتشار ملی ممنوع');
});
test('SUPPORT-117-02 malformed refs disable mutations',async({page})=>{
 await page.goto('/');
 const panel=page.getByTestId('internal-support-read');
 await panel.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 const write=panel.getByTestId('internal-support-write');
 await write.getByRole('textbox',{name:'عضو آزمایشی'}).fill('real-id');
 await expect(write.getByRole('button',{name:'ثبت اولیه تأییدنشده'})).toBeDisabled();
});
test('SUPPORT-117-03 cross-origin requests cannot write',async({request})=>{
 const ref=unique();
 const res=await request.post('/internal/demo/v1/support/'+ref+'/commands',{
  headers:{Origin:'https://untrusted.example'},data:{},
  failOnStatusCode:false});
 expect(res.status()).toBe(403);
 expect((await res.json()).nationalReleaseAllowed).toBe(false);
});
