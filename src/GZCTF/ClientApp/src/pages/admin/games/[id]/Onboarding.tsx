import {
  Alert,
  Badge,
  Button,
  Group,
  NumberInput,
  Stack,
  Table,
  Text,
  Textarea,
  Title,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import {
  mdiCheck,
  mdiClose,
  mdiSend,
  mdiUpload,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'
import { showErrorMsg } from '@Utils/Shared'
import api, { CaptainOnboardingCreatedModel } from '@Api'

const OnboardingPage: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const { t } = useTranslation()

  const [entriesText, setEntriesText] = useState('')
  const [expiresHours, setExpiresHours] = useState(72)
  const [sending, setSending] = useState(false)
  const [results, setResults] = useState<CaptainOnboardingCreatedModel[] | null>(null)

  const parseEntries = (): { teamName: string; captainEmail: string }[] => {
    return entriesText
      .split('\n')
      .map((line) => line.trim())
      .filter((line) => line.length > 0 && line.includes(','))
      .map((line) => {
        const [teamName, ...rest] = line.split(',')
        const captainEmail = rest.join(',').trim()
        return {
          teamName: teamName.trim(),
          captainEmail,
        }
      })
      .filter((e) => e.teamName.length > 0 && e.captainEmail.length > 0)
  }

  const parsedEntries = parseEntries()
  const validCount = parsedEntries.length
  const totalLines = entriesText.split('\n').filter((l) => l.trim().length > 0).length
  const invalidCount = totalLines - validCount

  const handleSend = async () => {
    if (validCount === 0) return
    setSending(true)
    setResults(null)
    try {
      const res = await api.admin.adminBulkCreateGameOnboarding(numId, {
        entries: parsedEntries,
        expiresInHours: expiresHours,
      })
      setResults(res.data ?? [])
      showNotification({
        color: 'teal',
        message: `Sent ${validCount} onboarding invite(s) successfully.`,
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSending(false)
    }
  }

  const handlePasteCsv = async () => {
    try {
      const text = await navigator.clipboard.readText()
      setEntriesText(text)
    } catch {
      showNotification({
        color: 'orange',
        message: 'Cannot access clipboard. Please type or paste manually.',
        icon: <Icon path={mdiClose} size={1} />,
      })
    }
  }

  return (
    <WithGameEditTab headProps={{ justify: 'apart' }} contentPos="right">
      <Stack>
        <Title order={4}>Bulk Captain Onboarding</Title>
        <Text c="dimmed" size="sm">
          Input team data below to create onboarding invitations. Each line should be:
          <Text component="span" ff="monospace" size="sm">
            {' '}teamName, captainEmail
          </Text>
        </Text>

        <Group wrap="nowrap">
          <NumberInput
            label="Link expiry (hours)"
            value={expiresHours}
            onChange={(v) => setExpiresHours(typeof v === 'string' ? parseInt(v) || 72 : v)}
            min={1}
            max={168}
            w={140}
          />
          <Button
            variant="light"
            leftSection={<Icon path={mdiUpload} size={1} />}
            onClick={handlePasteCsv}
            mt="1.5rem"
          >
            Paste from Clipboard
          </Button>
        </Group>

        <Textarea
          placeholder={`team1, captain1@example.com\nteam2, captain2@example.com`}
          minRows={10}
          maxRows={20}
          autosize
          value={entriesText}
          onChange={(e) => setEntriesText(e.currentTarget.value)}
          disabled={sending}
        />

        {entriesText.trim().length > 0 && (
          <Group>
            <Text size="sm">
              <Badge color={validCount > 0 ? 'teal' : 'gray'} variant="light" size="lg">
                {validCount} valid
              </Badge>
              {invalidCount > 0 && (
                <Badge color="red" variant="light" size="lg" ml="xs">
                  {invalidCount} invalid
                </Badge>
              )}
            </Text>
            {validCount > 0 && (
              <Table withTableBorder withColumnBorders>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>#</Table.Th>
                    <Table.Th>Team Name</Table.Th>
                    <Table.Th>Captain Email</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {parsedEntries.map((entry, idx) => (
                    <Table.Tr key={idx}>
                      <Table.Td>{idx + 1}</Table.Td>
                      <Table.Td>{entry.teamName}</Table.Td>
                      <Table.Td>{entry.captainEmail}</Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            )}
          </Group>
        )}

        <Button
          leftSection={<Icon path={mdiSend} size={1} />}
          disabled={validCount === 0 || sending}
          loading={sending}
          onClick={handleSend}
          size="lg"
          fullWidth
        >
          {sending
            ? `Sending ${validCount} invite(s)...`
            : `Send ${validCount} Onboarding Invite(s) via Email`}
        </Button>

        {results && results.length > 0 && (
          <Stack>
            <Title order={5}>Delivery Results</Title>
            <Alert color="teal" icon={<Icon path={mdiCheck} size={1} />}>
              {results.filter((r) => r.emailQueued).length} email(s) queued successfully.
              {results.filter((r) => !r.emailQueued).length > 0 &&
                ` ${results.filter((r) => !r.emailQueued).length} failed (SMTP not configured).`}
            </Alert>
            <Table withTableBorder withColumnBorders>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Team</Table.Th>
                  <Table.Th>Captain Email</Table.Th>
                  <Table.Th>Email Status</Table.Th>
                  <Table.Th>Magic Link</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {results.map((r) => (
                  <Table.Tr key={r.inviteId}>
                    <Table.Td>{r.teamName}</Table.Td>
                    <Table.Td>{r.captainEmail}</Table.Td>
                    <Table.Td>
                      <Badge color={r.emailQueued ? 'teal' : 'orange'} variant="light">
                        {r.emailQueued ? 'Queued' : 'SMTP Unavailable'}
                      </Badge>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" style={{ wordBreak: 'break-all' }} c="dimmed">
                        {r.onboardingUrl}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Stack>
        )}
      </Stack>
    </WithGameEditTab>
  )
}

export default OnboardingPage
