import { FC } from 'react'
import classes from '@Styles/LiveScoreboard.module.css'

export const LiveOceanBackground: FC = () => <div className={classes.oceanBackdrop} aria-hidden>
  <div className={classes.surfaceLight} />
  <div className={classes.caustics} />
  <div className={classes.currentHaze} />
  <div className={classes.particles}>{Array.from({ length: 16 }, (_, index) => <i key={index} />)}</div>
  <div className={`${classes.fishSchool} ${classes.fishSchoolLeft}`}><i /><i /><i /></div>
  <div className={`${classes.fishSchool} ${classes.fishSchoolRight}`}><i /><i /></div>
  <div className={`${classes.jellyfish} ${classes.jellyfishOne}`}><i /><span /><span /><span /></div>
  <div className={`${classes.jellyfish} ${classes.jellyfishTwo}`}><i /><span /><span /><span /></div>
</div>
