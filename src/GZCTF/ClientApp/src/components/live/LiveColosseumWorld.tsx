import { FC, lazy, Suspense, useEffect, useMemo, useState } from 'react'
import {
  ChallengeCategory,
  LiveScoreboardTeamModel,
  LiveScoreboardVisualIntensity,
} from '@Api'
import { LiveCategoryVisual, LiveSceneKind, LiveSpinPhase } from '@Components/live/types'
import { useChallengeCategoryLabelMap } from '@Utils/Shared'
import classes from '@Styles/LiveScoreboard.module.css'

const SunkenColosseumScene = lazy(() => import('@Components/live/scene/SunkenColosseumScene'))

const useReducedMotion = () => {
  const [reduced, setReduced] = useState(false)

  useEffect(() => {
    const media = window.matchMedia('(prefers-reduced-motion: reduce)')
    const update = () => setReduced(media.matches)
    update()
    media.addEventListener('change', update)
    return () => media.removeEventListener('change', update)
  }, [])

  return reduced
}

const hasWebGL = () => {
  try {
    const canvas = document.createElement('canvas')
    return Boolean(canvas.getContext('webgl2') || canvas.getContext('webgl'))
  } catch {
    return false
  }
}

export const LiveColosseumWorld: FC<{
  teams: LiveScoreboardTeamModel[]
  attackingTeams: Set<number>
  bloodTeams: Set<number>
  sceneKind?: LiveSceneKind
  sceneTeamId?: number
  spinPhase: LiveSpinPhase
  selected?: ChallengeCategory
  categories: ChallengeCategory[]
  intensity?: LiveScoreboardVisualIntensity
  frozen?: boolean
}> = props => {
  const [webgl] = useState(hasWebGL)
  const categoryMap = useChallengeCategoryLabelMap()
  const reducedMotion = useReducedMotion() || props.intensity === LiveScoreboardVisualIntensity.Calm
  const categoryVisuals = useMemo(() => [...categoryMap.values()].map(item => ({
    category: item.name,
    icon: item.icon,
    color: item.colors[6],
    glow: item.colors[4],
  } satisfies LiveCategoryVisual)), [categoryMap])

  return <div className={classes.worldLayer} aria-hidden>
    {webgl ? <Suspense fallback={<div className={classes.worldFallback}><i /><span /></div>}>
      <SunkenColosseumScene {...props} categoryVisuals={categoryVisuals} reducedMotion={reducedMotion} />
    </Suspense> : <div className={classes.worldFallback}><i /><span /></div>}
  </div>
}
