import { FC } from 'react'
import { GameMode, LiveScoreboardStateModel, SpeedrunRoundStatus } from '@Api'
import { LiveAnnouncement } from '@Components/live/types'
import { LiveAnnouncementOverlay } from '@Components/live/LiveAnnouncementOverlay'
import { LiveCategoryPool } from '@Components/live/LiveCategoryPool'
import { LiveCenterArena } from '@Components/live/LiveCenterArena'
import { LiveColosseumWorld } from '@Components/live/LiveColosseumWorld'
import { LiveEventStream } from '@Components/live/LiveEventStream'
import { LiveOceanBackground } from '@Components/live/LiveOceanBackground'
import { LiveScoreboardPanel } from '@Components/live/LiveScoreboardPanel'
import { LiveTopHud } from '@Components/live/LiveTopHud'
import { useLivePresentation } from '@Hooks/useLivePresentation'
import classes from '@Styles/LiveScoreboard.module.css'

const Fallback: FC<{ title: string; text: string }> = ({ title, text }) => <main className={classes.fallback}>
  <LiveOceanBackground />
  <div className={classes.fallbackPearl} aria-hidden />
  <div className={classes.fallbackBrand}>ITFest</div>
  <h1>{title}</h1>
  <p>{text}</p>
</main>

export const LiveScoreboardStage: FC<{
  state?: LiveScoreboardStateModel
  remainingSeconds: number
  injectedAnnouncement?: LiveAnnouncement
  preview?: boolean
}> = ({ state, remainingSeconds, injectedAnnouncement, preview }) => {
  const presentation = useLivePresentation(state, remainingSeconds, injectedAnnouncement)
  const round = state?.speedrunState?.currentRound

  if (!state) return <Fallback title="Connecting to the live stage" text="The scoreboard will appear when live data is ready." />
  if (state.gameMode !== GameMode.Speedrun) return <Fallback title="Live stage unavailable" text="This broadcast view is available for Speedrun games." />
  if (!state.config?.enabled && !preview) return <Fallback title="Live scoreboard is off" text="An administrator can enable it from the game settings." />

  const overtime = round?.status === SpeedrunRoundStatus.Overtime
  const remainingCategories = state.speedrunState?.remainingCategories ?? []
  const ordinaryCorrectTeamId = [...presentation.changedTeams]
    .filter(teamId => !presentation.bloodAttackTeams.has(teamId))
    .sort((left, right) => left - right)[0]
  const announcementTeamId = presentation.announcement?.teamId
    ?? (presentation.announcement?.teamName
      ? state.topTeams?.find(team => team.name === presentation.announcement?.teamName)?.id
      : undefined)
  const sceneKind = presentation.announcement?.sceneKind ?? (ordinaryCorrectTeamId !== undefined ? 'correct' : undefined)
  const sceneTeamId = announcementTeamId ?? ordinaryCorrectTeamId
  return <main className={`${classes.stage} ${overtime ? classes.stageOvertime : ''} ${state.config?.visualIntensity === 'Hype' ? classes.stageHype : ''}`}>
    <LiveOceanBackground />
    <LiveColosseumWorld teams={state.topTeams ?? []} attackingTeams={presentation.changedTeams}
      bloodTeams={presentation.bloodAttackTeams} sceneKind={sceneKind}
      sceneTeamId={sceneTeamId} spinPhase={presentation.spinPhase} selected={round?.category}
      categories={remainingCategories} intensity={state.config?.visualIntensity} frozen={state.scoreboardFrozen} />
    <LiveTopHud title={state.config?.title ?? state.gameTitle ?? 'ITFest Live Scoreboard'} subtitle={state.config?.subtitle}
      round={round} remainingSeconds={remainingSeconds} concealCategory={presentation.spinPhase === 'spinning'} />
    <div className={classes.mainGrid}>
      <LiveEventStream events={state.recentEvents ?? []} />
      <LiveCenterArena round={round} remaining={remainingCategories}
        spinPhase={presentation.spinPhase} teams={state.topTeams ?? []} attackingTeams={presentation.changedTeams}
        bloodTeams={presentation.bloodAttackTeams} frozen={state.scoreboardFrozen} />
      <LiveScoreboardPanel teams={state.topTeams ?? []} changedTeams={presentation.changedTeams}
        frozen={state.scoreboardFrozen} />
    </div>
    <LiveCategoryPool round={round} available={state.speedrunState?.remainingCategories ?? []}
      used={state.speedrunState?.usedCategories ?? []} concealActive={presentation.spinPhase === 'spinning'} />
    <LiveAnnouncementOverlay event={presentation.visibleAnnouncement} />
    <button className={`${classes.audioButton} ${presentation.audioEnabled ? classes.audioOn : ''}`} onClick={presentation.unlockAudio}>
      <i /><span>{presentation.audioEnabled ? 'Sound on' : 'Enable sound'}</span>
    </button>
  </main>
}
