import categorySelected from '../../assets/audio/live-scoreboard/category-selected.wav'
import correctSubmit from '../../assets/audio/live-scoreboard/correct-submit.wav'
import countdownTick from '../../assets/audio/live-scoreboard/countdown-tick.wav'
import firstBlood from '../../assets/audio/live-scoreboard/first-blood.wav'
import gameStart from '../../assets/audio/live-scoreboard/game-start.wav'
import hintDrop from '../../assets/audio/live-scoreboard/hint-drop.wav'
import overtime from '../../assets/audio/live-scoreboard/overtime.wav'
import reminder from '../../assets/audio/live-scoreboard/reminder.wav'
import roundFinished from '../../assets/audio/live-scoreboard/round-finished.wav'
import scoreUpdate from '../../assets/audio/live-scoreboard/score-update.wav'
import secondBlood from '../../assets/audio/live-scoreboard/second-blood.wav'
import spin from '../../assets/audio/live-scoreboard/spin.wav'
import thirdBlood from '../../assets/audio/live-scoreboard/third-blood.wav'
import wrongSubmit from '../../assets/audio/live-scoreboard/wrong-submit.wav'
import { StageSoundName } from '@Components/live/types'

export const stageSoundPack: Record<StageSoundName, string> = {
  spin,
  categorySelected,
  gameStart,
  hintDrop,
  firstBlood,
  secondBlood,
  thirdBlood,
  correctSubmit,
  wrongSubmit,
  reminder,
  countdownTick,
  overtime,
  roundFinished,
  scoreUpdate,
}
