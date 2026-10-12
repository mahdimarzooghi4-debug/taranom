import {expect,test} from '@playwright/test';
const synthetic=(tag:string)=>'test:'+tag+'-'+crypto.randomUUID().replaceAll('-','');
async function setup(page:import('@playwright/test').Page){
 const center=synthetic('guided-center'),department=synthetic('guided-dept'),doctor=synthetic('guided-doctor');
 await page.goto('/');
 await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
 await page.getByLabel('شناسه بخش آزمایشی').fill(department);
 await page.getByLabel('شناسه پزشک آزمایشی').fill(doctor);
 await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
 await expect(page.getByRole('status').first()).toContainText('PostgreSQL خوانده شد');
 return {center,department,doctor};
}
test('GUIDED-84-01 four manual journal transitions, persistent and verified no clinical rights',async({page,request})=>{
 const {center,department,doctor}=await setup(page);
 const guide=page.getByTestId('center-guided-flow');
 const labels=['مرحله ۱ از ۴','مرحله ۲ از ۴','مرحله ۳ از ۴','مرحله ۴ از ۴'];
 for(let i=0;i<4;i++){
   await expect(guide.getByTestId('center-guided-step')).toContainText(labels[i]);
   await guide.getByRole('button',{name:'ثبت مرحله بعد در PostgreSQL'}).click();
   await expect(page.getByRole('region',{name:'وضعیت ذخیره‌شده آزمایشی'}))
     .toContainText('نسخه ثبت‌شده: '+(i+1));
 }
 await expect(guide.getByTestId('center-guided-step')).toContainText('مسیر آزمایشی برای این پزشک پایان یافته');
 await expect(guide.getByRole('button',{name:'ثبت مرحله بعد در PostgreSQL'})).toHaveCount(0);
 await page.reload();
 await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
 await page.getByLabel('شناسه بخش آزمایشی').fill(department);
 await page.getByLabel('شناسه پزشک آزمایشی').fill(doctor);
 await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
 await expect(guide.getByTestId('center-guided-step')).toContainText('مسیر آزمایشی');
 const response=await request.get('/internal/demo/v1/centers/'+encodeURIComponent(center));
 expect(response.status()).toBe(200);
 const saved=await response.json();
 expect(saved.currentVersion).toBe(4);
 expect(saved.eventCount).toBe(4);
 expect(saved.departments).toContain(department);
 expect(saved.doctors.find((x:{doctorRef:string})=>x.doctorRef===doctor).affiliation).toBe('Ended');
 expect(saved.managerAuthenticated).toBe(false);
 expect(saved.professionalQualificationVerified).toBe(false);
 expect(saved.nationalReleaseAllowed).toBe(false);
});
test('GUIDED-84-02 concurrent real center event updates next manual step without duplicate',async({page,request})=>{
 const {center,doctor}=await setup(page);
 const prefix='/internal/demo/v1/centers/'+encodeURIComponent(center);
 const guide=page.getByTestId('center-guided-flow');
 await guide.getByRole('button',{name:'ثبت مرحله بعد در PostgreSQL'}).click();
 await expect(guide.getByTestId('center-guided-step')).toContainText('مرحله ۲ از ۴');
 const concurrent={requestId:crypto.randomUUID(),expectedVersion:1,kind:'RECORD_DOCTOR',
   departmentRef:null,doctorRef:doctor};
 const response=await request.post(prefix+'/commands',{data:concurrent});
 expect(response.status()).toBe(200);
 expect((await response.json()).currentVersion).toBe(2);
 // Our browser carries a genuinely stale Version=1. The .NET journal rejects
 // its new request with 409 and hands back the true current version.
 await guide.getByRole('button',{name:'ثبت مرحله بعد در PostgreSQL'}).click();
 await expect(page.getByRole('status').first()).toContainText('StaleRevision');
 await expect(guide.getByTestId('center-guided-step')).toContainText('مرحله ۳ از ۴');
 const saved=await (await request.get(prefix)).json();
 expect(saved.currentVersion).toBe(2);
 expect(saved.eventCount).toBe(2);
 expect(saved.professionalQualificationVerified).toBe(false);
});
test('GUIDED-84-03 invalid input, unknown source and manual steps never authorize real center',async({page})=>{
 await page.goto('/');
 const guide=page.getByTestId('center-guided-flow');
 await expect(guide.getByTestId('center-guided-step')).toContainText('ابتدا نسخه جاری');
 await expect(guide.getByRole('button',{name:'ثبت مرحله بعد در PostgreSQL'})).toHaveCount(0);
 await page.getByLabel('شناسه پزشک آزمایشی').fill('real:doctor');
 await expect(guide.getByTestId('center-guided-step')).toContainText('صرفاً test:');
 await setup(page);
 await page.route('**/internal/demo/v1/centers/test:*',route=>route.abort());
 await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
 await expect(page.getByRole('status').first()).toContainText('وضعیت معتبر فرض نمی‌شود');
 await expect(guide.getByRole('button',{name:'ثبت مرحله بعد در PostgreSQL'})).toHaveCount(0);
 await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
});
