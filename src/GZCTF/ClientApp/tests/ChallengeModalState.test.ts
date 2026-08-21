import assert from 'node:assert/strict'
import test from 'node:test'
import { challengeLoadErrorMessage, resolveChallengeModalLoadState } from '../src/utils/ChallengeModalState.ts'

test('a 404 challenge response renders an error state instead of a permanent skeleton', () => {
  const error = { response: { status: 404 } }

  assert.equal(resolveChallengeModalLoadState(true, false, error), 'error')
  assert.match(challengeLoadErrorMessage(error) ?? '', /disabled, inactive, or unavailable/)
})

test('the skeleton is limited to a genuinely pending open request', () => {
  assert.equal(resolveChallengeModalLoadState(true, false), 'loading')
  assert.equal(resolveChallengeModalLoadState(false, false), 'idle')
  assert.equal(resolveChallengeModalLoadState(true, true), 'ready')
})
