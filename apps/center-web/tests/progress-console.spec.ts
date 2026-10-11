import { expect, test } from '@playwright/test';
import { resolve } from 'node:path';
import { readdirSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

// Read-only evidence dashboard, generated offline from the public DRAFT
// capability registry; no member data, credentials, provider claims or API.
// Require precisely one generated public DRAFT console; future Sprint names
// cannot silently point to an old artifact or an absent file.
const results = resolve(process.cwd(), 'TestResults');
const candidates = readdirSync(results).filter(name => /^sprint\d+-epic-console\.html$/.test(name));
if (candidates.length !== 1) throw new Error('AMBIGUOUS_OR_MISSING_EPIC_CONSOLE');
const dashboard = pathToFileURL(resolve(results, candidates[0])).href;

test('PROGRESS-01: local dashboard displays full draft scope and DENIED admission', async ({ page }) => {
  await page.goto(dashboard);
  await expect(page.locator('html')).toHaveAttribute('lang', 'fa');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByRole('heading', { level: 1, name: 'کنسول آفلاین شواهد پیشرفت ترنم' })).toBeVisible();
  await expect(page.locator('section[data-epic]')).toHaveCount(13);
  await expect(page.locator('[data-capability]')).toHaveCount(39);
  await expect(page.getByText('DENIED_BY_DEFAULT').first()).toBeVisible();
  await expect(page.locator('footer')).toContainText('formal_acceptance_pct: null');
});

test('PROGRESS-02: search and evidence classes really filter without approval', async ({ page }) => {
  await page.goto(dashboard);
  const search = page.getByRole('searchbox', { name: 'جست‌وجو در Epic و قابلیت' });
  await search.fill('TRN-CAP-01-02');
  await expect(page.locator('section[data-epic]:visible')).toHaveCount(1);
  await expect(page.locator('[data-capability]:visible')).toHaveCount(1);
  await expect(page.getByRole('status')).toContainText('1 Epic');
  await search.fill('');
  await page.getByLabel('نوع ارجاع فنی').selectOption('NO_EVIDENCE');
  await expect(page.locator('[data-capability]:visible')).toHaveCount(13);
  await expect(page.getByRole('status')).toContainText('13 قابلیت پیشنهادی');
  await expect(page.getByRole('note').first()).toContainText('قابل‌محاسبه نیست');
});

test('PROGRESS-03: offline navigation has no network and retains all market obligations', async ({ page }) => {
  const connections: string[] = [];
  page.on('request', req => {
    if (req.url().startsWith('http')) connections.push(req.url());
  });
  await page.goto(dashboard);
  for (const id of ['MARKET-PUBLIC', 'MARKET-SUPPORT', 'MARKET-CORPORATE', 'NATIONAL-COVERAGE']) {
    await expect(page.locator('.dimensions')).toContainText(id);
  }
  await page.locator('section[data-epic] summary').first().click();
  await expect(page.locator('section[data-epic] [data-capability]').first()).toBeVisible();
  expect(connections).toEqual([]);
});
