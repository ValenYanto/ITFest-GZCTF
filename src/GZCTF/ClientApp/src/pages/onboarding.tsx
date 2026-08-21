import {
  Alert,
  Anchor,
  Badge,
  Button,
  Code,
  Group,
  Loader,
  PasswordInput,
  Stack,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { useInputState } from '@mantine/hooks'
import { mdiCheck, mdiClipboardOutline, mdiClose } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router'
import { AccountView } from '@Components/AccountView'
import { StrengthPasswordInput } from '@Components/StrengthPasswordInput'
import { encryptApiData } from '@Utils/Crypto'
import { tryGetClientError } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import { usePageTitle } from '@Hooks/usePageTitle'
import api, {
  CaptainOnboardingInfoModel,
  CaptainOnboardingRedeemResultModel,
} from '@Api'

const CaptainOnboardingPage: FC = () => {
  const [searchParams] = useSearchParams()
  const token = searchParams.get('token') ?? ''
  const { config } = useConfig()
  const { t } = useTranslation()

  const [info, setInfo] = useState<CaptainOnboardingInfoModel | null>(null)
  const [result, setResult] = useState<CaptainOnboardingRedeemResultModel | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [submitting, setSubmitting] = useState(false)
  const [userName, setUserName] = useInputState('')
  const [password, setPassword] = useInputState('')
  const [retypedPassword, setRetypedPassword] = useInputState('')

  usePageTitle('Captain Onboarding')

  useEffect(() => {
    const load = async () => {
      if (!token) {
        setError('Token onboarding tidak ditemukan pada link.')
        setLoading(false)
        return
      }

      try {
        const response = await api.onboarding.onboardingGetInfo(token)
        setInfo(response.data)
      } catch (err) {
        const clientError = tryGetClientError(err, t)
        setError(clientError.message || clientError.title)
      } finally {
        setLoading(false)
      }
    }

    void load()
  }, [t, token])

  const redeem = async (event: React.SyntheticEvent) => {
    event.preventDefault()

    if (password !== retypedPassword) {
      setError('Konfirmasi password tidak sama.')
      return
    }

    setError(null)
    setSubmitting(true)
    try {
      const response = await api.onboarding.onboardingRedeem(token, {
        userName,
        password: await encryptApiData(t, password, config.apiPublicKey),
      })
      setResult(response.data)
    } catch (err) {
      const clientError = tryGetClientError(err, t)
      setError(clientError.message || clientError.title)
    } finally {
      setSubmitting(false)
    }
  }

  if (loading) {
    return (
      <AccountView>
        <Loader />
        <Text size="sm">Memeriksa link onboarding...</Text>
      </AccountView>
    )
  }

  if (result) {
    return (
      <AccountView>
        <Icon path={mdiCheck} size={2.5} color="var(--mantine-color-teal-5)" />
        <Title order={3} ta="center">Tim berhasil dibuat</Title>
        <Text ta="center">
          Akun ini sekarang menjadi captain <b>{result.teamName}</b>.
        </Text>
        <Text size="sm" c="dimmed" ta="center">
          Bagikan invite code berikut kepada anggota. Anggota tetap mendaftar lewat halaman register biasa,
          lalu join tim menggunakan kode ini.
        </Text>
        <Group wrap="nowrap" w="100%">
          <Code block style={{ flex: 1, overflowWrap: 'anywhere' }}>{result.inviteCode}</Code>
          <Button
            variant="light"
            px="sm"
            onClick={() => void navigator.clipboard.writeText(result.inviteCode ?? '')}
          >
            <Icon path={mdiClipboardOutline} size={1} />
          </Button>
        </Group>
        <Group gap="xs" justify="center">
          {(result.gameTitles ?? []).map((title) => <Badge key={title}>{title}</Badge>)}
        </Group>
        <Button component={Link} to="/teams" fullWidth>
          Buka halaman tim
        </Button>
      </AccountView>
    )
  }

  if (!info) {
    return (
      <AccountView>
        <Alert color="red" icon={<Icon path={mdiClose} size={1} />} title="Link tidak dapat digunakan">
          {error}
        </Alert>
        <Anchor component={Link} to="/account/login">Kembali ke login</Anchor>
      </AccountView>
    )
  }

  return (
    <AccountView onSubmit={redeem}>
      <Title order={3} ta="center">Aktivasi Captain Tim</Title>
      <Text size="sm" c="dimmed" ta="center">
        Buat akun captain untuk tim <b>{info.teamName}</b>.
      </Text>
      <TextInput
        label="Email captain"
        value={info.captainEmail}
        disabled
        w="100%"
      />
      <TextInput
        required
        label="Username"
        value={userName}
        onChange={(event) => setUserName(event.currentTarget.value)}
        disabled={submitting}
        minLength={3}
        maxLength={15}
        w="100%"
      />
      <StrengthPasswordInput
        value={password}
        onChange={(event) => setPassword(event.currentTarget.value)}
        disabled={submitting}
      />
      <PasswordInput
        required
        label="Konfirmasi password"
        value={retypedPassword}
        onChange={(event) => setRetypedPassword(event.currentTarget.value)}
        disabled={submitting}
        error={retypedPassword.length > 0 && password !== retypedPassword}
        w="100%"
      />
      <Stack gap={4} w="100%">
        <Text size="xs" c="dimmed">Game yang sudah di-whitelist:</Text>
        <Group gap="xs">
          {((info.gameTitles?.length ?? 0) > 0 ? info.gameTitles! : [info.gameTitle])
            .filter(Boolean)
            .map((title) => <Badge key={title}>{title}</Badge>)}
        </Group>
      </Stack>
      {error && (
        <Alert color="red" icon={<Icon path={mdiClose} size={1} />} w="100%">
          {error}
        </Alert>
      )}
      <Button type="submit" fullWidth loading={submitting}>
        Buat akun dan tim
      </Button>
    </AccountView>
  )
}

export default CaptainOnboardingPage
