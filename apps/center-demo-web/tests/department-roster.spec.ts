import {expect,test} from '@playwright/test';

const unique=(part:string)=>'test:'+part+'-'+crypto.randomUUID().replaceAll('-','');
async function setup(page:import('@playwright/test').Page){
  const center=unique('roster-center'), department=unique('roster-dept');
  const doctor=unique('roster-doctor');
  await page.goto('/');
  await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
  await page.getByLabel('شناسه بخش آزمایشی').fill(department);
  await page.getByLabel('شناسه پزشک آزمایشی').fill(doctor);
  await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
  await expect(page.getByRole('status').first()).toContainText('PostgreSQL خوانده شد');
  return {center,department,doctor};
}
test('CENTER-93-01 current roster clears ended doctor but historical preserves affiliation',async({page})=>{
  const {center,department,doctor}=await setup(page);
  const actions=['ثبت بخش آزمایشی','ثبت وابستگی تأییدنشده پزشک آزمایشی',
                 'تخصیص آزمایشی پزشک به بخش'];
  for(let version=0;version<actions.length;version++){
    await page.getByRole('button',{name:actions[version],exact:true}).click();
    await expect(page.getByRole('region',{name:'وضعیت ذخیره‌شده آزمایشی'}))
      .toContainText('نسخه ثبت‌شده: '+(version+1));
  }
  const widget=page.getByTestId('department-roster');
  await expect(widget).toBeVisible();
  await expect(widget.getByTestId('department-roster-results')).toHaveCount(0);
  await widget.getByRole('button',{name:'بازخوانی دستی فهرست بخش از PostgreSQL'}).click();
  await expect(widget.getByTestId('department-roster-results')).toContainText(doctor);
  await expect(widget.getByTestId('department-roster-results')).toContainText('نسخه بازسازی‌شده: 3');
  await page.getByRole('button',{name:'پایان وابستگی آزمایشی پزشک'}).click();
  await expect(page.getByRole('region',{name:'وضعیت ذخیره‌شده آزمایشی'}))
    .toContainText('نسخه ثبت‌شده: 4');
  await expect(widget.getByTestId('department-roster-results')).toHaveCount(0);
  await widget.getByRole('button',{name:'بازخوانی دستی فهرست بخش از PostgreSQL'}).click();
  await expect(widget.getByTestId('department-roster-results')).toContainText('شمار وابستگی‌های تأییدنشده: 0');
  await expect(widget.getByTestId('department-roster-results')).not.toContainText(doctor);
  await widget.getByLabel('نسخه فهرست بخش').fill('3');
  await widget.getByRole('button',{name:'بازخوانی دستی فهرست بخش از PostgreSQL'}).click();
  await expect(widget.getByTestId('department-roster-results')).toContainText(doctor);
  await expect(widget.getByTestId('department-roster-results')).toContainText('تأیید مدیر: خیر');
  await page.reload();
  await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
  await page.getByLabel('شناسه بخش آزمایشی').fill(department);
  await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
  await widget.getByLabel('نسخه فهرست بخش').fill('3');
  await widget.getByRole('button',{name:'بازخوانی دستی فهرست بخش از PostgreSQL'}).click();
  await expect(widget.getByTestId('department-roster-results')).toContainText(doctor);
});
test('CENTER-93-02 stale center revision and real refs cannot imply verified license',async({page,request})=>{
  const {center,department}=await setup(page);
  const widget=page.getByTestId('department-roster');
  await widget.getByLabel('نسخه فهرست بخش').fill('00');
  await expect(widget.getByRole('button',{name:'بازخوانی دستی فهرست بخش از PostgreSQL'})).toBeDisabled();
  await widget.getByLabel('نسخه فهرست بخش').fill('0');
  const response=await request.post('/internal/demo/v1/centers/'+encodeURIComponent(center)+'/commands',{
    data:{requestId:crypto.randomUUID(),expectedVersion:0,kind:'DEFINE_DEPARTMENT',
          departmentRef:department,doctorRef:null}});
  expect(response.status()).toBe(200);
  await widget.getByRole('button',{name:'بازخوانی دستی فهرست بخش از PostgreSQL'}).click();
  await expect(widget.getByTestId('department-roster-status')).toContainText('نسخه مرکز تغییر کرده');
  await expect(widget.getByTestId('department-roster-results')).toHaveCount(0);
  await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
  await widget.getByRole('button',{name:'بازخوانی دستی فهرست بخش از PostgreSQL'}).click();
  await expect(widget.getByTestId('department-roster-results')).toContainText('تأیید مدیر: خیر');
});
