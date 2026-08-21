import { FC } from 'react'
import { LiveAnnouncement } from '@Components/live/types'
import classes from '@Styles/LiveScoreboard.module.css'

const announcementClasses: Partial<Record<LiveAnnouncement['kind'], string>> = {
  firstBlood: classes.announcement_firstBlood,
  blood: classes.announcement_blood,
  hint: classes.announcement_hint,
  overtime: classes.announcement_overtime,
  countdown: classes.announcement_countdown,
  correct: classes.announcement_correct,
  wrong: classes.announcement_wrong,
  category: classes.announcement_category,
  start: classes.announcement_start,
  finished: classes.announcement_finished,
}

const eyebrow: Partial<Record<LiveAnnouncement['kind'], string>> = {
  firstBlood: 'A landmark solve',
  blood: 'One of the first solves',
  hint: 'New information',
  overtime: 'The round continues',
  correct: 'Score update',
  wrong: 'Preview only',
  category: 'Up next',
  start: 'The round is live',
  finished: 'Scores are updated',
  reminder: 'Time remaining',
}

export const LiveAnnouncementOverlay: FC<{ event?: LiveAnnouncement }> = ({ event }) => event ? <div
  className={`${classes.announcement} ${announcementClasses[event.kind] ?? ''}`} role="status" aria-live="assertive">
  <div className={classes.announcementWater} aria-hidden />
  <div className={classes.announcementRipple} aria-hidden />
  <div className={classes.announcementCopy}>
    {eyebrow[event.kind] && <small>{eyebrow[event.kind]}</small>}
    <strong>{event.title}</strong>
    {event.text && <span>{event.text}</span>}
  </div>
</div> : null
