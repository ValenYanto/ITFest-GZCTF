import { ModalProps } from '@mantine/core'
import { useInputState } from '@mantine/hooks'
import { notifications, showNotification, updateNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose, mdiLoading } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChallengeModal } from '@Components/ChallengeModal'
import { challengeLoadErrorMessage, resolveChallengeModalLoadState } from '@Utils/ChallengeModalState'
import { encryptApiData } from '@Utils/Crypto'
import { showErrorMsg } from '@Utils/Shared'
import { ChallengeCategoryItemProps } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import api, { AnswerResult, ChallengeType, SubmissionType } from '@Api'

const AI_USAGE_DISCLOSURE_REQUIRED = 'Isi link AI atau pernyataan bahwa Anda tidak memakai AI sebelum mengirim flag.'
const AI_USAGE_DISCLOSURE_INVALID =
  'Isi harus berupa link percakapan AI dengan protokol http/https atau tepat "Saya tidak memakai AI".'
const NO_AI_DECLARATION = 'Saya tidak memakai AI'
const AI_USAGE_DISCLOSURE_MAX_LENGTH = 2000
const MAX_SOLVER_FILE_SIZE = 10 * 1024 * 1024

const isValidAiUsageDisclosure = (value: string) => {
  if (value === NO_AI_DECLARATION) return true

  try {
    const url = new URL(value)
    return Boolean(url.hostname) && (url.protocol === 'http:' || url.protocol === 'https:')
  } catch {
    return false
  }
}

interface GameChallengeModalProps extends ModalProps {
  gameId: number
  gameTitle: string
  gameEnded: boolean
  practiceMode?: boolean
  cateData: ChallengeCategoryItemProps
  title: string
  score: number
  challengeId: number
  status?: SubmissionType
  speedrun?: boolean
}

export const GameChallengeModal: FC<GameChallengeModalProps> = (props) => {
  const {
    gameId,
    gameTitle,
    gameEnded,
    practiceMode,
    challengeId,
    cateData,
    status,
    speedrun,
    title,
    score,
    ...modalProps
  } = props

  const {
    data: challenge,
    error,
    mutate,
  } = api.game.useGameGetChallenge(
    gameId,
    challengeId,
    {
      refreshInterval: speedrun ? 5 * 1000 : 120 * 1000,
      keepPreviousData: false,
      shouldRetryOnError: false,
    },
    modalProps.opened && gameId > 0 && challengeId > 0
  )

  const { config } = useConfig()
  const { t } = useTranslation()

  const wrongFlagHints = t('challenge.content.wrong_flag_hints', {
    returnObjects: true,
  }) as string[]

  const isDynamic =
    challenge?.type === ChallengeType.StaticContainer || challenge?.type === ChallengeType.DynamicContainer

  const [disabled, setDisabled] = useState(false)
  const [submitId, setSubmitId] = useState(0)
  const [flag, setFlag] = useInputState('')
  const [aiUsageDisclosure, setAiUsageDisclosure] = useState('')
  const [aiUsageDisclosureError, setAiUsageDisclosureError] = useState<string>()
  const [solverFile, setSolverFile] = useState<File | null>(null)
  const [solverFileError, setSolverFileError] = useState<string>()
  const [solvedChallengeId, setSolvedChallengeId] = useState<number | null>(null)

  useEffect(() => {
    setDisabled(false)
    setSubmitId(0)
    setFlag('')
    setAiUsageDisclosure('')
    setAiUsageDisclosureError(undefined)
    setSolverFile(null)
    setSolverFileError(undefined)
    setSolvedChallengeId(null)
  }, [gameId, challengeId])

  const loadState = resolveChallengeModalLoadState(modalProps.opened, Boolean(challenge), error)
  const challengeError = challengeLoadErrorMessage(error)

  const isLimitReached = (challenge?.limit && (challenge.attempts ?? 0) >= challenge.limit) || false

  const onCreate = async () => {
    if (!challengeId || disabled) return
    setDisabled(true)

    try {
      const res = await api.game.gameCreateContainer(gameId, challengeId)
      mutate({
        ...challenge,
        context: {
          ...challenge?.context,
          closeTime: res.data.expectStopAt,
          instanceEntry: res.data.entry,
        },
      })
      showNotification({
        color: 'teal',
        title: t('challenge.notification.instance.created.title'),
        message: t('challenge.notification.instance.created.message'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const requestDestroy = async () => {
    try {
      await mutate()

      if (!challenge?.context?.instanceEntry) return

      await api.game.gameDeleteContainer(gameId, challengeId)
      mutate({
        ...challenge,
        context: {
          ...challenge?.context,
          closeTime: null,
          instanceEntry: null,
        },
      })
      showNotification({
        color: 'teal',
        title: t('challenge.notification.instance.destroyed.title'),
        message: t('challenge.notification.instance.destroyed.message'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const onDestroy = async () => {
    if (!challengeId || disabled) return
    setDisabled(true)

    await requestDestroy()

    setDisabled(false)
  }

  const onExtend = async () => {
    if (!challengeId || disabled) return
    setDisabled(true)

    try {
      const res = await api.game.gameExtendContainerLifetime(gameId, challengeId)
      mutate({
        ...challenge,
        context: {
          ...challenge?.context,
          closeTime: res.data.expectStopAt,
        },
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const onSubmit = async () => {
    if (!challengeId || !flag) {
      showNotification({
        color: 'red',
        message: t('challenge.notification.flag.empty'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    const normalizedAiUsageDisclosure = aiUsageDisclosure.trim()
    if (!speedrun && !normalizedAiUsageDisclosure) {
      setAiUsageDisclosureError(AI_USAGE_DISCLOSURE_REQUIRED)
      showNotification({
        color: 'red',
        message: AI_USAGE_DISCLOSURE_REQUIRED,
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    if (!speedrun && normalizedAiUsageDisclosure.length > AI_USAGE_DISCLOSURE_MAX_LENGTH) {
      const message = `Link AI atau pernyataan penggunaan AI tidak boleh melebihi ${AI_USAGE_DISCLOSURE_MAX_LENGTH} karakter.`
      setAiUsageDisclosureError(message)
      showNotification({
        color: 'red',
        message,
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    if (!speedrun && !isValidAiUsageDisclosure(normalizedAiUsageDisclosure)) {
      setAiUsageDisclosureError(AI_USAGE_DISCLOSURE_INVALID)
      showNotification({
        color: 'red',
        message: AI_USAGE_DISCLOSURE_INVALID,
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    if (!speedrun && challenge?.requireSolverUpload && !solverFile) {
      const message = 'Upload file solver sebelum mengirim flag untuk challenge ini.'
      setSolverFileError(message)
      showNotification({
        color: 'red',
        message,
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    if (solverFile && solverFile.size > MAX_SOLVER_FILE_SIZE) {
      const message = 'Ukuran file solver maksimal 10 MB.'
      setSolverFileError(message)
      showNotification({
        color: 'red',
        message,
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    setDisabled(true)

    try {
      const encryptedFlag = await encryptApiData(t, flag, config.apiPublicKey)
      const res =
        !speedrun && solverFile
          ? await api.game.gameSubmitWithSolver(gameId, challengeId, {
              Flag: encryptedFlag,
              AiUsageDisclosure: normalizedAiUsageDisclosure,
              SolverFile: solverFile,
            })
          : await api.game.gameSubmit(gameId, challengeId, {
              flag: encryptedFlag,
              aiUsageDisclosure: speedrun ? undefined : normalizedAiUsageDisclosure,
            })
      setSubmitId(res.data)
      notifications.clean()
      showNotification({
        id: 'flag-submitted',
        color: 'orange',
        title: t('challenge.notification.flag.submitted.title'),
        message: t('challenge.notification.flag.submitted.message'),
        loading: true,
        autoClose: false,
      })

      const nxt = (challenge?.attempts ?? 0) + 1
      const attempts = challenge?.limit && challenge.limit > 0 ? Math.min(nxt, challenge.limit) : nxt

      mutate({
        attempts,
        ...challenge,
      })
      return
    } catch (e) {
      showErrorMsg(e, t)
      setDisabled(false)
      return
    }
  }

  useEffect(() => {
    if (!submitId) return

    const polling = setInterval(async () => {
      try {
        const res = await api.game.gameStatus(gameId, challengeId, submitId)
        if (res.data !== AnswerResult.FlagSubmitted) {
          setDisabled(false)
          setFlag('')
          setAiUsageDisclosure('')
          setAiUsageDisclosureError(undefined)
          setSolverFile(null)
          setSolverFileError(undefined)
          checkDataFlag(submitId, res.data)
          clearInterval(polling)
        }
      } catch (err) {
        setDisabled(false)
        setFlag('')
        setAiUsageDisclosure('')
        setAiUsageDisclosureError(undefined)
        setSolverFile(null)
        setSolverFileError(undefined)
        showErrorMsg(err, t)
        clearInterval(polling)
      }
    }, 500)

    return () => clearInterval(polling)
  }, [submitId])

  useEffect(() => {
    if (challengeId !== solvedChallengeId) return

    if (status !== SubmissionType.Unaccepted && status !== undefined) {
      // status has been updated, reset solved challenge id
      setSolvedChallengeId(null)
    }
  }, [status, challengeId, challenge])

  const checkDataFlag = async (id: number, data: string) => {
    if (data === AnswerResult.Accepted) {
      setSolvedChallengeId(challengeId)
      updateNotification({
        id: 'flag-submitted',
        color: 'teal',
        title: t('challenge.notification.flag.accepted.title'),
        message: gameEnded
          ? t('challenge.notification.flag.accepted.ended')
          : t('challenge.notification.flag.accepted.message'),
        icon: <Icon path={mdiCheck} size={1} />,
        autoClose: 8000,
        loading: false,
      })
      if (isDynamic && challenge.context?.instanceEntry) await requestDestroy()
      props.onClose()
    } else if (data === AnswerResult.WrongAnswer) {
      updateNotification({
        id: 'flag-submitted',
        color: 'red',
        title: t('challenge.notification.flag.wrong'),
        message: wrongFlagHints[Math.floor(Math.random() * wrongFlagHints.length)],
        icon: <Icon path={mdiClose} size={1} />,
        autoClose: 8000,
        loading: false,
      })
    } else {
      updateNotification({
        id: 'flag-submitted',
        color: 'yellow',
        title: t('challenge.notification.flag.unknown.title'),
        message: t('challenge.notification.flag.unknown.message', {
          id,
        }),
        icon: <Icon path={mdiLoading} size={1} />,
        autoClose: false,
        withCloseButton: true,
      })
    }
  }

  return (
    <ChallengeModal
      {...modalProps}
      gameTitle={gameTitle}
      challenge={challenge}
      fallbackTitle={title}
      fallbackScore={score}
      loading={loadState === 'loading'}
      errorMessage={challengeError}
      onRetry={() => void mutate()}
      cateData={cateData}
      solved={(status !== SubmissionType.Unaccepted && status !== undefined) || solvedChallengeId === challengeId}
      flag={flag}
      setFlag={setFlag}
      requireAiUsageDisclosure={!speedrun}
      aiUsageDisclosure={aiUsageDisclosure}
      aiUsageDisclosureError={aiUsageDisclosureError}
      setAiUsageDisclosure={(value) => {
        setAiUsageDisclosure(value)
        setAiUsageDisclosureError(undefined)
      }}
      solverFile={solverFile}
      solverFileError={solverFileError}
      setSolverFile={(file) => {
        setSolverFile(file)
        setSolverFileError(undefined)
      }}
      onCreate={onCreate}
      onDestroy={onDestroy}
      onSubmitFlag={onSubmit}
      disabled={disabled || isLimitReached}
      onExtend={onExtend}
      gameEnded={gameEnded}
      practiceMode={practiceMode}
    />
  )
}
