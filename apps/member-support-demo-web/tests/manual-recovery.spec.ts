import {expect,test} from '@playwright/test';
const ref=(t:string)=>'test:'+t+'-'+crypto.randomUUID().replaceAll('-','');

async function create(page:import('@playwright/test').Page){
  const caseRef=ref('manual-recovery'),member=ref('member'),evidence=ref('evidence'),submitter=ref('submitter');
  await page.goto('/member-demo/');
  await page.getByLabel('پرونده آزمایشی').fill(caseRef);
  await page.getByLabel('شناسه عضو فرضی').fill(member);
  await page.getByLabel('ارجاع مدرک تأییدنشده').fill(evidence);
  await page.getByLabel('ثبت‌کننده ادعایی').fill(submitter);
  await page.getByRole('button',{name:'بازخوانی پرونده پشتیبانی آزمایشی'}).click();
  await expect(page.getByTestId('support-status')).toContainText('PostgreSQL خوانده شد');
  return caseRef;
}
test('RECOVERY-83-01 response lost after committed submission; manual receipt survives reload and replay is exact',async({page,request})=>{
  const caseRef=await create(page);
  let dropped=false;
  const bodies:unknown[]=[];
  await page.route('**/internal/demo/v1/support/*/commands',async route=>{
    bodies.push(route.request().postDataJSON());
    if(!dropped){
      dropped=true; const real=await route.fetch();
      expect(real.status()).toBe(200);
      expect((await real.json()).outcome).toBe('RecordedUnverified');
      await route.abort();
    }else await route.continue();
  });
  await page.getByRole('button',{name:'ثبت درخواست اولیه آزمایشی'}).click();
  await expect(page.getByTestId('pending-support-write')).toBeVisible();
  const receipt=await page.getByTestId('pending-recovery-capsule').inputValue();
  expect(JSON.parse(receipt).contract).toBe('TRN-TECH-092');
  expect(JSON.parse(receipt).command).toEqual(bodies[0]);
  const original=await (await request.get('/internal/demo/v1/support/'+encodeURIComponent(caseRef))).json();
  expect(original.revision).toBe(1);
  await page.reload();
  await expect(page.getByTestId('pending-support-write')).toHaveCount(0);
  await page.getByLabel('متن رسید بازیابی آزمایشی').fill(receipt);
  await page.getByRole('button',{name:'بارگذاری رسید بازیابی بدون ارسال'}).click();
  await expect(page.getByTestId('support-status')).toContainText('هیچ دستوری ارسال نشده');
  await expect(page.getByTestId('pending-support-write')).toBeVisible();
  expect(bodies).toHaveLength(1);
  await expect(page.getByLabel('پرونده آزمایشی')).toHaveValue(caseRef);
  await page.getByRole('button',{name:'تکرار امن همان دستور آزمایشی'}).click();
  await expect(page.getByTestId('support-status')).toContainText('تکرار امن');
  expect(bodies).toHaveLength(2);expect(bodies[0]).toEqual(bodies[1]);
  const latest=await (await request.get('/internal/demo/v1/support/'+encodeURIComponent(caseRef))).json();
  expect(latest.revision).toBe(1);
  expect(latest.followUpCount).toBe(0);
  expect(latest.memberAuthenticated).toBe(false);
});

test('RECOVERY-83-02 follow-up acknowledged then lost; import restores same UUID and note lineage',async({page,request})=>{
  const caseRef=await create(page);
  await page.getByRole('button',{name:'ثبت درخواست اولیه آزمایشی'}).click();
  await expect(page.getByTestId('support-status')).toContainText('فقط ارجاع آزمایشی ثبت شد');
  const note=ref('note');
  await page.getByLabel('ارجاع یادداشت پیگیری تأییدنشده').fill(note);
  let lost=false;const commands:unknown[]=[];
  await page.route('**/internal/demo/v1/support/*/commands',async route=>{
    commands.push(route.request().postDataJSON());
    if(!lost){
      lost=true;const result=await route.fetch();
      expect(result.status()).toBe(200);
      expect((await result.json()).revision).toBe(2);
      await route.abort();
    }else await route.continue();
  });
  await page.getByRole('button',{name:'ثبت ارجاع پیگیری آزمایشی'}).click();
  await expect(page.getByTestId('pending-support-write')).toBeVisible();
  const receipt=await page.getByTestId('pending-recovery-capsule').inputValue();
  expect(JSON.parse(receipt).command.unverifiedFollowUpNoteRef).toBe(note);
  await page.reload();
  await page.getByLabel('متن رسید بازیابی آزمایشی').fill(receipt);
  await page.getByRole('button',{name:'بارگذاری رسید بازیابی بدون ارسال'}).click();
  await expect(page.getByTestId('pending-support-write')).toBeVisible();
  await page.getByRole('button',{name:'تکرار امن همان دستور آزمایشی'}).click();
  await expect(page.getByTestId('support-status')).toContainText('تکرار امن');
  expect(commands).toHaveLength(2);expect(commands[0]).toEqual(commands[1]);
  const saved=await (await request.get('/internal/demo/v1/support/'+encodeURIComponent(caseRef))).json();
  expect(saved.revision).toBe(2);expect(saved.followUpCount).toBe(1);
  expect(saved.followUps[0].noteRef).toBe(note);
  expect(saved.insurerContacted).toBe(false);
});

test('RECOVERY-83-03 malformed real identity, extra properties, changed IDs do not trigger network or a command',async({page})=>{
  await page.goto('/member-demo/');
  const cmd={
    requestId:crypto.randomUUID(),expectedRevision:0,action:'SUBMIT_UNVERIFIED',
    memberRef:ref('member'),topic:'MEDICAL_SERVICE_COMPLAINT',
    unverifiedEvidenceRef:ref('evidence'),claimedSubmitterRef:ref('actor'),
    originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
    claimedFollowUpActorRef:null,unverifiedFollowUpNoteRef:null
  };
  const good={contract:'TRN-TECH-092',syntheticOnly:true,caseRef:ref('case'),command:cmd};
  const mutationRequests:string[]=[];
  page.on('request',r=>{if(r.method()==='POST')mutationRequests.push(r.url());});
  for(const invalid of [
    {...good,caseRef:'real:member'},
    {...good,command:{...cmd,memberRef:'real:patient'}},
    {...good,command:{...cmd,requestId:'not-a-uuid'}},
    {...good,command:{...cmd,unexpectedAuthorization:true}},
    {...good,syntheticOnly:false},
    {...good,contract:'TRN-TECH-075'},
    {...good,command:{...cmd,action:'FOLLOW_UP_UNVERIFIED'}},
    {...good,extra:'privileged'},
    { ...good,command:{...cmd,expectedRevision:999} }
  ]){
    await page.getByLabel('متن رسید بازیابی آزمایشی').fill(JSON.stringify(invalid));
    await page.getByRole('button',{name:'بارگذاری رسید بازیابی بدون ارسال'}).click();
    await expect(page.getByTestId('support-status')).toContainText('معتبر نیست');
    await expect(page.getByTestId('pending-support-write')).toHaveCount(0);
  }
  await page.getByLabel('متن رسید بازیابی آزمایشی').fill(JSON.stringify(good));
  await page.getByRole('button',{name:'بارگذاری رسید بازیابی بدون ارسال'}).click();
  await expect(page.getByTestId('pending-support-write')).toBeVisible();
  await expect(page.getByTestId('support-status')).toContainText('هیچ دستوری ارسال نشده');
  expect(mutationRequests).toEqual([]);
  await expect(page.getByRole('button',{name:'ثبت درخواست اولیه آزمایشی'})).toBeDisabled();
});
