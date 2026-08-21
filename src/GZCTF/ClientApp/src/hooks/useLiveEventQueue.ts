import { useCallback, useEffect, useRef, useState } from 'react'
import { LiveAnnouncement } from '@Components/live/types'

export const useLiveEventQueue = (onPlay: (event: LiveAnnouncement) => void) => {
  const [active, setActive] = useState<LiveAnnouncement>()
  const [visible, setVisible] = useState<LiveAnnouncement>()
  const queue = useRef<LiveAnnouncement[]>([])
  const seen = useRef(new Set<string>())

  const enqueue = useCallback((event: LiveAnnouncement) => {
    if (seen.current.has(event.key)) return
    seen.current.add(event.key)
    queue.current.push(event)
    setActive((current) => current ?? queue.current.shift())
  }, [])

  const markSeen = useCallback((keys: string[]) => keys.forEach((key) => seen.current.add(key)), [])

  useEffect(() => {
    setVisible(undefined)
    if (!active) return

    onPlay(active)
    const revealTimer = active.showPopup === false
      ? undefined
      : window.setTimeout(() => setVisible(active), active.popupDelay ?? 0)
    const advanceTimer = window.setTimeout(() => setActive(queue.current.shift()), active.duration ?? 3200)

    return () => {
      if (revealTimer !== undefined) window.clearTimeout(revealTimer)
      window.clearTimeout(advanceTimer)
    }
  }, [active, onPlay])

  return { active, visible, enqueue, markSeen }
}
