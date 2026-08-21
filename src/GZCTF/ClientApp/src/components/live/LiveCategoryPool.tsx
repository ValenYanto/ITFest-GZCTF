import { CSSProperties, FC } from 'react'
import { ChallengeCategory, SpeedrunRoundModel, SpeedrunRoundStatus } from '@Api'
import { useChallengeCategoryLabelMap } from '@Utils/Shared'
import classes from '@Styles/LiveScoreboard.module.css'

const CategoryList: FC<{ values: ChallengeCategory[]; className: string; limit: number }> = ({ values, className, limit }) => {
  const categoryMap = useChallengeCategoryLabelMap()

  return <div className={classes.poolValues}>
    {values.length ? <>
      {values.slice(0, limit).map(value => {
        const visual = categoryMap.get(value)
        const style = visual ? {
          '--category-color': visual.colors[6],
          '--category-glow': visual.colors[4],
        } as CSSProperties : undefined

        return <span className={`${classes.categoryPill} ${className}`} style={style} key={value}>
          {visual && <svg viewBox="0 0 24 24" aria-hidden><path d={visual.icon} /></svg>}
          {value}
        </span>
      })}
      {values.length > limit && <span className={classes.morePill}>+{values.length - limit} more</span>}
    </> : <span className={classes.emptyPill}>None</span>}
  </div>
}

export const LiveCategoryPool: FC<{
  round?: SpeedrunRoundModel | null
  available: ChallengeCategory[]
  used: ChallengeCategory[]
  concealActive?: boolean
}> = ({ round, available, used, concealActive }) => <footer className={classes.categoryPool}>
  <div className={`${classes.poolGroup} ${classes.currentPool}`}><b>Current category</b>
    <CategoryList values={!concealActive && round?.category ? [round.category] : []}
      className={round?.status === SpeedrunRoundStatus.Ready ? classes.selectedPill : classes.activePill} limit={1} />
  </div>
  <div className={`${classes.poolGroup} ${classes.availablePool}`}><b>Remaining</b>
    <CategoryList values={available} className={classes.availablePill} limit={6} />
  </div>
  <div className={`${classes.poolGroup} ${classes.usedPool}`}><b>Completed</b>
    <CategoryList values={used} className={classes.usedPill} limit={4} />
  </div>
</footer>
