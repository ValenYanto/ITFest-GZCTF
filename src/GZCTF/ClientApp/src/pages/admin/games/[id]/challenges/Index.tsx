import {
  Alert,
  Button,
  Center,
  ComboboxItem,
  Group,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Text,
  Title,
} from '@mantine/core'
import { useModals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import { mdiAlertOutline, mdiCheck, mdiHexagonSlice6, mdiPlus, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import { Dispatch, FC, SetStateAction, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams, useSearchParams } from 'react-router'
import { BloodBonusModel } from '@Components/admin/BloodBonusModel'
import { ChallengeCreateModal } from '@Components/admin/ChallengeCreateModal'
import { ChallengeEditCard } from '@Components/admin/ChallengeEditCard'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'
import { showErrorMsg } from '@Utils/Shared'
import { ChallengeCategoryItem, ChallengeCategoryList, useChallengeCategoryLabelMap } from '@Utils/Shared'
import { useEditChallenges } from '@Hooks/useEdit'
import api, { ChallengeInfoModel, ChallengeCategory, ChallengeLifecycleFailureModel } from '@Api'

const GameChallengeEdit: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')

  const [createOpened, setCreateOpened] = useState(false)
  const [bonusOpened, setBonusOpened] = useState(false)
  const [category, setCategory] = useState<ChallengeCategory | null>(null)
  const challengeCategoryLabelMap = useChallengeCategoryLabelMap()
  const [disabled, setDisabled] = useState(false)
  const [selectedIds, setSelectedIds] = useState(new Set<number>())
  const [validationFailures, setValidationFailures] = useState<ChallengeLifecycleFailureModel[]>([])
  const [searchParams] = useSearchParams()
  const imported = searchParams.get('imported') === '1'

  const { t } = useTranslation()

  const { challenges, mutate } = useEditChallenges(numId)

  const filteredChallenges = category && challenges ? challenges?.filter((c) => c.category === category) : challenges

  const modals = useModals()

  const onToggle = (challenge: ChallengeInfoModel, setDisabled: Dispatch<SetStateAction<boolean>>) => {
    modals.openConfirmModal({
      title: challenge.isEnabled ? t('admin.button.challenges.disable') : t('admin.button.challenges.enable'),
      children: (
        <Text size="sm">
          {challenge.isEnabled
            ? t('admin.content.games.challenges.disable', { name: challenge.title })
            : t('admin.content.games.challenges.enable', { name: challenge.title })}
        </Text>
      ),
      onConfirm: () => onConfirmToggle(challenge, setDisabled),
      confirmProps: { color: 'orange' },
    })
  }

  const onConfirmToggle = async (challenge: ChallengeInfoModel, setDisabled: Dispatch<SetStateAction<boolean>>) => {
    const numId = parseInt(id ?? '-1')
    setDisabled(true)

    try {
      await api.edit.editUpdateGameChallenge(numId, challenge.id!, {
        isEnabled: !challenge.isEnabled,
      })
      showNotification({
        color: 'teal',
        message: t('admin.notification.games.challenges.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      await Promise.all([mutate(), api.edit.mutateEditGetGameChallenge(numId, challenge.id!)])
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const onBulkState = async (isEnabled: boolean, all = false) => {
    const challengeIds = all ? [] : [...selectedIds]
    if (!all && challengeIds.length === 0) return
    setDisabled(true)
    try {
      const response = await api.edit.editBulkUpdateChallengeState(numId, { challengeIds, isEnabled })
      const changedIds = response.data.changedChallengeIds ?? []
      setValidationFailures(response.data.failures ?? [])
      setSelectedIds(new Set())
      await Promise.all([
        mutate(),
        ...changedIds.map((challengeId) => api.edit.mutateEditGetGameChallenge(numId, challengeId)),
      ])
      showNotification({
        color: response.data.failures?.length ? 'orange' : 'teal',
        message: isEnabled
          ? `Enabled ${changedIds.length} challenge(s) and created ${response.data.createdInstanceCount ?? 0} missing instance(s).`
          : `Disabled ${changedIds.length} challenge(s).`,
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (error) {
      showErrorMsg(error, t)
    } finally {
      setDisabled(false)
    }
  }

  const allFilteredSelected =
    Boolean(filteredChallenges?.length) &&
    filteredChallenges!.every((challenge) => challenge.id !== undefined && selectedIds.has(challenge.id))

  const toggleAllFiltered = () => {
    const next = new Set(selectedIds)
    for (const challenge of filteredChallenges ?? []) {
      if (challenge.id === undefined) continue
      if (allFilteredSelected) next.delete(challenge.id)
      else next.add(challenge.id)
    }
    setSelectedIds(next)
  }

  const onFlushScoreboard = async () => {
    if (!numId) return

    setDisabled(true)

    try {
      await api.edit.editFlushScoreboardCache(numId)
      showNotification({
        color: 'teal',
        message: t('admin.notification.games.info.scoreboard_flushed'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      mutate()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  return (
    <WithGameEditTab
      headProps={{ justify: 'apart' }}
      isLoading={!challenges}
      head={
        <>
          <Select
            placeholder={t('admin.content.show_all')}
            clearable
            searchable
            w="16rem"
            value={category}
            nothingFoundMessage={t('admin.content.nothing_found')}
            onChange={(value) => setCategory(value as ChallengeCategory | null)}
            renderOption={ChallengeCategoryItem}
            data={ChallengeCategoryList.map((cate) => {
              const data = challengeCategoryLabelMap.get(cate)
              return { value: cate, label: data?.name, ...data } as ComboboxItem
            })}
          />
          <Group justify="right">
            <Button leftSection={<Icon path={mdiRefresh} size={1} />} disabled={disabled} onClick={onFlushScoreboard}>
              {t('admin.button.challenges.flush_scoreboard')}
            </Button>
            <Button leftSection={<Icon path={mdiHexagonSlice6} size={1} />} onClick={() => setBonusOpened(true)}>
              {t('admin.button.challenges.bonus')}
            </Button>
            <Button mr="18px" leftSection={<Icon path={mdiPlus} size={1} />} onClick={() => setCreateOpened(true)}>
              {t('admin.button.challenges.new')}
            </Button>
          </Group>
        </>
      }
    >
      <Stack gap="sm">
        {imported && (
          <Alert color="blue" icon={<Icon path={mdiAlertOutline} size={1} />} title="Imported game requires review">
            This game remains hidden and every imported challenge is disabled. Review flags, container settings,
            attachments, and hint schedules, then use “Enable all valid challenges”. Invalid challenges stay disabled
            and are listed with the reason.
          </Alert>
        )}
        {validationFailures.length > 0 && (
          <Alert
            color="orange"
            title="Challenges that were not enabled"
            icon={<Icon path={mdiAlertOutline} size={1} />}
            withCloseButton
            onClose={() => setValidationFailures([])}
          >
            <Stack gap={4}>
              {validationFailures.map((failure, index) => (
                <Text size="sm" key={failure.challengeId ?? index}>
                  <Text span fw={700}>
                    {failure.title ?? `Challenge #${failure.challengeId}`}
                  </Text>
                  : {failure.reason}
                </Text>
              ))}
            </Stack>
          </Alert>
        )}
        <Group justify="space-between">
          <Group gap="xs">
            <Button
              size="xs"
              variant="light"
              disabled={!filteredChallenges?.length || disabled}
              onClick={toggleAllFiltered}
            >
              {allFilteredSelected ? 'Clear visible selection' : 'Select all visible'}
            </Button>
            <Text size="sm" c="dimmed">
              {selectedIds.size} selected
            </Text>
          </Group>
          <Group gap="xs">
            <Button size="xs" disabled={disabled || selectedIds.size === 0} onClick={() => void onBulkState(true)}>
              Enable selected
            </Button>
            <Button
              size="xs"
              color="orange"
              variant="light"
              disabled={disabled || selectedIds.size === 0}
              onClick={() => void onBulkState(false)}
            >
              Disable selected
            </Button>
            <Button
              size="xs"
              variant="outline"
              disabled={disabled || !challenges?.length}
              onClick={() => void onBulkState(true, true)}
            >
              Enable all valid challenges
            </Button>
          </Group>
        </Group>
        <ScrollArea h="calc(100vh - 300px)" pos="relative" offsetScrollbars type="auto">
          {!filteredChallenges || filteredChallenges.length === 0 ? (
            <Center h="calc(100vh - 200px)">
              <Stack gap={0}>
                <Title order={2}>{t('admin.content.games.challenges.empty.title')}</Title>
                <Text>{t('admin.content.games.challenges.empty.description')}</Text>
              </Stack>
            </Center>
          ) : (
            <SimpleGrid pr={6} cols={{ base: 2, w18: 3, w24: 4, w30: 5, w36: 6, w42: 7, w48: 8 }} spacing="sm">
              {filteredChallenges &&
                filteredChallenges.map((challenge) => (
                  <ChallengeEditCard
                    key={challenge.id}
                    challenge={challenge}
                    onToggle={onToggle}
                    selected={challenge.id !== undefined && selectedIds.has(challenge.id)}
                    onSelectedChange={(selected) => {
                      if (challenge.id === undefined) return
                      const next = new Set(selectedIds)
                      if (selected) next.add(challenge.id)
                      else next.delete(challenge.id)
                      setSelectedIds(next)
                    }}
                  />
                ))}
            </SimpleGrid>
          )}
        </ScrollArea>
      </Stack>
      <ChallengeCreateModal
        title={t('admin.button.challenges.new')}
        size="30%"
        opened={createOpened}
        onClose={() => setCreateOpened(false)}
        onAddChallenge={(challenge) => mutate([challenge, ...(challenges ?? [])])}
      />
      <BloodBonusModel
        title={t('admin.button.challenges.bonus')}
        size="30%"
        opened={bonusOpened}
        onClose={() => setBonusOpened(false)}
      />
    </WithGameEditTab>
  )
}

export default GameChallengeEdit
