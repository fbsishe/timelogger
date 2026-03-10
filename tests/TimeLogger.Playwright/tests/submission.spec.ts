import { test, expect, Page } from '@playwright/test';

async function triggerAndAwaitSync(page: Page) {
  await page.goto('/timelog-sync');
  await page.waitForSelector('text=Timelog Sync', { timeout: 20_000 });

  // Read the current "Last synced" text before triggering
  const lastSyncedBefore = await page.locator('b').first().textContent();

  // Click Sync Now and wait for the enqueue snackbar
  await page.locator('button', { hasText: 'Sync Now' }).click();
  await expect(page.locator('.mud-snackbar')).toBeVisible({ timeout: 10_000 });

  // Poll by refreshing until "Last synced" time is more recent than before
  for (let i = 0; i < 12; i++) {
    await page.waitForTimeout(5_000);
    await page.locator('button', { hasText: 'Refresh' }).click();
    await page.waitForTimeout(1_000);

    const lastSyncedNow = await page.locator('b').first().textContent();
    if (lastSyncedNow && lastSyncedNow !== lastSyncedBefore && lastSyncedNow !== 'Never') {
      return; // sync completed
    }
  }

  throw new Error('Timelog sync did not complete within 60 seconds');
}

test('submit one entry for Aleksandr Volkov to Timelog', async ({ page }) => {
  // Ensure Timelog data is synced so ApiTaskId is populated
  await triggerAndAwaitSync(page);

  // Navigate to submission page
  await page.goto('/submission');
  await page.waitForSelector('text=Submission Pipeline', { timeout: 20_000 });

  // Wait for the table to load
  const aleksandrRow = page.locator('tr').filter({ hasText: 'Aleksandr Volkov' }).first();
  await expect(aleksandrRow).toBeVisible({ timeout: 15_000 });

  // Select the row
  await aleksandrRow.locator('input[type="checkbox"]').check();

  // Verify Submit Selected button shows (1)
  const submitBtn = page.locator('button', { hasText: /Submit Selected \(1\)/ });
  await expect(submitBtn).toBeEnabled();
  await submitBtn.click();

  // Confirm in the dialog
  const dialog = page.locator('.mud-dialog');
  await expect(dialog).toBeVisible({ timeout: 5_000 });
  await dialog.locator('button', { hasText: /^submit$/i }).click();

  // Wait for a success snackbar (submitted successfully)
  const successSnackbar = page.locator('.mud-snackbar').filter({ hasText: /submitted.*successfully/i });
  await expect(successSnackbar).toBeVisible({ timeout: 30_000 });

  // After page reloads, verify the entry moved to history with Success status
  const successChip = page.locator('.mud-chip', { hasText: 'Success' }).first();
  await expect(successChip).toBeVisible({ timeout: 15_000 });
});
