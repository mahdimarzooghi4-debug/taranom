import {expect,test} from '@playwright/test';

function fake(label:string){return 'test:'+label+'-'+crypto.randomUUID().replaceAll('-','');}
async function fresh(page:import('@playwright/test').Page){
  const caseRef=fake('unknown-ack'),memberRef=fake('member'),evidence=fake('evidence'),submitter=fake('submitter');
  await page.goto('/member-demo/');
  await page.getByLabel('پرونده آزمایشی').fill(caseRef);
  await page.getByLabel('شناسه عضو فرضی').fill(memberRef);
  await page.getByLabel('ارجاع مدرک تأییدنشده').fill(evidence);
  await page.getByLabel('ثبت‌کننده ادعایی').fill(submitter);
  await page.getByRole('button',{name:'بازخوانی پرونده پشتیبانی آزمایشی'}).click();
  await expect(page.getByTestId('support-status')).toContainText('PostgreSQL خوانده شد');
  return {caseRef};
}

test('ACK-82-01 committed submission with lost reply must retry SAME requestId, exactly once',async({page,request})=>{
  const {caseRef}=await fresh(page);
  const bodies:unknown[]=[];
  let failedOnce=false;
  await page.route('**/internal/demo/v1/support/*/commands',async route=>{
    const body=route.request().postDataJSON();bodies.push(body);
    if(!failedOnce){
      failedOnce=true;
      const actual=await route.fetch();
      expect(actual.status()).toBe(200);
      expect((await actual.json()).outcome).toBe('RecordedUnverified');
      await route.abort();
    }else await route.continue();
  });
  await page.getByRole('button',{name:'ثبت درخواست اولیه آزمایشی'}).click();
  await expect(page.getByTestId('support-status')).toContainText('نتیجه شبکه نامطمئن');
  await expect(page.getByTestId('pending-support-write')).toBeVisible();
  await expect(page.getByRole('button',{name:'ثبت درخواست اولیه آزمایشی'})).toBeDisabled();
  await expect(page.getByRole('button',{name:'ثبت ارجاع پیگیری آزمایشی'})).toBeDisabled();
  await expect(page.getByLabel('پرونده آزمایشی')).toBeDisabled();
  const before=await (await request.get('/internal/demo/v1/support/'+encodeURIComponent(caseRef))).json();
  expect(before.revision).toBe(1);
  await page.getByRole('button',{name:'تکرار امن همان دستور آزمایشی'}).click();
  await expect(page.getByTestId('support-status')).toContainText('تکرار امن همان دستور');
  await expect(page.getByTestId('pending-support-write')).toHaveCount(0);
  await expect(page.getByRole('region',{name:'وضعیت پرونده پشتیبانی آزمایشی'})).toContainText('نسخه: 1');
  expect(bodies).toHaveLength(2);
  expect(bodies[0]).toEqual(bodies[1]);
  const after=await (await request.get('/internal/demo/v1/support/'+encodeURIComponent(caseRef))).json();
  expect(after.revision).toBe(1);
  expect(after.followUpCount).toBe(0);
  expect(after.complaintLegallySubmitted).toBe(false);
  expect(after.memberAuthenticated).toBe(false);
});

test('ACK-82-02 uncertain followup never duplicates event and preserves note lineage',async({page,request})=>{
  const {caseRef}=await fresh(page);
  await page.getByRole('button',{name:'ثبت درخواست اولیه آزمایشی'}).click();
  await expect(page.getByTestId('support-status')).toContainText('فقط ارجاع آزمایشی ثبت شد');
  const note=fake('noteref');
  await page.getByLabel('ارجاع یادداشت پیگیری تأییدنشده').fill(note);
  const writes:unknown[]=[];
  let intercepted=false;
  await page.route('**/internal/demo/v1/support/*/commands',async route=>{
    const command=route.request().postDataJSON();writes.push(command);
    if(!intercepted){
      intercepted=true;
      const ack=await route.fetch();
      expect(ack.status()).toBe(200);
      expect((await ack.json()).revision).toBe(2);
      await route.abort();
    }else await route.continue();
  });
  await page.getByRole('button',{name:'ثبت ارجاع پیگیری آزمایشی'}).click();
  await expect(page.getByTestId('pending-support-write')).toBeVisible();
  await page.getByRole('button',{name:'تکرار امن همان دستور آزمایشی'}).click();
  await expect(page.getByTestId('support-status')).toContainText('تکرار امن');
  const current=page.getByRole('region',{name:'وضعیت پرونده پشتیبانی آزمایشی'});
  await expect(current).toContainText('نسخه: 2');
  await expect(current).toContainText(note);
  expect(writes[0]).toEqual(writes[1]);
  const actual=await (await request.get('/internal/demo/v1/support/'+encodeURIComponent(caseRef))).json();
  expect(actual.revision).toBe(2);
  expect(actual.followUpCount).toBe(1);
  expect(actual.insurerContacted).toBe(false);
  expect(actual.clinicalReviewCompleted).toBe(false);
});

test('ACK-82-03 forged success acknowledgement cannot unlock next command',async({page})=>{
  await fresh(page);
  let first=true;const writes:unknown[]=[];
  await page.route('**/internal/demo/v1/support/*/commands',async route=>{
    writes.push(route.request().postDataJSON());
    if(first){
      first=false;
      await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
        contract:'TRN-TECH-075',syntheticOnly:true,memberAuthenticated:false,
        complaintLegallySubmitted:false,insurerContacted:false,
        clinicalReviewCompleted:false,nationalReleaseAllowed:false,
        caseRef:route.request().url().split('/').at(-2),revision:0,
        outcome:'RecordedUnverified',topic:null,memberRef:null,
        originalRequestId:null,evidenceRef:null,claimedSubmitterRef:null,
        followUpCount:0,followUps:[]
      })});
    }else await route.continue();
  });
  await page.getByRole('button',{name:'ثبت درخواست اولیه آزمایشی'}).click();
  await expect(page.getByTestId('pending-support-write')).toBeVisible();
  await expect(page.getByRole('button',{name:'ثبت درخواست اولیه آزمایشی'})).toBeDisabled();
  await page.getByRole('button',{name:'تکرار امن همان دستور آزمایشی'}).click();
  await expect(page.getByTestId('support-status')).toContainText('فقط ارجاع آزمایشی ثبت شد');
  await expect(page.getByTestId('pending-support-write')).toHaveCount(0);
  expect(writes).toHaveLength(2);
  expect(writes[0]).toEqual(writes[1]);
});
