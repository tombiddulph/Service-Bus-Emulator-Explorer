// The API caps its scan and marks the count as not-exact when it may be truncated or a partial (timed-out) result.
export const formatMessageCount = (count?: number, isExact = true) =>
  count === undefined ? '—' : `${count}${isExact ? '' : '+'}`

export const messageCountTooltip = (isExact = true) =>
  isExact ? undefined : 'Count may be truncated or incomplete (scan limit or timeout reached).'
