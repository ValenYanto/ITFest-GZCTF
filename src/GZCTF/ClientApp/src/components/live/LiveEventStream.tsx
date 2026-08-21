import { FC } from 'react'
import { LiveScoreboardEventModel, NoticeType } from '@Api'
import classes from '@Styles/LiveScoreboard.module.css'

const eventMeta = (event: LiveScoreboardEventModel) => {
  if (event.type === NoticeType.FirstBlood) return ['First Blood', classes.bloodEvent]
  if (event.type === NoticeType.SecondBlood) return ['Second Blood', classes.bloodEvent]
  if (event.type === NoticeType.ThirdBlood) return ['Third Blood', classes.bloodEvent]
  if (event.message?.startsWith('Hint #')) return ['Hint released', classes.hintEvent]
  if (event.message?.toLowerCase().includes('category')) return ['Category', classes.roundEvent]
  if (event.message?.toLowerCase().includes('overtime')) return ['Overtime', classes.bloodEvent]
  if (event.message?.toLowerCase().includes('round')) return ['Round update', classes.roundEvent]
  return ['Update', classes.normalEvent]
}

export const LiveEventStream: FC<{ events: LiveScoreboardEventModel[] }> = ({ events }) => <aside className={`${classes.waterPanel} ${classes.eventPanel}`}>
  <header className={classes.panelHead}><div><span>Latest from the game</span><h2>Recent events</h2></div><i aria-hidden /></header>
  <div className={classes.eventRows}>{events.slice(0, 8).map((event, index) => {
    const [label, color] = eventMeta(event)
    return <article className={`${classes.eventRow} ${color}`} key={event.id}>
      <time>{String(index + 1).padStart(2, '0')}</time>
      <div><b>{label}</b><p>{event.message}</p></div>
    </article>
  })}{!events.length && <div className={classes.emptyState}><i />Events will appear here</div>}</div>
</aside>
