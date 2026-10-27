import {expect,test} from '@playwright/test';

function ref(label:string){return 'test:'+label+'-'+crypto.randomUUID().replaceAll('-','');}

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
 await expect(page.getByRole('note')).toContainText('هیچ پذیرش، رد، رزرو یا تأیید نوبتی ندارد');

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
