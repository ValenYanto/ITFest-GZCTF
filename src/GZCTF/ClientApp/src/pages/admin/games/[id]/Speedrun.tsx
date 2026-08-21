import {
  Alert,
  Badge,
  Button,
  Card,
  Group,
  NumberInput,
  SimpleGrid,
  Stack,
  Switch,
  Text,
  Textarea,
  TextInput,
  Title,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { FC, useCallback, useEffect, useState } from 'react'
import { useParams } from 'react-router'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'
import api, { GameMode, SpeedrunSettingsModel, SpeedrunRoundStatus } from '@Api'
import { handleAxiosError } from '@Utils/ApiHelper'
import { formatDurationSeconds, getInputNumber, parseDurationSeconds } from '@Utils/Shared'

const Speedrun: FC = () => {
  const { id } = useParams()
  const gameId = Number(id)
  const [mode, setMode] = useState<GameMode>()
  const [settings, setSettings] = useState<SpeedrunSettingsModel>()
  const [extendTime, setExtendTime] = useState('05:00')
  const [remainingTime, setRemainingTime] = useState('10:00')
  const [now, setNow] = useState(Date.now())
  const [busy, setBusy] = useState(false)

  const refresh = useCallback(async () => {
    const [game, speedrun] = await Promise.all([
      api.edit.editGetGame(gameId),
      api.edit.editGetSpeedrunSettings(gameId),
    ])
    setMode(game.data.mode)
    setSettings((current) =>
      current
        ? { ...current, state: speedrun.data.state, categories: speedrun.data.categories }
        : speedrun.data
    )
  }, [gameId])

  useEffect(() => {
    refresh()
    const pollTimer = window.setInterval(refresh, 3000)
    const countdownTimer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => {
      window.clearInterval(pollTimer)
      window.clearInterval(countdownTimer)
    }
  }, [refresh])

  const run = async (action: () => Promise<unknown>) => {
    setBusy(true)
    try {
      await action()
      await refresh()
      notifications.show({ color: 'teal', message: 'Speedrun action completed.' })
    } catch (error) {
      const responseTitle = (error as { response?: { data?: { title?: unknown } } })?.response?.data?.title
      notifications.show({
        color: 'red',
        message: typeof responseTitle === 'string' ? responseTitle : await handleAxiosError(error),
      })
    } finally {
      setBusy(false)
    }
  }

  const round = settings?.state?.currentRound
  const activeRound = round?.status === SpeedrunRoundStatus.Running || round?.status === SpeedrunRoundStatus.Overtime
  const roundEnd = round?.status === SpeedrunRoundStatus.Overtime ? round.overtimeEndsAtUtc : round?.endsAtUtc
  const timeLeft = roundEnd ? Math.max(0, Math.floor((roundEnd - now) / 1000)) : 0
  const save = () =>
    settings &&
    run(() =>
      api.edit.editUpdateSpeedrunSettings(gameId, {
        ...settings,
        state: undefined,
        categories: undefined,
      })
    )

  return (
    <WithGameEditTab isLoading={!settings}>
      {mode !== GameMode.Speedrun ? (
        <Alert color="yellow" title="Speedrun mode is disabled">
          Enable Speedrun mode in Information settings first.
        </Alert>
      ) : (
        <Stack>
          <Alert color="blue" title="Normal scoreboard and blood scoring remain active">
            Speedrun uses the normal Jeopardy scoreboard and blood scoring. This mode only controls category visibility,
            round timing, and overtime.
          </Alert>
          <Card withBorder>
            <Stack>
              <Group justify="space-between">
                <Title order={3}>Current round</Title>
                <Badge color={round?.status === SpeedrunRoundStatus.Overtime ? 'red' : 'blue'}>
                  {round?.status ?? 'Waiting'}
                </Badge>
              </Group>
              <Text size="xl" fw={700}>{round?.category ?? 'Waiting for next spin'}</Text>
              <Text>
                {activeRound ? `Time left: ${timeLeft > 0 ? formatDurationSeconds(timeLeft) : 'Ending...'}` : 'No active countdown.'}
              </Text>
              <Group>
                <Button disabled={busy || !!round} onClick={() => run(() => api.edit.editSpinSpeedrun(gameId))}>Spin wheel</Button>
                <Button disabled={busy || round?.status !== SpeedrunRoundStatus.Ready} onClick={() => run(() => api.edit.editStartSpeedrunRound(gameId, round!.id!))}>Start selected round</Button>
                <Button color="red" disabled={busy || !round} onClick={() => run(() => api.edit.editEndSpeedrunRound(gameId, round!.id!))}>End round</Button>
                <Button variant="light" disabled={busy || !activeRound} onClick={() => run(() => api.edit.editExtendSpeedrunRound(gameId, round!.id!, { seconds: 300 }))}>Extend +5</Button>
                <TextInput
                  w={120}
                  aria-label="Custom extend time"
                  placeholder="mm:ss"
                  value={extendTime}
                  onChange={(event) => setExtendTime(event.currentTarget.value)}
                />
                <Button
                  variant="light"
                  disabled={busy || !activeRound || !parseDurationSeconds(extendTime)}
                  onClick={() => run(() => api.edit.editExtendSpeedrunRound(gameId, round!.id!, { seconds: parseDurationSeconds(extendTime)! }))}
                >
                  Extend
                </Button>
              </Group>
              <Group>
                <TextInput
                  label="Set remaining time"
                  w={180}
                  placeholder="mm:ss or hh:mm:ss"
                  value={remainingTime}
                  onChange={(event) => setRemainingTime(event.currentTarget.value)}
                />
                <Button
                  mt={25}
                  disabled={busy || !activeRound || !parseDurationSeconds(remainingTime)}
                  onClick={() => run(() => api.edit.editSetSpeedrunRoundTimer(gameId, round!.id!, { seconds: parseDurationSeconds(remainingTime)! }))}
                >
                  Set timer
                </Button>
              </Group>
            </Stack>
          </Card>

          <Card withBorder>
            <Stack>
              <Title order={3}>Speedrun settings</Title>
              <SimpleGrid cols={2}>
                <Group grow align="end">
                  <NumberInput
                    label="Default round minutes"
                    min={0}
                    value={Math.floor((settings?.defaultRoundDurationSeconds ?? 1800) / 60)}
                    onChange={(value) => setSettings({
                      ...settings,
                      defaultRoundDurationSeconds:
                        getInputNumber(value) * 60 + (settings?.defaultRoundDurationSeconds ?? 1800) % 60,
                    })}
                  />
                  <NumberInput
                    label="Seconds"
                    min={0}
                    max={59}
                    value={(settings?.defaultRoundDurationSeconds ?? 1800) % 60}
                    onChange={(value) => setSettings({
                      ...settings,
                      defaultRoundDurationSeconds:
                        Math.floor((settings?.defaultRoundDurationSeconds ?? 1800) / 60) * 60 + getInputNumber(value),
                    })}
                  />
                </Group>
                <Group grow align="end">
                  <NumberInput
                    label="Overtime minutes"
                    min={0}
                    value={Math.floor((settings?.overtimeSeconds ?? 300) / 60)}
                    onChange={(value) => setSettings({
                      ...settings,
                      overtimeSeconds: getInputNumber(value) * 60 + (settings?.overtimeSeconds ?? 300) % 60,
                    })}
                  />
                  <NumberInput
                    label="Seconds"
                    min={0}
                    max={59}
                    value={(settings?.overtimeSeconds ?? 300) % 60}
                    onChange={(value) => setSettings({
                      ...settings,
                      overtimeSeconds:
                        Math.floor((settings?.overtimeSeconds ?? 300) / 60) * 60 + getInputNumber(value),
                    })}
                  />
                </Group>
                <Switch label="Allow manual extension" checked={settings?.allowManualExtend ?? true} onChange={(event) => setSettings({ ...settings, allowManualExtend: event.currentTarget.checked })} />
                <Switch label="Emergency overtime announcement" checked={settings?.emergencyHintEnabled ?? true} onChange={(event) => setSettings({ ...settings, emergencyHintEnabled: event.currentTarget.checked })} />
              </SimpleGrid>
              <Textarea label="Emergency overtime message" value={settings?.emergencyHintText ?? ''} onChange={(event) => setSettings({ ...settings, emergencyHintText: event.currentTarget.value })} />
              <Group><Button onClick={save} disabled={busy}>Save settings</Button></Group>
            </Stack>
          </Card>

          <Card withBorder>
            <Stack>
              <Group justify="space-between">
                <Title order={3}>Category pool</Title>
                <Group>
                  <Button variant="light" onClick={() => run(() => api.edit.editRefreshSpeedrunCategories(gameId))}>Refresh from challenges</Button>
                  <Button variant="outline" color="red" disabled={!!round} onClick={() => run(() => api.edit.editResetSpeedrunCategories(gameId))}>Reset used categories</Button>
                </Group>
              </Group>
              <SimpleGrid cols={{ base: 1, md: 2, lg: 3 }}>
                {settings?.categories?.map((category) => (
                  <Card key={category.id} withBorder padding="sm">
                    <Stack gap="xs">
                      <Text fw={700}>{category.category}</Text>
                      <Group gap="xs">
                        <Badge color={category.used ? 'gray' : 'green'}>{category.used ? 'Used' : 'Available'}</Badge>
                        <Badge color={category.included ? 'blue' : 'red'}>{category.included ? 'Enabled' : 'Disabled'}</Badge>
                      </Group>
                      <Group gap="xs">
                        <Button
                          size="xs"
                          variant="light"
                          disabled={busy || !category.used}
                          onClick={() => run(() => api.edit.editMakeSpeedrunCategoryAvailable(gameId, category.id!))}
                        >
                          Make Available
                        </Button>
                        <Button
                          size="xs"
                          variant="light"
                          disabled={busy || !!category.used}
                          onClick={() => run(() => api.edit.editMarkSpeedrunCategoryUsed(gameId, category.id!))}
                        >
                          Mark Used
                        </Button>
                        {category.included ? (
                          <Button
                            size="xs"
                            color="red"
                            variant="light"
                            disabled={busy}
                            onClick={() => run(() => api.edit.editDisableSpeedrunCategory(gameId, category.id!))}
                          >
                            Disable
                          </Button>
                        ) : (
                          <Button
                            size="xs"
                            variant="light"
                            disabled={busy}
                            onClick={() => run(() => api.edit.editEnableSpeedrunCategory(gameId, category.id!))}
                          >
                            Enable
                          </Button>
                        )}
                      </Group>
                    </Stack>
                  </Card>
                ))}
              </SimpleGrid>
            </Stack>
          </Card>
        </Stack>
      )}
    </WithGameEditTab>
  )
}

export default Speedrun
