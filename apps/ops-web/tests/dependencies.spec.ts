import {expect,test} from '@playwright/test';

test('DEPENDENCIES156-01 real localhost .NET projects 17 domains and 18 unapproved requirements',async({request})=>{
 const r=await request.get('/internal/ops/v1/dependencies');
 expect(r.status()).toBe(200);
 expect(r.headers()['cache-control']).toBe('no-store');
 expect(r.headers()['access-control-allow-origin']).toBeUndefined();
 const x=await r.json();
 expect(x.contract).toBe('TRN-TECH-165');
 expect(x.status).toBe('DRAFT_UNVERIFIED_DEPENDENCY_WORKLIST');
 expect(x.releaseAdmission).toBe('DENIED_BY_DEFAULT');
 expect(x.workItems).toHaveLength(18);
 expect(x.blockedDomains).toBe(17);
 expect(x.candidateCapabilities).toBe(39);
 expect(x.requiredOwnersUnassigned).toBe(18);
 expect(x.sourceFingerprint).toMatch(/^[0-9a-f]{64}$/);
 expect(x.formalApprovalObserved).toBe(false);
 expect(x.productionReady).toBe(false);
 expect(x.nationalReleaseAllowed).toBe(false);
 expect(new Set(x.workItems.flatMap((w:{blockedDomainIds:string[]})=>w.blockedDomainIds)).size).toBe(17);
 expect(x.workItems.every((w:{decisionOwner:string,evidenceStatus:string,formalAcceptanceObserved:boolean})=>
  w.decisionOwner==='NOT_ASSIGNED'&&w.evidenceStatus==='BLOCKED_UNVERIFIED'&&
  w.formalAcceptanceObserved===false)).toBe(true);
 expect((await request.post('/internal/ops/v1/dependencies',{data:{approved:true}})).status()).toBe(405);
 expect((await request.get('/operations/dependencies')).status()).toBe(200);
 expect((await request.post('/operations/dependencies',{data:{productionReady:true}})).status()).toBe(405);
 expect((await request.get('/internal/ops/v1/dependencies/approve')).status()).toBe(404);
});

test('DEPENDENCIES156-02 browser renders all source-backed items and filters by decision or domain',async({page})=>{
 await page.goto('/operations/dependencies');
 await expect(page.getByRole('heading',{name:'صف وابستگی‌های تکمیل محصول ترنم'})).toBeVisible();
 await expect(page.getByTestId('dependencies-status')).toContainText('هیچ موردی تأیید نشده');
 await expect(page.locator('[data-dependency]')).toHaveCount(18);
 await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
 await page.getByLabel('جست‌وجوی مدرک، حوزه یا تصمیم').fill('LEGAL_OPERATOR_IDENTITY');
 await expect(page.locator('[data-dependency]')).toHaveCount(1);
 await page.getByLabel('جست‌وجوی مدرک، حوزه یا تصمیم').fill('');
 await page.getByLabel('حوزه متأثر').selectOption('NATIONAL-COVERAGE');
 await expect(page.locator('[data-dependency]')).toHaveCount(3);
 await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
});

test('DEPENDENCIES156-03 forged approved row or release status cannot populate page',async({page})=>{
 await page.route('**/internal/ops/v1/dependencies',async route=>{
  const response=await route.fetch();const payload=await response.json();
  payload.workItems[0].humanDecisionStatus='APPROVED';
  payload.productionReady=true;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(payload)});
 });
 await page.goto('/operations/dependencies');
 await expect(page.getByTestId('dependencies-status')).toContainText('قابل تأیید نیست');
 await expect(page.locator('[data-dependency]')).toHaveCount(0);
});

test('DEPENDENCIES156-04 after initial valid display, transport loss clears stale items',async({page})=>{
 await page.goto('/operations/dependencies');
 await expect(page.locator('[data-dependency]')).toHaveCount(18);
 await page.route('**/internal/ops/v1/dependencies',route=>route.abort());
 await page.getByRole('button',{name:'بازخوانی وابستگی‌ها'}).click();
 await expect(page.getByTestId('dependencies-status')).toContainText('قابل تأیید نیست');
 await expect(page.locator('[data-dependency]')).toHaveCount(0);
 expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
});

test('DEPENDENCIES156-05 hostile Host, forwarded authority and unknown routes fail closed',async({request})=>{
 for(const headers of [{Host:'attacker.example:4318'},{'X-Forwarded-Host':'attacker.example'}]){
  const r=await request.get('/internal/ops/v1/dependencies',{headers,failOnStatusCode:false});
  expect(r.status()).toBe(403);
  expect((await r.json()).nationalReleaseAllowed).toBe(false);
 }
 expect((await request.get('/operations/dependencies/approval')).status()).toBe(404);
 expect((await request.get('/internal/ops/v1/dependencies/verified')).status()).toBe(404);
});
