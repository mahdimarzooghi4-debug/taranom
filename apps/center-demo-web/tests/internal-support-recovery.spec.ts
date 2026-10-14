import {expect,test} from '@playwright/test';
const unique=()=>('test:recovery-'+crypto.randomUUID().replaceAll('-',''));
async function openCase(page:import('@playwright/test').Page,ref:string){
 await page.goto('/');
 const panel=page.getByTestId('internal-support-read');
 await panel.getByRole('textbox',{name:'شناسه پرونده پیگیری آزمایشی'}).fill(ref);
 await panel.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 await expect(panel.getByTestId('support-read-result')).toContainText('نسخه 0');
 return panel;
}
test('SUPPORT-118-01 response lost after DB commit, exact retry only replays one event',async({page,request})=>{
 const ref=unique(),posted:string[]=[];let first=true;
 await page.route('**/internal/demo/v1/support/**/commands',async route=>{
  posted.push(route.request().postData()??'');
  if(first){first=false;const reply=await route.fetch();expect(reply.status()).toBe(200);
   await route.abort();return;}
  await route.continue();
 });
 const panel=await openCase(page,ref);
 const write=panel.getByTestId('internal-support-write');
 await write.getByRole('button',{name:'ثبت اولیه تأییدنشده'}).click();
 await expect(write.getByRole('status')).toContainText('نتیجه نامشخص');
 await expect(write.getByRole('textbox',{name:'عضو آزمایشی'})).toBeDisabled();
 await expect(panel.getByRole('textbox',{name:'شناسه پرونده پیگیری آزمایشی'})).toBeDisabled();
 const receipt=await write.getByRole('textbox',{name:'رسید قابل انتقال درخواست'}).inputValue();
 expect(receipt).toContain('TRN-TECH-127');
 await write.getByRole('button',{name:'تکرار ایمن همان درخواست'}).click();
 await expect(panel.getByTestId('support-read-result')).toContainText('نسخه 1');
 expect(posted).toHaveLength(2);expect(posted[0]).toBe(posted[1]);
 const state=await (await request.get('/internal/demo/v1/support/'+ref)).json();
 expect(state.revision).toBe(1);
 expect(state.originalRequestId).toBe(JSON.parse(posted[0]).requestId);
 expect(state.nationalReleaseAllowed).toBe(false);
});
test('SUPPORT-118-02 export and import exact receipt after reload; no new UUID',async({page,request})=>{
 const ref=unique(),sent:string[]=[];let first=true;
 await page.route('**/internal/demo/v1/support/**/commands',async route=>{
  sent.push(route.request().postData()??'');
  if(first){first=false;await route.abort();return;}
  await route.continue();
 });
 let panel=await openCase(page,ref);
 await panel.getByRole('button',{name:'ثبت اولیه تأییدنشده'}).click();
 const write=panel.getByTestId('internal-support-write');
 await expect(write.getByRole('status')).toContainText('نتیجه نامشخص');
 const receipt=await write.getByRole('textbox',{name:'رسید قابل انتقال درخواست'}).inputValue();
 await page.reload();
 panel=await page.getByTestId('internal-support-read');
 await panel.getByRole('textbox',{name:'شناسه پرونده پیگیری آزمایشی'}).fill(ref);
 await panel.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 await expect(panel.getByTestId('support-read-result')).toContainText('نسخه 0');
 const next=panel.getByTestId('internal-support-write');
 await next.getByRole('textbox',{name:'ورود رسید بازیابی'}).fill(receipt);
 await next.getByRole('button',{name:'بررسی و بارگذاری رسید'}).click();
 await expect(next.getByRole('button',{name:'تکرار ایمن همان درخواست'})).toBeEnabled();
 await next.getByRole('button',{name:'تکرار ایمن همان درخواست'}).click();
 await expect(panel.getByTestId('support-read-result')).toContainText('نسخه 1');
 expect(sent).toHaveLength(2);expect(sent[0]).toBe(sent[1]);
 expect((await (await request.get('/internal/demo/v1/support/'+ref)).json()).revision).toBe(1);
});
test('SUPPORT-118-03 forged or mismatched receipt never sends a POST',async({page})=>{
 const ref=unique(),panel=await openCase(page,ref);
 let count=0;page.on('request',r=>{if(r.url().includes('/commands'))count++;});
 const write=panel.getByTestId('internal-support-write');
 const importBox=write.getByRole('textbox',{name:'ورود رسید بازیابی'});
 await importBox.fill('{"contract":"TRN-TECH-127","syntheticOnly":true,"caseRef":"test:other","originalBody":"{}"}');
 await write.getByRole('button',{name:'بررسی و بارگذاری رسید'}).click();
 await expect(write.getByRole('alert')).toContainText('رسید نامعتبر');
 await expect(write.getByRole('button',{name:'ثبت اولیه تأییدنشده'})).toBeEnabled();
 expect(count).toBe(0);
});
test('SUPPORT-118-04 stale competing write cannot silently append or mint new request',async({page,request})=>{
 const ref=unique(),panel=await openCase(page,ref);
 const first=await request.post('/internal/demo/v1/support/'+ref+'/commands',{data:{
  requestId:crypto.randomUUID(),expectedRevision:0,action:'SUBMIT_UNVERIFIED',
  memberRef:'test:another-member',topic:'MEDICAL_SERVICE_COMPLAINT',
  unverifiedEvidenceRef:'test:other-evidence',claimedSubmitterRef:'test:other-submitter',
  originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
  claimedFollowUpActorRef:null,unverifiedFollowUpNoteRef:null
 }});
 expect(first.status()).toBe(200);
 const write=panel.getByTestId('internal-support-write');
 await write.getByRole('button',{name:'ثبت اولیه تأییدنشده'}).click();
 await expect(write.getByRole('status')).toContainText('نتیجه نامشخص');
 const saved=await write.getByRole('textbox',{name:'رسید قابل انتقال درخواست'}).inputValue();
 await write.getByRole('button',{name:'تکرار ایمن همان درخواست'}).click();
 await expect(write.getByRole('status')).toContainText('نتیجه نامشخص');
 expect(await write.getByRole('textbox',{name:'رسید قابل انتقال درخواست'}).inputValue()).toBe(saved);
 expect((await (await request.get('/internal/demo/v1/support/'+ref)).json()).revision).toBe(1);
});
