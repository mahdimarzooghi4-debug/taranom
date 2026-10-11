import { expect, test } from '@playwright/test';
function unique(prefix: string) { return 'test:' + prefix + '-' + crypto.randomUUID().replaceAll('-', ''); }
async function newSupport(page: import('@playwright/test').Page) {
  const caseRef = unique('support-case'), memberRef = unique('member');
  const evidenceRef = unique('evidence'), claimedSubmitterRef = unique('submitter');
  await page.goto('/member-demo/');
  await page.getByLabel('پرونده آزمایشی').fill(caseRef);
  await page.getByLabel('شناسه عضو فرضی').fill(memberRef);
  await page.getByLabel('ارجاع مدرک تأییدنشده').fill(evidenceRef);
  await page.getByLabel('ثبت‌کننده ادعایی').fill(claimedSubmitterRef);
  await page.getByRole('button', { name: 'بازخوانی پرونده پشتیبانی آزمایشی' }).click();
  await expect(page.getByTestId('support-status')).toContainText('PostgreSQL خوانده شد');
  return { caseRef, memberRef, evidenceRef, claimedSubmitterRef };
}
test('SUPPORT-01: real PostgreSQL browser submission, follow-up and reread', async ({ page }) => {
  const keys = await newSupport(page);
  await page.getByRole('button', { name: 'ثبت درخواست اولیه آزمایشی' }).click();
  await expect(page.getByTestId('support-status')).toContainText('فقط ارجاع آزمایشی ثبت شد');
  const current = page.getByRole('region', { name: 'وضعیت پرونده پشتیبانی آزمایشی' });
  await expect(current).toContainText('نسخه: 1');
  await expect(current).toContainText('نارضایتی از خدمت پزشکی');
  const note = unique('note');
  await page.getByLabel('ارجاع یادداشت پیگیری تأییدنشده').fill(note);
  await page.getByRole('button', { name: 'ثبت ارجاع پیگیری آزمایشی' }).click();
  await expect(current).toContainText('نسخه: 2');
  await expect(current).toContainText(note);
  await expect(current).toContainText('شکایت رسمی: خیر');
  await page.getByRole('button', { name: 'بازخوانی پرونده پشتیبانی آزمایشی' }).click();
  await expect(current).toContainText('نسخه: 2');
  await expect(current).toContainText('پیگیری‌های تأییدنشده: 1');
  await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
  expect(keys.caseRef).toContain('test:');
});
test('SUPPORT-02: read-only missing synthetic case and disallow real identifiers', async ({ request }) => {
  const base = '/internal/demo/v1/support/';
  const empty = await request.get(base + encodeURIComponent(unique('empty')));
  expect(empty.status()).toBe(200);
  const data = await empty.json();
  expect(data.revision).toBe(0);
  expect(data.followUpCount).toBe(0);
  expect(data.syntheticOnly).toBe(true);
  expect(data.complaintLegallySubmitted).toBe(false);
  expect(data.insurerContacted).toBe(false);
  expect((await request.get(base + 'real:complaint')).status()).toBe(400);
  expect((await request.get(base + encodeURIComponent(unique('case')) + '?approved=true')).status()).toBe(400);
});
test('SUPPORT-03: original topic immutable, conflicts and replay preserve revision', async ({ request }) => {
  const caseRef = unique('scope'), url = '/internal/demo/v1/support/' + encodeURIComponent(caseRef);
  const payload = {
    requestId: crypto.randomUUID(), expectedRevision: 0, action: 'SUBMIT_UNVERIFIED',
    memberRef: unique('member'), topic: 'INSURANCE_COVERAGE_DISPUTE',
    unverifiedEvidenceRef: unique('evidence'), claimedSubmitterRef: unique('submitter'),
    originalSubmissionRequestId: '00000000-0000-0000-0000-000000000000',
    claimedFollowUpActorRef: null, unverifiedFollowUpNoteRef: null
  };
  const first = await request.post(url + '/commands', { data: payload });
  expect(first.status()).toBe(200);
  expect((await first.json()).revision).toBe(1);
  const replay = await request.post(url + '/commands', { data: payload });
  expect(replay.status()).toBe(200);
  expect((await replay.json()).outcome).toBe('Replayed');
  expect((await request.post(url + '/commands', { data: {
    ...payload, memberRef: unique('other')
  } })).status()).toBe(409);
  expect((await request.post(url + '/commands', { data: {
    ...payload, requestId: crypto.randomUUID()
  } })).status()).toBe(409);
  const follow = { ...payload, requestId: crypto.randomUUID(), expectedRevision: 1,
    action: 'FOLLOW_UP_UNVERIFIED', originalSubmissionRequestId: payload.requestId,
    claimedFollowUpActorRef: unique('actor'), unverifiedFollowUpNoteRef: unique('note') };
  expect((await request.post(url + '/commands', { data: follow })).status()).toBe(200);
  const illegal = await request.post(url + '/commands', { data: {
    ...follow, requestId: crypto.randomUUID(), expectedRevision: 2,
    topic: 'MEDICAL_SERVICE_COMPLAINT'
  }});
  expect(illegal.status()).toBe(409);
  const end = await (await request.get(url)).json();
  expect(end.revision).toBe(2);
  expect(end.followUpCount).toBe(1);
  expect(end.topic).toBe('InsuranceCoverageDispute');
  expect(end.insurerContacted).toBe(false);
});
test('SUPPORT-04: extra fields, wrong origin and free-text are not accepted', async ({ request }) => {
  const path = '/internal/demo/v1/support/' + encodeURIComponent(unique('denied')) + '/commands';
  const standard = {
    requestId: crypto.randomUUID(), expectedRevision: 0, action: 'SUBMIT_UNVERIFIED',
    memberRef: unique('member'), topic: 'MEDICAL_SERVICE_COMPLAINT',
    unverifiedEvidenceRef: unique('evidence'), claimedSubmitterRef: unique('submitter'),
    originalSubmissionRequestId: '00000000-0000-0000-0000-000000000000',
    claimedFollowUpActorRef: null, unverifiedFollowUpNoteRef: null
  };
  const extra = await request.post(path, { data: {...standard, insuranceApproved: true} });
  expect(extra.status()).toBe(400);
  const text = await request.post(path, { data: {...standard, unverifiedEvidenceRef: 'patient name and actual diagnosis'} });
  expect(text.status()).toBe(400);
  const origin = await request.post(path, { data: standard, headers: {Origin: 'https://not-trusted.example'} });
  expect(origin.status()).toBe(403);
  expect((await origin.json()).memberAuthenticated).toBe(false);
  expect((await (await request.get(path.replace('/commands',''))).json()).revision).toBe(0);
});
test('SUPPORT-05: backend network failure clears stale case and disables commands', async ({ page }) => {
  await newSupport(page);
  await page.getByRole('button', { name: 'ثبت درخواست اولیه آزمایشی' }).click();
  await expect(page.getByRole('button', { name: 'ثبت ارجاع پیگیری آزمایشی' })).toBeEnabled();
  await page.route('**/internal/demo/v1/support/**', route => route.abort());
  await page.getByRole('button', { name: 'بازخوانی پرونده پشتیبانی آزمایشی' }).click();
  await expect(page.getByTestId('support-status')).toContainText('هیچ وضعیت قبلی معتبر فرض نمی‌شود');
  await expect(page.getByRole('button', { name: 'ثبت ارجاع پیگیری آزمایشی' })).toBeDisabled();
  await expect(page.getByRole('region', { name: 'وضعیت پرونده پشتیبانی آزمایشی' })).toHaveCount(0);
});


test('SURFACE-01: independent local member and center pages never include the other workstream controls', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'گردش بخش و وابستگی پزشک در پایگاه‌داده آزمایشی' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'ثبت درخواست اولیه آزمایشی' })).toHaveCount(0);
  await page.goto('/member-demo/');
  await expect(page.getByRole('heading', { name: 'درخواست و پیگیری پشتیبانی عضو — فقط شناسه‌های ساختگی' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'ثبت درخواست اولیه آزمایشی' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'ثبت بخش آزمایشی' })).toHaveCount(0);
  await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
});

test('SURFACE-02: separate member bundle is same-origin and test-only, without external requests', async ({ page }) => {
  const outside: string[] = [];
  page.on('request', request => {
    if (new URL(request.url()).origin !== 'http://127.0.0.1:4320')
      outside.push(request.url());
  });
  await page.goto('/member-demo/');
  await page.getByRole('button', { name: 'بازخوانی پرونده پشتیبانی آزمایشی' }).click();
  await expect(page.getByTestId('support-status')).toContainText('PostgreSQL خوانده شد');
  expect(outside).toEqual([]);
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByRole('note')).toContainText('هیچ اطلاعات شخص حقیقی وارد نکنید');
});


test('GATEWAY-01: unknown production-looking URLs never fallback to the center or member entrypoint', async ({ request }) => {
  for (const path of ['/api/v1/members', '/login', '/member', '/member-demo/unknown',
                       '/internal/demo/v1/clinical/patient', '/center/administrator']) {
    const res = await request.get(path, { failOnStatusCode: false });
    expect(res.status(), path).toBe(404);
    const html = await res.text();
    expect(html).not.toContain('گردش آزمایشی مرکز');
    expect(html).not.toContain('پنل آزمایشی پشتیبانی عضو');
  }
  const center = await request.get('/');
  const member = await request.get('/member-demo/');
  expect(center.status()).toBe(200);
  expect(member.status()).toBe(200);
  expect(await center.text()).toContain('گردش آزمایشی مرکز');
  expect(await member.text()).toContain('پنل آزمایشی پشتیبانی عضو');
});

test('GATEWAY-02: hostile Host cannot read even public synthetic support or built member HTML', async ({ request }) => {
  for (const path of ['/member-demo/', '/internal/demo/v1/support/test:evil-read',
                       '/internal/demo/v1/centers/test:evil-read']) {
    const res = await request.get(path, {
      headers: { Host: 'malicious.example:4320' }, failOnStatusCode: false
    });
    expect(res.status(), path).toBe(403);
    const denial = await res.json();
    expect(denial.outcome).toBe('UNTRUSTED_LOOPBACK_AUTHORITY');
    expect(denial.memberAuthenticated).toBe(false);
    expect(denial.managerAuthenticated).toBe(false);
    expect(denial.nationalReleaseAllowed).toBe(false);
  }
});

test('GATEWAY-03: forwarded authority is refused rather than implicitly trusted', async ({ request }) => {
  for (const headers of [
    { 'X-Forwarded-Host': 'malicious.example' },
    { 'X-Forwarded-Proto': 'https' },
    { 'Forwarded': 'host=malicious.example' }
  ]) {
    const res = await request.get('/member-demo/', { headers, failOnStatusCode: false });
    expect(res.status()).toBe(403);
    expect((await res.json()).nationalReleaseAllowed).toBe(false);
  }
});
