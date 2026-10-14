import { afterEach, expect, it, vi } from 'vitest'

afterEach(() => { vi.unstubAllEnvs(); vi.resetModules() })

it('full mock mode supplies metadata and supports sending without a backend', async () => {
  vi.stubEnv('VITE_USE_MOCK', 'true')
  vi.resetModules()
  const { apiClient, isMockEnabled } = await import('./client')
  expect(isMockEnabled).toBe(true)
  expect((await apiClient.get('/environment')).data).toMatchObject({ name: 'Mock', readOnly: false })
  expect((await apiClient.get('/connection')).data.ready).toBe(true)
  await apiClient.post('/queues/orders/messages', { body: 'mock-sent-message' })
  const page = (await apiClient.get('/queues/orders/messages', { params: { state: 'active', take: 250 } })).data
  expect(page.items.some((item: { body: string }) => item.body === 'mock-sent-message')).toBe(true)
})
