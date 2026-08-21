export type StageSoundName =
  | 'spin' | 'categorySelected' | 'gameStart' | 'hintDrop' | 'firstBlood' | 'secondBlood' | 'thirdBlood'
  | 'correctSubmit' | 'wrongSubmit' | 'reminder' | 'countdownTick' | 'overtime' | 'roundFinished' | 'scoreUpdate'

export type AnnouncementKind =
  | 'spin' | 'category' | 'start' | 'firstBlood' | 'blood' | 'hint' | 'correct' | 'wrong'
  | 'reminder' | 'countdown' | 'overtime' | 'finished'

export type LiveSceneKind = 'firstBlood' | 'blood' | 'hint' | 'correct' | 'wrong'

export interface LiveAnnouncement {
  key: string
  kind: AnnouncementKind
  title: string
  text?: string
  teamName?: string
  teamId?: number
  sound: StageSoundName
  duration?: number
  popupDelay?: number
  showPopup?: boolean
  sceneKind?: LiveSceneKind
}

export interface LiveCategoryVisual {
  category: string
  icon: string
  color: string
  glow: string
}

export type LiveSpinPhase = 'idle' | 'spinning' | 'revealed'

export interface LivePresentationState {
  announcement?: LiveAnnouncement
  spinPhase: LiveSpinPhase
  changedTeams: Set<number>
  bloodAttackTeams: Set<number>
}
