import type { ReplayDlqResult } from '../api/types'

export const summarizeReplayResult = (result: ReplayDlqResult) => {
  const notFound = new Set(result.notFound ?? [])
  const retryIds = result.outcomes
    .filter((outcome) => !outcome.sent && !outcome.sendOutcomeUnknown && !notFound.has(outcome.messageId))
    .map((outcome) => outcome.messageId)
  const cleanupCount = result.outcomes.filter(
    (outcome) => outcome.sent && !outcome.removedFromDlq && outcome.error,
  ).length
  const unknownCount = result.outcomes.filter((outcome) => outcome.sendOutcomeUnknown).length
  const details = []

  if (retryIds.length) {
    details.push(`${retryIds.length} message${retryIds.length === 1 ? ' was' : 's were'} not sent and remain selected for a safe retry.`)
  }
  if (cleanupCount) {
    details.push(`${cleanupCount} active cop${cleanupCount === 1 ? 'y was' : 'ies were'} sent, but ${cleanupCount === 1 ? 'it remains' : 'they remain'} in the DLQ. Do not replay ${cleanupCount === 1 ? 'it' : 'them'} again; reselect ${cleanupCount === 1 ? 'the message' : 'the messages'} and use Delete selected DLQ to finish cleanup.`)
  }
  if (unknownCount) {
    details.push(`${unknownCount} send outcome${unknownCount === 1 ? ' is' : 's are'} unknown. Refresh and check the destination before retrying.`)
  }
  if (notFound.size) {
    details.push(`${notFound.size} message${notFound.size === 1 ? ' was' : 's were'} not found in the DLQ.`)
  }
  if (result.error) details.push(result.error)

  return {
    retryIds,
    title: result.isPartial ? 'DLQ replay partially completed' : 'DLQ replay complete',
    message: `Replayed ${result.count} message${result.count === 1 ? '' : 's'}.${details.length ? ` ${details.join(' ')}` : ''}`,
    color: result.isPartial ? 'yellow' : 'green',
  } as const
}
