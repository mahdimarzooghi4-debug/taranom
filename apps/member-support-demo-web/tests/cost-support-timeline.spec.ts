import {expect,test} from '@playwright/test';

async function setup(page:import('@playwright/test').Page){
  await page.goto('/member-demo/costs');
  const preview=await page.getByLabel('UUID پیش‌برآورد').inputValue();
  await page.getByRole('button',{name:'ثبت دستی پیش‌برآورد در PostgreSQL'}).click();
  const handoff=page.getByTestId('cost-support-handoff');
  const caseRef='test:timeline-'+Date.now();
  await handoff.getByLabel('پرونده اختلاف بیمه').fill(caseRef);
  await handoff.getByRole('button',{name:'ثبت دستی اعتراض آزمایشی در Journal پشتیبانی'}).click();
  await expect(handoff.getByTestId('cost-support-message')).toContainText('فقط درخواست ساختگی');
  await handoff.getByRole('button',{name:'راستی‌آزمایی مستقل رابطه دو Journal'}).click();
  await expect(handoff.getByTestId('cost-support-linked-result')).toContainText('نسخه اعتراض: 1');
  return {preview,caseRef,handoff,timeline:page.getByTestId('cost-support-timeline')};
}

test('COST-91-01 explicit read-only linked audit history survives reload',async({page})=>{
  const reads:string[]=[];
  const writes:string[]=[];
  page.on('request',r=>{
    if(r.url().includes('/timeline?'))reads.push(r.method());
    if(r.method()==='POST'&&r.url().includes('/support/'))writes.push(r.url());
  });
  const {preview,caseRef,handoff,timeline}=await setup(page);
  await expect(timeline).toBeVisible();
  expect(reads).toEqual([]);
  expect(writes).toHaveLength(1);
  await timeline.getByRole('button',{name:'خواندن دستی صفحه اول اعتراض'}).click();
  await expect(timeline.getByTestId('cost-support-timeline-events').locator('li')).toHaveCount(1);
  await expect(timeline.getByTestId('cost-support-timeline-events')).toContainText('SUBMIT_UNVERIFIED');
  expect(writes).toHaveLength(1);
  await page.reload();
  await page.getByLabel('UUID پیش‌برآورد').fill(preview);
  await page.getByRole('button',{name:'بازیابی با UUID پس از Reload'}).click();
  await handoff.getByLabel('پرونده اختلاف بیمه').fill(caseRef);
  await handoff.getByRole('button',{name:'راستی‌آزمایی مستقل رابطه دو Journal'}).click();
  await timeline.getByRole('button',{name:'خواندن دستی صفحه اول اعتراض'}).click();
  await expect(timeline.getByTestId('cost-support-timeline-events').locator('li')).toHaveCount(1);
  expect(reads).toEqual(['GET','GET']);
  expect(writes).toHaveLength(1);
  await expect(page.getByText(/در فوریت پزشکی، کمک فوری/)).toBeVisible();
});

test('COST-91-02 pagination is pinned to independent cost and support PG revisions',async({page,request})=>{
  const {preview,caseRef,handoff,timeline}=await setup(page);
  const state=await (await request.get('/internal/demo/v1/support/'+caseRef)).json() as {
    memberRef:string;evidenceRef:string;claimedSubmitterRef:string;
    originalRequestId:string;revision:number
  };
  const zero='00000000-0000-0000-0000-000000000000';
  const add=async(revision:number)=>{
    const command={
      requestId:crypto.randomUUID(),expectedRevision:revision,
      action:'FOLLOW_UP_UNVERIFIED',memberRef:state.memberRef,
      topic:'INSURANCE_COVERAGE_DISPUTE',
      unverifiedEvidenceRef:state.evidenceRef,
      claimedSubmitterRef:state.claimedSubmitterRef,
      originalSubmissionRequestId:state.originalRequestId??zero,
      claimedFollowUpActorRef:'test:timeline-e2e-actor',
      unverifiedFollowUpNoteRef:'test:timeline-note-'+revision
    };
    const res=await request.post('/internal/demo/v1/support/'+caseRef+'/commands',{data:command});
    expect(res.status()).toBe(200);
  };
  for(let rev=1;rev<=4;rev++)await add(rev);
  await handoff.getByRole('button',{name:'راستی‌آزمایی مستقل رابطه دو Journal'}).click();
  await expect(timeline).toBeVisible();
  await timeline.getByRole('button',{name:'خواندن دستی صفحه اول اعتراض'}).click();
  await expect(timeline.getByTestId('cost-support-timeline-events').locator('li')).toHaveCount(3);
  await timeline.getByRole('button',{name:'صفحه بعد با همان دو نسخه'}).click();
  await expect(timeline.getByTestId('cost-support-timeline-events').locator('li')).toHaveCount(5);
  await timeline.getByRole('button',{name:'خواندن دستی صفحه اول اعتراض'}).click();
  await expect(timeline.getByTestId('cost-support-timeline-events').locator('li')).toHaveCount(3);
  await add(5);
  await timeline.getByRole('button',{name:'صفحه بعد با همان دو نسخه'}).click();
  await expect(timeline.getByTestId('cost-support-timeline-events').locator('li')).toHaveCount(0);
  await expect(timeline.getByTestId('cost-support-timeline-message'))
    .toContainText('نسخه یا مرجع تغییر کرده است');
  await handoff.getByRole('button',{name:'راستی‌آزمایی مستقل رابطه دو Journal'}).click();
  await expect(handoff.getByTestId('cost-support-linked-result')).toContainText('نسخه اعتراض: 6');
  await expect(page.getByText(/در فوریت پزشکی، کمک فوری/)).toBeVisible();
});
