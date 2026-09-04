using System;
using System.Collections.Generic;
using System.Linq;
using GZCTF.Controllers;
using GZCTF.Models.Request.Game;
using GZCTF.Utils;
using Xunit;

namespace GZCTF.Test.UnitTests.Controllers;

public class GameControllerTests
{
    [Fact]
    public void MaskChallengeBloodsAfterFreeze_PreservesEarlierBloodsAndAnonymizesLaterOnes()
    {
        var freezeTime = DateTimeOffset.UtcNow;
        var original = new ChallengeInfo
        {
            Id = 7,
            Title = "Freeze Test",
            Category = ChallengeCategory.Pwn,
            Score = 500,
            SolvedCount = 2,
            Bloods =
            [
                new Blood
                {
                    Id = 11, Name = "Before Freeze", Avatar = "/before.webp",
                    SubmitTimeUtc = freezeTime.AddSeconds(-1)
                },
                new Blood
                {
                    Id = 22, Name = "After Freeze", Avatar = "/after.webp",
                    SubmitTimeUtc = freezeTime.AddSeconds(1)
                }
            ]
        };
        Dictionary<ChallengeCategory, IEnumerable<ChallengeInfo>> challenges = new()
        {
            [ChallengeCategory.Pwn] = [original]
        };

        var masked = GameController.MaskChallengeBloodsAfterFreeze(challenges, freezeTime)
            [ChallengeCategory.Pwn].Single();

        Assert.Equal("Before Freeze", masked.Bloods[0].Name);
        Assert.Equal(11, masked.Bloods[0].Id);
        Assert.Equal("????", masked.Bloods[1].Name);
        Assert.Equal(0, masked.Bloods[1].Id);
        Assert.Null(masked.Bloods[1].Avatar);
        Assert.Equal("After Freeze", original.Bloods[1].Name);
    }
}
