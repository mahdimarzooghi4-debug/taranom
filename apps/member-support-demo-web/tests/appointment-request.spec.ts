import { expect, test } from '@playwright/test';

function ref(label: string) {
  return 'test:' + label + '-' + crypto.randomUUID().replaceAll('-', '');
}

test('MEMBER-APPOINTMENT-02: patient selects a doctor assigned to the chosen center department', async ({ page, request }) => {
  const memberRef = ref('member');
  const centerRef = ref('center');
  const departmentRef = ref('department');
  const requestedDoctorRef = ref('patient-selected-doctor');
  const slotRef = ref('requested-slot');

  const centerCommand = async (
    expectedVersion: number,
    kind: string,
    department: string | null,
    doctor: string | null,
  ) => {
    const response = await request.post(
      '/internal/demo/v1/centers/' + encodeURIComponent(centerRef) + '/commands',
      { data: {
        requestId: crypto.randomUUID(),
        expectedVersion,
        kind,
        departmentRef: department,
        doctorRef: doctor,
      } },
    );
    expect(response.status()).toBe(200);
  };
  await centerCommand(0, 'DEFINE_DEPARTMENT', departmentRef, null);
  await centerCommand(1, 'RECORD_DOCTOR', null, requestedDoctorRef);
  await centerCommand(2, 'ASSIGN_DOCTOR', departmentRef, requestedDoctorRef);

  const postedBodies: string[] = [];
  await page.route('**/internal/demo/v1/appointment-requests/*', async route => {
    postedBodies.push(route.request().postData() ?? '');
    if (postedBodies.length === 1) {
      // The journal records the command, then the browser loses the response.
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
  await page.getByRole('button', { name: 'خواندن تخصص‌های ثبت‌شده مرکز' }).click();
  await expect(page.getByRole('status')).toContainText('خوانده شد');
  const department = page.getByLabel('تخصص یا بخش');
  await expect(department.locator('option')).toHaveCount(2);
  await department.selectOption(departmentRef);
  await page.getByRole('button', { name: 'خواندن پزشکان این تخصص در همان نسخه' }).click();
  await expect(page.getByRole('status')).toContainText('پزشکان ثبت‌شده این تخصص بارگذاری شدند');
  const doctor = page.getByLabel('پزشک انتخاب‌شده توسط بیمار');
  await expect(doctor.locator('option')).toHaveCount(2);
  await doctor.selectOption(requestedDoctorRef);
  await page.getByLabel('شناسه بازه زمانی درخواستی آزمایشی').fill(slotRef);
  await page.getByRole('button', { name: 'ارسال درخواست نوبت آزمایشی' }).click();
  await expect(page.getByRole('status').last()).toContainText('پاسخی دریافت نشد');
  expect(postedBodies).toHaveLength(1);

  await page.getByRole('button', { name: 'ارسال مجدد دقیق همان درخواست' }).click();
  await expect(page.getByRole('status').last()).toContainText('درخواست ثبت شد؛ نوبت هنوز تأیید نشده است');
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
