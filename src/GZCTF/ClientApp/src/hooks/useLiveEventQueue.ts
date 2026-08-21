import { useCallback, useEffect, useRef, useState } from 'react'
import { LiveAnnouncement } from '@Components/live/types'
import { dequeueLiveEvent } from '@Utils/LiveEventQueue'

export const useLiveEventQueue = (onPlay: (event: LiveAnnouncement) => void) => {
  const [active, setActive] = useState<LiveAnnouncement>()
  const [visible, setVisible] = useState<LiveAnnouncement>()
  const queue = useRef<LiveAnnouncement[]>([])
  const seen = useRef(new Set<string>())
  const activationTimer = useRef<number | undefined>(undefined)

  const scheduleActivation = useCallback(() => {
    if (activationTimer.current !== undefined) return
    activationTimer.current = window.setTimeout(() => {
      activationTimer.current = undefined
      setActive((current) => current ?? dequeueLiveEvent(queue.current))
    }, 0)
  }, [])

  const enqueue = useCallback(
    (event: LiveAnnouncement) => {
      if (seen.current.has(event.key)) return
      seen.current.add(event.key)
      queue.current.push(event)
      // Defer activation by one task so events discovered in the same poll/render can be prioritized together.
      scheduleActivation()
    },
    [scheduleActivation]
  )

  const markSeen = useCallback((keys: string[]) => keys.forEach((key) => seen.current.add(key)), [])

  useEffect(() => {
    setVisible(undefined)
    if (!active) return

    onPlay(active)
    const revealTimer =
      active.showPopup === false ? undefined : window.setTimeout(() => setVisible(active), active.popupDelay ?? 0)
    const advanceTimer = window.setTimeout(() => setActive(dequeueLiveEvent(queue.current)), active.duration ?? 3200)

    return () => {
      if (revealTimer !== undefined) window.clearTimeout(revealTimer)
      window.clearTimeout(advanceTimer)
    }
  }, [active, onPlay])

  useEffect(
    () => () => {
      if (activationTimer.current !== undefined) window.clearTimeout(activationTimer.current)
    },
    []
  )

  return { active, visible, enqueue, markSeen }
}
