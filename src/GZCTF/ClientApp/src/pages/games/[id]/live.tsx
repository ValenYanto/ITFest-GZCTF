import { FC } from 'react'
import { useParams } from 'react-router'
import { LiveScoreboardStage } from '@Components/live/LiveScoreboardStage'
import { useLiveState } from '@Hooks/useLiveState'

const LiveScoreboardPage: FC = () => {
  const gameId = Number(useParams().id)
  const { state, remainingSeconds } = useLiveState(gameId)
  return <LiveScoreboardStage state={state} remainingSeconds={remainingSeconds} />
}

export default LiveScoreboardPage
