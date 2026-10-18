import {expect,test} from '@playwright/test';

// TRN-TECH-175: real PostgreSQL events under a purely synthetic localhost fixture.
const id=(label:string)=>'test:'+label+'-'+crypto.randomUUID().replaceAll('-','');
const api=(center:string)=>'/internal/demo/v1/centers/'+encodeURIComponent(center);
async function seed(request:import('@playwright/test').APIRequestContext){
 const center=id('manual-adjust'),from=id('ward-a'),to=id('ward-b'),doctor=id('doctor');
 const records:[string,string|null,string|null][]=[
  ['DEFINE_DEPARTMENT',from,null],
  ['DEFINE_DEPARTMENT',to,null],
  ['RECORD_DOCTOR',null,doctor],
  ['ASSIGN_DOCTOR',from,doctor]];
 for(let i=0;i<records.length;i++){
  const [kind,departmentRef,doctorRef]=records[i];
  const response=await request.post(api(center)+'/commands',{
   data:{requestId:crypto.randomUUID(),expectedVersion:i,kind,departmentRef,doctorRef}});
  expect(response.status()).toBe(200);
  expect((await response.json()).currentVersion).toBe(i+1);
 }
 return {center,from,to,doctor};
}
async function open(page:import('@playwright/test').Page,s:{
 center:string;from:string;to:string;doctor:string
}){
 await page.goto('/');
 await page.getByLabel('شناسه مرکز آزمایشی').fill(s.center);
 await page.getByLabel('شناسه بخش آزمایشی').fill(s.from);
 await page.getByLabel('شناسه پزشک آزمایشی').fill(s.doctor);
 await page.getByLabel('شناسه بخش مقصد آزمایشی').fill(s.to);
 await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
 await expect(page.getByRole('region',{name:'وضعیت ذخیره‌شده آزمایشی'}))
  .toContainText('نسخه ثبت‌شده: 4');
 return page.getByTestId('center-department-adjustment');
}
test('ADJUST166-01 two explicit PostgreSQL writes, never automatic transfer',async({page,request})=>{
 const s=await seed(request);
 const ui=await open(page,s);
 await expect(ui.getByTestId('center-adjustment-step')).toContainText('گام ۱');
 await ui.getByRole('button',{name:'ثبت مستقل انتساب به بخش مقصد'}).click();
 await expect(page.getByRole('region',{name:'وضعیت ذخیره‌شده آزمایشی'}))
  .toContainText('نسخه ثبت‌شده: 5');
 const midway=await (await request.get(api(s.center))).json();
 expect(midway.doctors.find((d:{doctorRef:string})=>d.doctorRef===s.doctor)
  .departmentRefs.sort()).toEqual([s.from,s.to].sort());
 expect(midway.managerAuthenticated).toBe(false);
 expect(midway.professionalQualificationVerified).toBe(false);
 expect(midway.nationalReleaseAllowed).toBe(false);
 await expect(ui.getByTestId('center-adjustment-step')).toContainText('گام ۲');
 await ui.getByRole('button',{name:'برداشتن مستقل انتساب از بخش مبدأ'}).click();
 await expect(page.getByRole('region',{name:'وضعیت ذخیره‌شده آزمایشی'}))
  .toContainText('نسخه ثبت‌شده: 6');
 await expect(ui.getByTestId('center-adjustment-step')).toContainText('فقط بخش مقصد');
 await expect(ui.getByRole('button',{name:'برداشتن مستقل انتساب از بخش مبدأ'})).toHaveCount(0);
 const final=await (await request.get(api(s.center))).json();
 expect(final.doctors.find((d:{doctorRef:string})=>d.doctorRef===s.doctor)
  .departmentRefs).toEqual([s.to]);
 expect(final.eventCount).toBe(6);
 expect(final.doctors[0].affiliation).toBe('PendingIndependentVerification');
 expect(final.nationalReleaseAllowed).toBe(false);
 const before=await (await request.get(api(s.center)+'/versions/5')).json();
 expect(before.snapshot.doctors[0].departmentRefs.sort()).toEqual([s.from,s.to].sort());
});
test('ADJUST166-02 version changed by independent writer fails closed; no extra event',async({page,request})=>{
 const s=await seed(request),ui=await open(page,s);
 await ui.getByRole('button',{name:'ثبت مستقل انتساب به بخش مقصد'}).click();
 await expect(ui.getByTestId('center-adjustment-step')).toContainText('گام ۲');
 const competing=await request.post(api(s.center)+'/commands',{data:{
  requestId:crypto.randomUUID(),expectedVersion:5,kind:'UNASSIGN_DOCTOR',
  departmentRef:s.from,doctorRef:s.doctor}});
 expect(competing.status()).toBe(200);
 await ui.getByRole('button',{name:'برداشتن مستقل انتساب از بخش مبدأ'}).click();
 await expect(page.getByRole('status').first()).toContainText('StaleRevision');
 await expect(ui.getByTestId('center-adjustment-step')).toContainText('فقط بخش مقصد');
 await expect(ui.getByRole('button',{name:'برداشتن مستقل انتساب از بخش مبدأ'})).toHaveCount(0);
 const result=await (await request.get(api(s.center))).json();
 expect(result.currentVersion).toBe(6);
 expect(result.eventCount).toBe(6);
 expect(result.nationalReleaseAllowed).toBe(false);
});
test('ADJUST166-03 invalid target and lost ACK never trigger an automatic second command',async({page,request})=>{
 const s=await seed(request),ui=await open(page,s);
 await page.getByLabel('شناسه بخش مقصد آزمایشی').fill(s.from);
 await expect(ui.getByTestId('center-adjustment-step')).toContainText('دو بخش متفاوت');
 await expect(ui.getByRole('button',{name:'ثبت مستقل انتساب به بخش مقصد'})).toHaveCount(0);
 await page.getByLabel('شناسه بخش مقصد آزمایشی').fill(id('missing'));
 await expect(ui.getByTestId('center-adjustment-step')).toContainText('هر دو بخش');
 await expect(ui.getByRole('button',{name:'ثبت مستقل انتساب به بخش مقصد'})).toHaveCount(0);
 await page.getByLabel('شناسه بخش مقصد آزمایشی').fill(s.to);
 let count=0;
 await page.route('**/internal/demo/v1/centers/**/commands',async route=>{
  count++;await route.fetch();await route.abort();
 });
 await ui.getByRole('button',{name:'ثبت مستقل انتساب به بخش مقصد'}).click();
 await expect(page.getByRole('alert',{name:'بازیابی دستور با نتیجه نامعلوم'})).toBeVisible();
 await expect(ui.getByTestId('center-adjustment-step')).toContainText('پاسخ ثبت قبلی نامعلوم');
 await expect(ui.getByRole('button',{name:'برداشتن مستقل انتساب از بخش مبدأ'})).toHaveCount(0);
 expect(count).toBe(1);
 await page.unrouteAll({behavior:'wait'});
 await page.getByRole('button',{name:'تلاش دستی مجدد با همان درخواست'}).click();
 await expect(ui.getByTestId('center-adjustment-step')).toContainText('گام ۲');
 const saved=await (await request.get(api(s.center))).json();
 expect(saved.eventCount).toBe(5);
 expect(saved.nationalReleaseAllowed).toBe(false);
});