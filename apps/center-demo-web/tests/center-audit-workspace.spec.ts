import {expect,test} from '@playwright/test';

const testRef=(part:string)=>'test:'+part+'-'+crypto.randomUUID().replaceAll('-','');
async function setup(page:import('@playwright/test').Page){
  const center=testRef('audit-center'),dep=testRef('audit-ward'),doctor=testRef('audit-doctor');
  await page.goto('/');
  await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
  await page.getByLabel('شناسه بخش آزمایشی').fill(dep);
  await page.getByLabel('شناسه پزشک آزمایشی').fill(doctor);
  await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
  await expect(page.getByRole('status').first()).toContainText('PostgreSQL خوانده شد');
  return {center,dep,doctor,panel:page.getByTestId('center-audit-workspace')};
}
async function command(page:import('@playwright/test').Page,name:string,v:number){
  await page.getByRole('button',{name,exact:true}).click();
  await expect(page.getByRole('region',{name:'وضعیت ذخیره‌شده آزمایشی'}))
    .toContainText('نسخه ثبت‌شده: '+v);
}
test('CENTER-95-01 one source-backed center snapshot and historical event pages after reload',async({page})=>{
  const seen:string[]=[];
  page.on('request',r=>{if(r.url().includes('/audit-workspace?'))seen.push(r.method());});
  const {center,dep,doctor,panel}=await setup(page);
  await command(page,'ثبت بخش آزمایشی',1);
  await command(page,'ثبت وابستگی تأییدنشده پزشک آزمایشی',2);
  await command(page,'تخصیص آزمایشی پزشک به بخش',3);
  await expect(panel).toBeVisible();
  expect(seen).toEqual([]);
  await panel.getByRole('button',{name:'بازخوانی یکپارچه ممیزی'}).click();
  await expect(panel.getByTestId('center-audit-result')).toContainText('نسخه بازسازی‌شده: 3');
  await expect(panel.getByTestId('center-audit-result')).toContainText(doctor);
  await expect(panel.getByTestId('center-audit-events').locator('li')).toHaveCount(3);
  await command(page,'پایان وابستگی آزمایشی پزشک',4);
  await expect(panel.getByTestId('center-audit-result')).toHaveCount(0);
  await panel.getByRole('button',{name:'بازخوانی یکپارچه ممیزی'}).click();
  await expect(panel.getByTestId('center-audit-result')).toContainText('همکاری پایان یافته');
  await expect(panel.getByTestId('center-audit-result')).toContainText('نسخه بازسازی‌شده: 4');
  await panel.getByRole('button',{name:'صفحه بعد همان نسخه ممیزی'}).click();
  await expect(panel.getByTestId('center-audit-events').locator('li')).toHaveCount(4);
  await panel.getByLabel('نسخه ممیزی مرکز').fill('3');
  await panel.getByRole('button',{name:'بازخوانی یکپارچه ممیزی'}).click();
  await expect(panel.getByTestId('center-audit-result')).toContainText('در انتظار تأیید مستقل');
  await expect(panel.getByTestId('center-audit-result')).toContainText(dep);
  await expect(panel.getByTestId('center-audit-events').locator('li')).toHaveCount(3);
  await page.reload();
  await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
  await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
  await panel.getByLabel('نسخه ممیزی مرکز').fill('3');
  await panel.getByRole('button',{name:'بازخوانی یکپارچه ممیزی'}).click();
  await expect(panel.getByTestId('center-audit-result')).toContainText(doctor);
  expect(seen).toEqual(['GET','GET','GET','GET','GET']);
  await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
});
test('CENTER-95-02 concurrent center write invalidates entire audit and malformed versions disabled',async({page,request})=>{
  const {center,panel}=await setup(page);
  await command(page,'ثبت بخش آزمایشی',1);
  await panel.getByRole('button',{name:'بازخوانی یکپارچه ممیزی'}).click();
  await expect(panel.getByTestId('center-audit-result')).toBeVisible();
  const newDep=testRef('external-ward');
  const response=await request.post('/internal/demo/v1/centers/'+encodeURIComponent(center)+'/commands',{
    data:{requestId:crypto.randomUUID(),expectedVersion:1,kind:'DEFINE_DEPARTMENT',
      departmentRef:newDep,doctorRef:null}});
  expect(response.status()).toBe(200);
  await panel.getByRole('button',{name:'بازخوانی یکپارچه ممیزی'}).click();
  await expect(panel.getByTestId('center-audit-result')).toHaveCount(0);
  await expect(panel.getByTestId('center-audit-status')).toContainText('نسخه مرکز تغییر کرده');
  await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
  await panel.getByRole('button',{name:'بازخوانی یکپارچه ممیزی'}).click();
  await expect(panel.getByTestId('center-audit-result')).toContainText(newDep);
  await panel.getByLabel('نسخه ممیزی مرکز').fill('01');
  await expect(panel.getByRole('button',{name:'بازخوانی یکپارچه ممیزی'})).toBeDisabled();
  await panel.getByLabel('نسخه ممیزی مرکز').fill('3');
  await expect(panel.getByRole('button',{name:'بازخوانی یکپارچه ممیزی'})).toBeDisabled();
  await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
});
