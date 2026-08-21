import { Alert, Stack, Text } from '@mantine/core'
import { mdiSnowflake } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useState } from 'react'
import { useParams } from 'react-router'
import { ScoreboardTable } from '@Components/ScoreboardTable'
import { TeamRank } from '@Components/TeamRank'
import { WithGameTab } from '@Components/WithGameTab'
import { WithNavBar } from '@Components/WithNavbar'
import { ScoreTimeLine } from '@Components/charts/ScoreTimeLine'
import { MobileScoreboardTable } from '@Components/mobile/ScoreboardTable'
import { useIsMobile } from '@Utils/ThemeOverride'
import { useGameScoreboard, useGameTeamInfo } from '@Hooks/useGame'

const FreezeNotice: FC = () => (
  <Alert
    color="cyan"
    variant="light"
    icon={<Icon path={mdiSnowflake} size={1} />}
    title="Scoreboard frozen"
  >
    <Text size="sm">
      The ranking shown here is locked to the freeze snapshot. Submissions and scoring remain active, and all
      accumulated results will appear when the scoreboard is unfrozen.
    </Text>
  </Alert>
)

const Scoreboard: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const { teamInfo, error } = useGameTeamInfo(numId)
  const { scoreboard } = useGameScoreboard(numId)

  const [divisionId, setDivisionId] = useState<number | null>(null)
  const isMobile = useIsMobile(1080)
  const isVertical = useIsMobile()

  return (
    <WithNavBar width="90%" minWidth={0}>
      {isMobile ? (
        <Stack pt="md">
          {scoreboard?.scoreboardFrozen && <FreezeNotice />}
          {teamInfo && !error && <TeamRank />}
          {isVertical ? (
            <MobileScoreboardTable divisionId={divisionId} setDivisionId={setDivisionId} />
          ) : (
            <ScoreboardTable divisionId={divisionId} setDivisionId={setDivisionId} />
          )}
        </Stack>
      ) : (
        <WithGameTab>
          <Stack pb="2rem">
            {scoreboard?.scoreboardFrozen && <FreezeNotice />}
            <ScoreTimeLine divisionId={divisionId} />
            <ScoreboardTable divisionId={divisionId} setDivisionId={setDivisionId} />
          </Stack>
        </WithGameTab>
      )}
    </WithNavBar>
  )
}

export default Scoreboard
