import {
  ActionIcon,
  Badge,
  Button,
  Group,
  Modal,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { modals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import {
  mdiCheck,
  mdiClose,
  mdiDeleteOutline,
  mdiMagnify,
  mdiPlus,
  mdiShieldCheckOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'
import { showErrorMsg } from '@Utils/Shared'
import api, { TeamWithDetailedUserInfo, WhitelistSource, WhitelistTeamModel } from '@Api'

const sourceColors: Record<WhitelistSource, string> = {
  [WhitelistSource.BulkOnboarding]: 'blue',
  [WhitelistSource.ManualWhitelist]: 'teal',
  [WhitelistSource.None]: 'gray',
}

const WhitelistPage: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const { t } = useTranslation()

  const [whitelist, setWhitelist] = useState<WhitelistTeamModel[]>([])
  const [loading, setLoading] = useState(true)
  const [searchModalOpen, setSearchModalOpen] = useState(false)
  const [searchQuery, setSearchQuery] = useState('')
  const [searchResults, setSearchResults] = useState<TeamWithDetailedUserInfo[]>([])
  const [selectedTeams, setSelectedTeams] = useState<TeamWithDetailedUserInfo[]>([])
  const [searching, setSearching] = useState(false)

  const loadWhitelist = async () => {
    setLoading(true)
    try {
      const res = await api.admin.adminGetWhitelist(numId)
      setWhitelist(res.data ?? [])
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    if (numId > 0) {
      loadWhitelist()
    }
  }, [numId])

  const doSearch = async (query: string) => {
    if (!query.trim()) return
    setSearching(true)
    try {
      const res = await api.admin.adminSearchTeamsForWhitelist(numId, { query })
      const existingIds = new Set(whitelist.map((w) => w.teamId))
      setSearchResults((res.data ?? []).filter((team) => !existingIds.has(team.id!)))
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSearching(false)
    }
  }

  const onAddWhitelist = async (teamIds: number[]) => {
    try {
      await api.admin.adminAddWhitelist(numId, { teamIds })
      showNotification({
        color: 'teal',
        message: `Added ${teamIds.length} team(s) to whitelist.`,
        icon: <Icon path={mdiCheck} size={1} />,
      })
      setSearchModalOpen(false)
      setSelectedTeams([])
      setSearchQuery('')
      setSearchResults([])
      loadWhitelist()
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const onRemoveWhitelist = async (teamId: number, teamName: string) => {
    modals.openConfirmModal({
      title: 'Remove from Whitelist',
      children: (
        <Text size="sm">
          Remove <strong>{teamName}</strong> from whitelist? The team's participation will be rejected.
        </Text>
      ),
      labels: { confirm: 'Remove', cancel: 'Cancel' },
      confirmProps: { color: 'red' },
      onConfirm: async () => {
        try {
          await api.admin.adminRemoveWhitelist(numId, teamId)
          showNotification({
            color: 'teal',
            message: `Removed ${teamName} from whitelist.`,
            icon: <Icon path={mdiCheck} size={1} />,
          })
          loadWhitelist()
        } catch (e) {
          showErrorMsg(e, t)
        }
      },
    })
  }

  const addTeam = (team: TeamWithDetailedUserInfo) => {
    if (!selectedTeams.find((t) => t.id === team.id)) {
      setSelectedTeams([...selectedTeams, team])
    }
  }

  return (
    <WithGameEditTab
      headProps={{ justify: 'apart' }}
      contentPos="right"
      isLoading={loading}
      head={
        <Button
          leftSection={<Icon path={mdiPlus} size={1} />}
          onClick={() => setSearchModalOpen(true)}
          variant="outline"
        >
          Add to Whitelist
        </Button>
      }
    >
      <Stack>
        {whitelist.length === 0 ? (
          <Text c="dimmed" ta="center" py="xl">
            No teams are currently whitelisted for this game.
          </Text>
        ) : (
          <Table>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Team</Table.Th>
                <Table.Th>Captain Email</Table.Th>
                <Table.Th>Source</Table.Th>
                <Table.Th>Status</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {whitelist.map((entry) => (
                <Table.Tr key={entry.teamId}>
                  <Table.Td>
                    <Text fw={500}>{entry.teamName}</Text>
                  </Table.Td>
                  <Table.Td>{entry.captainEmail ?? '-'}</Table.Td>
                  <Table.Td>
                    <Badge color={entry.source ? sourceColors[entry.source] : 'gray'} variant="light">
                      {entry.source === WhitelistSource.BulkOnboarding
                        ? 'Bulk Onboarding'
                        : entry.source === WhitelistSource.ManualWhitelist
                          ? 'Manual'
                          : 'None'}
                    </Badge>
                  </Table.Td>
                  <Table.Td>{entry.status}</Table.Td>
                  <Table.Td>
                    <Tooltip label="Remove from whitelist">
                      <ActionIcon
                        color="red"
                        variant="subtle"
                        onClick={() => entry.teamId !== undefined && onRemoveWhitelist(entry.teamId, entry.teamName ?? '')}
                      >
                        <Icon path={mdiDeleteOutline} size={0.9} />
                      </ActionIcon>
                    </Tooltip>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Stack>

      <Modal
        opened={searchModalOpen}
        onClose={() => {
          setSearchModalOpen(false)
          setSelectedTeams([])
          setSearchQuery('')
          setSearchResults([])
        }}
        title="Add Teams to Whitelist"
        size="lg"
      >
        <Stack>
          <Group wrap="nowrap">
            <TextInput
              placeholder="Search team by name..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.currentTarget.value)}
              onKeyDown={(e) => e.key === 'Enter' && doSearch(searchQuery)}
              style={{ flex: 1 }}
              rightSection={
                <ActionIcon onClick={() => doSearch(searchQuery)}>
                  <Icon path={mdiMagnify} size={0.9} />
                </ActionIcon>
              }
            />
          </Group>

          {searchResults.length > 0 && (
            <Table>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Team Name</Table.Th>
                  <Table.Th>Captain</Table.Th>
                  <Table.Th />
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {searchResults.map((team) => (
                  <Table.Tr key={team.id}>
                    <Table.Td>{team.name}</Table.Td>
                    <Table.Td>
                      {team.members?.find((m) => m.userId === team.captainId)?.userName ?? '-'}
                    </Table.Td>
                    <Table.Td>
                      <Button
                        size="xs"
                        variant={selectedTeams.find((t) => t.id === team.id) ? 'filled' : 'outline'}
                        color={selectedTeams.find((t) => t.id === team.id) ? 'teal' : 'blue'}
                        leftSection={
                          selectedTeams.find((t) => t.id === team.id) ? (
                            <Icon path={mdiCheck} size={0.8} />
                          ) : (
                            <Icon path={mdiPlus} size={0.8} />
                          )
                        }
                        onClick={() => addTeam(team)}
                      >
                        {selectedTeams.find((t) => t.id === team.id) ? 'Selected' : 'Select'}
                      </Button>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          )}

          {searchResults.length === 0 && searchQuery && !searching && (
            <Text c="dimmed" size="sm" ta="center">
              No teams found matching "{searchQuery}"
            </Text>
          )}

          {selectedTeams.length > 0 && (
            <>
              <Title order={5}>Selected Teams ({selectedTeams.length})</Title>
              <Group gap="xs">
                {selectedTeams.map((team) => (
                  <Badge
                    key={team.id}
                    variant="filled"
                    rightSection={
                      <ActionIcon
                        size="xs"
                        variant="transparent"
                        c="white"
                        onClick={() => setSelectedTeams(selectedTeams.filter((t) => t.id !== team.id))}
                      >
                        <Icon path={mdiClose} size={0.7} />
                      </ActionIcon>
                    }
                  >
                    {team.name}
                  </Badge>
                ))}
              </Group>
              <Button
                fullWidth
                leftSection={<Icon path={mdiShieldCheckOutline} size={1} />}
                onClick={() => onAddWhitelist(selectedTeams.map((t) => t.id!))}
              >
                Whitelist {selectedTeams.length} Team(s)
              </Button>
            </>
          )}
        </Stack>
      </Modal>
    </WithGameEditTab>
  )
}

export default WhitelistPage
