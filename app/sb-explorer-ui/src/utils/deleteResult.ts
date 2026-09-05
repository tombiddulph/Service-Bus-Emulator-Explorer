import type { DeleteDlqResult } from '../api/types'

export const summarizeDeleteResult = (result: DeleteDlqResult) => {
  const notFound = new Set(result.notFound ?? [])
  const retryIds = result.outcomes
    .filter(outcome => !outcome.deleted && !notFound.has(outcome.messageId))
    .map(outcome => outcome.messageId)
  const details: string[] = []
  if (retryIds.length) details.push(`${retryIds.length} message${retryIds.length === 1 ? '' : 's'} could not be confirmed deleted and remain selected.`)
  if (notFound.size) details.push(`${notFound.size} message${notFound.size === 1 ? ' was' : 's were'} not found.`)
  if (result.error) details.push(result.error)
  return {
    retryIds,
    title: result.isPartial ? 'DLQ deletion incomplete' : 'DLQ deletion complete',
    message: `Deleted ${result.count} message${result.count === 1 ? '' : 's'}.${details.length ? ` ${details.join(' ')}` : ''}`,
    color: result.isPartial ? 'yellow' : 'green',
  } as const
}
