import {expect,test} from '@playwright/test';

const host='http://127.0.0.1:4335';
const ref='test:qa-'+Math.random().toString(16).slice(2);
test('AI133-01 real Python HTTP, persisted dataset/model/evaluation and Persian React view',async({page})=>{
 const outside:string[]=[];
 page.on('request',req=>{if(!req.url().startsWith(host+'/'))outside.push(req.url());});
 await page.goto(host+'/operations/ai-lab');
 await expect(page.getByRole('heading',{name:'مدیریت آزمایشی Dataset و نسخه مدل'})).toBeVisible();
 await page.getByRole('textbox',{name:'محدوده تست'}).fill(ref);
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
 await expect(page.getByRole('status')).toContainText('اطلاعات نسخه‌دار');
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
