import {expect,test} from '@playwright/test';

function ref(label:string){return 'test:'+label+'-'+crypto.randomUUID().replaceAll('-','');}

test('APPOINTMENT-QUEUE-02: center queue filters by requested department and patient-selected doctor',async({page,request})=>{
 const center=ref('center-filter');
 const department=ref('department-match');
 const otherDepartment=ref('department-other');
 const doctor=ref('doctor-match');
 const otherDoctor=ref('doctor-other');
 const matchingRefs:string[]=[];
 const otherRefs:string[]=[];
 const add=async (departmentRef:string,doctorRef:string,matching:boolean)=>{
  const requestRef=ref('appointment-filter');
  (matching?matchingRefs:otherRefs).push(requestRef);
  const response=await request.post('/internal/demo/v1/appointment-requests/'+
   encodeURIComponent(requestRef),{data:{
    requestId:crypto.randomUUID(),expectedRevision:0,memberRef:ref('member'),
    centerRef:center,departmentRef,requestedDoctorRef:doctorRef,slotRef:ref('slot'),
   }});
  expect(response.status()).toBe(200);
 };
 for(let index=0;index<11;index++)await add(department,doctor,true);
 await add(department,otherDoctor,false);
 await add(otherDepartment,doctor,false);

 await page.goto('/appointment-requests-qa');
 await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
 await page.getByLabel('شناسه تخصص برای فیلتر (اختیاری)').fill(department);
 await page.getByLabel('شناسه پزشک برای فیلتر (اختیاری)').fill(doctor);
 await page.getByRole('button',{name:'خواندن صف درخواست نوبت'}).click();
 await expect(page.getByRole('status')).toContainText('یکپارچگی درخواست‌ها بررسی شد');
 const visible=page.getByRole('region',{name:'درخواست‌های این صفحه'});
 await expect(visible.locator('li')).toHaveCount(10);
 const visibleRefs=(await visible.locator('li').allTextContents()).join(' ');
 const expectedRefs=[...matchingRefs].sort();
 for(const requestRef of expectedRefs.slice(0,10))
  expect(visibleRefs).toContain(requestRef);
 expect(visibleRefs).not.toContain(expectedRefs[10]);
 for(const requestRef of otherRefs)
  expect(visibleRefs).not.toContain(requestRef);
 await expect(visible).toContainText(department);
 await expect(visible).toContainText(doctor);

 const endpoint='/internal/demo/v1/centers/'+encodeURIComponent(center)+
  '/appointment-requests?limit=10&departmentRef='+encodeURIComponent(department)+
  '&requestedDoctorRef='+encodeURIComponent(doctor);
 const invalidFilter=await request.get(endpoint+'&departmentRef=prod:real-department');
 expect(invalidFilter.status()).toBe(400);
 const firstResponse=await request.get(endpoint);
 expect(firstResponse.status()).toBe(200);
 const firstPage=await firstResponse.json();
 expect(firstPage.departmentRefFilter).toBe(department);
 expect(firstPage.requestedDoctorRefFilter).toBe(doctor);
 expect(firstPage.requests).toHaveLength(10);
 expect(firstPage.hasMore).toBe(true);
 for(const row of firstPage.requests)
  expect(row).toMatchObject({centerRef:center,departmentRef:department,requestedDoctorRef:doctor});

 await page.getByRole('button',{name:'صفحه بعد درخواست‌ها'}).click();
 await expect(visible.locator('li')).toHaveCount(1);
 const nextResponse=await request.get(endpoint+'&afterRequestRef='+
  encodeURIComponent(firstPage.nextAfterRequestRef));
 expect(nextResponse.status()).toBe(200);
 const secondPage=await nextResponse.json();
 expect(secondPage.requests).toHaveLength(1);
 expect(secondPage.hasMore).toBe(false);
 expect(secondPage.requests[0]).toMatchObject({centerRef:center,departmentRef:department,requestedDoctorRef:doctor});
 expect(matchingRefs.sort()[10]).toBe(secondPage.requests[0].requestRef);
});

test('APPOINTMENT-QUEUE-01: center sees only verified synthetic request intent, never a booking',async({page,request})=>{
 const center=ref('center-queue');
 const otherCenter=ref('other-center');
 const requestRef=ref('appointment-queue');
 const otherRequestRef=ref('appointment-other-queue');
 const make=(target:string)=>({
  requestId:crypto.randomUUID(),expectedRevision:0,
  memberRef:ref('member'),centerRef:target,departmentRef:ref('department'),
  requestedDoctorRef:ref('doctor'),slotRef:ref('slot')
 });
 const first=await request.post('/internal/demo/v1/appointment-requests/'+
  encodeURIComponent(requestRef),{data:make(center)});
 expect(first.status()).toBe(200);
 const second=await request.post('/internal/demo/v1/appointment-requests/'+
  encodeURIComponent(otherRequestRef),{data:make(otherCenter)});
 expect(second.status()).toBe(200);

 await page.goto('/appointment-requests-qa');
 await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
 await page.getByRole('button',{name:'خواندن صف درخواست نوبت'}).click();
 await expect(page.getByRole('status')).toContainText('یکپارچگی درخواست‌ها بررسی شد');
 const visible=page.getByRole('region',{name:'درخواست‌های این صفحه'});
 await expect(visible).toContainText(requestRef);
 await expect(visible).not.toContainText(otherRequestRef);
 await expect(visible).toContainText('پزشک انتخاب‌شده توسط بیمار');
 await expect(visible).toContainText('نوبت تأیید نشده');
 await expect(page.getByRole('note')).toContainText('هیچ رزرو یا اقدام بالینی انجام نمی‌شود');

 const response=await request.get('/internal/demo/v1/centers/'+
  encodeURIComponent(center)+'/appointment-requests?limit=10');
 expect(response.status()).toBe(200);
 const json=await response.json();
 expect(json.contract).toBe('TRN-TECH-217');
 expect(json.requests.map((x:{requestRef:string})=>x.requestRef)).toEqual([requestRef]);
 for(const flag of ['memberAuthenticated','centerAuthenticated',
  'requestedDoctorLicensed','slotAvailabilityVerified','bookingConfirmed',
  'clinicalActionAllowed','nationalReleaseAllowed'])expect(json[flag]).toBe(false);
});
