import { Html } from '@react-three/drei'
import { Canvas, useFrame, useThree } from '@react-three/fiber'
import { CSSProperties, FC, RefObject, useMemo, useRef } from 'react'
import {
  AdditiveBlending,
  CatmullRomCurve3,
  Color,
  Group,
  InstancedMesh,
  MathUtils,
  Mesh,
  MeshBasicMaterial,
  MeshPhysicalMaterial,
  Object3D,
  ShaderMaterial,
  TubeGeometry,
  Vector3,
} from 'three'
import {
  ChallengeCategory,
  LiveScoreboardTeamModel,
  LiveScoreboardVisualIntensity,
} from '@Api'
import { LiveCategoryVisual, LiveSceneKind, LiveSpinPhase } from '@Components/live/types'
import classes from '@Styles/LiveScoreboard.module.css'

interface SunkenColosseumSceneProps {
  teams: LiveScoreboardTeamModel[]
  attackingTeams: Set<number>
  bloodTeams: Set<number>
  sceneKind?: LiveSceneKind
  sceneTeamId?: number
  spinPhase: LiveSpinPhase
  selected?: ChallengeCategory
  categories: ChallengeCategory[]
  categoryVisuals: LiveCategoryVisual[]
  intensity?: LiveScoreboardVisualIntensity
  reducedMotion: boolean
  frozen?: boolean
}

const stone = '#183a42'
const stoneDark = '#0a2029'
const aqua = '#66eadb'
const pearl = '#edfffa'
const coral = '#ff5964'
interface TeamOrbitSlot {
  angle: number
  radiusX: number
  radiusZ: number
  height: number
  ring: number
}

const teamColor = (id: number) => `hsl(${Math.abs(id * 137.508) % 360} 72% 68%)`

const distributeTeams = (teams: LiveScoreboardTeamModel[]): Map<number, TeamOrbitSlot> => {
  const slots = new Map<number, TeamOrbitSlot>()
  if (!teams.length) return slots

  const stableTeams = [...teams].sort((left, right) => (left.id ?? 0) - (right.id ?? 0))
  const capacities: number[] = []
  let capacity = 0
  for (let ring = 0; capacity < stableTeams.length; ring += 1) {
    const ringCapacity = 10 + ring * 4
    capacities.push(ringCapacity)
    capacity += ringCapacity
  }

  const counts = capacities.map((ringCapacity, ring) => {
    const previousCapacity = capacities.slice(0, ring).reduce((total, value) => total + value, 0)
    return Math.min(ringCapacity, Math.max(0, stableTeams.length - previousCapacity))
  })

  let teamIndex = 0
  counts.forEach((count, ring) => {
    const radiusX = 4.2 + ring * .72
    const radiusZ = 2.15 + ring * .48
    const stagger = ring % 2 ? Math.PI / Math.max(count, 1) : 0
    for (let index = 0; index < count; index += 1) {
      const team = stableTeams[teamIndex++]
      if (!team) continue
      slots.set(team.id ?? teamIndex, {
        angle: index / Math.max(count, 1) * Math.PI * 2 + stagger,
        radiusX,
        radiusZ,
        height: -.62 + ring * .14,
        ring,
      })
    }
  })

  return slots
}

const OrbitClock: FC<{
  angle: RefObject<number>
  paused: boolean
  reducedMotion: boolean
}> = ({ angle, paused, reducedMotion }) => {
  useFrame((_, delta) => {
    if (!paused) angle.current += delta * (reducedMotion ? .012 : .035)
  })
  return null
}

const CameraRig: FC<{
  spinPhase: LiveSpinPhase
  sceneKind?: LiveSceneKind
  focusPosition: RefObject<Vector3>
  reducedMotion: boolean
}> = ({ spinPhase, sceneKind, focusPosition, reducedMotion }) => {
  const { camera } = useThree()
  const lookAt = useMemo(() => new Vector3(), [])
  const sceneStarted = useRef<number | undefined>(undefined)
  const previousScene = useRef(sceneKind)

  useFrame(({ clock }, delta) => {
    if (previousScene.current !== sceneKind) {
      previousScene.current = sceneKind
      sceneStarted.current = clock.elapsedTime
    }
    sceneStarted.current ??= clock.elapsedTime
    const elapsed = clock.elapsedTime - sceneStarted.current
    const spinning = spinPhase === 'spinning'
    const firstBlood = sceneKind === 'firstBlood'
    const hint = sceneKind === 'hint'
    const correct = sceneKind === 'correct'
    const winner = focusPosition.current
    const followWinner = firstBlood && elapsed < 2.7
    const targetZ = hint ? 8.4 : firstBlood ? elapsed < 3.65 ? 8.9 : 9.65 : correct ? 10.35 : sceneKind === 'blood' ? 10.55 : spinning ? 10.2 : 11.75
    const targetY = hint ? 1.7 : firstBlood ? 2.15 : correct ? 2.2 : spinning ? 2.05 : 2.5
    const drift = reducedMotion ? 0 : Math.sin(clock.elapsedTime * .09) * .12
    const tension = firstBlood ? MathUtils.smoothstep(elapsed, 3.35, 3.72) * (1 - MathUtils.smoothstep(elapsed, 3.72, 4.15)) : 0
    const shake = tension * (reducedMotion ? .045 : .16)
    const shakeX = Math.sin(clock.elapsedTime * 58) * shake
    const shakeY = Math.sin(clock.elapsedTime * 71 + .8) * shake * .65
    const targetX = (followWinner ? winner.x * .32 : correct ? winner.x * .16 : firstBlood ? 0 : drift) + shakeX

    camera.position.z = MathUtils.damp(camera.position.z, targetZ, firstBlood || hint || correct ? 1.6 : 2.2, delta)
    camera.position.y = MathUtils.damp(camera.position.y, targetY + shakeY, hint ? 2.8 : tension ? 16 : 2.2, delta)
    camera.position.x = MathUtils.damp(camera.position.x, targetX, tension ? 18 : firstBlood || correct ? 1.8 : 2.2, delta)
    lookAt.set((followWinner ? winner.x * .3 : correct ? winner.x * .14 : 0) - shakeX * .45, (hint ? -.48 : followWinner ? winner.y * .28 : correct ? winner.y * .1 : spinning ? -.35 : -.55) + shakeY * .38, 0)
    camera.lookAt(lookAt)
  })
  return null
}

const Sediment: FC<{ reducedMotion: boolean; hype: boolean }> = ({ reducedMotion, hype }) => {
  const mesh = useRef<InstancedMesh>(null)
  const dummy = useMemo(() => new Object3D(), [])
  const particles = useMemo(() => Array.from({ length: 150 }, (_, index) => ({
    x: ((index * 47) % 101) / 100 * 18 - 9,
    y: ((index * 67) % 97) / 96 * 10 - 3.5,
    z: ((index * 31) % 89) / 88 * 10 - 5,
    scale: .012 + index % 5 * .007,
    pace: .025 + index % 7 * .008,
  })), [])

  useFrame(({ clock }) => {
    if (!mesh.current) return
    particles.forEach((item, index) => {
      const drift = reducedMotion ? 0 : clock.elapsedTime * item.pace
      dummy.position.set(item.x + Math.sin(drift * 1.7 + index) * .18, ((item.y + drift + 3.5) % 10) - 3.5, item.z)
      dummy.scale.setScalar(item.scale * (hype ? 1.2 : 1))
      dummy.updateMatrix()
      mesh.current!.setMatrixAt(index, dummy.matrix)
    })
    mesh.current.instanceMatrix.needsUpdate = true
  })

  return <instancedMesh ref={mesh} args={[undefined, undefined, particles.length]} frustumCulled={false}>
    <sphereGeometry args={[1, 5, 5]} />
    <meshBasicMaterial color="#b8eee6" transparent opacity={.28} depthWrite={false} />
  </instancedMesh>
}

const Seagrass: FC<{ position: [number, number, number]; scale?: number; phase?: number }> = ({ position, scale = 1, phase = 0 }) => {
  const grass = useRef<Group>(null)
  useFrame(({ clock }) => {
    if (grass.current) grass.current.rotation.z = Math.sin(clock.elapsedTime * .55 + phase) * .055
  })
  return <group ref={grass} position={position} scale={scale}>
    {[-.22, 0, .23].map((x, index) => <mesh key={x} position={[x, .48 + index * .08, 0]} rotation={[0, 0, x * .8]}>
      <capsuleGeometry args={[.035, .85 + index * .15, 4, 7]} />
      <meshStandardMaterial color={index === 1 ? '#1f766d' : '#175650'} roughness={1} />
    </mesh>)}
  </group>
}

const Colosseum: FC = () => {
  const columns = useMemo(() => Array.from({ length: 18 }, (_, index) => {
    const angle = Math.PI * .08 + index / 17 * Math.PI * .84
    return {
      position: [Math.cos(angle) * 8.2, -.85, -Math.sin(angle) * 8.2 + .6] as [number, number, number],
      height: 2.4 + index % 4 * .38,
      lean: (index % 5 - 2) * .025,
    }
  }), [])
  const rocks = useMemo(() => Array.from({ length: 28 }, (_, index) => {
    const angle = index / 28 * Math.PI * 2
    const radius = 6.1 + index % 4 * .8
    return [Math.cos(angle) * radius, -2 + index % 3 * .08, Math.sin(angle) * radius] as [number, number, number]
  }), [])

  return <group>
    <mesh position={[0, -2.18, 0]} scale={[1.35, .16, 1]} receiveShadow>
      <cylinderGeometry args={[7.4, 8.2, 1, 64]} />
      <meshStandardMaterial color={stoneDark} roughness={.96} metalness={.04} />
    </mesh>
    {[5.2, 6.4, 7.5].map((radius, index) => <mesh key={radius} position={[0, -1.92 + index * .18, 0]} rotation={[Math.PI / 2, 0, 0]}>
      <torusGeometry args={[radius, .18 + index * .05, 8, 64]} />
      <meshStandardMaterial color={index === 0 ? '#214950' : stone} roughness={.9} />
    </mesh>)}
    <mesh position={[0, -1.98, 0]} rotation={[-Math.PI / 2, 0, 0]} receiveShadow>
      <circleGeometry args={[5.25, 64]} />
      <meshStandardMaterial color="#12343d" roughness={1} />
    </mesh>
    <mesh position={[0, -1.955, 0]} rotation={[-Math.PI / 2, 0, 0]}>
      <ringGeometry args={[2.8, 3.6, 64]} />
      <meshStandardMaterial color="#1d4d52" emissive="#123b40" emissiveIntensity={.25} roughness={.84} />
    </mesh>
    {columns.map((column, index) => <group key={index} position={column.position} rotation={[0, 0, column.lean]}>
      <mesh position={[0, column.height / 2, 0]}><cylinderGeometry args={[.27, .36, column.height, 10]} /><meshStandardMaterial color={index % 3 ? stone : '#21464a'} roughness={.93} /></mesh>
      <mesh position={[0, column.height + .08, 0]} rotation={[0, index * .4, index % 4 === 0 ? .22 : 0]}><cylinderGeometry args={[.42, .34, .2, 10]} /><meshStandardMaterial color="#20434a" roughness={.95} /></mesh>
      <mesh position={[0, .1, 0]}><cylinderGeometry args={[.48, .56, .22, 10]} /><meshStandardMaterial color="#17383f" roughness={1} /></mesh>
    </group>)}
    {rocks.map((position, index) => <mesh key={index} position={position} rotation={[index * .17, index * .39, index * .09]} scale={[.5 + index % 4 * .13, .26 + index % 3 * .13, .42 + index % 5 * .09]}><dodecahedronGeometry args={[1, 0]} /><meshStandardMaterial color={index % 3 ? '#0d2931' : '#174047'} roughness={1} /></mesh>)}
    {Array.from({ length: 12 }, (_, index) => {
      const angle = index / 12 * Math.PI * 2
      const radius = 5.6 + index % 3 * .75
      return <Seagrass key={index} position={[Math.cos(angle) * radius, -1.7, Math.sin(angle) * radius]} scale={.55 + index % 4 * .13} phase={index} />
    })}
    {[-1, 1].map(side => <group key={side} position={[side * 5.8, -1.45, -.3]} scale={side < 0 ? [-1, 1, 1] : 1}>
      <mesh rotation={[0, 0, -.32]} position={[0, .6, 0]}><cylinderGeometry args={[.07, .18, 1.55, 7]} /><meshStandardMaterial color="#a75d58" emissive="#4b2026" emissiveIntensity={.2} roughness={.9} /></mesh>
      {[0, 1, 2].map(branch => <mesh key={branch} position={[.22 + branch * .15, .85 + branch * .25, 0]} rotation={[0, 0, .7 - branch * .22]}><capsuleGeometry args={[.045, .55 - branch * .08, 3, 6]} /><meshStandardMaterial color="#cf7770" roughness={.9} /></mesh>)}
    </group>)}
  </group>
}

const GodRays: FC<{ blood: boolean; firstBlood: boolean }> = ({ blood, firstBlood }) => <group position={[0, 6, -2]}>
  {[-4.2, -1.7, 1.3, 4].map((x, index) => <mesh key={x} position={[x, 0, index % 2 ? -1 : 1]} rotation={[0, 0, x * -.012]}><coneGeometry args={[1.15 + index % 2 * .4, 13, 20, 1, true]} /><meshBasicMaterial color={blood ? '#ff9c92' : '#8ef4e5'} transparent opacity={firstBlood ? .07 : blood ? .04 : .032} blending={AdditiveBlending} depthWrite={false} side={2} /></mesh>)}
</group>

const TidalImpact: FC<{ color: string; reducedMotion: boolean }> = ({ color, reducedMotion }) => {
  const group = useRef<Group>(null)
  const started = useRef<number | undefined>(undefined)
  useFrame(({ clock }) => {
    started.current ??= clock.elapsedTime
    if (!group.current) return
    const progress = MathUtils.clamp((clock.elapsedTime - started.current - 4.02) / 1.05, 0, 1)
    const wave = Math.sin(progress * Math.PI)
    group.current.scale.setScalar(.2 + progress * 7.4)
    group.current.rotation.y += reducedMotion ? 0 : .009
    group.current.children.forEach((child, index) => {
      const material = (child as Mesh).material as MeshBasicMaterial
      material.opacity = wave * (index ? .36 : .7)
    })
  })
  return <group ref={group} position={[0, -.48, .05]} scale={.01}>
    <mesh rotation={[Math.PI / 2, 0, 0]}><torusGeometry args={[.72, .055, 8, 96]} /><meshBasicMaterial color={color} transparent opacity={0} blending={AdditiveBlending} depthWrite={false} /></mesh>
    <mesh position={[0, 1.7, -.2]}><coneGeometry args={[.48, 7.4, 32, 1, true]} /><meshBasicMaterial color="#fff4e8" transparent opacity={0} blending={AdditiveBlending} depthWrite={false} side={2} /></mesh>
  </group>
}

const OracleIllumination: FC<{ active: boolean; color: string; reducedMotion: boolean }> = ({ active, color, reducedMotion }) => {
  const group = useRef<Group>(null)
  useFrame(({ clock }, delta) => {
    if (!group.current) return
    group.current.scale.setScalar(MathUtils.damp(group.current.scale.x, active ? 1 : .001, reducedMotion ? 4.5 : 2.5, delta))
    group.current.rotation.y += reducedMotion ? 0 : delta * .14
    group.current.children.forEach((child, index) => {
      if (!(child instanceof Mesh)) return
      const material = child.material as MeshBasicMaterial
      material.opacity = active ? .1 + Math.sin(clock.elapsedTime * 1.8 + index) * .025 : 0
    })
  })
  return <group ref={group} position={[0, 2.8, 0]} scale={.001}>
    <mesh position={[0, 1.4, 0]}><coneGeometry args={[1.5, 7.8, 48, 1, true]} /><meshBasicMaterial color="#fff1b5" transparent opacity={0} blending={AdditiveBlending} depthWrite={false} side={2} /></mesh>
    <mesh position={[0, -2.8, 0]} rotation={[Math.PI / 2, 0, 0]}><torusGeometry args={[1.05, .045, 8, 96]} /><meshBasicMaterial color={color} transparent opacity={0} blending={AdditiveBlending} depthWrite={false} /></mesh>
    <pointLight position={[0, -2.1, 0]} color="#fff1bf" intensity={active ? 16 : 0} distance={9} decay={2} />
  </group>
}

const AbyssalConvergence: FC<{ active: boolean; color: string }> = ({ active, color }) => {
  const group = useRef<Group>(null)
  const started = useRef<number | undefined>(undefined)
  const curves = useMemo(() => Array.from({ length: 5 }, (_, index) => {
    const angle = index / 5 * Math.PI * 2
    return new CatmullRomCurve3([
      new Vector3(Math.cos(angle) * 7.6, 1.2 + index % 2, Math.sin(angle) * 4.2),
      new Vector3(Math.cos(angle + .45) * 4.2, 2.1, Math.sin(angle + .45) * 2.4),
      new Vector3(Math.cos(angle) * 1.4, .1, Math.sin(angle) * .8),
      new Vector3(0, -.45, 0),
    ])
  }), [])

  useFrame(({ clock }, delta) => {
    if (!active) started.current = undefined
    else started.current ??= clock.elapsedTime
    if (!group.current) return
    const elapsed = started.current === undefined ? 0 : clock.elapsedTime - started.current
    const target = active && elapsed > 2.8 ? 1 : .001
    group.current.scale.setScalar(MathUtils.damp(group.current.scale.x, target, 2.1, delta))
    group.current.children.forEach((child, index) => {
      const material = (child as Mesh).material as MeshBasicMaterial
      const fold = MathUtils.clamp((elapsed - 2.65 - index * .08) / 1.15, 0, 1)
      material.opacity = Math.sin(fold * Math.PI) * .3
    })
  })

  return <group ref={group} scale={.001}>
    {curves.map((curve, index) => <mesh key={index}><tubeGeometry args={[curve, 72, .085 + index % 2 * .03, 7, false]} /><meshBasicMaterial color={index % 2 ? '#fff2dc' : color} transparent opacity={0} blending={AdditiveBlending} depthWrite={false} /></mesh>)}
  </group>
}

const PearlAltar: FC<{
  attacking: boolean
  blood: boolean
  firstBlood: boolean
  hint: boolean
  spinPhase: LiveSpinPhase
  category?: LiveCategoryVisual
  reducedMotion: boolean
}> = ({ attacking, blood, firstBlood, hint, spinPhase, category, reducedMotion }) => {
  const pearlMesh = useRef<Mesh>(null)
  const pearlMaterial = useRef<MeshPhysicalMaterial>(null)
  const halo = useRef<Mesh>(null)
  const targetColor = useMemo(() => new Color(category?.glow ?? pearl), [category?.glow])
  const targetEmissive = useMemo(() => new Color(category?.color ?? aqua), [category?.color])
  const baseColor = useMemo(() => new Color(pearl), [])
  const baseEmissive = useMemo(() => new Color(aqua), [])
  const phaseStarted = useRef<number | undefined>(undefined)
  const previousPhase = useRef(spinPhase)
  const categoryVisible = Boolean(category) && spinPhase !== 'spinning'

  useFrame(({ clock }, delta) => {
    if (previousPhase.current !== spinPhase) {
      previousPhase.current = spinPhase
      phaseStarted.current = clock.elapsedTime
    }
    phaseStarted.current ??= clock.elapsedTime
    const revealElapsed = clock.elapsedTime - phaseStarted.current
    const categoryActive = categoryVisible && (spinPhase !== 'revealed' || revealElapsed > .9)
    if (pearlMesh.current) {
      const impact = firstBlood ? 1.3 + Math.sin(clock.elapsedTime * 4.5) * .06 : hint ? 1.18 + Math.sin(clock.elapsedTime * 2.6) * .035 : attacking ? 1.13 + Math.sin(clock.elapsedTime * 10) * .07 : 1 + Math.sin(clock.elapsedTime * 1.4) * (reducedMotion ? .015 : .04)
      pearlMesh.current.scale.setScalar(MathUtils.damp(pearlMesh.current.scale.x, spinPhase === 'spinning' ? .78 : impact, firstBlood ? 2.1 : 4, delta))
    }
    if (pearlMaterial.current) {
      pearlMaterial.current.color.lerp(categoryActive ? targetColor : baseColor, 1 - Math.exp(-delta * 2.8))
      pearlMaterial.current.emissive.lerp(categoryActive ? targetEmissive : baseEmissive, 1 - Math.exp(-delta * 3.2))
      pearlMaterial.current.emissiveIntensity = MathUtils.damp(pearlMaterial.current.emissiveIntensity, firstBlood ? 4.1 : hint ? 3.15 : attacking ? 2.2 : categoryActive ? 1.35 : .72, 4, delta)
    }
    if (halo.current) halo.current.rotation.z += reducedMotion ? 0 : delta * (firstBlood ? 2 : attacking ? 1.4 : .2)
  })

  const style = category ? { '--category-color': category.color, '--category-glow': category.glow } as CSSProperties : undefined
  return <group position={[0, -.9, 0]}>
    {[1.38, 1.05, .73].map((radius, index) => <mesh key={radius} position={[0, -.7 + index * .24, 0]}><cylinderGeometry args={[radius - .16, radius, .35, 12]} /><meshStandardMaterial color={index === 2 ? '#2b5555' : stone} roughness={.78} metalness={.12} /></mesh>)}
    <mesh ref={halo} position={[0, .2, 0]} rotation={[Math.PI / 2, 0, 0]}><torusGeometry args={[firstBlood ? 1.24 : 1.05, firstBlood ? .05 : .025, 8, 80]} /><meshBasicMaterial color={blood ? coral : category?.glow ?? aqua} transparent opacity={firstBlood ? .9 : attacking ? .74 : .3} blending={AdditiveBlending} /></mesh>
    <mesh ref={pearlMesh} position={[0, .47, 0]}>
      <sphereGeometry args={[.76, 48, 48]} />
      <meshPhysicalMaterial ref={pearlMaterial} color={pearl} emissive={aqua} emissiveIntensity={.72} roughness={.12} clearcoat={1} clearcoatRoughness={.08} iridescence={.92} iridescenceIOR={1.3} />
      {categoryVisible && category?.icon && <Html center sprite distanceFactor={7.2} position={[0, 0, .77]} className={classes.categoryPearlIconWrapper}><div className={classes.categoryPearlIcon} style={{ ...style, animationDelay: spinPhase === 'revealed' ? '.9s' : '0s' }}><svg viewBox="0 0 24 24" aria-hidden><path d={category.icon} /></svg></div></Html>}
    </mesh>
    <mesh position={[-.21, .67, .59]} scale={[.42, .21, .12]} rotation={[0, 0, -.4]}><sphereGeometry args={[.7, 20, 20]} /><meshBasicMaterial color="#ffffff" transparent opacity={.28} blending={AdditiveBlending} depthWrite={false} /></mesh>
    <pointLight position={[0, .55, 0]} color={blood ? coral : category?.glow ?? aqua} intensity={firstBlood ? 22 : hint ? 18 : blood ? 15 : attacking ? 11 : 7} distance={firstBlood ? 13 : 9} decay={2} />
    {firstBlood && <TidalImpact color={category?.glow ?? aqua} reducedMotion={reducedMotion} />}
  </group>
}

const TeamAscension: FC<{ color: string; reducedMotion: boolean }> = ({ color, reducedMotion }) => {
  const group = useRef<Group>(null)
  const started = useRef<number | undefined>(undefined)
  useFrame(({ clock }, delta) => {
    started.current ??= clock.elapsedTime
    const elapsed = clock.elapsedTime - started.current
    if (!group.current) return
    group.current.rotation.y += reducedMotion ? 0 : delta * .75
    group.current.children.forEach((child, index) => {
      if (!(child instanceof Mesh)) return
      const material = child.material as MeshBasicMaterial
      const reveal = MathUtils.clamp((elapsed - index * .15) / .9, 0, 1)
      material.opacity = Math.sin(Math.min(1, reveal) * Math.PI * .7) * (index === 3 ? .24 : .68)
    })
  })
  return <group ref={group}>
    {[.28, .42, .58].map((radius, index) => <mesh key={radius} rotation={[Math.PI / 2 + index * .32, index * .48, 0]}><torusGeometry args={[radius, .026 + index * .008, 8, 64]} /><meshBasicMaterial color={index % 2 ? '#fff4e8' : color} transparent opacity={0} blending={AdditiveBlending} depthWrite={false} /></mesh>)}
    <mesh position={[0, 2.6, 0]}><coneGeometry args={[.62, 6.4, 32, 1, true]} /><meshBasicMaterial color="#fff4e8" transparent opacity={0} blending={AdditiveBlending} depthWrite={false} side={2} /></mesh>
    <pointLight color={color} intensity={16} distance={7} decay={2} />
  </group>
}

const RejectedTether: FC<{
  source: RefObject<Vector3>
  color: string
  offset: number
}> = ({ source, color, offset }) => {
  const meshes = useRef<(Mesh | null)[]>([])
  const started = useRef<number | undefined>(undefined)

  useFrame(({ clock }) => {
    started.current ??= clock.elapsedTime
    const elapsed = clock.elapsedTime - started.current
    const approach = MathUtils.smoothstep(elapsed, .12, 1.05)
    const reject = MathUtils.smoothstep(elapsed, 1.22, 2.05)
    const release = 1 - MathUtils.smoothstep(elapsed, 2.05, 3.05)
    const reach = Math.max(.2, approach * .78 - reject * .48)
    const start = source.current.clone()
    const target = new Vector3(0, -.45, 0)

    meshes.current.forEach((mesh, strand) => {
      if (!mesh) return
      const side = strand - 1
      const distortion = reject * Math.sin(clock.elapsedTime * 25 + strand + offset) * .2
      const midpoint = start.clone().lerp(target, .48)
      midpoint.y += .65 + side * .1 + distortion
      midpoint.x += side * .3 + distortion * .5
      midpoint.z += Math.cos(offset + strand) * .2
      const end = start.clone().lerp(target, reach)
      const curve = new CatmullRomCurve3([start, midpoint.clone().lerp(start, 1 - approach), end])
      const geometry = new TubeGeometry(curve, 44, .026 + strand * .006, 7, false)
      mesh.geometry.dispose()
      mesh.geometry = geometry
      const material = mesh.material as MeshBasicMaterial
      material.opacity = approach * release * (.28 + Math.sin(clock.elapsedTime * 18 + strand) * .12)
    })
  })

  return <group>{[-1, 0, 1].map((strand, index) => <mesh key={strand} ref={mesh => { meshes.current[index] = mesh }}><tubeGeometry args={[new CatmullRomCurve3([new Vector3(), new Vector3(0, .1, 0)]), 2, .02, 5, false]} /><meshBasicMaterial color={strand === 0 ? '#d3ebe9' : color} transparent opacity={0} blending={AdditiveBlending} depthWrite={false} /></mesh>)}</group>
}

const SuccessfulTether: FC<{
  source: RefObject<Vector3>
  color: string
  offset: number
  strong?: boolean
  delay?: number
}> = ({ source, color, offset, strong = false, delay = 0 }) => {
  const meshes = useRef<(Mesh | null)[]>([])
  const collar = useRef<Group>(null)
  const started = useRef<number | undefined>(undefined)

  useFrame(({ clock }) => {
    started.current ??= clock.elapsedTime
    const elapsed = Math.max(0, clock.elapsedTime - started.current - delay)
    const grow = MathUtils.smoothstep(elapsed, .1, strong ? 1.45 : .95)
    const lock = MathUtils.smoothstep(elapsed, strong ? 1.45 : .95, strong ? 1.85 : 1.3)
    const release = 1 - MathUtils.smoothstep(elapsed, strong ? 3.45 : 2.35, strong ? 4.15 : 3.1)
    const start = source.current.clone()
    const target = new Vector3(0, -.45, 0)

    meshes.current.forEach((mesh, strand) => {
      if (!mesh) return
      const side = strand ? 1 : -1
      const midpoint = start.clone().lerp(target, .5)
      midpoint.y += (strong ? .85 : .55) + side * .14 + Math.sin(clock.elapsedTime * 3 + strand + offset) * .1
      midpoint.x += side * (strong ? .38 : .26)
      midpoint.z += Math.cos(offset + strand) * .18
      const end = start.clone().lerp(target, grow)
      const curve = new CatmullRomCurve3([start, midpoint.clone().lerp(start, 1 - grow), end])
      const geometry = new TubeGeometry(curve, strong ? 58 : 42, (strong ? .052 : .03) + strand * .007, 7, false)
      mesh.geometry.dispose()
      mesh.geometry = geometry
      const material = mesh.material as MeshBasicMaterial
      material.opacity = grow * release * ((strong ? .48 : .34) + Math.sin(clock.elapsedTime * 12 + strand) * .12)
    })

    if (collar.current) {
      collar.current.scale.setScalar(lock * release * (strong ? 1.45 : 1.1) * (1 + Math.sin(clock.elapsedTime * 11) * .06))
      collar.current.rotation.z += strong ? .11 : .075
    }
  })

  return <group>
    {[0, 1].map(strand => <mesh key={strand} ref={mesh => { meshes.current[strand] = mesh }}><tubeGeometry args={[new CatmullRomCurve3([new Vector3(), new Vector3(0, .1, 0)]), 2, .02, 5, false]} /><meshBasicMaterial color={strand ? '#e9fffb' : color} transparent opacity={0} blending={AdditiveBlending} depthWrite={false} /></mesh>)}
    <group ref={collar} position={[0, -.45, 0]} scale={.001}>
      {[0, 1, 2].map(index => <mesh key={index} rotation={[Math.PI / 2 + index * .45, index * .7, 0]}><torusGeometry args={[.68 + index * .11, strong ? .03 : .018, 7, 64]} /><meshBasicMaterial color={index % 2 ? '#e8fffa' : color} transparent opacity={strong ? .82 : .64} blending={AdditiveBlending} depthWrite={false} /></mesh>)}
      <pointLight color={color} intensity={strong ? 19 : 12} distance={strong ? 7 : 5} decay={2} />
    </group>
  </group>
}

const TeamLantern: FC<{
  team: LiveScoreboardTeamModel
  index: number
  slot: TeamOrbitSlot
  orbitAngle: RefObject<number>
  attacking: boolean
  blood: boolean
  firstBlood: boolean
  focused: boolean
  wrong: boolean
  reducedMotion: boolean
  spinning: boolean
  labelled: boolean
  focusPosition: RefObject<Vector3>
  frozen?: boolean
}> = ({ team, index, slot, orbitAngle, attacking, blood, firstBlood, focused, wrong, reducedMotion, spinning, labelled, focusPosition, frozen }) => {
  const beacon = useRef<Group>(null)
  const labelAnchor = useRef<Group>(null)
  const worldPosition = useRef(new Vector3())
  const cinematicStarted = useRef<number | undefined>(undefined)
  const wasFirstBlood = useRef(firstBlood)
  const accent = teamColor(team.id ?? index + 1)
  const podium = team.rank === 1 ? '#ffd889' : team.rank === 2 ? '#d5eced' : team.rank === 3 ? '#d49b78' : '#8b7254'

  useFrame(({ clock }, delta) => {
    if (!beacon.current) return
    if (wasFirstBlood.current !== firstBlood) {
      wasFirstBlood.current = firstBlood
      cinematicStarted.current = firstBlood ? clock.elapsedTime : undefined
    }
    if (firstBlood) cinematicStarted.current ??= clock.elapsedTime
    const cinematicElapsed = firstBlood && cinematicStarted.current !== undefined
      ? clock.elapsedTime - cinematicStarted.current
      : 0
    const angle = slot.angle + orbitAngle.current * (1 - slot.ring * .06)
    const retreat = spinning ? 1.12 : 1
    const bob = reducedMotion || firstBlood || wrong ? 0 : Math.sin(clock.elapsedTime * .58 + index * .73) * .045
    const rise = firstBlood ? MathUtils.smoothstep(cinematicElapsed, .35, 1.65) * 1.22 : 0
    const targetX = Math.cos(angle) * slot.radiusX * retreat
    const targetZ = Math.sin(angle) * slot.radiusZ * retreat
    beacon.current.position.x = MathUtils.damp(beacon.current.position.x, targetX, 8.5, delta)
    beacon.current.position.y = MathUtils.damp(beacon.current.position.y, slot.height + bob + rise, firstBlood ? 2 : 6.5, delta)
    beacon.current.position.z = MathUtils.damp(beacon.current.position.z, targetZ, 8.5, delta)
    const baseScale = labelled ? 1.16 : .88
    const wrongPulse = wrong ? 1.08 + Math.sin(clock.elapsedTime * 24) * .16 : 1
    const pulse = firstBlood ? 1.34 + Math.sin(clock.elapsedTime * 4.5) * .045 : attacking ? 1.22 + Math.sin(clock.elapsedTime * 8) * .07 : spinning ? .82 : wrongPulse
    beacon.current.scale.setScalar(MathUtils.damp(beacon.current.scale.x, baseScale * pulse, firstBlood ? 2.6 : 7, delta))
    beacon.current.getWorldPosition(worldPosition.current)
    if (focused) focusPosition.current.copy(worldPosition.current)
    if (labelAnchor.current) {
      labelAnchor.current.position.x = worldPosition.current.x >= 0 ? .7 : -.7
      labelAnchor.current.position.y = worldPosition.current.z < -1 ? .2 : -.02
    }
  })

  const labelStyle = { '--team-color': accent } as CSSProperties
  return <>
    <group ref={beacon} position={[Math.cos(slot.angle) * slot.radiusX, slot.height, Math.sin(slot.angle) * slot.radiusZ]}>
      <mesh rotation={[Math.PI / 2, 0, 0]}><torusGeometry args={[firstBlood ? .38 : .28, firstBlood ? .065 : .044, 8, 36]} /><meshStandardMaterial color={podium} metalness={.72} roughness={.34} emissive={firstBlood ? '#ffd6a0' : accent} emissiveIntensity={firstBlood ? 2.1 : attacking ? 1.15 : .22} /></mesh>
      <mesh><sphereGeometry args={[.2, labelled ? 28 : 16, labelled ? 28 : 16]} /><meshPhysicalMaterial color={accent} emissive={blood ? coral : accent} emissiveIntensity={firstBlood ? 3.2 : attacking ? 3 : wrong ? 1.8 : .85} roughness={.14} clearcoat={1} iridescence={.75} /></mesh>
      {labelled && <pointLight color={firstBlood ? '#ffd6a0' : blood ? coral : accent} intensity={firstBlood ? 11 : attacking ? 6 : wrong ? 3.2 : 1.1} distance={firstBlood ? 5.4 : attacking ? 2.8 : 2.4} decay={2} />}
      {attacking && <mesh rotation={[Math.PI / 2, 0, 0]}><torusGeometry args={[.28, .018, 7, 48]} /><meshBasicMaterial color={blood ? coral : accent} transparent opacity={.72} blending={AdditiveBlending} depthWrite={false} /></mesh>}
      {wrong && <mesh rotation={[Math.PI / 2, .4, 0]}><torusGeometry args={[.31, .02, 6, 36]} /><meshBasicMaterial color={accent} transparent opacity={.45} blending={AdditiveBlending} depthWrite={false} /></mesh>}
      {firstBlood && <TeamAscension color={coral} reducedMotion={reducedMotion} />}
      {labelled && <group ref={labelAnchor} position={[.7, 0, .08]}><Html center sprite distanceFactor={7.6} className={classes.sceneLabelWrapper}>
        <div className={`${classes.sceneTeamLabel} ${team.rank === 1 ? classes.sceneTeamLeader : ''} ${attacking ? classes.sceneTeamAttacking : ''} ${blood ? classes.sceneTeamBlood : ''} ${firstBlood ? classes.sceneTeamFirstBlood : ''}`} style={labelStyle}><span>{team.rank ?? index + 1}</span><strong>{team.name || 'Team'}</strong><b>{frozen ? '???' : team.score?.toLocaleString() ?? 0}</b></div>
      </Html></group>}
    </group>
    {attacking && <SuccessfulTether source={worldPosition} color={blood ? coral : accent} strong={blood || firstBlood} offset={index * .73} delay={firstBlood ? 1.75 : 0} />}
    {wrong && <RejectedTether source={worldPosition} color={accent} offset={index * .73} />}
  </>
}

const CategoryMedallion: FC<{
  visual: LiveCategoryVisual
  index: number
  count: number
  phase: LiveSpinPhase
  selected: boolean
  parentRotation: RefObject<Group | null>
  reducedMotion: boolean
}> = ({ visual, index, count, phase, selected, parentRotation, reducedMotion }) => {
  const medallion = useRef<Group>(null)
  const material = useRef<MeshBasicMaterial>(null)
  const phaseStarted = useRef<number | undefined>(undefined)
  const previousPhase = useRef(phase)
  const base = index / Math.max(count, 1) * Math.PI * 2
  const style = { '--category-color': visual.color, '--category-glow': visual.glow } as CSSProperties

  useFrame(({ clock }, delta) => {
    if (!medallion.current) return
    if (previousPhase.current !== phase) {
      previousPhase.current = phase
      phaseStarted.current = clock.elapsedTime
    }
    phaseStarted.current ??= clock.elapsedTime
    const elapsed = clock.elapsedTime - phaseStarted.current
    const active = phase === 'spinning'
    const revealed = phase === 'revealed'
    let radius = 2.2 + index % 3 * .28
    let targetY = .75 + Math.sin(clock.elapsedTime * 2.4 + index) * (reducedMotion ? .08 : .55)
    let targetScale = active ? 1 : .001

    if (revealed && selected) {
      const absorbing = elapsed > 1.05
      radius = 0
      targetY = absorbing ? -.23 : 2.05
      targetScale = absorbing ? .001 : 1.75
    } else if (revealed) {
      radius = 5.7 + index % 3 * .55
      targetY = -1.15 - index % 2 * .65
      targetScale = .001
    } else if (phase === 'idle') {
      radius = 6.4 + index % 3 * .5
      targetY = -1.5 - index % 2 * .4
      targetScale = .001
    }

    const targetX = Math.cos(base) * radius
    const targetZ = Math.sin(base) * radius * .62
    const pace = revealed ? selected && elapsed > 1.05 ? 3.6 : selected ? 2.5 : 1.8 : phase === 'idle' ? 3 : 6
    medallion.current.position.x = MathUtils.damp(medallion.current.position.x, targetX, pace, delta)
    medallion.current.position.y = MathUtils.damp(medallion.current.position.y, targetY, pace, delta)
    medallion.current.position.z = MathUtils.damp(medallion.current.position.z, targetZ, pace, delta)
    medallion.current.scale.setScalar(MathUtils.damp(medallion.current.scale.x, targetScale, pace, delta))
    medallion.current.rotation.y = -(parentRotation.current?.rotation.y ?? 0) - base
    medallion.current.visible = medallion.current.scale.x > .015
    if (material.current) material.current.opacity = MathUtils.damp(material.current.opacity, revealed && !selected ? 0 : 1, 3, delta)
  })

  return <group ref={medallion} position={[Math.cos(base) * 2.2, .65, Math.sin(base) * 1.35]} scale={.001}>
    <mesh rotation={[Math.PI / 2, 0, 0]}><cylinderGeometry args={[.34, .34, .09, 24]} /><meshStandardMaterial color={visual.color} emissive={visual.glow} emissiveIntensity={phase === 'spinning' ? .75 : 1.35} metalness={.38} roughness={.28} /></mesh>
    <mesh position={[0, 0, .07]}><ringGeometry args={[.23, .29, 32]} /><meshBasicMaterial ref={material} color={visual.glow} transparent opacity={1} /></mesh>
    <Html center sprite distanceFactor={7.4} position={[0, .5, .1]} className={classes.vortexLabelWrapper}><div className={`${classes.vortexLabel} ${phase === 'revealed' && selected ? classes.vortexLabelSelected : ''}`} style={style}>{visual.icon && <svg viewBox="0 0 24 24" aria-hidden><path d={visual.icon} /></svg>}<strong>{visual.category}</strong></div></Html>
    {selected && phase === 'revealed' && <pointLight color={visual.glow} intensity={5} distance={4.5} decay={2} />}
  </group>
}

const flowVertex = `
  varying vec2 vUv;
  void main() {
    vUv = uv;
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
  }
`
const flowFragment = `
  uniform float uTime;
  uniform vec3 uColor;
  uniform float uPhase;
  varying vec2 vUv;
  void main() {
    float travel = fract(vUv.x * 5.0 - uTime * .72 + uPhase);
    float band = smoothstep(.02, .22, travel) * (1.0 - smoothstep(.48, .92, travel));
    float edge = sin(vUv.y * 3.14159);
    gl_FragColor = vec4(uColor, (.18 + band * .82) * edge * .42);
  }
`

const FlowingVortexCurrent: FC<{ curve: CatmullRomCurve3; index: number; reducedMotion: boolean }> = ({ curve, index, reducedMotion }) => {
  const material = useRef<ShaderMaterial>(null)
  const mesh = useRef<Mesh>(null)
  useFrame(({ clock }) => {
    if (material.current) material.current.uniforms.uTime.value = clock.elapsedTime * (reducedMotion ? .28 : 1)
    if (mesh.current) {
      mesh.current.rotation.y = Math.sin(clock.elapsedTime * .7 + index) * .06
      mesh.current.scale.setScalar(1 + Math.sin(clock.elapsedTime * 1.2 + index) * .025)
    }
  })
  return <mesh ref={mesh}><tubeGeometry args={[curve, 96, .042 + index * .012, 7, false]} /><shaderMaterial ref={material} vertexShader={flowVertex} fragmentShader={flowFragment} uniforms={{ uTime: { value: 0 }, uColor: { value: new Color(index === 1 ? '#c6fff7' : aqua) }, uPhase: { value: index * .31 } }} transparent blending={AdditiveBlending} depthWrite={false} /></mesh>
}

const VortexFlowRings: FC<{ reducedMotion: boolean }> = ({ reducedMotion }) => {
  const group = useRef<Group>(null)
  useFrame(({ clock }, delta) => {
    if (!group.current) return
    group.current.rotation.y -= delta * (reducedMotion ? .25 : 1.1)
    group.current.children.forEach((child, index) => {
      child.scale.setScalar(1 + Math.sin(clock.elapsedTime * 1.8 + index * .9) * .08)
      child.position.y = -.5 + index * .56 + Math.sin(clock.elapsedTime * 1.25 + index) * .09
      const material = (child as Mesh).material as MeshBasicMaterial
      material.opacity = .24 + Math.sin(clock.elapsedTime * 2 + index) * .12
    })
  })
  return <group ref={group}>{[.75, 1.25, 1.75, 2.25].map((radius, index) => <mesh key={radius} rotation={[Math.PI / 2, index * .35, 0]}><torusGeometry args={[radius, .022 + index * .007, 6, 72]} /><meshBasicMaterial color="#8affef" transparent opacity={.32} blending={AdditiveBlending} depthWrite={false} /></mesh>)}</group>
}

const Vortex: FC<{ categories: ChallengeCategory[]; selected?: ChallengeCategory; phase: LiveSpinPhase; reducedMotion: boolean; visuals: LiveCategoryVisual[] }> = ({ categories, selected, phase, reducedMotion, visuals }) => {
  const vortex = useRef<Group>(null)
  const medallions = useRef<Group>(null)
  const active = phase === 'spinning'
  const revealed = phase === 'revealed'
  const labelsRef = useRef<ChallengeCategory[]>([])
  if (phase === 'spinning') {
    const source = categories.length ? categories : Object.values(ChallengeCategory)
    labelsRef.current = (selected && !source.includes(selected) ? [selected, ...source] : source).slice(0, 8)
  } else if (!labelsRef.current.length && selected) labelsRef.current = [selected]
  const labels = labelsRef.current
  const visualMap = useMemo(() => new Map(visuals.map(visual => [visual.category, visual])), [visuals])
  const fallbackVisual: LiveCategoryVisual = { category: ChallengeCategory.Misc, icon: '', color: '#2e7777', glow: aqua }
  const curves = useMemo(() => [0, 1, 2].map(index => new CatmullRomCurve3(Array.from({ length: 18 }, (_, point) => {
    const progress = point / 17
    const angle = progress * Math.PI * (4.1 + index * .45) + index * Math.PI * .66
    const radius = .52 + progress * 2.5
    return new Vector3(Math.cos(angle) * radius, -.38 + progress * 2.85, Math.sin(angle) * radius * .68)
  }))), [])

  useFrame((_, delta) => {
    if (vortex.current) {
      const target = selected || active || revealed ? 1 : .001
      vortex.current.scale.setScalar(MathUtils.damp(vortex.current.scale.x, target, active ? 3.8 : 2.2, delta))
      vortex.current.visible = vortex.current.scale.x > .02
    }
    if (medallions.current) {
      const rotationSpeed = active
        ? 2.45
        : revealed
          ? reducedMotion ? .04 : .12
          : 0
      medallions.current.rotation.y -= delta * rotationSpeed
    }
  })

  return <group ref={vortex} position={[0, -.25, .15]} scale={.001}>
    {active && curves.map((curve, index) => <FlowingVortexCurrent key={index} curve={curve} index={index} reducedMotion={reducedMotion} />)}
    {active && <VortexFlowRings reducedMotion={reducedMotion} />}
    <group ref={medallions}>{labels.map((category, index) => <CategoryMedallion key={category} visual={visualMap.get(category) ?? { ...fallbackVisual, category }} index={index} count={labels.length} phase={phase} selected={category === selected} parentRotation={medallions} reducedMotion={reducedMotion} />)}</group>
  </group>
}

const TidalAtmosphere: FC<{ active: boolean }> = ({ active }) => {
  const group = useRef<Group>(null)
  useFrame(({ clock }, delta) => {
    if (!group.current) return
    group.current.scale.setScalar(MathUtils.damp(group.current.scale.x, active ? 1 : .001, active ? 2.4 : 1.8, delta))
    group.current.rotation.y += delta * .06
    group.current.children.forEach((child, index) => {
      child.rotation.x = Math.sin(clock.elapsedTime * .35 + index) * .18
      child.rotation.z = Math.cos(clock.elapsedTime * .27 + index) * .14
    })
  })
  return <group ref={group} position={[0, -.2, -1.7]} scale={.001}>{[0, 1, 2, 3].map(index => <mesh key={index} position={[(index - 1.5) * .9, index % 2 * .5, -index * .2]} scale={[1.7 + index * .2, .75 + index % 2 * .2, .7]}><sphereGeometry args={[1.2, 20, 16]} /><meshBasicMaterial color={index % 2 ? '#9f1932' : '#d33c4a'} transparent opacity={.035 + index * .008} blending={AdditiveBlending} depthWrite={false} /></mesh>)}</group>
}

const FishSchool: FC<{ reducedMotion: boolean }> = ({ reducedMotion }) => {
  const school = useRef<Group>(null)
  useFrame(({ clock }) => {
    if (!school.current || reducedMotion) return
    school.current.position.x = -7 + clock.elapsedTime * .22 % 14
    school.current.position.y = 3.2 + Math.sin(clock.elapsedTime * .35) * .35
  })
  return <group ref={school} position={[-6, 3.2, -3.5]}>{Array.from({ length: 9 }, (_, index) => <group key={index} position={[(index % 3) * -.55, (index % 4 - 1.5) * .24, -index % 3 * .35]} scale={.32 + index % 3 * .08}><mesh scale={[1.6, .55, .4]}><sphereGeometry args={[.22, 10, 7]} /><meshStandardMaterial color="#4f8586" roughness={.8} /></mesh><mesh position={[-.38, 0, 0]} rotation={[0, 0, -Math.PI / 2]}><coneGeometry args={[.18, .34, 5]} /><meshStandardMaterial color="#416f73" /></mesh></group>)}</group>
}

const Scene: FC<SunkenColosseumSceneProps> = props => {
  const slots = useMemo(() => distributeTeams(props.teams), [props.teams])
  const category = props.categoryVisuals.find(visual => visual.category === props.selected)
  const firstBlood = props.sceneKind === 'firstBlood'
  const hint = props.sceneKind === 'hint'
  const correct = props.sceneKind === 'correct'
  const wrong = props.sceneKind === 'wrong'
  const blood = props.bloodTeams.size > 0 || props.sceneKind === 'blood' || firstBlood
  const attacking = props.attackingTeams.size > 0 || firstBlood
  const orbitPaused = attacking || wrong || props.sceneKind === 'blood'
  const orbitAngle = useRef(0)
  const focusPosition = useRef(new Vector3())

  return <>
    <color attach="background" args={['#020b12']} />
    <fog attach="fog" args={['#03141c', 8, 25]} />
    <ambientLight intensity={firstBlood ? .16 : hint ? .3 : .42} color="#79b9b2" />
    <hemisphereLight args={['#8aefe2', '#041015', firstBlood ? .46 : hint ? .78 : 1.05]} />
    <directionalLight position={[-4, 9, 5]} color={blood ? '#ffc2b7' : hint ? '#fff0bc' : '#b8fff3'} intensity={firstBlood ? 3.8 : hint ? 3.1 : blood ? 2.6 : 2.15} castShadow />
    <OrbitClock angle={orbitAngle} paused={orbitPaused} reducedMotion={props.reducedMotion} />
    <CameraRig spinPhase={props.spinPhase} sceneKind={props.sceneKind} focusPosition={focusPosition} reducedMotion={props.reducedMotion} />
    <GodRays blood={blood} firstBlood={firstBlood} />
    <Colosseum />
    <PearlAltar attacking={attacking} blood={blood} firstBlood={firstBlood} hint={hint} spinPhase={props.spinPhase} category={category} reducedMotion={props.reducedMotion} />
    <group scale={props.spinPhase === 'spinning' ? .92 : 1}>{props.teams.map((team, index) => {
      const slot = slots.get(team.id ?? index + 1)
      if (!slot) return null
      const isWinner = firstBlood && team.id === props.sceneTeamId
      const isCorrectFocus = correct && team.id === props.sceneTeamId
      const isRejected = wrong && team.id === props.sceneTeamId
      return <TeamLantern key={team.id ?? index} team={team} index={index} slot={slot} orbitAngle={orbitAngle} attacking={props.attackingTeams.has(team.id!) || isWinner} blood={props.bloodTeams.has(team.id!) || isWinner} firstBlood={isWinner} focused={isWinner || isCorrectFocus} wrong={isRejected} reducedMotion={props.reducedMotion} spinning={props.spinPhase === 'spinning'} labelled={(team.rank ?? index + 1) <= 10} focusPosition={focusPosition} frozen={props.frozen} />
    })}</group>
    <Vortex categories={props.categories} selected={props.selected} phase={props.spinPhase} reducedMotion={props.reducedMotion} visuals={props.categoryVisuals} />
    <AbyssalConvergence active={firstBlood} color={category?.glow ?? coral} />
    <OracleIllumination active={hint} color={category?.glow ?? aqua} reducedMotion={props.reducedMotion} />
    <TidalAtmosphere active={firstBlood} />
    <FishSchool reducedMotion={props.reducedMotion} />
    <Sediment reducedMotion={props.reducedMotion} hype={props.intensity === LiveScoreboardVisualIntensity.Hype} />
  </>
}

const SunkenColosseumScene: FC<SunkenColosseumSceneProps> = props => <Canvas
  aria-hidden
  shadows
  camera={{ position: [0, 2.5, 11.75], fov: 44, near: .1, far: 40 }}
  dpr={[1, 1.5]}
  gl={{ alpha: false, antialias: true, powerPreference: 'high-performance' }}
>
  <Scene {...props} />
</Canvas>

export default SunkenColosseumScene
