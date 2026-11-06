// Render the existing brand asset into square application icon slots without cropping it.
import {chromium} from '@playwright/test';
import {readFile,mkdir} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const asset=new URL('../public/assets/appointments/logo.png',import.meta.url);
const output=new URL('../public/assets/install/',import.meta.url);
await mkdir(output,{recursive:true});
const logo=(await readFile(asset)).toString('base64');
const browser=await chromium.launch(process.env.TARANOM_ICON_CHROMIUM?{executablePath:process.env.TARANOM_ICON_CHROMIUM}:{});
try{
 for(const size of [192,512]){
  const page=await browser.newPage({viewport:{width:size,height:size},deviceScaleFactor:1});
  await page.setContent(`<html><head><style>html,body{margin:0;width:100%;height:100%;background:white}body{display:grid;place-items:center}img{display:block;width:80%;height:80%;object-fit:contain}</style></head><body><img alt="" src="data:image/png;base64,${logo}"></body></html>`);
  await page.locator('img').evaluate(image=>image.decode());
  await page.screenshot({path:fileURLToPath(new URL(`icon-${size}.png`,output))});await page.close();
 }
}finally{await browser.close()}
