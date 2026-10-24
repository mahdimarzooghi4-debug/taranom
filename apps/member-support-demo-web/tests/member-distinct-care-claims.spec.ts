import {expect,test} from '@playwright/test';
// TRN-TECH-188: synthetic PostgreSQL journal. Neither claim is clinically verified.
const api='/internal/demo/v1/care-coordination/';
const contact='ثبت ادعای تماس هماهنگ‌کننده';
const clinical='پیوست ارجاع به منبع بالینیِ تأییدنشده';
const history='تاریخچه هماهنگی ادعایی';
async function setup(page:import('@playwright/test').Page){
 await page.goto('/member-demo/coordination');
 const ref=await page.getByLabel('شناسه آزمایشی پرونده').inputValue();
 await page.getByLabel('مرجع یادداشت تماس ادعایی، تأییدنشده').fill('test:note179');
 await page.getByLabel('مرجع منبع بالینی ادعایی، تأییدنشده').fill('test:source179');
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(page.getByRole('region',{name:history})).toContainText('نسخه پرونده: 0');
 return ref;
}
async function begin(page:import('@playwright/test').Page){
 await page.getByRole('button',{name:'ثبت درخواست ادعایی هماهنگی'}).click();
 await expect(page.getByRole('region',{name:history})).toContainText('نسخه پرونده: 1');
}
test('MEMBER179-01 distinct manually chosen claim types persist separate refs',async({page,request})=>{
 const ref=await setup(page);await begin(page);
 await page.getByRole('button',{name:contact}).click();
 await expect(page.getByRole('region',{name:history})).toContainText('نسخه پرونده: 2');
 await page.getByRole('button',{name:clinical}).click();
 const timeline=page.getByRole('region',{name:history});
 await expect(timeline).toContainText('نسخه پرونده: 3');
 await expect(timeline).toContainText('ادعای تماس تأییدنشده');
 await expect(timeline).toContainText('مرجع بالینی ادعایی تأییدنشده');
 const s=await (await request.get(api+ref)).json();
 expect(s.events[1].action).toBe('RecordUnverifiedCoordinatorContact');
 expect(s.events[1].unverifiedNoteOrDocumentRef).toBe('test:note179');
 expect(s.events[2].action).toBe('AttachUnverifiedClinicalSourceReference');
 expect(s.events[2].unverifiedNoteOrDocumentRef).toBe('test:source179');
 for(const k of ['realMemberAuthenticated','realCoordinatorVerified','clinicalSourceVerified',
 'clinicianAuthorized','referralOrdered','referralDelivered','appointmentBooked','treatmentStarted',
 'medicalRecordAccessGranted','emergencyServiceDispatched','consentGranted',
 'walletMutated','nationalReleaseAllowed'])expect(s[k],k).toBe(false);
});
test('MEMBER179-02 guided manual steps also bind to their own refs',async({page,request})=>{
 const ref=await setup(page);
 const next=page.getByTestId('care-guided-workflow').getByRole('button',{name:'ثبت گام پیشنهادی با کلیک مستقل'});
 await next.click();
 let s=await (await request.get(api+ref)).json();expect(s.events).toHaveLength(1);
 await next.click();await expect(page.getByRole('region',{name:history})).toContainText('نسخه پرونده: 2');
 await next.click();await expect(page.getByRole('region',{name:history})).toContainText('نسخه پرونده: 3');
 s=await (await request.get(api+ref)).json();
 expect(s.events.map((e:{unverifiedNoteOrDocumentRef:string|null})=>e.unverifiedNoteOrDocumentRef))
 .toEqual([null,'test:note179','test:source179']);
});
test('MEMBER179-03 invalid contact ref blocks contact but cannot contaminate clinical action',async({page,request})=>{
 const ref=await setup(page);await begin(page);
 await page.getByLabel('مرجع یادداشت تماس ادعایی، تأییدنشده').fill('real:forbidden');
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(page.getByRole('button',{name:contact})).toBeDisabled();
 await expect(page.getByRole('button',{name:clinical})).toBeEnabled();
 let count=0;
 page.on('request',r=>{if(r.method()==='POST'&&r.url().includes('/care-coordination/'))count++;});
 await page.getByRole('button',{name:clinical}).click();
 await expect(page.getByRole('region',{name:history})).toContainText('نسخه پرونده: 2');
 expect(count).toBe(1);
 const s=await (await request.get(api+ref)).json();
 expect(s.events).toHaveLength(2);
 expect(s.events[1].action).toBe('AttachUnverifiedClinicalSourceReference');
 expect(s.events[1].unverifiedNoteOrDocumentRef).toBe('test:source179');
});
test('MEMBER179-04 lost clinical-source ACK recovers exact UUID by read only',async({page,request})=>{
 const ref=await setup(page);await begin(page);
 let body='',accepted=0;
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  if(route.request().method()!=='POST')return route.continue();
  body=route.request().postData()??'';accepted++;
  const response=await route.fetch();expect(response.status()).toBe(200);
  await route.abort();
 });
 await page.getByRole('button',{name:clinical}).click();
 await expect(page.getByRole('alert')).toContainText('ممکن است رویداد');
 await expect(page.getByLabel('مرجع منبع بالینی ادعایی، تأییدنشده')).toBeDisabled();
 await page.unrouteAll({behavior:'wait'});
 let additional=0;
 page.on('request',r=>{if(r.method()==='POST'&&r.url().includes('/care-coordination/'))additional++;});
 await page.getByRole('button',{name:'بررسی فقط‌خواندنی فرمان قبلی'}).click();
 await expect(page.getByRole('alert')).toHaveCount(0);
 await expect(page.getByRole('region',{name:history})).toContainText('نسخه پرونده: 2');
 expect(accepted).toBe(1);expect(additional).toBe(0);
 const s=await (await request.get(api+ref)).json();
 expect(s.events[1].requestId).toBe(JSON.parse(body).requestId);
 expect(s.events[1].action).toBe('AttachUnverifiedClinicalSourceReference');
 expect(s.events[1].unverifiedNoteOrDocumentRef).toBe('test:source179');
});
