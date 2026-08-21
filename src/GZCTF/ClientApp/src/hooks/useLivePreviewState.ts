import { useCallback, useEffect, useRef, useState } from 'react'
import {
  ChallengeCategory,
  GameMode,
  LiveScoreboardConfigModel,
  LiveScoreboardEventModel,
  LiveScoreboardStateModel,
  NoticeType,
  SpeedrunRoundStatus,
} from '@Api'
import { LiveAnnouncement } from '@Components/live/types'

const PREVIEW_CATEGORIES = [
  ChallengeCategory.Web,
  ChallengeCategory.Pwn,
  ChallengeCategory.Crypto,
  ChallengeCategory.Reverse,
  ChallengeCategory.Forensics,
  ChallengeCategory.Misc,
]

const PREVIEW_TEAMS = [
  ['NULL SECTOR', 2840, 14],
  ['ByteBenders', 2650, 13],
  ['N0tFound', 2435, 12],
  ['Sudo Society', 2260, 11],
  ['Packet Pirates', 2075, 10],
  ['Kernel Panic', 1910, 9],
  ['Cipher Crew', 1730, 8],
  ['Stack Smashers', 1585, 8],
  ['404 Brain', 1390, 7],
  ['Blue Phoenix', 1210, 6],
  ['Coral Cache', 1080, 6],
  ['Deep Stack', 965, 5],
  ['Shell Company', 870, 5],
  ['Tide Breakers', 790, 4],
  ['Abyss Watch', 705, 4],
  ['Sea Quence', 620, 3],
  ['Null Nautilus', 540, 3],
  ['Reef Raiders', 450, 2],
] as const

const createState = (gameId: number, config: LiveScoreboardConfigModel): LiveScoreboardStateModel => ({
  gameId,
  gameTitle: config.title || 'ITFest Speedrun Championship',
  gameMode: GameMode.Speedrun,
  scoreboardFrozen: false,
  serverTimeUtc: Date.now(),
  config: { ...config, enabled: true },
  speedrunState: {
    isSpeedrun: true,
    currentRound: null,
    usedCategories: [],
    remainingCategories: PREVIEW_CATEGORIES,
    message: 'Preview simulator ready',
  },
  topTeams: PREVIEW_TEAMS.map(([name, score, solvedCount], index) => ({
    id: index + 1,
    rank: index + 1,
    name,
    score,
    solvedCount,
  })),
  recentEvents: [],
})

export const useLivePreviewState = (gameId: number, config: LiveScoreboardConfigModel) => {
  const [state, setState] = useState(() => createState(gameId, config))
  const [remainingSeconds, setRemainingSeconds] = useState(0)
  const [selectedTeamId, setSelectedTeamId] = useState(1)
  const [selectedCategory, setSelectedCategory] = useState(ChallengeCategory.Web)
  const [injectedAnnouncement, setInjectedAnnouncement] = useState<LiveAnnouncement>()
  const [showcaseRunning, setShowcaseRunning] = useState(false)
  const [showcaseStep, setShowcaseStep] = useState('Ready')
  const roundId = useRef(1000)
  const eventId = useRef(1000)
  const timers = useRef<number[]>([])

  const clearTimers = useCallback(() => {
    timers.current.forEach(window.clearTimeout)
    timers.current = []
  }, [])

  const addEvent = useCallback((event: Omit<LiveScoreboardEventModel, 'id' | 'createdAt'>) => {
    const id = `preview-${++eventId.current}`
    setState(current => ({
      ...current,
      serverTimeUtc: Date.now(),
      recentEvents: [{ ...event, id, createdAt: Date.now() }, ...(current.recentEvents ?? [])].slice(0, 20),
    }))
  }, [])

  const reset = useCallback(() => {
    clearTimers()
    setShowcaseRunning(false)
    setShowcaseStep('Ready')
    setRemainingSeconds(0)
    setInjectedAnnouncement(undefined)
    setState(createState(gameId, config))
  }, [clearTimers, config, gameId])

  const spin = useCallback(() => {
    const id = ++roundId.current
    setRemainingSeconds(300)
    setState(current => ({
      ...current,
      speedrunState: {
        ...current.speedrunState,
        currentRound: {
          id,
          category: selectedCategory,
          status: SpeedrunRoundStatus.Ready,
          timeLeftSeconds: 300,
        },
      },
    }))
    addEvent({ type: NoticeType.Normal, message: 'Choosing the next category' })
  }, [addEvent, selectedCategory])

  const start = useCallback(() => {
    setRemainingSeconds(300)
    setState(current => {
      const round = current.speedrunState?.currentRound
      const category = round?.category ?? selectedCategory
      return {
        ...current,
        speedrunState: {
          ...current.speedrunState,
          currentRound: {
            ...round,
            id: round?.id ?? ++roundId.current,
            category,
            status: SpeedrunRoundStatus.Running,
            startedAtUtc: Date.now(),
            timeLeftSeconds: 300,
          },
          remainingCategories: (current.speedrunState?.remainingCategories ?? []).filter(value => value !== category),
        },
      }
    })
    addEvent({ type: NoticeType.Normal, message: `${selectedCategory} round is now live` })
  }, [addEvent, selectedCategory])

  const scoreTeam = useCallback((points: number, bloodType?: NoticeType) => {
    const teamName = PREVIEW_TEAMS[selectedTeamId - 1]?.[0] ?? 'Unknown team'
    setState(current => {
      const topTeams = (current.topTeams ?? []).map(team => {
        if (team.id !== selectedTeamId) return team
        return { ...team, score: (team.score ?? 0) + points, solvedCount: (team.solvedCount ?? 0) + 1 }
      }).sort((left, right) => (right.score ?? 0) - (left.score ?? 0))
        .map((team, index) => ({ ...team, rank: index + 1 }))
      return { ...current, topTeams }
    })

    if (bloodType) addEvent({
      type: bloodType,
      message: `${teamName} solved Tidal Lock`,
      teamId: selectedTeamId,
      teamName,
      challengeTitle: `${selectedCategory} — Tidal Lock`,
    })
    else addEvent({ type: NoticeType.Normal, message: `${teamName} solved a ${selectedCategory} challenge` })
  }, [addEvent, selectedCategory, selectedTeamId])

  const hint = useCallback(() => addEvent({
    type: NoticeType.NewHint,
    message: `Hint #${eventId.current % 3 + 1} released for Tidal Lock`,
    challengeTitle: `${selectedCategory} — Tidal Lock`,
  }), [addEvent, selectedCategory])

  const countdown = useCallback(() => {
    setRemainingSeconds(10)
    setState(current => ({
      ...current,
      speedrunState: {
        ...current.speedrunState,
        currentRound: {
          ...current.speedrunState?.currentRound,
          id: current.speedrunState?.currentRound?.id ?? ++roundId.current,
          category: current.speedrunState?.currentRound?.category ?? selectedCategory,
          status: SpeedrunRoundStatus.Running,
          timeLeftSeconds: 10,
        },
      },
    }))
  }, [selectedCategory])

  const overtime = useCallback(() => {
    setRemainingSeconds(180)
    setState(current => ({
      ...current,
      speedrunState: {
        ...current.speedrunState,
        currentRound: {
          ...current.speedrunState?.currentRound,
          id: current.speedrunState?.currentRound?.id ?? ++roundId.current,
          category: current.speedrunState?.currentRound?.category ?? selectedCategory,
          status: SpeedrunRoundStatus.Overtime,
          isOvertime: true,
          timeLeftSeconds: 180,
        },
      },
    }))
    addEvent({ type: NoticeType.Normal, message: 'The round has entered overtime' })
  }, [addEvent, selectedCategory])

  const finish = useCallback(() => {
    setRemainingSeconds(0)
    setState(current => {
      const category = current.speedrunState?.currentRound?.category
      return {
        ...current,
        speedrunState: {
          ...current.speedrunState,
          currentRound: null,
          usedCategories: category
            ? [...new Set([...(current.speedrunState?.usedCategories ?? []), category])]
            : current.speedrunState?.usedCategories,
        },
      }
    })
    addEvent({ type: NoticeType.Normal, message: 'Round finished and standings updated' })
  }, [addEvent])

  const reminder = useCallback(() => {
    setRemainingSeconds(60)
    setState(current => ({
      ...current,
      speedrunState: {
        ...current.speedrunState,
        currentRound: {
          ...current.speedrunState?.currentRound,
          id: current.speedrunState?.currentRound?.id ?? ++roundId.current,
          category: current.speedrunState?.currentRound?.category ?? selectedCategory,
          status: SpeedrunRoundStatus.Running,
          timeLeftSeconds: 60,
        },
      },
    }))
  }, [selectedCategory])

  const wrongSubmit = useCallback(() => {
    const teamName = state.topTeams?.find(team => team.id === selectedTeamId)?.name
      ?? PREVIEW_TEAMS[selectedTeamId - 1]?.[0]
      ?? 'Unknown team'
    setInjectedAnnouncement({
      key: `preview-wrong-${++eventId.current}`,
      kind: 'wrong',
      title: 'Incorrect submission',
      teamName,
      teamId: selectedTeamId,
      sound: 'wrongSubmit',
      sceneKind: 'wrong',
      showPopup: false,
      duration: 3400,
    })
  }, [selectedTeamId, state.topTeams])

  const schedule = useCallback((delay: number, step: string, action: () => void) => {
    timers.current.push(window.setTimeout(() => {
      setShowcaseStep(step)
      action()
    }, delay))
  }, [])

  const stopShowcase = useCallback(() => {
    clearTimers()
    setShowcaseRunning(false)
    setShowcaseStep('Stopped')
  }, [clearTimers])

  const runShowcase = useCallback(() => {
    reset()
    setShowcaseRunning(true)
    setShowcaseStep('Choosing category')
    spin()
    schedule(7200, 'Round started', start)
    schedule(10800, 'Pearl tether', () => scoreTeam(180))
    schedule(14800, 'Oracle illumination', hint)
    schedule(21400, 'Abyssal convergence', () => scoreTeam(260, NoticeType.FirstBlood))
    schedule(30200, 'Final countdown', countdown)
    schedule(41800, 'Overtime', overtime)
    schedule(46800, 'Round finished', finish)
    schedule(51000, 'Complete', () => setShowcaseRunning(false))
  }, [countdown, finish, hint, overtime, reset, schedule, scoreTeam, spin, start])

  useEffect(() => {
    const status = state.speedrunState?.currentRound?.status
    if (status !== SpeedrunRoundStatus.Running && status !== SpeedrunRoundStatus.Overtime) return
    const interval = window.setInterval(() => setRemainingSeconds(value => Math.max(0, value - 1)), 1000)
    return () => window.clearInterval(interval)
  }, [state.speedrunState?.currentRound?.status])

  useEffect(() => () => clearTimers(), [clearTimers])

  return {
    state,
    remainingSeconds,
    selectedTeamId,
    setSelectedTeamId,
    selectedCategory,
    setSelectedCategory,
    injectedAnnouncement,
    showcaseRunning,
    showcaseStep,
    reset,
    spin,
    start,
    solve: () => scoreTeam(150),
    firstBlood: () => scoreTeam(250, NoticeType.FirstBlood),
    secondBlood: () => scoreTeam(200, NoticeType.SecondBlood),
    thirdBlood: () => scoreTeam(175, NoticeType.ThirdBlood),
    hint,
    reminder,
    countdown,
    overtime,
    finish,
    wrongSubmit,
    runShowcase,
    stopShowcase,
  }
}
