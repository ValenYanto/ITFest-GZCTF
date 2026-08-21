import { useCallback, useEffect, useRef, useState } from 'react'
import { LiveScoreboardConfigModel } from '@Api'
import { stageSoundPack } from '@Components/live/stageSoundPack'
import { StageSoundName } from '@Components/live/types'

const patterns: Record<StageSoundName, [number, number, OscillatorType][]> = {
  spin: [[180, .22, 'sine'], [220, .22, 'sine'], [270, .22, 'sine'], [330, .22, 'sine'], [400, .22, 'sine'],
    [480, .22, 'sine'], [570, .22, 'sine'], [680, .22, 'sine'], [810, .25, 'sine'], [960, .32, 'sine']],
  categorySelected: [[523, .16, 'sine'], [659, .16, 'sine'], [784, .3, 'sine']],
  gameStart: [[262, .18, 'triangle'], [392, .18, 'triangle'], [523, .45, 'triangle']],
  hintDrop: [[880, .1, 'sine'], [1175, .12, 'sine'], [1568, .25, 'sine']],
  firstBlood: [[90, .35, 'sawtooth'], [180, .25, 'triangle'], [720, .45, 'sine']],
  secondBlood: [[130, .25, 'sawtooth'], [520, .3, 'sine']],
  thirdBlood: [[160, .22, 'triangle'], [440, .28, 'sine']],
  correctSubmit: [[523, .12, 'sine'], [659, .12, 'sine'], [784, .2, 'sine']],
  wrongSubmit: [[220, .16, 'square'], [155, .25, 'square']],
  reminder: [[500, .22, 'triangle']],
  countdownTick: [[760, .09, 'square']],
  overtime: [[180, .2, 'sawtooth'], [260, .2, 'sawtooth'], [180, .3, 'sawtooth']],
  roundFinished: [[523, .18, 'sine'], [392, .18, 'sine'], [262, .35, 'sine']],
  scoreUpdate: [[620, .08, 'sine'], [820, .12, 'sine']],
}

export const useStageSound = (config?: LiveScoreboardConfigModel) => {
  const [enabled, setEnabled] = useState(false)
  const context = useRef<AudioContext | undefined>(undefined)
  const preloaded = useRef<HTMLAudioElement[]>([])
  const configRef = useRef(config)

  useEffect(() => {
    configRef.current = config
  }, [config])

  const unlock = useCallback(() => {
    const ctx = context.current ?? new AudioContext()
    context.current = ctx
    void ctx.resume()
    if (!preloaded.current.length) {
      preloaded.current = Object.values(stageSoundPack).map(url => {
        const audio = new Audio()
        audio.preload = 'auto'
        audio.src = url
        audio.load()
        return audio
      })
    }
    setEnabled(true)
  }, [])

  const fallback = useCallback((name: StageSoundName) => {
    const ctx = context.current
    if (!ctx) return
    const volume = configRef.current?.volume ?? .75

    if (name === 'firstBlood') {
      const now = ctx.currentTime
      const master = ctx.createGain()
      const compressor = ctx.createDynamicsCompressor()
      master.gain.setValueAtTime(volume * .48, now)
      master.gain.exponentialRampToValueAtTime(.001, now + 2.7)
      master.connect(compressor).connect(ctx.destination)

      const impact = ctx.createOscillator()
      const impactGain = ctx.createGain()
      impact.type = 'sine'
      impact.frequency.setValueAtTime(82, now)
      impact.frequency.exponentialRampToValueAtTime(38, now + .7)
      impactGain.gain.setValueAtTime(.001, now)
      impactGain.gain.exponentialRampToValueAtTime(.9, now + .025)
      impactGain.gain.exponentialRampToValueAtTime(.001, now + .9)
      impact.connect(impactGain).connect(master)
      impact.start(now)
      impact.stop(now + .95)

      const swell = ctx.createOscillator()
      const swellFilter = ctx.createBiquadFilter()
      const swellGain = ctx.createGain()
      swell.type = 'sawtooth'
      swell.frequency.setValueAtTime(110, now + .08)
      swell.frequency.exponentialRampToValueAtTime(245, now + 1.5)
      swellFilter.type = 'lowpass'
      swellFilter.frequency.setValueAtTime(240, now)
      swellFilter.frequency.exponentialRampToValueAtTime(1100, now + 1.45)
      swellGain.gain.setValueAtTime(.001, now)
      swellGain.gain.exponentialRampToValueAtTime(.24, now + .45)
      swellGain.gain.exponentialRampToValueAtTime(.001, now + 1.75)
      swell.connect(swellFilter).connect(swellGain).connect(master)
      swell.start(now + .06)
      swell.stop(now + 1.8)

      for (const [index, frequency] of [392, 587, 784, 1175].entries()) {
        const voice = ctx.createOscillator()
        const gain = ctx.createGain()
        const at = now + .82 + index * .17
        voice.type = index < 2 ? 'triangle' : 'sine'
        voice.frequency.setValueAtTime(frequency, at)
        gain.gain.setValueAtTime(.001, at)
        gain.gain.exponentialRampToValueAtTime(.2, at + .035)
        gain.gain.exponentialRampToValueAtTime(.001, at + .72)
        voice.connect(gain).connect(master)
        voice.start(at)
        voice.stop(at + .75)
      }
      return
    }

    let at = ctx.currentTime
    for (const [frequency, duration, type] of patterns[name]) {
      const oscillator = ctx.createOscillator()
      const gain = ctx.createGain()
      oscillator.type = type
      oscillator.frequency.setValueAtTime(frequency, at)
      gain.gain.setValueAtTime(volume * .14, at)
      gain.gain.exponentialRampToValueAtTime(.001, at + duration)
      oscillator.connect(gain).connect(ctx.destination)
      oscillator.start(at)
      oscillator.stop(at + duration)
      at += duration * .75
    }
  }, [])

  const play = useCallback((name: StageSoundName) => {
    const currentConfig = configRef.current
    if (!enabled || !currentConfig?.soundEnabled) return
    const url = currentConfig.sounds?.[name]?.trim() || stageSoundPack[name]
    const audio = new Audio(url)
    audio.volume = currentConfig.volume ?? .75
    void audio.play().catch(() => fallback(name))
  }, [enabled, fallback])

  return { audioEnabled: enabled, unlockAudio: unlock, play }
}
