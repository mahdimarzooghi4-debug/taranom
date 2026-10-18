import {expect,test} from '@playwright/test';
const endpoint='/internal/demo/v1/care-coordination/';
async function start(page:import('@playwright/test').Page){
 await page.goto('/member-demo/coordination');
 const ref=await page.getByLabel('شناسه آزمایشی پرونده').inputValue();
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'}))
  .toContainText('نسخه پرونده: 0');
 return {ref,guide:page.getByTestId('care-guided-workflow')};
}
test('CARE167-01 manual three-step nonclinical workbench rehydrates from PostgreSQL',async({page,request})=>{
 const {ref,guide}=await start(page);
 const expectVersion=async(n:number)=>{
  await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'}))
   .toContainText('نسخه پرونده: '+n);
 };
 await expect(guide.getByTestId('care-guided-step')).toContainText('گام ۱');
 await guide.getByRole('button',{name:'ثبت گام پیشنهادی با کلیک مستقل'}).click();
 await expectVersion(1);
 await expect(guide.getByTestId('care-guided-step')).toContainText('ادعای تماس');
 await guide.getByRole('button',{name:'ثبت گام پیشنهادی با کلیک مستقل'}).click();
 await expectVersion(2);
 await expect(guide.getByTestId('care-guided-step')).toContainText('پیوست ارجاع');
 await guide.getByRole('button',{name:'ثبت گام پیشنهادی با کلیک مستقل'}).click();
 await expectVersion(3);
 await expect(guide.getByTestId('care-guided-counts')).toContainText('ادعاهای تماس: 1');
 await expect(guide.getByTestId('care-guided-counts')).toContainText('ارجاعات تأییدنشده: 1');
 await expect(guide.getByRole('button',{name:'ثبت گام پیشنهادی با کلیک مستقل'})).toHaveCount(0);
 const data=await (await request.get(endpoint+encodeURIComponent(ref))).json();
 expect(data.events.map((x:{action:string})=>x.action)).toEqual([
  'RequestNonclinicalCoordination','RecordUnverifiedCoordinatorContact',
  'AttachUnverifiedClinicalSourceReference'
 ]);
 for(const key of ['realMemberAuthenticated','realCoordinatorVerified','clinicianAuthorized',
  'clinicalSourceVerified','referralOrdered','referralDelivered',
  'appointmentBooked','treatmentStarted','emergencyServiceDispatched',
  'medicalRecordAccessGranted','consentGranted','walletMutated','nationalReleaseAllowed'])
  expect(data[key],key).toBe(false);
 await page.reload();
 await page.getByLabel('شناسه آزمایشی پرونده').fill(ref);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(guide.getByTestId('care-guided-step')).toContainText('هر دو نوع ادعای پیگیری');
 await expect(guide.getByTestId('care-guided-counts')).toContainText('نسخه ژورنال: 3');
});
test('CARE167-02 out-of-order unverified evidence stays a claim; no inferred real contact',async({page,request})=>{
 const {ref,guide}=await start(page);
 await guide.getByRole('button',{name:'ثبت گام پیشنهادی با کلیک مستقل'}).click();
 await expect(guide.getByTestId('care-guided-step')).toContainText('ادعای تماس');
 await page.getByRole('button',{name:'پیوست ارجاع به منبع بالینیِ تأییدنشده'}).click();
 await expect(guide.getByTestId('care-guided-step')).toContainText('ادعای تماس');
 await expect(guide.getByTestId('care-guided-counts')).toContainText('ادعاهای تماس: 0');
 await expect(guide.getByTestId('care-guided-counts')).toContainText('ارجاعات تأییدنشده: 1');
 await guide.getByRole('button',{name:'ثبت گام پیشنهادی با کلیک مستقل'}).click();
 await expect(guide.getByTestId('care-guided-step')).toContainText('هر دو نوع ادعای پیگیری');
 const data=await (await request.get(endpoint+encodeURIComponent(ref))).json();
 expect(data.events.map((x:{action:string})=>x.action)).toEqual([
  'RequestNonclinicalCoordination','AttachUnverifiedClinicalSourceReference',
  'RecordUnverifiedCoordinatorContact']);
 expect(data.referralOrdered).toBe(false);
 expect(data.clinicianAuthorized).toBe(false);
 expect(data.nationalReleaseAllowed).toBe(false);
});
test('CARE167-03 lost ACK blocks guided write; GET reconciliation does not send another POST',async({page,request})=>{
 const {ref,guide}=await start(page);
 let body='',count=0;
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  count++;body=route.request().postData()??'';
  await route.fetch();await route.abort();
 });
 await guide.getByRole('button',{name:'ثبت گام پیشنهادی با کلیک مستقل'}).click();
 await expect(page.getByRole('alert')).toContainText('ممکن است رویداد');
 await expect(guide.getByTestId('care-guided-step')).toContainText('فرمان قبلی نامعلوم');
 await expect(guide.getByRole('button',{name:'ثبت گام پیشنهادی با کلیک مستقل'})).toHaveCount(0);
 await page.unrouteAll({behavior:'wait'});
 let newPosts=0;
 page.on('request',r=>{
  if(r.method()==='POST'&&r.url().includes('care-coordination/'))newPosts++;
 });
 await page.getByRole('button',{name:'بررسی فقط‌خواندنی فرمان قبلی'}).click();
 await expect(page.getByTestId('care-status')).toContainText('بدون بازپخش یا ثبت جدید');
 await expect(guide.getByTestId('care-guided-step')).toContainText('ادعای تماس');
 expect(count).toBe(1);expect(newPosts).toBe(0);
 const result=await (await request.get(endpoint+encodeURIComponent(ref))).json();
 expect(result.revision).toBe(1);expect(result.events).toHaveLength(1);
 expect(result.events[0].requestId).toBe(JSON.parse(body).requestId);
 expect(result.nationalReleaseAllowed).toBe(false);
});
