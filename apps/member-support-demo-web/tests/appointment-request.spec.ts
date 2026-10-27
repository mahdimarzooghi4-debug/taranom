import { expect, test, type APIRequestContext, type Page } from '@playwright/test';

function ref(label: string) {
  return 'test:' + label + '-' + crypto.randomUUID().replaceAll('-', '');
}

async function seedCenter(
  request: APIRequestContext,
  centerRef: string,
  departmentRef: string,
  doctorRef: string,
) {
  const command = async (
    expectedVersion: number,
    kind: string,
    department: string | null,
    doctor: string | null,
  ) => {
    const response = await request.post(
      '/internal/demo/v1/centers/' + encodeURIComponent(centerRef) + '/commands',
      { data: {
        requestId: crypto.randomUUID(), expectedVersion, kind,
        departmentRef: department, doctorRef: doctor,
      } },
    );
    expect(response.status()).toBe(200);
  };
  await command(0, 'DEFINE_DEPARTMENT', departmentRef, null);
  await command(1, 'RECORD_DOCTOR', null, doctorRef);
  await command(2, 'ASSIGN_DOCTOR', departmentRef, doctorRef);
}

async function prepareMemberForm(
  page: Page,
  memberRef: string,
  centerRef: string,
  departmentRef: string,
  doctorRef: string,
  slotRef: string,
) {
  await page.goto('/member-demo/appointment-requests-qa');
  await expect(page.getByRole('heading', { name: 'درخواست نوبت — محیط آزمایشی عضو' })).toBeVisible();
  await expect(page.getByRole('note')).toContainText('رزرو، نسخه یا اقدام بالینی انجام نمی‌شود');
  await page.getByLabel('شناسه عضو آزمایشی').fill(memberRef);
  await page.getByLabel('شناسه مرکز درمانی آزمایشی').fill(centerRef);
  await page.getByRole('button', { name: 'خواندن تخصص‌های ثبت‌شده مرکز' }).click();
  await expect(page.getByRole('status').first()).toContainText('خوانده شد');
  const department = page.getByLabel('تخصص یا بخش');
  await expect(department.locator('option')).toHaveCount(2);
  await department.selectOption(departmentRef);
  await page.getByRole('button', { name: 'خواندن پزشکان این تخصص در همان نسخه' }).click();
  await expect(page.getByRole('status').first()).toContainText('پزشکان ثبت‌شده این تخصص بارگذاری شدند');
  const doctor = page.getByLabel('پزشک انتخاب‌شده توسط بیمار');
  await expect(doctor.locator('option')).toHaveCount(2);
  await doctor.selectOption(doctorRef);
  await page.getByLabel('شناسه بازه زمانی درخواستی آزمایشی').fill(slotRef);
}

async function readCenterQueue(request: APIRequestContext, centerRef: string) {
  const response = await request.get(
    '/internal/demo/v1/centers/' + encodeURIComponent(centerRef) + '/appointment-requests?limit=10',
  );
  expect(response.status()).toBe(200);
  const queue = await response.json();
  expect(queue.contract).toBe('TRN-TECH-217');
  for (const flag of [
    'memberAuthenticated', 'centerAuthenticated', 'requestedDoctorLicensed',
    'slotAvailabilityVerified', 'bookingConfirmed', 'clinicalActionAllowed',
    'nationalReleaseAllowed',
  ]) expect(queue[flag]).toBe(false);
  return queue;
}

test('MEMBER-APPOINTMENT-05: uncertain request survives reload and is reconciled before retry', async ({ page, request }) => {
  const memberRef = ref('member');
  const centerRef = ref('center');
  const departmentRef = ref('department');
  const requestedDoctorRef = ref('patient-selected-doctor');
  const slotRef = ref('requested-slot');
  await seedCenter(request, centerRef, departmentRef, requestedDoctorRef);
  let originalRequestRef = '';
  let postCount = 0;
  await page.route('**/internal/demo/v1/appointment-requests/*', async route => {
    if (route.request().method() !== 'POST') {
      await route.continue();
      return;
    }
    postCount++;
    originalRequestRef = decodeURIComponent(new URL(route.request().url()).pathname.split('/').pop() ?? '');
    await route.fetch();
    await route.abort();
  });

  await prepareMemberForm(page, memberRef, centerRef, departmentRef, requestedDoctorRef, slotRef);
  await page.getByRole('button', { name: 'ارسال درخواست نوبت آزمایشی' }).click();
  await expect(page.getByRole('status').last()).toContainText('پیش از هر ارسال مجدد');
  expect(postCount).toBe(1);
  expect(originalRequestRef).toMatch(/^test:appointment-[a-f0-9]{32}$/);

  await page.reload();
  await expect(page.getByRole('status').last()).toContainText('قبلی بازیابی شد');
  await page.getByRole('button', { name: 'بررسی وضعیت همین درخواست' }).click();
  await expect(page.getByRole('status').last()).toContainText('ثبت همان درخواست با بازخوانی تأیید شد');
  expect(postCount).toBe(1);
  await expect(page.getByTestId('appointment-request-ref')).toHaveText(originalRequestRef);

  const queue = await readCenterQueue(request, centerRef);
  expect(queue.requests).toHaveLength(1);
  expect(queue.requests[0]).toMatchObject({
    requestRef: originalRequestRef, centerRef, revision: 1,
    departmentRef, requestedDoctorRef, slotRef,
  });
});

test('MEMBER-APPOINTMENT-03: lost acknowledgement is reconciled by exact journal readback', async ({ page, request }) => {
  const memberRef = ref('member');
  const centerRef = ref('center');
  const departmentRef = ref('department');
  const requestedDoctorRef = ref('patient-selected-doctor');
  const slotRef = ref('requested-slot');
  await seedCenter(request, centerRef, departmentRef, requestedDoctorRef);
  const postedBodies: string[] = [];
  await page.route('**/internal/demo/v1/appointment-requests/*', async route => {
    if (route.request().method() !== 'POST') {
      await route.continue();
      return;
    }
    postedBodies.push(route.request().postData() ?? '');
    await route.fetch();
    await route.abort();
  });

  await prepareMemberForm(page, memberRef, centerRef, departmentRef, requestedDoctorRef, slotRef);
  await page.getByRole('button', { name: 'ارسال درخواست نوبت آزمایشی' }).click();
  await expect(page.getByRole('status').last()).toContainText('پیش از هر ارسال مجدد');
  expect(postedBodies).toHaveLength(1);

  await page.getByRole('button', { name: 'بررسی وضعیت همین درخواست' }).click();
  await expect(page.getByRole('status').last()).toContainText('ثبت همان درخواست با بازخوانی تأیید شد');
  expect(postedBodies).toHaveLength(1);
  const requestRef = (await page.getByTestId('appointment-request-ref').textContent())?.trim();
  expect(requestRef).toMatch(/^test:appointment-[a-f0-9]{32}$/);

  const queue = await readCenterQueue(request, centerRef);
  expect(queue.requests).toHaveLength(1);
  expect(queue.requests[0]).toMatchObject({
    requestRef, centerRef, revision: 1, departmentRef, requestedDoctorRef, slotRef,
  });
});

test('MEMBER-APPOINTMENT-04: exact retry is enabled only after a verified empty read', async ({ page, request }) => {
  const memberRef = ref('member');
  const centerRef = ref('center');
  const departmentRef = ref('department');
  const requestedDoctorRef = ref('patient-selected-doctor');
  const slotRef = ref('requested-slot');
  await seedCenter(request, centerRef, departmentRef, requestedDoctorRef);
  const postedBodies: string[] = [];
  await page.route('**/internal/demo/v1/appointment-requests/*', async route => {
    if (route.request().method() !== 'POST') {
      await route.continue();
      return;
    }
    postedBodies.push(route.request().postData() ?? '');
    if (postedBodies.length === 1) {
      await route.abort();
      return;
    }
    await route.continue();
  });

  await prepareMemberForm(page, memberRef, centerRef, departmentRef, requestedDoctorRef, slotRef);
  await page.getByRole('button', { name: 'ارسال درخواست نوبت آزمایشی' }).click();
  await expect(page.getByRole('status').last()).toContainText('پیش از هر ارسال مجدد');
  expect(postedBodies).toHaveLength(1);
  await expect(page.getByRole('button', { name: 'ارسال مجدد دقیق همان درخواست' })).toHaveCount(0);

  await page.getByRole('button', { name: 'بررسی وضعیت همین درخواست' }).click();
  await expect(page.getByRole('status').last()).toContainText('این درخواست هنوز ثبت نشده');
  await page.getByRole('button', { name: 'ارسال مجدد دقیق همان درخواست' }).click();
  await expect(page.getByRole('status').last()).toContainText('درخواست ثبت شد؛ نوبت هنوز تأیید نشده است');
  expect(postedBodies).toHaveLength(2);
  expect(postedBodies[1]).toBe(postedBodies[0]);

  const queue = await readCenterQueue(request, centerRef);
  expect(queue.requests).toHaveLength(1);
  expect(queue.requests[0]).toMatchObject({
    centerRef, revision: 1, departmentRef, requestedDoctorRef, slotRef,
  });
});
