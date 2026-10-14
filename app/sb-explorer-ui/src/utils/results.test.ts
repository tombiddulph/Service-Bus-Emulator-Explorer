import { describe, expect, it } from 'vitest'
import { summarizeReplayResult } from './replayResult'
import { summarizeDeleteResult } from './deleteResult'
import { apiErrorMessage } from './apiError'

describe('bulk result summaries', () => {
  it('only selects safely retryable replay failures', () => {
    const summary = summarizeReplayResult({ count: 1, isPartial: true, notFound: ['gone'], outcomes: [
      { messageId: 'ok', sent: true, removedFromDlq: true },
      { messageId: 'retry', sent: false, removedFromDlq: false },
      { messageId: 'unknown', sent: false, removedFromDlq: false, sendOutcomeUnknown: true },
      { messageId: 'cleanup', sent: true, removedFromDlq: false, error: 'settlement failed' },
      { messageId: 'gone', sent: false, removedFromDlq: false },
    ] })
    expect(summary.retryIds).toEqual(['retry'])
    expect(summary.message).toContain('Do not replay')
    expect(summary.message).toContain('unknown')
    expect(summary.color).toBe('yellow')
  })
  it('excludes missing and deleted messages from deletion retries', () => {
    expect(summarizeDeleteResult({ count: 1, status: 'partial', isPartial: true, notFound: ['gone'], outcomes: [
      { messageId: 'ok', deleted: true }, { messageId: 'retry', deleted: false }, { messageId: 'gone', deleted: false },
    ] }).retryIds).toEqual(['retry'])
  })
  it('formats field validation errors', () => {
    expect(apiErrorMessage({ isAxiosError: true, response: { data: { errors: { name: ['Invalid name.'] } } } })).toBe('name: Invalid name.')
  })
})
