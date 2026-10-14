import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MantineProvider } from '@mantine/core'
import { expect, it, vi } from 'vitest'
import MessageGrid from './MessageGrid'

const defaults = { state: 'active' as const, skip: 0, take: 25, selectedIds: [], onSelectionChange: vi.fn(), onPageChange: vi.fn() }

it('shows an error instead of an empty-queue claim and offers retry', async () => {
  const retry = vi.fn()
  render(<MantineProvider><MessageGrid {...defaults} error={new Error('Broker unavailable')} onRetry={retry} /></MantineProvider>)
  expect(screen.getByText('Broker unavailable')).toBeInTheDocument()
  expect(screen.queryByText('No active messages.')).not.toBeInTheDocument()
  await userEvent.click(screen.getByRole('button', { name: 'Retry' }))
  expect(retry).toHaveBeenCalledOnce()
})

it('retains stale data and supports selection and pagination', async () => {
  const selection = vi.fn()
  const page = vi.fn()
  render(<MantineProvider><MessageGrid {...defaults} error={new Error('Offline')} messages={{ items: [{ messageId: 'one', bodyPreview: 'hello' }], hasMore: true }} onSelectionChange={selection} onPageChange={page} /></MantineProvider>)
  expect(screen.getByText(/showing stale data/)).toBeInTheDocument()
  expect(screen.getByText('hello')).toBeInTheDocument()
  await userEvent.click(screen.getByRole('checkbox', { name: 'Select message one' }))
  expect(selection).toHaveBeenCalledWith(['one'])
  await userEvent.click(screen.getByRole('button', { name: 'Next' }))
  expect(page).toHaveBeenCalledWith(25)
})
