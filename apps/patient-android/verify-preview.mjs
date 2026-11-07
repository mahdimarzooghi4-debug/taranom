// Validate the bundled APK bytes and web routes. This does not emulate Android WebView.
import {chromium} from '../member-web/node_modules/@playwright/test/index.mjs';
import {execFileSync} from 'node:child_process';
import {mkdtemp,readFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join,resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
const root=fileURLToPath(new URL('./',import.meta.url));
const apk=resolve(process.argv[2]||join(root,'build/taranom-patient-android-preview.apk'));
const stage=await mkdtemp(join(tmpdir(),'taranom-apk-'));
execFileSync('python3',['-c',`import zipfile,sys,hashlib,pathlib
apk,root,dist=sys.argv[1:]
with zipfile.ZipFile(apk) as z:
 assert 'classes.dex' in z.namelist()
 for n in z.namelist():
  assert not n.endswith(('.keystore','.jks'))
  if n.startswith('assets/web/'):
   rel=n[len('assets/web/'):];p=pathlib.Path(root)/rel;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(z.read(n))
   assert z.read(n)==(pathlib.Path(dist)/rel).read_bytes(),n
print('APK bundle matches web build; dex present; no signing keys bundled')`,apk,stage,resolve(root,'../member-web/dist')],{stdio:'inherit'});
const browser=await chromium.launch(process.env.TARANOM_ICON_CHROMIUM?{executablePath:process.env.TARANOM_ICON_CHROMIUM}:{});
try{
 const page=await browser.newPage({viewport:{width:390,height:800}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',async route=>{
  const u=new URL(route.request().url());if(u.origin!=='https://appassets.androidplatform.net')return route.abort();
  let path=decodeURIComponent(u.pathname);if(path.includes('..')||path.includes('\\'))return route.abort();
  if(path.startsWith('/api/'))throw Error('Unconfigured preview must not issue private API requests');
  if(path==='/patient-app'||path.startsWith('/patient-app/'))path='/index.html';
  const type=path.endsWith('.js')?'text/javascript':path.endsWith('.css')?'text/css':path.endsWith('.html')?'text/html':path.endsWith('.svg')?'image/svg+xml':path.endsWith('.png')?'image/png':path.endsWith('.woff2')?'font/woff2':'application/manifest+json';
  try{return await route.fulfill({body:await readFile(join(stage,path)),contentType:type,headers:{'Cache-Control':'no-store'}})}catch{return route.fulfill({status:404,body:''})}
 });
 await page.goto('https://appassets.androidplatform.net/patient-app');await page.getByRole('heading',{name:'سلام',exact:true}).waitFor();await page.getByRole('alert').filter({hasText:'نشست ورود معتبر'}).waitFor();
 assert(await page.locator('.appointments-logo').evaluate(i=>i.complete&&i.naturalWidth>0));
 await page.getByRole('navigation',{name:'ناوبری اصلی'}).getByRole('link',{name:/حساب من/}).click();await page.getByRole('heading',{name:'حساب من',exact:true}).waitFor();await page.getByRole('alert').filter({hasText:'نشست ورود'}).waitFor();
 await page.getByRole('link',{name:'پشتیبانی',exact:true}).click();await page.getByRole('heading',{name:'پشتیبانی ترنم',exact:true}).waitFor();
 assert.deepEqual(errors,[]);console.log('Bundled home, logo, account and support render; absent session fails closed; no page errors');
}finally{await browser.close();await rm(stage,{recursive:true,force:true})}
