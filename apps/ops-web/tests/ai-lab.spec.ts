import {expect,test} from '@playwright/test';

const host='http://127.0.0.1:4335';
const ref='test:qa-'+Math.random().toString(16).slice(2);
test('AI133-01 real Python HTTP, persisted dataset/model/evaluation and Persian React view',async({page})=>{
 const outside:string[]=[];
 page.on('request',req=>{if(!req.url().startsWith(host+'/'))outside.push(req.url());});
 await page.goto(host+'/operations/ai-lab');
 await expect(page.getByRole('heading',{name:'مدیریت آزمایشی Dataset و نسخه مدل'})).toBeVisible();
 await page.getByRole('textbox',{name:'محدوده تست'}).fill(ref);
 await page.getByRole('button',{name:'بازخوانی رجیستری'}).click();
 const inventory=page.getByTestId('ai-qa-inventory');
 await expect(inventory).toContainText('Dataset: 0');
 await page.getByRole('button',{name:'ثبت Dataset ساختگی'}).click();
 await expect(inventory).toContainText('Dataset: 1');
 await page.getByRole('button',{name:'ثبت نسخه مدل ساختگی'}).click();
 await expect(inventory).toContainText('مدل: 1');
 await page.getByRole('button',{name:'ارزیابی مستقل ساختگی'}).click();
 await expect(inventory).toContainText('ارزیابی: 1');
 await expect(inventory).toContainText('MAE 0.5 / MSE 0.25');
 await expect(inventory).toContainText('مدل عملیاتی: هیچ');
 await page.reload();
 await page.getByRole('textbox',{name:'محدوده تست'}).fill(ref);
 await page.getByRole('button',{name:'بازخوانی رجیستری'}).click();
 await expect(page.getByTestId('ai-qa-inventory')).toContainText('ارزیابی: 1');
 expect(outside).toEqual([]);
});
test('AI133-02 forged authorization envelope, hostile header and production method denied',async({page,request})=>{
 const denied=await request.post(host+'/internal/ai-test/v1/activate',{data:{enabled:true}});
 expect(denied.status()).toBe(404);
 const hint=await request.get(host+'/internal/ai-test/v1/inventory?tenant=test:other',
     {headers:{Authorization:'Bearer fake'}});
 expect(hint.status()).toBe(403);
 await page.route('**/internal/ai-test/v1/inventory?*',route=>route.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
    contract:'TRN-TECH-142',outcome:'INVENTORY_READ',syntheticOnly:true,
    adminAuthenticated:true,operatorAuthorized:true,trainingAllowed:true,
    inferenceSupported:true,stageAllowed:true,productionAllowed:true,
    data:{tenant:'test:qa-tenant',datasets:[],model_versions:[],evaluations:[],
     stage_admission:'READY',training_allowed:true,production_allowed:true,
     real_data_eligible:true}
  })
 }));
 await page.goto(host+'/operations/ai-lab');
 await expect(page.getByRole('status')).toContainText('داده نامعتبر');
 await expect(page.getByTestId('ai-qa-inventory')).toHaveCount(0);
 await expect(page.getByRole('note')).toContainText('DENIED');
});


test('AI135-01 browser Shadow fit and independent benchmark persist without promotion',async({page,request})=>{
 const tenant='test:shadow-'+Math.random().toString(16).slice(2);
 const outside:string[]=[];
 page.on('request',req=>{if(!req.url().startsWith(host+'/'))outside.push(req.url());});
 await page.goto(host+'/operations/ai-lab');
 await expect(page.getByRole('heading',{name:'مدیریت آزمایشی Dataset و نسخه مدل'})).toBeVisible();
 await page.getByRole('textbox',{name:'محدوده تست'}).fill(tenant);
 await page.getByRole('button',{name:'بازخوانی رجیستری'}).click();
 const inventory=page.getByTestId('ai-qa-inventory');
 await expect(inventory).toContainText('Dataset: 0');
 await page.getByRole('button',{name:'ثبت Dataset ساختگی'}).click();
 await expect(inventory).toContainText('Dataset: 1');
 await page.getByRole('button',{name:'آموزش Shadow ساختگی'}).click();
 await expect(inventory).toContainText('Shadow: 1');
 await expect(inventory).toContainText('SYNTHETIC_SHADOW_ONLY');
 await page.getByRole('button',{name:'مقایسه مستقل Shadow'}).click();
 await expect(inventory).toContainText('Benchmark: 1');
 await expect(inventory).toContainText('Baseline MSE 9 / Shadow MSE 0');
 await expect(inventory).toContainText('انتخاب برنده: خیر');
 await expect(inventory).toContainText('مدل عملیاتی: هیچ');
 const response=await request.get(host+'/internal/ai-test/v1/inventory?tenant='+encodeURIComponent(tenant));
 expect(response.status()).toBe(200);
 const payload=await response.json();
 expect(payload.productionAllowed).toBe(false);
 expect(payload.trainingAllowed).toBe(false);
 expect(payload.adminAuthenticated).toBe(false);
 expect(payload.data.training_runs).toHaveLength(1);
 expect(payload.data.shadow_benchmarks).toHaveLength(1);
 expect(payload.data.training_runs[0].candidate_approved).toBe(false);
 expect(payload.data.shadow_benchmarks[0].winner_selected).toBe(false);
 await page.reload();
 await page.getByRole('textbox',{name:'محدوده تست'}).fill(tenant);
 await page.getByRole('button',{name:'بازخوانی رجیستری'}).click();
 await expect(page.getByTestId('ai-qa-inventory')).toContainText('Benchmark: 1');
 expect(outside).toEqual([]);
});
test('AI135-02 forged approved Shadow result and production route denied',async({page,request})=>{
 const rejected=await request.post(host+'/internal/ai-test/v1/models/promote',{data:{productionAllowed:true}});
 expect(rejected.status()).toBe(404);
 await page.route('**/internal/ai-test/v1/inventory?*',route=>route.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   contract:'TRN-TECH-142',outcome:'INVENTORY_READ',syntheticOnly:true,
   adminAuthenticated:false,operatorAuthorized:false,trainingAllowed:false,
   inferenceSupported:false,stageAllowed:false,productionAllowed:false,
   data:{tenant:'test:qa-tenant',datasets:[],model_versions:[],evaluations:[],
    training_runs:[{training_ref:'test:forged',status:'SYNTHETIC_SHADOW_ONLY',
      artifact_sha256:'A'.repeat(64),candidate_approved:true}],
    shadow_benchmarks:[{benchmark_ref:'test:forged-b',status:'SYNTHETIC_DIAGNOSTICS_ONLY',
      winner_selected:true,baseline_mse:'9',shadow_mse:'0',baseline_mae:'3',shadow_mae:'0'}],
    stage_admission:'DENIED',production_allowed:false,training_allowed:false,real_data_eligible:false}
  })
 }));
 await page.goto(host+'/operations/ai-lab');
 await expect(page.getByRole('status')).toContainText('داده نامعتبر');
 await expect(page.getByTestId('ai-qa-inventory')).toHaveCount(0);
});
