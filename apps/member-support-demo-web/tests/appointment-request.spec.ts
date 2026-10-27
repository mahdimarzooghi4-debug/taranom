import { expect, test } from '@playwright/test';

function ref(label: string) {
  return 'test:' + label + '-' + crypto.randomUUID().replaceAll('-', '');
}

test('MEMBER-APPOINTMENT-01: exact manual retry records synthetic intent visible to its center', async ({ page, request }) => {
  const memberRef = ref('member');
  const centerRef = ref('center');
  const departmentRef = ref('department');
  const requestedDoctorRef = ref('patient-chosen-doctor');
  const slotRef = ref('requested-slot');
  const postedBodies: string[] = [];

  await page.route('**/internal/demo/v1/appointment-requests/*', async route => {
    postedBodies.push(route.request().postData() ?? '');
    if (postedBodies.length === 1) {
      // The journal records the command, then the browser loses the response.
      // The UI must wait for the user's exact manual retry; no auto retry.
      await route.fetch();
      await route.abort();
      return;
    }
    await route.continue();
  });

  await page.goto('/member-demo/appointment-requests-qa');
  await expect(page.getByRole('heading', { name: 'درخواست نوبت — محیط آزمایشی عضو' })).toBeVisible();
  await expect(page.getByRole('note')).toContainText('رزرو، نسخه یا اقدام بالینی انجام نمی‌شود');
  await page.getByLabel('شناسه عضو آزمایشی').fill(memberRef);
  await page.getByLabel('شناسه مرکز درمانی آزمایشی').fill(centerRef);
  await page.getByLabel('شناسه تخصص یا بخش آزمایشی').fill(departmentRef);
  await page.getByLabel('شناسه پزشک انتخاب‌شده توسط بیمار').fill(requestedDoctorRef);
  await page.getByLabel('شناسه بازه زمانی درخواستی آزمایشی').fill(slotRef);
  await page.getByRole('button', { name: 'ارسال درخواست نوبت آزمایشی' }).click();
  await expect(page.getByRole('status')).toContainText('پاسخی دریافت نشد');
  expect(postedBodies).toHaveLength(1);

  await page.getByRole('button', { name: 'ارسال مجدد دقیق همان درخواست' }).click();
  await expect(page.getByRole('status')).toContainText('درخواست ثبت شد؛ نوبت هنوز تأیید نشده است');
  expect(postedBodies).toHaveLength(2);
  expect(postedBodies[1]).toBe(postedBodies[0]);
  const requestRef = (await page.getByTestId('appointment-request-ref').textContent())?.trim();
  expect(requestRef).toMatch(/^test:appointment-[a-f0-9]{32}$/);

  const response = await request.get(
    '/internal/demo/v1/centers/' + encodeURIComponent(centerRef) + '/appointment-requests?limit=10',
  );
  expect(response.status()).toBe(200);
  const queue = await response.json();
  expect(queue.contract).toBe('TRN-TECH-217');
  expect(queue.requests).toHaveLength(1);
  expect(queue.requests[0]).toMatchObject({
    requestRef,
    centerRef,
    revision: 1,
    departmentRef,
    requestedDoctorRef,
    slotRef,
  });
  for (const flag of [
    'memberAuthenticated', 'centerAuthenticated', 'requestedDoctorLicensed',
    'slotAvailabilityVerified', 'bookingConfirmed', 'clinicalActionAllowed',
    'nationalReleaseAllowed',
  ]) expect(queue[flag]).toBe(false);
});
