import { CSSProperties, FC } from 'react'
import { ChallengeCategory, LiveScoreboardTeamModel, SpeedrunRoundModel, SpeedrunRoundStatus } from '@Api'
import { LiveSpinPhase } from '@Components/live/types'
import { useChallengeCategoryLabelMap } from '@Utils/Shared'
import classes from '@Styles/LiveScoreboard.module.css'

export const LiveCenterArena: FC<{
  round?: SpeedrunRoundModel | null
  remaining: ChallengeCategory[]
  spinPhase: LiveSpinPhase
  teams: LiveScoreboardTeamModel[]
  attackingTeams: Set<number>
  bloodTeams: Set<number>
  frozen?: boolean
}> = ({ round, remaining, spinPhase, teams, attackingTeams, bloodTeams, frozen }) => {
  const categoryMap = useChallengeCategoryLabelMap()
  const categoryVisual = round?.category ? categoryMap.get(round.category) : undefined
  const categoryStyle = categoryVisual ? {
    '--category-color': categoryVisual.colors[6],
    '--category-glow': categoryVisual.colors[4],
  } as CSSProperties : undefined
  let label = 'The sunken championship'
  let title = 'Awaiting the next descent'
  let detail = remaining.length ? `${remaining.length} categories remain beneath the surface` : 'Every category has been played'

  if (spinPhase === 'spinning') {
    label = 'The current is choosing'
    title = 'Category vortex'
    detail = 'One medallion will rise from the deep'
  } else if (round?.status === SpeedrunRoundStatus.Ready) {
    label = 'The next trial'
    title = String(round.category)
    detail = 'Teams take their places around the altar'
  } else if (round?.status === SpeedrunRoundStatus.Running) {
    label = 'Current category'
    title = String(round.category)
    detail = 'Every solve awakens the central pearl'
  } else if (round?.status === SpeedrunRoundStatus.Overtime) {
    label = 'Overtime below the surface'
    title = String(round.category)
    detail = 'The colosseum stays open until the final challenge falls'
  }

  const attacking = attackingTeams.size > 0
  const blood = bloodTeams.size > 0

  return <section className={`${classes.centerArena} ${spinPhase === 'spinning' ? classes.arenaSpinning : ''} ${spinPhase === 'revealed' ? classes.arenaRevealed : ''} ${round?.status === SpeedrunRoundStatus.Running || round?.status === SpeedrunRoundStatus.Overtime ? classes.arenaActive : ''} ${attacking ? classes.arenaImpact : ''} ${blood ? classes.arenaBloodImpact : ''}`} aria-label="Team arena">
    <div className={classes.roundReadout} style={categoryStyle}>
      <span>{label}</span>
      <h1>{title}</h1>
      <p>{detail}</p>
      {attacking && <b className={blood ? classes.bloodSolveLabel : ''}>{blood ? 'Blood current' : 'Solve current'}</b>}
    </div>
    <div className={classes.srOnly}>
      {teams.slice(0, 10).map(team => `${team.rank}. ${team.name}, ${frozen ? '???' : team.score} points`).join('. ')}
    </div>
  </section>
}
