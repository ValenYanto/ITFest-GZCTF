import { FC, useEffect, useState } from 'react'
import { useParams } from 'react-router'
import api, { ChallengeCategory, LiveScoreboardConfigModel, Role } from '@Api'
import { LiveScoreboardStage } from '@Components/live/LiveScoreboardStage'
import { WithRole } from '@Components/WithRole'
import { useLivePreviewState } from '@Hooks/useLivePreviewState'
import classes from '@Styles/LiveScoreboardPreview.module.css'

const PreviewStage: FC<{ gameId: number; config: LiveScoreboardConfigModel }> = ({ gameId, config }) => {
  const preview = useLivePreviewState(gameId, config)
  const [deckOpen, setDeckOpen] = useState(true)
  const teams = preview.state.topTeams ?? []

  return <div className={classes.preview}>
    <LiveScoreboardStage state={preview.state} remainingSeconds={preview.remainingSeconds}
      injectedAnnouncement={preview.injectedAnnouncement} preview />
    {deckOpen ? <section className={classes.controlDeck} aria-label="Live scoreboard preview controls">
      <header className={classes.deckHeader}>
        <div className={classes.deckIdentity}><i /><div><span>Admin preview</span><b>Live stage controls</b></div></div>
        <button onClick={() => setDeckOpen(false)}>Hide controls</button>
      </header>
      <div className={classes.deckBody}>
        <div className={classes.selectors}>
          <label>Team<select value={preview.selectedTeamId}
            onChange={event => preview.setSelectedTeamId(Number(event.currentTarget.value))}>
            {teams.map(team => <option key={team.id} value={team.id}>{team.rank}. {team.name}</option>)}
          </select></label>
          <label>Category<select value={preview.selectedCategory}
            onChange={event => preview.setSelectedCategory(event.currentTarget.value as ChallengeCategory)}>
            {Object.values(ChallengeCategory).map(category => <option key={category}>{category}</option>)}
          </select></label>
        </div>
        <div className={classes.triggerGrid}>
          <button onClick={preview.spin}>Spin category</button><button onClick={preview.start}>Start round</button>
          <button onClick={preview.solve}>Solve +150</button><button onClick={preview.firstBlood}>First Blood</button>
          <button onClick={preview.secondBlood}>Second Blood</button><button onClick={preview.thirdBlood}>Third Blood</button>
          <button onClick={preview.hint}>Release hint</button><button onClick={preview.reminder}>60s reminder</button>
          <button onClick={preview.countdown}>10s countdown</button><button onClick={preview.overtime}>Overtime</button>
          <button onClick={preview.finish}>Finish round</button><button onClick={preview.wrongSubmit}>Wrong submit</button>
          <button onClick={preview.reset}>Reset stage</button>
        </div>
        <div className={classes.showcase}>
          <div className={classes.showcaseStatus}><span>Showcase</span><b>{preview.showcaseStep}</b></div>
          <div className={classes.showcaseActions}><button disabled={preview.showcaseRunning} onClick={preview.runShowcase}>Play sequence</button>
            <button disabled={!preview.showcaseRunning} onClick={preview.stopShowcase}>Stop</button></div>
        </div>
      </div>
    </section> : <button className={classes.deckClosed} onClick={() => setDeckOpen(true)}>Open preview controls</button>}
  </div>
}

const LiveScoreboardPreviewPage: FC = () => {
  const gameId = Number(useParams().id)
  const [config, setConfig] = useState<LiveScoreboardConfigModel>()

  useEffect(() => {
    let active = true
    void api.edit.editGetLiveScoreboardConfig(gameId).then(response => {
      if (active) setConfig(response.data)
    })
    return () => { active = false }
  }, [gameId])

  return <WithRole requiredRole={Role.Admin}>
    {config ? <PreviewStage gameId={gameId} config={config} /> : <div className={classes.loading}>Loading live stage preview</div>}
  </WithRole>
}

export default LiveScoreboardPreviewPage
