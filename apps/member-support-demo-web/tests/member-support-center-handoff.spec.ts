import {expect,test} from '@playwright/test';
test('HANDOFF171-05 export only an accepted source-backed synthetic member case',async({page,request})=>{
 const caseRef='test:member171-ui-'+crypto.randomUUID().replaceAll('-','');
 const memberRef='test:member171-ui';
 await page.goto('/member-demo/');
 await page.locator('#sup-case').fill(caseRef);
 await page.locator('#sup-member').fill(memberRef);
 await expect(page.getByTestId('member-support-handoff')).toHaveCount(0);
 await page.getByRole('button',{name:'بازخوانی پرونده پشتیبانی آزمایشی'}).click();
 await expect(page.getByTestId('support-status')).toContainText('PostgreSQL خوانده شد');
 await page.getByRole('button',{name:'ثبت درخواست اولیه آزمایشی'}).click();
 await expect(page.getByTestId('support-status')).toContainText('فقط ارجاع آزمایشی ثبت شد');
 const ui=page.getByTestId('member-support-handoff');
 await expect(ui).toBeVisible();
 const ticket=JSON.parse(await ui.getByLabel('رسید خروجی تحویل آزمایشی پرونده').inputValue());
 expect(Object.keys(ticket).sort()).toEqual([
  'format','version','caseRef','memberRef','topic','evidenceRef',
  'originalRequestId','revision'].sort());
 expect(ticket.caseRef).toBe(caseRef);
 expect(ticket.memberRef).toBe(memberRef);
 expect(ticket.topic).toBe('MedicalServiceComplaint');
 expect(ticket.revision).toBe(1);
 expect(ticket.format).toBe('TRN-TECH-180-TEST-SUPPORT-HANDOFF');
 const source=await (await request.get('/internal/demo/v1/support/'+encodeURIComponent(caseRef))).json();
 expect(source.originalRequestId).toBe(ticket.originalRequestId);
 expect(source.evidenceRef).toBe(ticket.evidenceRef);
 expect(source.nationalReleaseAllowed).toBe(false);
 expect(source.memberAuthenticated).toBe(false);
 // Changing the claimed member without re-reading source cannot retain a ticket.
 await page.locator('#sup-member').fill('test:altered-member');
 await expect(ui).toHaveCount(0);
});
