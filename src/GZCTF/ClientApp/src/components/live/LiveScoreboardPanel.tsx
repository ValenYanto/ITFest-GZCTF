import { FC } from 'react'
import { LiveScoreboardTeamModel } from '@Api'
import classes from '@Styles/LiveScoreboard.module.css'

const podiumClass = (rank?: number) => {
  if (rank === 1) return classes.podium1
  if (rank === 2) return classes.podium2
  if (rank === 3) return classes.podium3
  return ''
}

export const LiveScoreboardPanel: FC<{
  teams: LiveScoreboardTeamModel[]
  changedTeams: Set<number>
  frozen?: boolean
}> = ({ teams, changedTeams, frozen }) => <aside className={`${classes.waterPanel} ${classes.scoreboardPanel}`}>
  <header className={classes.panelHead}>
    <div><span>Top 10 teams</span><h2>Standings</h2></div>
    <div className={classes.scoreLabels}><span>Score</span><span>Solves</span></div>
  </header>
  <div className={classes.scoreRows}>{teams.slice(0, 10).map(team => <div key={team.id}
    className={`${classes.scoreRow} ${podiumClass(team.rank)} ${changedTeams.has(team.id!) ? classes.scorePulse : ''}`}>
    <strong>{team.rank ?? 0}</strong>
    <div><b>{team.name}</b>{team.rank === 1 && <small>Leader</small>}</div>
    <span>{frozen ? '???' : team.score?.toLocaleString() ?? 0}</span>
    <em>{frozen ? '???' : team.solvedCount ?? 0}</em>
  </div>)}</div>
</aside>
