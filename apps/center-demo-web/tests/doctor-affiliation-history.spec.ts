import {expect,test} from '@playwright/test';

const synthetic=(tag:string)=>'test:'+tag+'-'+crypto.randomUUID().replaceAll('-');
async function workspace(page:import('@playwright/test').Page){
  const center=synthetic('doctor-center');
  const department=synthetic('doctor-ward');
  const doctor=synthetic('doctor-person');
  await page.goto('/');
  await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
  await page.getByLabel('شناسه بخش آزمایشی').fill(department);
  await page.getByLabel('شناسه پزشک آزمایشی').fill(doctor);
  await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
  await expect(page.getByRole('status').first()).toContainText('PostgreSQL خوانده شد');
  return {center,department,doctor,panel:page.getByTestId('doctor-affiliation-history')};
}
async function write(page:import('@playwright/test').Page,label:string,version:number){
  await page.getByRole('button',{name:label,exact:true}).click();
  await expect(page.getByRole('region',{name:'وضعیت ذخیره‌شده آزمایشی'}))
    .toContainText('نسخه ثبت‌شده: '+version);
}
test('CENTER-94-01 manual, persisted doctor lineage before/after ending and reload',async({page})=>{
  const reads:string[]=[];const writes:string[]=[];
  page.on('request',r=>{
    if(r.url().includes('/affiliation-history?'))reads.push(r.method());
    if(r.method()==='POST'&&r.url().includes('/centers/'))writes.push(r.url());
  });
  const {center,department,doctor,panel}=await workspace(page);
  await write(page,'ثبت بخش آزمایشی',1);
  await write(page,'ثبت وابستگی تأییدنشده پزشک آزمایشی',2);
  await write(page,'تخصیص آزمایشی پزشک به بخش',3);
  await expect(panel).toBeVisible();
  expect(reads).toEqual([]);
  await panel.getByRole('button',{name:'بازخوانی دستی سابقه پزشک'}).click();
  await expect(panel.getByTestId('doctor-history-result')).toContainText('در انتظار بررسی مستقل');
  await expect(panel.getByTestId('doctor-history-result')).toContainText(department);
  await expect(panel.getByTestId('doctor-history-events').locator('li')).toHaveCount(2);
  await write(page,'پایان وابستگی آزمایشی پزشک',4);
  await expect(panel.getByTestId('doctor-history-result')).toHaveCount(0);
  await panel.getByRole('button',{name:'بازخوانی دستی سابقه پزشک'}).click();
  await expect(panel.getByTestId('doctor-history-result')).toContainText('وضعیت وابستگی: پایان‌یافته');
  await expect(panel.getByTestId('doctor-history-result')).toContainText('بخش‌های همان نسخه: هیچ');
  await expect(panel.getByTestId('doctor-history-events').locator('li')).toHaveCount(3);
  await panel.getByLabel('نسخه تاریخچه پزشک').fill('3');
  await panel.getByRole('button',{name:'بازخوانی دستی سابقه پزشک'}).click();
  await expect(panel.getByTestId('doctor-history-result')).toContainText('در انتظار بررسی مستقل');
  await expect(panel.getByTestId('doctor-history-result')).toContainText(department);
  await page.reload();
  await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
  await page.getByLabel('شناسه پزشک آزمایشی').fill(doctor);
  await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
  await panel.getByLabel('نسخه تاریخچه پزشک').fill('3');
  await panel.getByRole('button',{name:'بازخوانی دستی سابقه پزشک'}).click();
  await expect(panel.getByTestId('doctor-history-result')).toContainText(department);
  expect(reads).toEqual(['GET','GET','GET','GET']);
  expect(writes).toHaveLength(4);
  await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
});

test('CENTER-94-02 unrelated center event invalidates stale doctor history; real refs denied',async({page,request})=>{
  const {center,doctor,panel}=await workspace(page);
  await write(page,'ثبت بخش آزمایشی',1);
  await write(page,'ثبت وابستگی تأییدنشده پزشک آزمایشی',2);
  await panel.getByRole('button',{name:'بازخوانی دستی سابقه پزشک'}).click();
  await expect(panel.getByTestId('doctor-history-result')).toBeVisible();
  const second=synthetic('other-department');
  const outside=await request.post('/internal/demo/v1/centers/'+encodeURIComponent(center)+'/commands',{
    data:{requestId:crypto.randomUUID(),expectedVersion:2,kind:'DEFINE_DEPARTMENT',
      departmentRef:second,doctorRef:null}
  });
  expect(outside.status()).toBe(200);
  await panel.getByRole('button',{name:'بازخوانی دستی سابقه پزشک'}).click();
  await expect(panel.getByTestId('doctor-history-result')).toHaveCount(0);
  await expect(panel.getByTestId('doctor-history-status'))
    .toContainText('نسخه مرکز تغییر کرده');
  await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
  await panel.getByRole('button',{name:'بازخوانی دستی سابقه پزشک'}).click();
  await expect(panel.getByTestId('doctor-history-result')).toBeVisible();
  await panel.getByLabel('نسخه تاریخچه پزشک').fill('01');
  await expect(panel.getByRole('button',{name:'بازخوانی دستی سابقه پزشک'})).toBeDisabled();
  await page.getByLabel('شناسه پزشک آزمایشی').fill('real:doctor');
  await expect(panel).toHaveCount(0);
  await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
});
