using System;
using GZCTF.Models.Data;
using GZCTF.Services;
using GZCTF.Utils;
using Xunit;

namespace GZCTF.Test.UnitTests.Services;

public class SpeedrunServiceTests
{
    [Fact]
    public void HintElapsedTime_FollowsAdminTimerWhenMovedForward()
    {
        var now = DateTimeOffset.UtcNow;
        var round = new SpeedrunRound
        {
            Status = SpeedrunRoundStatus.Running,
            StartedAtUtc = now,
            EndsAtUtc = now.AddMinutes(20),
            DurationSeconds = 1800
        };

        Assert.Equal(600, SpeedrunService.GetHintElapsedSeconds(round, now));
    }

    [Fact]
    public void HintElapsedTime_DoesNotRegressWhenTimerIsExtended()
    {
        var now = DateTimeOffset.UtcNow;
        var round = new SpeedrunRound
        {
            Status = SpeedrunRoundStatus.Running,
            StartedAtUtc = now.AddMinutes(-12),
            EndsAtUtc = now.AddMinutes(28),
            DurationSeconds = 1800,
            ManuallyExtendedSeconds = 600
        };

        Assert.Equal(720, SpeedrunService.GetHintElapsedSeconds(round, now));
    }

    [Fact]
    public void HintElapsedTime_DoesNotReleaseBeforeTheScheduledSecond()
    {
        var now = DateTimeOffset.UtcNow;
        var round = new SpeedrunRound
        {
            Status = SpeedrunRoundStatus.Running,
            StartedAtUtc = now.AddSeconds(-599.9),
            EndsAtUtc = now.AddSeconds(1200.1),
            DurationSeconds = 1800
        };

        Assert.Equal(599, SpeedrunService.GetHintElapsedSeconds(round, now));
    }

    [Fact]
    public void HintAnnouncement_GroupsMultipleChallenges()
    {
        Assert.Equal("Hint #1 released for Crypto Lock.",
            SpeedrunService.BuildHintReleaseMessage(0, ["Crypto Lock"]));
        Assert.Equal("Hint #1 released for 2 challenges.",
            SpeedrunService.BuildHintReleaseMessage(0, ["Crypto Lock", "Web Gate"]));
    }
}
