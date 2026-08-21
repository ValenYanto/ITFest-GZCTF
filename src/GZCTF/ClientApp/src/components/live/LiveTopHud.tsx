import { FC } from 'react'
import { SpeedrunRoundModel, SpeedrunRoundStatus } from '@Api'
import { formatDurationSeconds } from '@Utils/Shared'
import classes from '@Styles/LiveScoreboard.module.css'

export const LiveTopHud: FC<{
  title: string
  subtitle?: string | null
  round?: SpeedrunRoundModel | null
  remainingSeconds: number
  concealCategory?: boolean
}> = ({ title, subtitle, round, remainingSeconds, concealCategory }) => {
  const active = round?.status === SpeedrunRoundStatus.Running || round?.status === SpeedrunRoundStatus.Overtime
  const urgent = active && remainingSeconds <= 30
  const critical = active && remainingSeconds <= 10
  const timer = round?.status === SpeedrunRoundStatus.Ready ? 'Ready' : active ? formatDurationSeconds(remainingSeconds) : '--:--'
  const category = concealCategory ? 'Choosing category' : round?.category ?? 'Waiting for the next round'
  const status = concealCategory
    ? 'Selecting'
    : round?.status === SpeedrunRoundStatus.Overtime
      ? 'Overtime'
      : round?.status === SpeedrunRoundStatus.Running
        ? 'Round live'
        : round?.status === SpeedrunRoundStatus.Ready
          ? 'Ready'
          : 'Standby'

  return <header className={classes.topHud}>
    <div className={classes.brandLockup}>
      <div className={classes.brandMark} aria-label="ITFest"><span>IT</span><b>Fest</b></div>
      <div className={classes.eventIdentity}><strong>{title}</strong>{subtitle && <span>{subtitle}</span>}</div>
    </div>
    <div className={classes.clockModule}>
      <span>Round time</span>
      <strong className={`${urgent ? classes.urgent : ''} ${critical ? classes.critical : ''}`}>{timer}</strong>
    </div>
    <div className={classes.liveSummary}>
      <div className={classes.liveFlag}><i /><b>Live</b></div>
      <div className={classes.roundSummary}><span>{status}</span><strong>{category}</strong></div>
    </div>
  </header>
}
