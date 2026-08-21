#!/usr/bin/env python3
"""Generate the original Deep Sea Cinematic live-scoreboard sound pack."""

from __future__ import annotations

import hashlib
import math
import random
import wave
from array import array
from pathlib import Path

SAMPLE_RATE = 32_000
HEADROOM = 0.88
ROOT_DIR = Path(__file__).resolve().parents[1]
OUTPUT_DIR = ROOT_DIR / "src/GZCTF/ClientApp/src/assets/audio/live-scoreboard"
FIRST_BLOOD_VOICE = ROOT_DIR / "tools/audio/first-blood-announcer.wav"


def envelope(position: float, duration: float, attack: float, release: float) -> float:
    if position < 0 or position >= duration:
        return 0.0
    attack_gain = min(1.0, position / max(attack, 1e-5))
    release_gain = min(1.0, (duration - position) / max(release, 1e-5))
    return math.sin(min(attack_gain, release_gain) * math.pi / 2) ** 2


def add_tone(
    samples: list[float], start: float, duration: float, frequency: float,
    gain: float, *, end_frequency: float | None = None, attack: float = 0.015,
    release: float = 0.25, harmonics: tuple[tuple[float, float], ...] = ((1.0, 1.0),),
    tremolo: tuple[float, float] | None = None,
) -> None:
    first = max(0, int(start * SAMPLE_RATE))
    count = int(duration * SAMPLE_RATE)
    phase = 0.0
    for offset in range(count):
        index = first + offset
        if index >= len(samples):
            break
        t = offset / SAMPLE_RATE
        progress = t / duration
        target = end_frequency if end_frequency is not None else frequency
        current_frequency = frequency * ((target / frequency) ** progress)
        phase += 2 * math.pi * current_frequency / SAMPLE_RATE
        value = sum(weight * math.sin(phase * multiple) for multiple, weight in harmonics)
        if tremolo:
            value *= 1 - tremolo[1] + tremolo[1] * (.5 + .5 * math.sin(2 * math.pi * tremolo[0] * t))
        samples[index] += gain * envelope(t, duration, attack, release) * value


def add_current(
    samples: list[float], rng: random.Random, start: float, duration: float,
    gain: float, cutoff: float, *, attack: float = .3, release: float = .4,
    rising: bool = False,
) -> None:
    first = max(0, int(start * SAMPLE_RATE))
    count = int(duration * SAMPLE_RATE)
    state = 0.0
    for offset in range(count):
        index = first + offset
        if index >= len(samples):
            break
        t = offset / SAMPLE_RATE
        progress = t / duration
        local_cutoff = cutoff * (0.55 + progress * 1.35) if rising else cutoff
        alpha = 1 - math.exp(-2 * math.pi * local_cutoff / SAMPLE_RATE)
        state += alpha * (rng.uniform(-1, 1) - state)
        pulse = .72 + .28 * math.sin(2 * math.pi * (0.22 + progress * .35) * t)
        samples[index] += gain * envelope(t, duration, attack, release) * state * pulse


def add_bubble(
    samples: list[float], start: float, frequency: float, gain: float,
    duration: float = .22,
) -> None:
    add_tone(
        samples, start, duration, frequency, gain,
        end_frequency=frequency * 2.15, attack=.006, release=duration * .58,
        harmonics=((1, 1), (2.03, .22), (3.9, .06)),
    )


def add_bubble_stream(
    samples: list[float], rng: random.Random, start: float, duration: float,
    count: int, gain: float, low: float = 420, high: float = 1250,
) -> None:
    for _ in range(count):
        at = start + rng.random() * duration
        frequency = rng.uniform(low, high)
        add_bubble(samples, at, frequency, gain * rng.uniform(.55, 1.0), rng.uniform(.12, .28))


def add_pearl(
    samples: list[float], start: float, frequency: float, gain: float,
    duration: float = 1.1,
) -> None:
    add_tone(
        samples, start, duration, frequency, gain, attack=.008,
        release=duration * .82,
        harmonics=((1, 1), (2.01, .34), (3.98, .16), (6.11, .07)),
        tremolo=(4.2, .16),
    )
    add_tone(
        samples, start + .035, duration * .78, frequency * 1.503, gain * .24,
        attack=.015, release=duration * .7,
        harmonics=((1, 1), (2.0, .18)),
    )


def add_pressure_hit(samples: list[float], start: float, gain: float, duration: float = .9) -> None:
    add_tone(
        samples, start, duration, 78, gain, end_frequency=34,
        attack=.008, release=duration * .88,
        harmonics=((1, 1), (2, .22), (3, .08)),
    )


def add_echo(samples: list[float], delay: float, decay: float, repeats: int = 2) -> None:
    original = samples.copy()
    for repeat in range(1, repeats + 1):
        shift = int(delay * repeat * SAMPLE_RATE)
        weight = decay ** repeat
        for index in range(shift, len(samples)):
            samples[index] += original[index - shift] * weight


def add_accelerating_riser(
    samples: list[float], start: float, duration: float, gain: float,
    start_frequency: float, end_frequency: float,
) -> None:
    first = int(start * SAMPLE_RATE)
    count = int(duration * SAMPLE_RATE)
    phase = 0.0
    pulse_phase = 0.0
    for offset in range(count):
        index = first + offset
        if index >= len(samples):
            break
        t = offset / SAMPLE_RATE
        progress = t / duration
        curve = progress ** 1.7
        frequency = start_frequency * ((end_frequency / start_frequency) ** curve)
        phase += 2 * math.pi * frequency / SAMPLE_RATE
        pulse_rate = 1.15 + progress ** 2.2 * 8.5
        pulse_phase += 2 * math.pi * pulse_rate / SAMPLE_RATE
        pulse = .36 + .64 * (.5 + .5 * math.sin(pulse_phase)) ** 2
        brightness = (
            math.sin(phase) +
            (.1 + progress * .22) * math.sin(phase * 2.01) +
            progress * .1 * math.sin(phase * 3.97)
        )
        rise = .12 + .88 * MathCurve.smooth(progress)
        samples[index] += gain * rise * pulse * envelope(t, duration, .18, .12) * brightness


def add_impact_crack(
    samples: list[float], rng: random.Random, start: float, gain: float,
    duration: float = .24,
) -> None:
    first = int(start * SAMPLE_RATE)
    count = int(duration * SAMPLE_RATE)
    low_state = 0.0
    for offset in range(count):
        index = first + offset
        if index >= len(samples):
            break
        t = offset / SAMPLE_RATE
        noise = rng.uniform(-1, 1)
        low_state += .16 * (noise - low_state)
        grit = noise - low_state * .72
        decay = math.exp(-t * 24)
        samples[index] += gain * decay * grit


def read_mono_pcm16(path: Path) -> tuple[list[float], int]:
    if not path.exists():
        raise FileNotFoundError(
            f"Missing announcer source: {path}. Run tools/generate_first_blood_voice.ps1 first."
        )
    with wave.open(str(path), "rb") as source:
        if source.getnchannels() != 1 or source.getsampwidth() != 2 or source.getcomptype() != "NONE":
            raise ValueError(f"Announcer source must be mono 16-bit PCM WAV: {path}")
        pcm = array("h")
        pcm.frombytes(source.readframes(source.getnframes()))
        return [sample / 32768 for sample in pcm], source.getframerate()


def resample(samples: list[float], source_rate: int, target_rate: int, speed: float = 1.0) -> list[float]:
    output_count = max(1, int(len(samples) * target_rate / source_rate / speed))
    step = source_rate * speed / target_rate
    output = [0.0] * output_count
    for index in range(output_count):
        position = index * step
        left = min(int(position), len(samples) - 1)
        right = min(left + 1, len(samples) - 1)
        fraction = position - left
        output[index] = samples[left] * (1 - fraction) + samples[right] * fraction
    return output


def add_announcer(
    samples: list[float], start: float, gain: float,
    source_path: Path = FIRST_BLOOD_VOICE,
) -> None:
    voice, source_rate = read_mono_pcm16(source_path)
    threshold = .0025
    active = [index for index, sample in enumerate(voice) if abs(sample) > threshold]
    if not active:
        raise ValueError(f"Announcer source contains no audible speech: {source_path}")
    padding = int(source_rate * .025)
    voice = voice[max(0, active[0] - padding):min(len(voice), active[-1] + padding)]
    voice = resample(voice, source_rate, SAMPLE_RATE, speed=.9)

    peak = max(abs(sample) for sample in voice) or 1
    voice = [sample / peak for sample in voice]
    duration = len(voice) / SAMPLE_RATE
    body_state = 0.0
    processed = [0.0] * len(voice)
    for offset, sample in enumerate(voice):
        t = offset / SAMPLE_RATE
        body_state += .035 * (sample - body_state)
        presence = sample + body_state * .32
        processed[offset] = gain * envelope(t, duration, .018, .12) * presence

    first = int(start * SAMPLE_RATE)
    for offset, sample in enumerate(processed):
        index = first + offset
        if index < len(samples):
            samples[index] += sample

    for delay, weight in ((.105, .23), (.205, .16), (.36, .09), (.59, .045)):
        shift = int(delay * SAMPLE_RATE)
        for offset, sample in enumerate(processed):
            index = first + shift + offset
            if index >= len(samples):
                break
            samples[index] += sample * weight


class MathCurve:
    @staticmethod
    def smooth(value: float) -> float:
        value = max(0.0, min(1.0, value))
        return value * value * (3 - 2 * value)


def make_buffer(duration: float) -> list[float]:
    return [0.0] * int(duration * SAMPLE_RATE)


def spin(rng: random.Random) -> list[float]:
    out = make_buffer(6.65)
    add_current(out, rng, 0, 6.55, .58, 175, attack=.5, rising=True)
    add_tone(out, .05, 6.42, 54, .18, end_frequency=112, attack=.55, release=.42,
             harmonics=((1, 1), (2, .19)), tremolo=(1.8, .35))
    for index in range(13):
        at = .5 + index * .44
        add_bubble(out, at, 300 + index * 58, .08 + index * .004, .28)
    add_bubble_stream(out, rng, .2, 5.9, 34, .055, 370, 1100)
    add_pearl(out, 5.78, 392, .18, .82)
    return out


def category_selected(rng: random.Random) -> list[float]:
    out = make_buffer(2.65)
    add_current(out, rng, 0, 1.38, .34, 240, attack=.06, release=.25, rising=True)
    add_tone(out, .0, 1.45, 180, .23, end_frequency=690, attack=.04, release=.25,
             harmonics=((1, 1), (2, .14)))
    add_pressure_hit(out, 1.12, .42, .68)
    add_pearl(out, 1.18, 523.25, .46, 1.35)
    add_pearl(out, 1.32, 783.99, .25, 1.1)
    add_bubble_stream(out, rng, 1.05, .85, 11, .055, 650, 1450)
    add_echo(out, .14, .22, 2)
    return out


def game_start(rng: random.Random) -> list[float]:
    out = make_buffer(2.85)
    add_current(out, rng, 0, 1.75, .43, 135, attack=.16, release=.22, rising=True)
    add_tone(out, .05, 1.42, 72, .34, end_frequency=146, attack=.08, release=.26,
             harmonics=((1, 1), (2, .2), (3, .08)))
    add_pressure_hit(out, 1.08, .55, .86)
    for at, freq, gain in [(1.18, 261.63, .27), (1.42, 392, .31), (1.66, 523.25, .42)]:
        add_pearl(out, at, freq, gain, 1.05)
    add_bubble_stream(out, rng, 1.15, 1.0, 9, .04, 500, 1050)
    return out


def hint_drop(rng: random.Random) -> list[float]:
    out = make_buffer(2.75)
    add_current(out, rng, 0, 1.9, .18, 390, attack=.25, release=.5, rising=True)
    add_tone(out, .2, 1.0, 420, .13, end_frequency=920, attack=.3, release=.28,
             harmonics=((1, 1), (2.02, .12)))
    add_bubble_stream(out, rng, .55, .85, 14, .055, 680, 1700)
    add_pearl(out, 1.2, 880, .31, 1.42)
    add_pearl(out, 1.42, 1174.66, .22, 1.15)
    add_echo(out, .18, .24, 2)
    return out


def first_blood(rng: random.Random) -> list[float]:
    out = make_buffer(6.75)

    # Ascension and tether approach: every layer accelerates into the attach point.
    add_current(out, rng, 0, 3.42, .5, 105, attack=.22, release=.09, rising=True)
    add_tone(out, .02, 3.37, 42, .24, end_frequency=118, attack=.18, release=.08,
             harmonics=((1, 1), (2, .28), (3, .08)), tremolo=(1.35, .24))
    add_accelerating_riser(out, .08, 3.3, .34, 94, 940)
    add_accelerating_riser(out, .76, 2.62, .2, 175, 1480)
    for index in range(24):
        progress = index / 23
        at = .48 + 2.7 * (1 - (1 - progress) ** 1.55)
        add_bubble(out, at, 330 + progress * 1050, .035 + progress * .055, .23 - progress * .07)

    # Attachment at 3.42 s: transient definition, physical sub pressure, and audible harmonics.
    impact = 3.42
    add_impact_crack(out, rng, impact, 1.25, .27)
    add_pressure_hit(out, impact, 1.55, 1.48)
    add_tone(out, impact, 2.25, 42, 1.18, end_frequency=31, attack=.004, release=2.05,
             harmonics=((1, 1), (2, .5), (3, .24), (4, .1)))
    add_tone(out, impact + .012, 1.25, 82, .68, end_frequency=53, attack=.003, release=1.05,
             harmonics=((1, 1), (2, .24), (3, .08)))
    add_current(out, rng, impact, .82, 1.15, 920, attack=.002, release=.66)
    add_bubble_stream(out, rng, impact + .04, 1.16, 34, .085, 480, 1900)

    # Pearl bloom and cavern tail open after the room-shaking hit.
    for at, freq, gain, duration in [
        (impact + .1, 196, .36, 2.55),
        (impact + .25, 392, .44, 2.35),
        (impact + .42, 587.33, .38, 2.1),
        (impact + .61, 783.99, .32, 1.86),
        (impact + .82, 1174.66, .2, 1.48),
    ]:
        add_pearl(out, at, freq, gain, duration)

    # Announcer enters after the transient, before the scene popup at 5 seconds.
    add_announcer(out, impact + .47, .74)
    add_echo(out, .29, .12, 3)
    return out


def blood_cue(rng: random.Random, second: bool) -> list[float]:
    out = make_buffer(2.55)
    gain = .62 if second else .52
    add_pressure_hit(out, 0, gain, .82)
    add_current(out, rng, .02, .88, .3, 440, attack=.01, release=.45)
    add_bubble_stream(out, rng, .1, .78, 12 if second else 9, .05, 480, 1350)
    notes = (293.66, 587.33) if second else (246.94, 493.88)
    add_pearl(out, .44, notes[0], .3, 1.45)
    add_pearl(out, .72 if second else .88, notes[1], .26, 1.38)
    if second:
        add_pearl(out, 1.02, 880, .13, .95)
    add_echo(out, .16, .18, 2)
    return out


def correct_submit(rng: random.Random) -> list[float]:
    out = make_buffer(1.75)
    add_current(out, rng, 0, .86, .28, 330, attack=.03, release=.17, rising=True)
    add_tone(out, .02, .72, 220, .17, end_frequency=660, attack=.02, release=.16)
    add_bubble_stream(out, rng, .08, .58, 8, .042, 520, 1150)
    add_pressure_hit(out, .63, .28, .48)
    add_pearl(out, .65, 523.25, .31, .95)
    add_pearl(out, .79, 783.99, .18, .82)
    return out


def wrong_submit(rng: random.Random) -> list[float]:
    out = make_buffer(1.45)
    add_current(out, rng, 0, .72, .34, 490, attack=.01, release=.32)
    add_tone(out, 0, .58, 430, .18, end_frequency=175, attack=.01, release=.13,
             harmonics=((1, 1), (1.49, .25)))
    add_tone(out, .38, .82, 146, .28, end_frequency=83, attack=.015, release=.64,
             harmonics=((1, 1), (2, .16)), tremolo=(7.0, .28))
    for at, freq in [(.13, 760), (.25, 510), (.37, 340)]:
        add_bubble(out, at, freq, .045, .12)
    return out


def reminder(rng: random.Random) -> list[float]:
    out = make_buffer(1.8)
    add_current(out, rng, 0, 1.35, .1, 220, attack=.18, release=.6)
    add_pearl(out, .08, 493.88, .29, 1.35)
    add_pearl(out, .31, 739.99, .14, 1.05)
    add_bubble(out, .18, 920, .045, .2)
    add_echo(out, .2, .2, 2)
    return out


def countdown_tick(rng: random.Random) -> list[float]:
    del rng
    out = make_buffer(.42)
    add_pressure_hit(out, 0, .25, .3)
    add_bubble(out, .018, 690, .19, .22)
    add_pearl(out, .03, 760, .16, .34)
    return out


def overtime(rng: random.Random) -> list[float]:
    out = make_buffer(3.2)
    add_current(out, rng, 0, 3.0, .22, 95, attack=.16, release=.5)
    for at in (0, .88, 1.76):
        add_pressure_hit(out, at, .52, .68)
        add_tone(out, at + .04, .72, 116, .2, end_frequency=84,
                 attack=.01, release=.52, harmonics=((1, 1), (2, .22)))
    add_pearl(out, 2.1, 220, .17, .86)
    return out


def round_finished(rng: random.Random) -> list[float]:
    out = make_buffer(3.5)
    add_current(out, rng, 0, 3.15, .13, 145, attack=.25, release=1.2)
    add_pressure_hit(out, 0, .34, .78)
    for at, freq, gain in [(0.2, 523.25, .3), (.56, 392, .28), (.92, 293.66, .25), (1.34, 196, .21)]:
        add_pearl(out, at, freq, gain, 1.6)
    add_bubble_stream(out, rng, .4, 1.75, 12, .035, 450, 900)
    add_echo(out, .24, .17, 2)
    return out


def score_update(rng: random.Random) -> list[float]:
    del rng
    out = make_buffer(.9)
    add_bubble(out, .02, 610, .16, .28)
    add_pearl(out, .03, 622.25, .18, .58)
    add_bubble(out, .25, 820, .15, .27)
    add_pearl(out, .26, 830.61, .2, .58)
    return out


BUILDERS = {
    "spin": spin,
    "category-selected": category_selected,
    "game-start": game_start,
    "hint-drop": hint_drop,
    "first-blood": first_blood,
    "second-blood": lambda rng: blood_cue(rng, True),
    "third-blood": lambda rng: blood_cue(rng, False),
    "correct-submit": correct_submit,
    "wrong-submit": wrong_submit,
    "reminder": reminder,
    "countdown-tick": countdown_tick,
    "overtime": overtime,
    "round-finished": round_finished,
    "score-update": score_update,
}


def finalize(samples: list[float]) -> tuple[array, float]:
    fade = min(len(samples) // 2, int(.025 * SAMPLE_RATE))
    for index in range(fade):
        factor = math.sin((index + 1) / fade * math.pi / 2) ** 2
        samples[index] *= factor
        samples[-index - 1] *= factor

    mean = sum(samples) / len(samples)
    samples = [sample - mean for sample in samples]
    peak = max(abs(sample) for sample in samples) or 1.0
    scale = HEADROOM / peak
    pcm = array("h", (round(max(-1, min(1, sample * scale)) * 32767) for sample in samples))
    return pcm, HEADROOM


def write_wave(path: Path, samples: array) -> None:
    with wave.open(str(path), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(SAMPLE_RATE)
        output.writeframes(samples.tobytes())


def main() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    expected = {f"{name}.wav" for name in BUILDERS}
    for stale in OUTPUT_DIR.glob("*.wav"):
        if stale.name not in expected:
            stale.unlink()

    total_size = 0
    print(f"Generating Deep Sea Cinematic pack at {SAMPLE_RATE} Hz")
    for name, builder in BUILDERS.items():
        seed = int.from_bytes(hashlib.sha256(name.encode()).digest()[:8], "little")
        samples = builder(random.Random(seed))
        pcm, peak = finalize(samples)
        path = OUTPUT_DIR / f"{name}.wav"
        write_wave(path, pcm)
        total_size += path.stat().st_size
        duration = len(pcm) / SAMPLE_RATE
        print(f"  {path.name:24} {duration:5.2f}s  peak={peak:.2f}  {path.stat().st_size / 1024:6.1f} KiB")

    print(f"Generated {len(BUILDERS)} cues ({total_size / 1024 / 1024:.2f} MiB total)")


if __name__ == "__main__":
    main()
