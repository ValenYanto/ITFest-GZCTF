import { Button, Card, FileButton, Group, Select, SimpleGrid, Slider, Stack, Switch, Text, TextInput, Title } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { FC, useEffect, useState } from 'react'
import { useParams } from 'react-router'
import api, { LiveScoreboardConfigModel, LiveScoreboardSoundModel, LiveScoreboardVisualIntensity } from '@Api'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'
import { handleAxiosError } from '@Utils/ApiHelper'

const soundNames: (keyof LiveScoreboardSoundModel)[] = [
  'spin', 'categorySelected', 'gameStart', 'hintDrop', 'firstBlood', 'secondBlood', 'thirdBlood',
  'correctSubmit', 'wrongSubmit', 'reminder', 'countdownTick', 'overtime', 'roundFinished', 'scoreUpdate',
]

const maxSoundSize = 10 * 1024 * 1024
const audioExtensions = ['.mp3', '.wav', '.ogg', '.m4a', '.mp4']

const isAudioFile = (file: File) => file.type.startsWith('audio/') ||
  audioExtensions.some((extension) => file.name.toLowerCase().endsWith(extension))

const LiveScoreboardEdit: FC = () => {
  const { id } = useParams()
  const gameId = Number(id)
  const [config, setConfig] = useState<LiveScoreboardConfigModel>()
  const [busy, setBusy] = useState(false)
  const [uploadingSound, setUploadingSound] = useState<keyof LiveScoreboardSoundModel>()

  const load = async () => setConfig((await api.edit.editGetLiveScoreboardConfig(gameId)).data)
  useEffect(() => { void load() }, [gameId])

  const run = async (action: () => Promise<unknown>) => {
    setBusy(true)
    try {
      await action()
      await load()
      notifications.show({ color: 'teal', message: 'Live Scoreboard settings saved.' })
    } catch (error) {
      notifications.show({ color: 'red', message: await handleAxiosError(error) })
    } finally {
      setBusy(false)
    }
  }

  const uploadSound = async (name: keyof LiveScoreboardSoundModel, file: File | null) => {
    if (!file || !config) return
    if (!isAudioFile(file)) {
      notifications.show({ color: 'red', message: 'Choose an MP3, WAV, OGG, M4A, or audio MP4 file.' })
      return
    }
    if (file.size > maxSoundSize) {
      notifications.show({ color: 'red', message: 'Sound files must be 10 MB or smaller.' })
      return
    }

    setUploadingSound(name)
    try {
      const uploaded = (await api.assets.assetsUpload({ files: [file] })).data[0]
      if (!uploaded?.hash) throw new Error('The uploaded sound did not return a file URL.')
      const url = `/assets/${uploaded.hash}/${encodeURIComponent(uploaded.name)}`
      const updated = { ...config, sounds: { ...config.sounds, [name]: url } }
      await api.edit.editUpdateLiveScoreboardConfig(gameId, updated)
      setConfig((await api.edit.editGetLiveScoreboardConfig(gameId)).data)
      notifications.show({ color: 'teal', message: `${name} sound uploaded and saved.` })
    } catch (error) {
      notifications.show({ color: 'red', message: await handleAxiosError(error) })
    } finally {
      setUploadingSound(undefined)
    }
  }

  return (
    <WithGameEditTab isLoading={!config}>
      {config && <Stack>
        <Card withBorder><Stack>
          <Title order={3}>Live Scoreboard</Title>
          <Switch label="Enable Live Scoreboard" checked={config.enabled ?? false}
            onChange={(event) => setConfig({ ...config, enabled: event.currentTarget.checked })} />
          <TextInput label="Display title" value={config.title}
            onChange={(event) => setConfig({ ...config, title: event.currentTarget.value })} />
          <TextInput label="Subtitle / tagline" value={config.subtitle ?? ''}
            onChange={(event) => setConfig({ ...config, subtitle: event.currentTarget.value })} />
          <Select label="Visual intensity" value={config.visualIntensity ?? LiveScoreboardVisualIntensity.Normal}
            data={Object.values(LiveScoreboardVisualIntensity)}
            onChange={(value) => setConfig({ ...config, visualIntensity: value as LiveScoreboardVisualIntensity })} />
          <Group>
            <Button disabled={busy} onClick={() => run(() => api.edit.editUpdateLiveScoreboardConfig(gameId, config))}>Save settings</Button>
            <Button component="a" href={`/games/${gameId}/live`} target="_blank" variant="light">Open Live Scoreboard</Button>
            <Button component="a" href={`/admin/games/${gameId}/live-scoreboard-preview`} target="_blank" variant="outline">Open Design Preview</Button>
          </Group>
        </Stack></Card>
        <Card withBorder><Stack>
          <Title order={3}>Stage audio</Title>
          <Text size="sm" c="dimmed">
            The original Deep Sea Cinematic pack plays automatically. Upload an MP3, WAV, OGG, M4A,
            or audio MP4 up to 10 MB, or paste a direct audio-file URL to override an event. YouTube and
            file-preview page links will not play. On the live screen, click Enable sound once to satisfy
            the browser autoplay policy.
          </Text>
          <Switch label="Enable sound effects" checked={config.soundEnabled ?? true}
            onChange={(event) => setConfig({ ...config, soundEnabled: event.currentTarget.checked })} />
          <Slider label={(value) => `${value}%`} value={(config.volume ?? 0.7) * 100}
            onChange={(value) => setConfig({ ...config, volume: value / 100 })} />
          <SimpleGrid cols={{ base: 1, md: 2 }}>
            {soundNames.map((name) => <Stack key={name} gap="xs">
              <TextInput label={`${name} sound URL`} placeholder="https://example.com/sound.mp3"
                value={config.sounds?.[name] ?? ''}
                disabled={uploadingSound === name}
                onChange={(event) => setConfig({
                  ...config, sounds: { ...config.sounds, [name]: event.currentTarget.value || null },
                })} />
              <Group justify="space-between" wrap="nowrap">
                <Text size="xs" c="dimmed" lineClamp={1}>
                  {config.sounds?.[name]?.startsWith('/assets/')
                    ? 'Uploaded audio override'
                    : config.sounds?.[name]
                      ? 'Direct URL override'
                      : 'Built-in Deep Sea sound'}
                </Text>
                <FileButton onChange={(file) => void uploadSound(name, file)}
                  accept="audio/mpeg,audio/wav,audio/x-wav,audio/ogg,audio/mp4,audio/x-m4a,.mp3,.wav,.ogg,.m4a,.mp4">
                  {(props) => <Button {...props} size="xs" variant="light"
                    loading={uploadingSound === name} disabled={busy || uploadingSound !== undefined}>
                    Upload
                  </Button>}
                </FileButton>
              </Group>
            </Stack>)}
          </SimpleGrid>
          <Group>
            <Button disabled={busy || uploadingSound !== undefined}
              onClick={() => run(() => api.edit.editUpdateLiveScoreboardConfig(gameId, config))}>Save audio</Button>
            <Button variant="outline" disabled={busy || uploadingSound !== undefined}
              onClick={() => run(() => api.edit.editResetLiveScoreboardSounds(gameId))}>Use Deep Sea pack</Button>
          </Group>
        </Stack></Card>
      </Stack>}
    </WithGameEditTab>
  )
}

export default LiveScoreboardEdit
