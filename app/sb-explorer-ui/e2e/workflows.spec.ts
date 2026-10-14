import { expect, test } from '@playwright/test'

test.beforeEach(async ({ page }) => {
  await page.route(/^https?:\/\/[^/]+\/api\//, async route => {
    const path = new URL(route.request().url()).pathname
    let data: unknown = []
    if (path.endsWith('/environment')) data = { name: 'Test', readOnly: false, refreshIntervalMs: 8000 }
    else if (path.endsWith('/queues')) data = [{ name: 'orders', status: 'Active', activeMessageCount: 1, deadLetterMessageCount: 1 }]
    else if (path.endsWith('/topics')) data = [{ name: 'events', status: 'Active', activeMessageCount: 1, deadLetterMessageCount: 0 }]
    else if (path.endsWith('/subscriptions')) data = [{ name: 'consumer', status: 'Active', activeMessageCount: 1, deadLetterMessageCount: 0 }]
    else if (path.endsWith('/messages')) data = { items: [{ messageId: 'message-one', bodyPreview: 'hello', body: 'hello' }], hasMore: false }
    else if (path.endsWith('/purge')) data = { status: 'Completed', removedCount: 1 }
    else if (path.endsWith('/replay')) data = { count: 0, isPartial: true, outcomes: [{ messageId: 'message-one', sent: false, removedFromDlq: false, error: 'Try again' }] }
    else if (path.endsWith('/delete')) data = { count: 1, status: 'completed', isPartial: false, outcomes: [{ messageId: 'message-one', deleted: true }] }
    await route.fulfill({ json: data })
  })
})

test('send, inspect and confirm purge', async ({ page }) => {
  await page.goto('/queues/orders')
  await expect(page.getByText('hello', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Send message', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Send', exact: true }).click()
  await expect(page.getByText('Message sent', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Purge active messages' }).click()
  await expect(page.getByRole('dialog')).toContainText('permanently deletes')
  await expect(page.getByRole('dialog').getByRole('button', { name: 'Purge', exact: true })).toBeDisabled()
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click()
  await expect(page.getByRole('dialog')).not.toBeVisible()
})

test('read-only mode disables mutations', async ({ page }) => {
  await page.route('**/api/environment', route => route.fulfill({ json: { name: 'Test', readOnly: true } }))
  await page.goto('/queues/orders')
  await expect(page.getByText('Read-only', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Send message', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Purge active messages' })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Delete', exact: true })).toBeDisabled()
})

test('deleting an entity requires its exact name', async ({ page }) => {
  await page.goto('/queues/orders')
  await page.getByRole('button', { name: 'Delete', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByRole('button', { name: 'Delete', exact: true })).toBeDisabled()
  await dialog.getByLabel('Type entity name to confirm').fill('wrong-name')
  await expect(dialog.getByRole('button', { name: 'Delete', exact: true })).toBeDisabled()
  await dialog.getByLabel('Type entity name to confirm').fill('orders')
  await dialog.getByRole('button', { name: 'Delete', exact: true }).click()
  await expect(page).toHaveURL(/\/queues$/)
})

test('create entities and navigate subscriptions', async ({ page }) => {
  await page.goto('/queues')
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await page.getByRole('dialog').getByRole('textbox', { name: /^Name/ }).fill('new-queue')
  await page.getByRole('dialog').getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page.getByRole('dialog')).not.toBeVisible()
  await page.goto('/topics/events')
  await page.getByRole('button', { name: 'Create subscription', exact: true }).first().click()
  await page.getByRole('dialog').getByRole('textbox', { name: /^Name/ }).fill('new-consumer')
  await page.getByRole('dialog').getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page.getByRole('dialog')).not.toBeVisible()
  await page.getByRole('button', { name: 'consumer', exact: true }).click()
  await expect(page.getByText('hello', { exact: true })).toBeVisible()
})

test('partial DLQ replay leaves safe failures selected', async ({ page }) => {
  await page.goto('/queues/orders')
  await page.getByRole('tab', { name: /Dead/ }).click()
  await page.getByRole('checkbox', { name: 'Select message message-one' }).check()
  await page.getByRole('button', { name: 'Replay selected' }).click()
  await page.getByRole('dialog').getByLabel('Type entity name to confirm').fill('orders')
  await page.getByRole('dialog').getByRole('button', { name: 'Confirm', exact: true }).click()
  await expect(page.getByText('DLQ replay partially completed', { exact: true })).toBeVisible()
  await expect(page.getByRole('checkbox', { name: 'Select message message-one' })).toBeChecked()
})

test('broker failures are not presented as empty queues', async ({ page }) => {
  await page.route('**/api/queues/orders/messages?**', route => route.fulfill({ status: 503, json: { detail: 'Emulator unavailable' } }))
  await page.goto('/queues/orders')
  await expect(page.getByText('Emulator unavailable')).toBeVisible({ timeout: 15000 })
  await expect(page.getByText('No active messages.')).not.toBeVisible()
})
