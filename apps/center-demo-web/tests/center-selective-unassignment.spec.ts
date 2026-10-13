import {expect,test} from '@playwright/test';
const id=(s:string)=>'test:'+s+'-'+crypto.randomUUID().replaceAll('-','');
const api=(c:string)=>'/internal/demo/v1/centers/'+encodeURIComponent(c);
test('CENTER-99-01: remove one ward but preserve other, pending doctor and exact history',async({page,request})=>{
  const center=id('selective'),a=id('ward-a'),b=id('ward-b'),doctor=id('doctor');
  await page.goto('/');
  await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
  await page.getByLabel('شناسه بخش آزمایشی').fill(a);
  await page.getByLabel('شناسه پزشک آزمایشی').fill(doctor);
  await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
  const write=async(name:string,version:number)=>{
    await page.getByRole('button',{name,exact:true}).click();
    await expect(page.getByRole('region',{name:'وضعیت ذخیره‌شده آزمایشی'}))
      .toContainText('نسخه ثبت‌شده: '+version);
  };
  await write('ثبت بخش آزمایشی',1);
  await write('ثبت وابستگی تأییدنشده پزشک آزمایشی',2);
  await write('تخصیص آزمایشی پزشک به بخش',3);
  await page.getByLabel('شناسه بخش آزمایشی').fill(b);
  await write('ثبت بخش آزمایشی',4);
  await write('تخصیص آزمایشی پزشک به بخش',5);
  await page.getByLabel('شناسه بخش آزمایشی').fill(a);
  await write('حذف انتساب پزشک فقط از بخش انتخابی (آزمایشی)',6);
  const current=await (await request.get(api(center))).json();
  expect(current.eventCount).toBe(6);
  expect(current.doctors[0].departmentRefs).toEqual([b]);
  expect(current.doctors[0].affiliation).toBe('PendingIndependentVerification');
  expect(current.managerAuthenticated).toBe(false);
  expect(current.professionalQualificationVerified).toBe(false);
  expect(current.nationalReleaseAllowed).toBe(false);
  const old=await (await request.get(api(center)+'/versions/5')).json();
  expect(old.snapshot.doctors[0].departmentRefs.sort()).toEqual([a,b].sort());
  const rosterA=await (await request.get(api(center)+'/departments/'+encodeURIComponent(a)+
    '/roster?expectedCurrentVersion=6&asOfVersion=6')).json();
  const rosterB=await (await request.get(api(center)+'/departments/'+encodeURIComponent(b)+
    '/roster?expectedCurrentVersion=6&asOfVersion=6')).json();
  expect(rosterA.doctorCount).toBe(0);expect(rosterB.doctorCount).toBe(1);
  const lineage=await request.get(api(center)+'/doctors/'+encodeURIComponent(doctor)+
    '/affiliation-history?expectedCurrentVersion=6&asOfVersion=6&afterVersion=5&limit=3');
  expect(lineage.status()).toBe(200);
  const events=(await lineage.json()).events;
  expect(events).toHaveLength(1);expect(events[0].kind).toBe('UnassignDoctorFromDepartment');
  expect(events[0].departmentRef).toBe(a);
  await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
  const audit=page.getByTestId('center-audit-workspace');
  await audit.getByRole('button',{name:'بازخوانی یکپارچه ممیزی'}).click();
  await expect(audit.getByTestId('center-audit-result')).toContainText(b);
  const history=page.getByTestId('doctor-affiliation-history');
  await history.getByRole('button',{name:'بازخوانی دستی سابقه پزشک'}).click();
  await expect(history.getByTestId('doctor-history-result')).toContainText(b);
  await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
});
test('CENTER-99-02: absent ward link does not end doctor or append',async({request})=>{
  const center=id('denial'),doc=id('doctor'),ward=id('ward');
  async function post(version:number,kind:string,departmentRef:string|null,doctorRef:string|null){
    const res=await request.post(api(center)+'/commands',{data:{requestId:crypto.randomUUID(),
      expectedVersion:version,kind,departmentRef,doctorRef}});
    return {code:res.status(),data:await res.json()};
  }
  expect((await post(0,'DEFINE_DEPARTMENT',ward,null)).code).toBe(200);
  expect((await post(1,'RECORD_DOCTOR',null,doc)).code).toBe(200);
  const no=await post(2,'UNASSIGN_DOCTOR',ward,doc);
  expect(no.code).toBe(409);expect(no.data.outcome).toBe('NotApplicable');
  expect((await post(2,'UNASSIGN_DOCTOR',ward,null)).code).toBe(400);
  expect((await post(2,'UNASSIGN_DOCTOR','real:ward',doc)).code).toBe(400);
  const state=await (await request.get(api(center))).json();
  expect(state.eventCount).toBe(2);
  expect(state.doctors[0].affiliation).toBe('PendingIndependentVerification');
  expect(state.doctors[0].departmentRefs).toEqual([]);
  expect(state.nationalReleaseAllowed).toBe(false);
});
