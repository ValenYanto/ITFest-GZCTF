import assert from 'node:assert/strict'
import test from 'node:test'
import { crossedReminderPoints, dequeueLiveEvent, withoutBloodScoreChanges } from '../src/utils/LiveEventQueue.ts'

test('simultaneous live events play hint, blood, then reminder', () => {
  const queue = [
    { key: 'blood', kind: 'firstBlood' },
    { key: 'reminder', kind: 'reminder' },
    { key: 'hint', kind: 'hint' },
  ]

  assert.equal(dequeueLiveEvent(queue)?.key, 'hint')
  assert.equal(dequeueLiveEvent(queue)?.key, 'blood')
  assert.equal(dequeueLiveEvent(queue)?.key, 'reminder')
  assert.equal(dequeueLiveEvent(queue), undefined)
})

test('equal-priority events retain insertion order', () => {
  const queue = [
    { key: 'hint-1', kind: 'hint' },
    { key: 'hint-2', kind: 'hint' },
  ]

  assert.equal(dequeueLiveEvent(queue)?.key, 'hint-1')
  assert.equal(dequeueLiveEvent(queue)?.key, 'hint-2')
})

test('reminder detects a threshold skipped between scoreboard polls', () => {
  assert.deepEqual(crossedReminderPoints(1205, 1198), [1200])
  assert.deepEqual(crossedReminderPoints(600, 599), [])
  assert.deepEqual(crossedReminderPoints(1198, 1205), [])
})

test('blood score changes do not trigger the ordinary solve animation', () => {
  assert.deepEqual(withoutBloodScoreChanges([11, 22, 33], new Set([22])), [11, 33])
  assert.deepEqual(withoutBloodScoreChanges([22], new Set([22])), [])
})
