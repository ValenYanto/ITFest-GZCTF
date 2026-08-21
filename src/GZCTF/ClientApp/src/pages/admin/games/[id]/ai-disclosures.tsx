import {
  Badge,
  Button,
  Card,
  Center,
  Group,
  ScrollArea,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { mdiDownload, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useCallback, useEffect, useMemo, useState } from 'react'
import { useParams } from 'react-router'
import api, { AiUsageDisclosureModel, AnswerResult } from '@Api'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'
import { handleAxiosError } from '@Utils/ApiHelper'

const getSafeDisclosureUrl = (value: string) => {
  try {
    const url = new URL(value)
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : null
  } catch {
    return null
  }
}

const statusColor: Record<AnswerResult, string> = {
  [AnswerResult.FlagSubmitted]: 'gray',
  [AnswerResult.Accepted]: 'green',
  [AnswerResult.WrongAnswer]: 'red',
  [AnswerResult.CheatDetected]: 'orange',
  [AnswerResult.NotFound]: 'gray',
}

const AiDisclosures: FC = () => {
  const { id } = useParams()
  const gameId = Number(id)
  const [items, setItems] = useState<AiUsageDisclosureModel[]>()
  const [query, setQuery] = useState('')
  const [loading, setLoading] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const response = await api.edit.editGetGameAiDisclosures(gameId, { count: 500 })
      setItems(response.data)
    } catch (error) {
      notifications.show({
        color: 'red',
        title: 'Failed to load AI disclosures',
        message: await handleAxiosError(error),
      })
    } finally {
      setLoading(false)
    }
  }, [gameId])

  useEffect(() => {
    void load()
  }, [load])

  const filteredItems = useMemo(() => {
    const normalizedQuery = query.trim().toLocaleLowerCase()
    if (!normalizedQuery) return items ?? []

    return (items ?? []).filter((item) =>
      [item.team, item.user, item.challenge, item.answer, item.aiUsageDisclosure]
        .some((value) => value?.toLocaleLowerCase().includes(normalizedQuery)))
  }, [items, query])

  return (
    <WithGameEditTab
      isLoading={!items || loading}
      head={
        <Button
          variant="light"
          leftSection={<Icon path={mdiRefresh} size={1} />}
          loading={loading}
          onClick={() => void load()}
        >
          Refresh
        </Button>
      }
    >
      <Card withBorder>
        <Stack gap="md">
          <Group justify="space-between" align="flex-end">
            <div>
              <Title order={3}>AI Usage Disclosures</Title>
              <Text c="dimmed" size="sm">
                Review the AI link or no-AI declaration attached to each Jeopardy submission.
              </Text>
            </div>
            <TextInput
              w="20rem"
              maw="100%"
              label="Search"
              placeholder="Team, user, challenge, flag, or disclosure"
              value={query}
              onChange={(event) => setQuery(event.currentTarget.value)}
            />
          </Group>

          {!filteredItems.length ? (
            <Center mih="12rem">
              <Text c="dimmed">
                {items?.length ? 'No disclosure matches the search.' : 'No AI disclosures have been submitted.'}
              </Text>
            </Center>
          ) : (
            <ScrollArea type="auto">
              <Table striped highlightOnHover miw="72rem" verticalSpacing="sm">
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>Time</Table.Th>
                    <Table.Th>Team / User</Table.Th>
                    <Table.Th>Challenge</Table.Th>
                    <Table.Th>Result</Table.Th>
                    <Table.Th>Submitted Flag</Table.Th>
                    <Table.Th>AI Link / Declaration</Table.Th>
                    <Table.Th>Solver</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {filteredItems.map((item) => {
                    const disclosureUrl = item.aiUsageDisclosure
                      ? getSafeDisclosureUrl(item.aiUsageDisclosure)
                      : null
                    return (
                      <Table.Tr key={item.submissionId}>
                        <Table.Td>
                          <Text size="sm" ff="monospace">
                            {dayjs(item.submitTimeUtc).format('YYYY-MM-DD HH:mm:ss')}
                          </Text>
                        </Table.Td>
                        <Table.Td>
                          <Text fw={600} size="sm">{item.team || '-'}</Text>
                          <Text c="dimmed" size="xs">{item.user || '-'}</Text>
                        </Table.Td>
                        <Table.Td>{item.challenge || '-'}</Table.Td>
                        <Table.Td>
                          <Badge color={item.status ? statusColor[item.status] : 'gray'} variant="light">
                            {item.status}
                          </Badge>
                        </Table.Td>
                        <Table.Td>
                          <Text ff="monospace" size="sm" maw="18rem" truncate="end" title={item.answer}>
                            {item.answer}
                          </Text>
                        </Table.Td>
                        <Table.Td miw="20rem">
                          {disclosureUrl ? (
                            <Text
                              component="a"
                              href={disclosureUrl}
                              target="_blank"
                              rel="noopener noreferrer"
                              c="blue"
                              size="sm"
                              style={{ overflowWrap: 'anywhere' }}
                            >
                              {item.aiUsageDisclosure}
                            </Text>
                          ) : (
                            <Text size="sm" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
                              {item.aiUsageDisclosure}
                            </Text>
                          )}
                        </Table.Td>
                        <Table.Td miw="12rem">
                          {item.hasSolverFile ? (
                            <Stack gap={2}>
                              <Button
                                component="a"
                                href={`/api/edit/games/${gameId}/submissions/${item.submissionId}/solver`}
                                size="xs"
                                variant="light"
                                maw="16rem"
                                leftSection={<Icon path={mdiDownload} size={0.8} />}
                              >
                                <Text span truncate="end">
                                  {item.solverFileName ?? 'Download solver'}
                                </Text>
                              </Button>
                              {item.solverFileSize != null && (
                                <Text c="dimmed" size="xs">
                                  {(item.solverFileSize / 1024).toFixed(1)} KB
                                </Text>
                              )}
                            </Stack>
                          ) : (
                            <Text c="dimmed" size="sm">Not uploaded</Text>
                          )}
                        </Table.Td>
                      </Table.Tr>
                    )
                  })}
                </Table.Tbody>
              </Table>
            </ScrollArea>
          )}
        </Stack>
      </Card>
    </WithGameEditTab>
  )
}

export default AiDisclosures
