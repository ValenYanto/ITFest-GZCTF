import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Card,
  FileButton,
  Group,
  MultiSelect,
  NumberInput,
  SimpleGrid,
  Stack,
  Table,
  Text,
  Textarea,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { modals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import {
  mdiCheck,
  mdiClipboardOutline,
  mdiEmailSyncOutline,
  mdiFileUploadOutline,
  mdiLinkOff,
  mdiMagnify,
  mdiRefresh,
  mdiSend,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AdminPage } from '@Components/admin/AdminPage'
import { showErrorMsg } from '@Utils/Shared'
import api, {
  CaptainOnboardingCreatedModel,
  CaptainOnboardingEntryModel,
  CaptainOnboardingRecordModel,
  CaptainOnboardingStatus,
  GameInfoModel,
} from '@Api'

const statusView: Record<CaptainOnboardingStatus, { label: string; color: string }> = {
  [CaptainOnboardingStatus.Pending]: { label: 'Belum dibuka', color: 'gray' },
  [CaptainOnboardingStatus.Opened]: { label: 'Sudah dibuka', color: 'cyan' },
  [CaptainOnboardingStatus.Redeemed]: { label: 'Sudah redeem', color: 'teal' },
  [CaptainOnboardingStatus.Expired]: { label: 'Expired', color: 'orange' },
  [CaptainOnboardingStatus.Revoked]: { label: 'Dicabut', color: 'red' },
}

const formatTime = (value?: string | number | null) =>
  value ? dayjs(value).format('DD MMM YYYY HH:mm:ss') : '-'

const parseEntries = (input: string): {
  entries: CaptainOnboardingEntryModel[]
  invalidLines: number[]
} => {
  const entries: CaptainOnboardingEntryModel[] = []
  const invalidLines: number[] = []

  input.split(/\r?\n/).forEach((rawLine, index) => {
    const line = rawLine.trim()
    if (!line) return

    const separator = line.indexOf(',')
    const teamName = separator >= 0 ? line.slice(0, separator).trim() : ''
    const captainEmail = separator >= 0 ? line.slice(separator + 1).trim() : ''

    if (!teamName || !captainEmail || !captainEmail.includes('@')) {
      invalidLines.push(index + 1)
      return
    }

    entries.push({ teamName, captainEmail })
  })

  return { entries, invalidLines }
}

const GlobalOnboardingPage: FC = () => {
  const { t } = useTranslation()
  const [games, setGames] = useState<GameInfoModel[]>([])
  const [gameIds, setGameIds] = useState<string[]>([])
  const [entriesText, setEntriesText] = useState('')
  const [expiresHours, setExpiresHours] = useState(72)
  const [sending, setSending] = useState(false)
  const [results, setResults] = useState<CaptainOnboardingCreatedModel[]>([])
  const [history, setHistory] = useState<CaptainOnboardingRecordModel[]>([])
  const [historyQuery, setHistoryQuery] = useState('')
  const [historyLoading, setHistoryLoading] = useState(true)
  const [actionInviteId, setActionInviteId] = useState<number | null>(null)
  const [latestLinks, setLatestLinks] = useState<Record<number, string>>({})

  const parsed = useMemo(() => parseEntries(entriesText), [entriesText])

  const loadHistory = useCallback(async (showError = false) => {
    try {
      const response = await api.admin.adminGetOnboardingHistory({
        count: 500,
        skip: 0,
        query: historyQuery.trim() || undefined,
      })
      setHistory(response.data)
    } catch (err) {
      if (showError) showErrorMsg(err, t)
    } finally {
      setHistoryLoading(false)
    }
  }, [historyQuery, t])

  useEffect(() => {
    const loadGames = async () => {
      try {
        const response = await api.edit.editGetGames({ count: 100, skip: 0 })
        setGames(response.data.data)
      } catch (err) {
        showErrorMsg(err, t)
      }
    }

    void loadGames()
  }, [t])

  useEffect(() => {
    setHistoryLoading(true)
    void loadHistory(true)
    const interval = window.setInterval(() => void loadHistory(false), 3000)
    return () => window.clearInterval(interval)
  }, [loadHistory])

  const importCsv = async (file: File | null) => {
    if (!file) return
    setEntriesText(await file.text())
  }

  const sendInvites = async () => {
    if (gameIds.length === 0 || parsed.entries.length === 0 || parsed.invalidLines.length > 0) return

    setSending(true)
    setResults([])
    try {
      const response = await api.admin.adminBulkCreateOnboarding({
        gameIds: gameIds.map(Number),
        entries: parsed.entries,
        expiresInHours: expiresHours,
      })
      setResults(response.data)
      setLatestLinks((current) => ({
        ...current,
        ...Object.fromEntries(response.data.map((item) => [item.inviteId, item.onboardingUrl])),
      }))
      setEntriesText('')
      await loadHistory()
      showNotification({
        color: 'teal',
        icon: <Icon path={mdiCheck} size={1} />,
        message: `${response.data.length} undangan onboarding berhasil dibuat.`,
      })
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setSending(false)
    }
  }

  const resendInvite = async (inviteId: number) => {
    setActionInviteId(inviteId)
    try {
      const response = await api.admin.adminResendOnboarding(inviteId, {
        expiresInHours: expiresHours,
      })
      if (response.data.onboardingUrl)
        setLatestLinks((current) => ({ ...current, [inviteId]: response.data.onboardingUrl! }))
      showNotification({
        color: response.data.emailQueued ? 'teal' : 'orange',
        message: response.data.emailQueued
          ? 'Link baru dibuat dan email masuk antrean.'
          : 'Link baru dibuat, tetapi SMTP tidak tersedia. Gunakan link cadangan.',
      })
      await loadHistory()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setActionInviteId(null)
    }
  }

  const confirmRevoke = (record: CaptainOnboardingRecordModel) => {
    const inviteId = record.inviteId
    if (inviteId === undefined) return

    modals.openConfirmModal({
      title: 'Tarik link onboarding',
      children: (
        <Text size="sm">
          Link untuk <b>{record.teamName}</b> akan langsung tidak valid. Riwayatnya tetap disimpan.
        </Text>
      ),
      labels: { confirm: 'Tarik link', cancel: 'Batal' },
      confirmProps: { color: 'red' },
      onConfirm: async () => {
        setActionInviteId(inviteId)
        try {
          await api.admin.adminRevokeOnboarding(inviteId)
          setLatestLinks((current) => {
            const next = { ...current }
            delete next[inviteId]
            return next
          })
          showNotification({ color: 'teal', message: 'Link onboarding berhasil dicabut.' })
          await loadHistory()
        } catch (err) {
          showErrorMsg(err, t)
        } finally {
          setActionInviteId(null)
        }
      },
    })
  }

  const statusCounts = useMemo(() => {
    const counts = new Map<CaptainOnboardingStatus, number>()
    Object.values(CaptainOnboardingStatus).forEach((status) => counts.set(status, 0))
    history.forEach((record) => {
      if (record.status !== undefined)
        counts.set(record.status, (counts.get(record.status) ?? 0) + 1)
    })
    return counts
  }, [history])

  return (
    <AdminPage>
      <Stack w="100%">
        <div>
          <Title order={3}>Bulk Captain Onboarding</Title>
          <Text c="dimmed" size="sm">
            Provision tim berbayar, kirim link aktivasi ke captain, dan whitelist tim tersebut ke satu atau
            beberapa game sekaligus.
          </Text>
        </div>

        <Alert color="blue" title="Alur anggota tim tidak berubah">
          Captain membuat akun dari link email. Anggota lain tetap register secara normal, lalu memakai invite
          code tim bawaan GZCTF. Untuk warm-up dan final terpisah, pilih kedua game di bawah.
        </Alert>

        <SimpleGrid cols={{ base: 2, sm: 3, lg: 5 }}>
          {Object.values(CaptainOnboardingStatus).map((status) => (
            <Card key={status} withBorder padding="md">
              <Text size="xs" c="dimmed" tt="uppercase" fw={700}>
                {statusView[status].label}
              </Text>
              <Text size="xl" fw={800} c={statusView[status].color}>
                {statusCounts.get(status) ?? 0}
              </Text>
            </Card>
          ))}
        </SimpleGrid>

        <Group justify="space-between" align="end">
          <div>
            <Title order={4}>Monitoring Onboarding</Title>
            <Text size="xs" c="dimmed">
              Diperbarui otomatis setiap 3 detik. Status dibuka dapat dipicu oleh link scanner milik provider email;
              status redeem adalah konfirmasi definitif.
            </Text>
          </div>
          <Group>
            <TextInput
              leftSection={<Icon path={mdiMagnify} size={0.9} />}
              placeholder="Cari tim atau email"
              value={historyQuery}
              onChange={(event) => setHistoryQuery(event.currentTarget.value)}
              w={260}
            />
            <Tooltip label="Refresh sekarang">
              <ActionIcon
                variant="light"
                size="lg"
                loading={historyLoading}
                onClick={() => void loadHistory(true)}
              >
                <Icon path={mdiRefresh} size={1} />
              </ActionIcon>
            </Tooltip>
          </Group>
        </Group>

        <Table.ScrollContainer minWidth={1250}>
          <Table striped highlightOnHover withTableBorder>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Tim / Captain</Table.Th>
                <Table.Th>Game</Table.Th>
                <Table.Th>Status</Table.Th>
                <Table.Th>Aktivitas</Table.Th>
                <Table.Th>Pengiriman</Table.Th>
                <Table.Th>Berlaku sampai</Table.Th>
                <Table.Th ta="right">Aksi</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {history.map((record, index) => {
                const status = record.status ?? CaptainOnboardingStatus.Pending
                const canRevoke =
                  status !== CaptainOnboardingStatus.Redeemed &&
                  status !== CaptainOnboardingStatus.Revoked
                const canResend = status !== CaptainOnboardingStatus.Redeemed
                const inviteId = record.inviteId
                const backupLink = inviteId === undefined ? undefined : latestLinks[inviteId]

                return (
                  <Table.Tr key={inviteId ?? `onboarding-${index}`}>
                    <Table.Td>
                      <Text fw={700}>{record.teamName}</Text>
                      <Text size="xs" c="dimmed">{record.captainEmail}</Text>
                    </Table.Td>
                    <Table.Td>
                      <Group gap={4}>
                        {(record.gameTitles ?? []).map((title) => (
                          <Badge key={title} size="sm" variant="light">{title}</Badge>
                        ))}
                      </Group>
                    </Table.Td>
                    <Table.Td>
                      <Badge color={statusView[status].color}>
                        {statusView[status].label}
                      </Badge>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs">Dibuka: {formatTime(record.openedAtUtc)}</Text>
                      <Text size="xs">Redeem: {formatTime(record.consumedAtUtc)}</Text>
                    </Table.Td>
                    <Table.Td>
                      <Group gap={5}>
                        <Badge size="sm" color={record.lastEmailQueued ? 'teal' : 'orange'} variant="light">
                          {record.lastEmailQueued ? 'Queued' : 'SMTP gagal'}
                        </Badge>
                        <Text size="xs">{record.sendCount}x</Text>
                      </Group>
                      <Text size="xs" c="dimmed">{formatTime(record.lastSentAtUtc)}</Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs">{formatTime(record.expiresAtUtc)}</Text>
                    </Table.Td>
                    <Table.Td>
                      <Group justify="flex-end" gap={5} wrap="nowrap">
                        {backupLink && (
                          <Tooltip label="Salin link terbaru">
                            <ActionIcon
                              variant="light"
                              onClick={() => void navigator.clipboard.writeText(backupLink)}
                            >
                              <Icon path={mdiClipboardOutline} size={0.9} />
                            </ActionIcon>
                          </Tooltip>
                        )}
                        <Tooltip label="Buat token baru dan kirim ulang">
                          <ActionIcon
                            variant="light"
                            color="cyan"
                            disabled={!canResend}
                            loading={actionInviteId === inviteId}
                            onClick={() => inviteId !== undefined && void resendInvite(inviteId)}
                          >
                            <Icon path={mdiEmailSyncOutline} size={0.9} />
                          </ActionIcon>
                        </Tooltip>
                        <Tooltip label="Tarik link">
                          <ActionIcon
                            variant="light"
                            color="red"
                            disabled={!canRevoke}
                            onClick={() => confirmRevoke(record)}
                          >
                            <Icon path={mdiLinkOff} size={0.9} />
                          </ActionIcon>
                        </Tooltip>
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                )
              })}
              {!historyLoading && history.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={7}>
                    <Text ta="center" c="dimmed" py="xl">Belum ada riwayat onboarding.</Text>
                  </Table.Td>
                </Table.Tr>
              )}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>

        <Title order={4} mt="md">Kirim Batch Baru</Title>

        <MultiSelect
          required
          searchable
          clearable
          label="Game yang diizinkan"
          description="Pilih semua game yang boleh diikuti tim ini, misalnya Warm-up dan Final."
          placeholder="Pilih satu atau beberapa game"
          data={games
            .filter((game) => game.id !== undefined)
            .map((game) => ({ value: String(game.id), label: game.title }))}
          value={gameIds}
          onChange={setGameIds}
        />

        <Group align="end">
          <NumberInput
            label="Masa berlaku link (jam)"
            min={1}
            max={168}
            value={expiresHours}
            onChange={(value) => setExpiresHours(typeof value === 'number' ? value : 72)}
            w={210}
          />
          <FileButton onChange={(file) => void importCsv(file)} accept=".csv,text/csv,text/plain">
            {(props) => (
              <Button {...props} variant="light" leftSection={<Icon path={mdiFileUploadOutline} size={1} />}>
                Import CSV
              </Button>
            )}
          </FileButton>
        </Group>

        <Textarea
          required
          autosize
          minRows={10}
          maxRows={20}
          label="Daftar tim"
          description="Satu tim per baris dengan format: namatim,emailketua"
          placeholder={'Team Alpha,captain-alpha@example.com\nTeam Beta,captain-beta@example.com'}
          value={entriesText}
          onChange={(event) => setEntriesText(event.currentTarget.value)}
        />

        <Group justify="space-between">
          <Text size="sm" c={parsed.invalidLines.length > 0 ? 'red' : 'dimmed'}>
            {parsed.entries.length} baris valid
            {parsed.invalidLines.length > 0 && `; format salah pada baris ${parsed.invalidLines.join(', ')}`}
          </Text>
          <Button
            leftSection={<Icon path={mdiSend} size={1} />}
            loading={sending}
            disabled={gameIds.length === 0 || parsed.entries.length === 0 || parsed.invalidLines.length > 0}
            onClick={() => void sendInvites()}
          >
            Buat dan kirim undangan
          </Button>
        </Group>

        {results.length > 0 && (
          <Alert color="teal" icon={<Icon path={mdiCheck} size={1} />} title="Batch terakhir berhasil">
            {results.length} undangan dibuat. Link cadangan tersedia pada ikon salin di tabel monitoring selama
            halaman ini belum direfresh.
          </Alert>
        )}
      </Stack>
    </AdminPage>
  )
}

export default GlobalOnboardingPage
