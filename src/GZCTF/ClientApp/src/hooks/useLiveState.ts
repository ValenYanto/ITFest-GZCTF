import { useCallback, useEffect, useState } from 'react'
import api, { LiveScoreboardStateModel, SpeedrunRoundStatus } from '@Api'

export const useLiveState = (gameId: number) => {
  const [state, setState] = useState<LiveScoreboardStateModel>()
  const [now, setNow] = useState(Date.now())

  const refresh = useCallback(async () => {
    const next = (await api.game.gameLiveScoreboard(gameId)).data
    setState(next)
  }, [gameId])

  useEffect(() => {
    void refresh()
    const poll = window.setInterval(refresh, 2000)
    const tick = window.setInterval(() => setNow(Date.now()), 1000)
    const focus = () => void refresh()
    window.addEventListener('focus', focus)
    return () => {
      window.clearInterval(poll)
      window.clearInterval(tick)
      window.removeEventListener('focus', focus)
    }
  }, [refresh])

  const round = state?.speedrunState?.currentRound
  const active = round?.status === SpeedrunRoundStatus.Running || round?.status === SpeedrunRoundStatus.Overtime
  const end = round?.status === SpeedrunRoundStatus.Overtime ? round.overtimeEndsAtUtc : round?.endsAtUtc
  const remainingSeconds = end ? Math.max(0, Math.floor((end - now) / 1000)) : 0

  return { state, round, active, remainingSeconds, refresh }
}
