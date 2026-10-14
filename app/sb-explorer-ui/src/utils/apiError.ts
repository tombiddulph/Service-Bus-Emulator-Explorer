import axios from 'axios'

export function apiErrorMessage(error: unknown): string {
  if (axios.isAxiosError<{ detail?: string; errors?: Record<string, string[]> }>(error)) {
    const problem = error.response?.data
    if (problem?.errors) return Object.entries(problem.errors).map(([field, errors]) => `${field}: ${errors.join(' ')}`).join('\n')
    if (problem?.detail) return problem.detail
  }
  return error instanceof Error ? error.message : 'Check the emulator connection and try again.'
}
