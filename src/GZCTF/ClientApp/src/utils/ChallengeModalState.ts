export type ChallengeModalLoadState = 'idle' | 'loading' | 'error' | 'ready'

type ApiError = {
  response?: { status?: number; data?: { title?: string } }
  message?: string
}

export const resolveChallengeModalLoadState = (
  opened: boolean,
  hasChallenge: boolean,
  error?: unknown
): ChallengeModalLoadState => {
  if (!opened) return 'idle'
  if (error) return 'error'
  return hasChallenge ? 'ready' : 'loading'
}

export const challengeLoadErrorMessage = (error?: unknown): string | undefined => {
  if (!error) return undefined
  const requestError = error as ApiError
  const status = requestError.response?.status
  return (
    requestError.response?.data?.title ??
    (
      {
        400: 'The challenge request is invalid or no longer available.',
        403: 'Your team does not have permission to open this challenge.',
        404: 'This challenge is disabled, inactive, or unavailable for your team.',
        500: 'The server could not load this challenge. Please retry or contact an administrator.',
      } as Record<number, string>
    )[status ?? 0] ??
    requestError.message ??
    'The challenge could not be loaded.'
  )
}
