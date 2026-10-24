import {expect,test} from '@playwright/test';
const root='/internal/demo/v1/care-coordination/';
const origin={Origin:'http://127.0.0.1:4320'};
async function setup(page:import('@playwright/test').Page,request:import('@playwright/test').APIRequestContext){
 const ref='test:care180-'+crypto.randomUUID().replaceAll('-','');
 const cmd={requestId:crypto.randomUUID(),expectedRevision:0,
 action:'REQUEST_NONCLINICAL_COORDINATION',memberRef:'test:member180',
 originalMemberRequestRef:'test:source180',
 originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
 claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
 expect((await request.post(root+ref+'/commands',{data:cmd,headers:origin})).status()).toBe(200);
 await page.goto('/');
 const panel=page.getByTestId('center-care-coordination');
 await panel.getByLabel('شناسه پرونده هماهنگی مرکز').fill(ref);
 await panel.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await expect(panel.getByTestId('center-care-source')).toContainText('نسخه 1');
 return {ref,panel};
}
test('CARE180-C1 center persisted lost contact ACK, reimport and independent GET with NO POST',async({page,request})=>{
 const {ref,panel}=await setup(page,request);
 let sent='';
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  if(route.request().method()!=='POST')return route.continue();
  sent=route.request().postData()??'';
  expect((await route.fetch()).status()).toBe(200);await route.abort();
 });
 await panel.getByRole('button',{name:'ثبت مستقل ادعای تماس هماهنگ‌کننده'}).click();
 await expect(panel.getByTestId('center-care-uncertain')).toBeVisible();
 const receipt=await panel.getByTestId('center-care-recovery-export').inputValue();
 expect(JSON.parse(receipt).surface).toBe('CENTER');
 expect(JSON.parse(receipt).body).toBe(sent);
 await page.unrouteAll({behavior:'wait'});await page.reload();
 const fresh=page.getByTestId('center-care-coordination');
 let extra=0;page.on('request',r=>{if(r.method()==='POST'&&r.url().includes('/care-coordination/'))extra++;});
 await fresh.getByLabel('متن رسید بازیابی هماهنگی مرکز').fill(receipt);
 await fresh.getByRole('button',{name:'بارگذاری رسید هماهنگی مرکز بدون ارسال'}).click();
 await expect(fresh.getByTestId('center-care-uncertain')).toBeVisible();
 await expect(fresh.getByTestId('center-care-status')).toContainText('هیچ POST');
 expect(extra).toBe(0);
 await fresh.getByRole('button',{name:'بررسی فقط‌خواندنی همان تماس'}).click();
 await expect(fresh.getByTestId('center-care-uncertain')).toHaveCount(0);
 await expect(fresh.getByTestId('center-care-source')).toContainText('نسخه 2');
 expect(extra).toBe(0);
 const history=await (await request.get(root+ref)).json();
 expect(history.events[1].requestId).toBe(JSON.parse(sent).requestId);
});
test('CARE180-C2 clinical source forged ACK: reload import cannot clear before explicit same payload replay',async({page,request})=>{
 const {ref,panel}=await setup(page,request);
 let body='';
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  body=route.request().postData()??'';
  const c=JSON.parse(body), forged=await (await request.get(root+ref)).json();
  forged.outcome='RecordedUnverified';forged.revision=2;
  forged.events.push({revision:2,requestId:c.requestId,
   action:'AttachUnverifiedClinicalSourceReference',memberRef:c.memberRef,
   originalMemberRequestRef:c.originalMemberRequestRef,
   originalSubmissionRequestId:c.originalSubmissionRequestId,
   claimedActorRef:c.claimedActorRef,
   unverifiedNoteOrDocumentRef:c.unverifiedNoteOrDocumentRef});
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(forged)});
 });
 await panel.getByRole('button',{name:'ثبت مستقل مرجع بالینی تأییدنشده'}).click();
 await expect(panel.getByTestId('center-care-uncertain')).toBeVisible();
 const receipt=await panel.getByTestId('center-care-recovery-export').inputValue();
 await page.unrouteAll({behavior:'wait'});await page.reload();
 const recovered=page.getByTestId('center-care-coordination');
 await recovered.getByLabel('متن رسید بازیابی هماهنگی مرکز').fill(receipt);
 await recovered.getByRole('button',{name:'بارگذاری رسید هماهنگی مرکز بدون ارسال'}).click();
 await recovered.getByRole('button',{name:'بررسی فقط‌خواندنی همان مرجع'}).click();
 await expect(recovered.getByTestId('center-care-uncertain')).toBeVisible();
 await recovered.getByRole('button',{name:'بازپخش دستی دقیق UUID قبلی'}).click();
 await expect(recovered.getByTestId('center-care-uncertain')).toHaveCount(0);
 const persisted=await (await request.get(root+ref)).json();
 expect(persisted.events).toHaveLength(2);
 expect(persisted.events[1].action).toBe('AttachUnverifiedClinicalSourceReference');
 expect(persisted.events[1].requestId).toBe(JSON.parse(body).requestId);
 expect(persisted.nationalReleaseAllowed).toBe(false);
});
test('CARE180-C3 deny member-only root, real refs and extra properties with zero POST',async({page})=>{
 await page.goto('/');
 const panel=page.getByTestId('center-care-coordination');
 const base={contract:'TRN-TECH-189',syntheticOnly:true,surface:'CENTER',caseRef:'test:care180',
 body:JSON.stringify({requestId:crypto.randomUUID(),expectedRevision:1,
 action:'RECORD_UNVERIFIED_COORDINATOR_CONTACT',
 memberRef:'test:member',originalMemberRequestRef:'test:request',
 originalSubmissionRequestId:crypto.randomUUID(),
 claimedActorRef:'test:claimed',unverifiedNoteOrDocumentRef:'test:note'})};
 let mutations=0;page.on('request',r=>{if(r.method()==='POST')mutations++;});
 for(const invalid of [
  {...base,surface:'MEMBER'},
  {...base,caseRef:'real:patient'},
  {...base,body:base.body.replace('test:claimed','real:doctor')},
  {...base,body:base.body.replace('RECORD_UNVERIFIED_COORDINATOR_CONTACT','REQUEST_NONCLINICAL_COORDINATION')},
  {...base,extra:'clinicalGrant'}
 ]){
  await panel.getByLabel('متن رسید بازیابی هماهنگی مرکز').fill(JSON.stringify(invalid));
  await panel.getByRole('button',{name:'بارگذاری رسید هماهنگی مرکز بدون ارسال'}).click();
  await expect(panel.getByTestId('center-care-status')).toContainText('نامعتبر');
  await expect(panel.getByTestId('center-care-uncertain')).toHaveCount(0);
 }
 await panel.getByLabel('متن رسید بازیابی هماهنگی مرکز').fill(JSON.stringify(base));
 await panel.getByRole('button',{name:'بارگذاری رسید هماهنگی مرکز بدون ارسال'}).click();
 await expect(panel.getByTestId('center-care-uncertain')).toBeVisible();
 expect(mutations).toBe(0);
});
