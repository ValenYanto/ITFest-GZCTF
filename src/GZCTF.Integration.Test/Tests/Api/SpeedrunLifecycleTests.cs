using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Edit;
using GZCTF.Models.Request.Game;
using GZCTF.Services;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public class SpeedrunLifecycleTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task CurrentRound_ExposesOnlyEnabledCategoryChallenges_AndRejectsInactiveAccess()
    {
        const string playerPassword = "Speedrun@Player123";
        const string adminPassword = "Speedrun@Admin123";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(),
            playerPassword);
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(),
            adminPassword, role: Role.Admin);
        var team = await TestDataSeeder.CreateTeamAsync(factory.Services, user.Id,
            $"Speedrun {TestDataSeeder.RandomName(8)}");
        var game = await TestDataSeeder.CreateGameAsync(factory.Services,
            $"Speedrun {TestDataSeeder.RandomName(8)}");
        var active = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id,
            "Enabled Current", "flag{enabled_current}");
        var disabled = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id,
            "Disabled Current", "flag{disabled_current}");
        var inactive = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id,
            "Enabled Inactive", "flag{enabled_inactive}");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var gameEntity = await context.Games.SingleAsync(item => item.Id == game.Id);
            gameEntity.Mode = GameMode.Speedrun;
            var disabledEntity = await context.GameChallenges.SingleAsync(item => item.Id == disabled.Id);
            disabledEntity.IsEnabled = false;
            var inactiveEntity = await context.GameChallenges.SingleAsync(item => item.Id == inactive.Id);
            inactiveEntity.Category = ChallengeCategory.Crypto;
            context.SpeedrunCategories.AddRange(
                new SpeedrunCategory { GameId = game.Id, Category = ChallengeCategory.Misc },
                new SpeedrunCategory { GameId = game.Id, Category = ChallengeCategory.Crypto });
            context.SpeedrunRounds.Add(new SpeedrunRound
            {
                GameId = game.Id,
                Category = ChallengeCategory.Misc,
                Status = SpeedrunRoundStatus.Running,
                StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                EndsAtUtc = DateTimeOffset.UtcNow.AddMinutes(10),
                DurationSeconds = 660,
                DurationMinutes = 11,
                OvertimeSeconds = 300,
                OvertimeMinutes = 5
            });
            await context.SaveChangesAsync();
        }

        await TestDataSeeder.JoinGameAsync(factory.Services, game.Id, team.Id, user.Id);
        using var playerClient = factory.CreateClient();
        await Login(playerClient, user.UserName, playerPassword);

        var details = await playerClient.GetAsync($"/api/Game/{game.Id}/Details");
        details.EnsureSuccessStatusCode();
        Assert.Equal([active.Id], await ReadChallengeIds(details));

        Assert.Equal(HttpStatusCode.NotFound,
            (await playerClient.GetAsync($"/api/Game/{game.Id}/Challenges/{disabled.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await playerClient.GetAsync($"/api/Game/{game.Id}/Challenges/{inactive.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await playerClient.PostAsJsonAsync($"/api/Game/{game.Id}/Challenges/{disabled.Id}",
                new FlagSubmitModel { Flag = disabled.Flag })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await playerClient.PostAsJsonAsync($"/api/Game/{game.Id}/Challenges/{inactive.Id}",
                new FlagSubmitModel { Flag = inactive.Flag })).StatusCode);

        using var adminClient = factory.CreateClient();
        await Login(adminClient, admin.UserName, adminPassword);
        var enableMutation = await adminClient.PutAsJsonAsync($"/api/Edit/Games/{game.Id}/Challenges/{active.Id}",
            new ChallengeUpdateModel { IsEnabled = false });
        Assert.Equal(HttpStatusCode.Conflict, enableMutation.StatusCode);
        var categoryMutation = await adminClient.PutAsJsonAsync($"/api/Edit/Games/{game.Id}/Challenges/{active.Id}",
            new ChallengeUpdateModel { Category = ChallengeCategory.Web });
        Assert.Equal(HttpStatusCode.Conflict, categoryMutation.StatusCode);
        var hintMutation = await adminClient.PutAsJsonAsync($"/api/Edit/Games/{game.Id}/Challenges/{active.Id}",
            new ChallengeUpdateModel { Hints = ["unsafe"], SpeedrunHintReleaseSeconds = [0] });
        Assert.Equal(HttpStatusCode.Conflict, hintMutation.StatusCode);
        var flagMutation = await adminClient.PostAsJsonAsync($"/api/Edit/Games/{game.Id}/Challenges/{active.Id}/Flags",
            new[] { new FlagCreateModel { Flag = "flag{unsafe}" } });
        Assert.Equal(HttpStatusCode.Conflict, flagMutation.StatusCode);
    }

    [Fact]
    public async Task SpinAndStart_RejectCategoryWithoutEnabledChallenges()
    {
        var game = await TestDataSeeder.CreateGameAsync(factory.Services,
            $"Empty Speedrun {TestDataSeeder.RandomName(8)}");
        var challenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id,
            "Disabled Empty", "flag{disabled_empty}");

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gameEntity = await context.Games.SingleAsync(item => item.Id == game.Id);
        gameEntity.Mode = GameMode.Speedrun;
        var challengeEntity = await context.GameChallenges.SingleAsync(item => item.Id == challenge.Id);
        challengeEntity.Category = ChallengeCategory.Web;
        challengeEntity.IsEnabled = false;
        context.SpeedrunCategories.Add(new SpeedrunCategory
        {
            GameId = game.Id,
            Category = ChallengeCategory.Web,
            Included = true,
            Used = false
        });
        await context.SaveChangesAsync();

        var speedrunService = scope.ServiceProvider.GetRequiredService<SpeedrunService>();
        Assert.Null(await speedrunService.Spin(gameEntity, null));

        var readyRound = new SpeedrunRound
        {
            GameId = game.Id,
            Category = ChallengeCategory.Web,
            Status = SpeedrunRoundStatus.Ready,
            DurationSeconds = 300,
            DurationMinutes = 5,
            OvertimeSeconds = 60,
            OvertimeMinutes = 1
        };
        context.SpeedrunRounds.Add(readyRound);
        await context.SaveChangesAsync();
        Assert.False(await speedrunService.Start(gameEntity, readyRound.Id));
    }

    [Fact]
    public async Task ParallelStatePolling_ReleasesEachHintAndAnnouncementExactlyOnce()
    {
        var game = await TestDataSeeder.CreateGameAsync(factory.Services,
            $"Hint Polling {TestDataSeeder.RandomName(8)}");
        var challenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id,
            "Polling Hints", "flag{polling_hints}");
        int roundId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var gameEntity = await context.Games.SingleAsync(item => item.Id == game.Id);
            gameEntity.Mode = GameMode.Speedrun;
            var challengeEntity = await context.GameChallenges.SingleAsync(item => item.Id == challenge.Id);
            challengeEntity.Hints = ["First concurrent hint", "Second concurrent hint"];
            challengeEntity.SpeedrunHintReleaseSeconds = [0, 0];
            challengeEntity.SpeedrunHintReleaseMinutes = [0, 0];
            context.SpeedrunCategories.Add(new SpeedrunCategory
            {
                GameId = game.Id,
                Category = ChallengeCategory.Misc,
                Included = true,
                Used = true
            });
            var round = new SpeedrunRound
            {
                GameId = game.Id,
                Category = ChallengeCategory.Misc,
                Status = SpeedrunRoundStatus.Running,
                StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-10),
                EndsAtUtc = DateTimeOffset.UtcNow.AddMinutes(10),
                DurationSeconds = 610,
                DurationMinutes = 10,
                OvertimeSeconds = 60,
                OvertimeMinutes = 1
            };
            context.SpeedrunRounds.Add(round);
            await context.SaveChangesAsync();
            roundId = round.Id;
        }

        using var client = factory.CreateClient();
        var responses = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => client.GetAsync($"/api/Game/{game.Id}/Speedrun/State")));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        await using var assertionScope = factory.Services.CreateAsyncScope();
        var assertionContext = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logs = await assertionContext.SpeedrunHintReleaseLogs.AsNoTracking()
            .Where(log => log.RoundId == roundId && log.ChallengeId == challenge.Id).ToArrayAsync();
        Assert.Equal(2, logs.Length);
        Assert.Equal([0, 1], logs.Select(log => log.HintIndex).Order().ToArray());

        var announcements = await assertionContext.GameNotices.AsNoTracking()
            .Where(notice => notice.GameId == game.Id && notice.Values != null)
            .ToArrayAsync();
        Assert.Equal(2, announcements.Count(notice => notice.Values!.Any(value =>
            value.StartsWith("Hint #", StringComparison.Ordinal))));
    }

    [Fact]
    public async Task SettingTimerForward_ReleasesOneGroupedHintForOnlyUnsolvedChallenges()
    {
        const string password = "HintTimeline@Player123";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password);
        var team = await TestDataSeeder.CreateTeamAsync(factory.Services, user.Id,
            $"Hint Timeline {TestDataSeeder.RandomName(8)}");
        var game = await TestDataSeeder.CreateGameAsync(factory.Services,
            $"Hint Timeline {TestDataSeeder.RandomName(8)}");
        var solvedChallenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id,
            "Already Solved", "flag{already_solved}");
        var firstUnsolvedChallenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id,
            "First Unsolved", "flag{first_unsolved}");
        var secondUnsolvedChallenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id,
            "Second Unsolved", "flag{second_unsolved}");
        var participation = await TestDataSeeder.JoinGameAsync(factory.Services, game.Id, team.Id, user.Id);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gameEntity = await context.Games.SingleAsync(item => item.Id == game.Id);
        gameEntity.Mode = GameMode.Speedrun;
        var challengeEntities = await context.GameChallenges
            .Where(item => item.Id == solvedChallenge.Id || item.Id == firstUnsolvedChallenge.Id ||
                           item.Id == secondUnsolvedChallenge.Id)
            .ToArrayAsync();
        foreach (var challengeEntity in challengeEntities)
        {
            challengeEntity.Hints = ["Scheduled after ten elapsed minutes"];
            challengeEntity.SpeedrunHintReleaseSeconds = [600];
            challengeEntity.SpeedrunHintReleaseMinutes = [10];
        }
        var round = new SpeedrunRound
        {
            GameId = game.Id,
            Category = challengeEntities[0].Category,
            Status = SpeedrunRoundStatus.Running,
            StartedAtUtc = DateTimeOffset.UtcNow,
            EndsAtUtc = DateTimeOffset.UtcNow.AddMinutes(30),
            DurationSeconds = 1800,
            DurationMinutes = 30,
            OvertimeSeconds = 300,
            OvertimeMinutes = 5
        };
        var submission = new Submission
        {
            Answer = solvedChallenge.Flag,
            Status = AnswerResult.Accepted,
            SubmitTimeUtc = DateTimeOffset.UtcNow,
            UserId = user.Id,
            TeamId = team.Id,
            ParticipationId = participation.Id,
            GameId = game.Id,
            ChallengeId = solvedChallenge.Id
        };
        context.SpeedrunRounds.Add(round);
        context.Submissions.Add(submission);
        await context.SaveChangesAsync();
        context.FirstSolves.Add(new FirstSolve
        {
            ParticipationId = participation.Id,
            ChallengeId = solvedChallenge.Id,
            SubmissionId = submission.Id,
            AcceptedTimeUtc = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var speedrunService = scope.ServiceProvider.GetRequiredService<SpeedrunService>();
        Assert.True(await speedrunService.SetRemainingTime(gameEntity, round.Id, 1200));
        Assert.True(await speedrunService.SetRemainingTime(gameEntity, round.Id, 1200));

        var logs = await context.SpeedrunHintReleaseLogs.AsNoTracking()
            .Where(log => log.RoundId == round.Id).ToArrayAsync();
        Assert.Equal(2, logs.Length);
        Assert.DoesNotContain(logs, log => log.ChallengeId == solvedChallenge.Id);
        Assert.Equal([firstUnsolvedChallenge.Id, secondUnsolvedChallenge.Id],
            logs.Select(log => log.ChallengeId).Order().ToArray());
        var notices = await context.GameNotices.AsNoTracking()
            .Where(notice => notice.GameId == game.Id && notice.Values != null).ToArrayAsync();
        Assert.Single(notices, notice => notice.Values!.Contains("Hint #1 released for 2 challenges."));
    }

    [Fact]
    public async Task LiveScoreboard_ReturnsHintReleasedByTheSamePoll()
    {
        var game = await TestDataSeeder.CreateGameAsync(factory.Services,
            $"Live Hint {TestDataSeeder.RandomName(8)}");
        var challenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id,
            "Live Poll Hint", "flag{live_poll_hint}");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var gameEntity = await context.Games.SingleAsync(item => item.Id == game.Id);
            gameEntity.Mode = GameMode.Speedrun;
            var challengeEntity = await context.GameChallenges.SingleAsync(item => item.Id == challenge.Id);
            challengeEntity.Hints = ["Visible without waiting for another poll"];
            challengeEntity.SpeedrunHintReleaseSeconds = [5];
            challengeEntity.SpeedrunHintReleaseMinutes = [0];
            context.SpeedrunRounds.Add(new SpeedrunRound
            {
                GameId = game.Id,
                Category = challengeEntity.Category,
                Status = SpeedrunRoundStatus.Running,
                StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-10),
                EndsAtUtc = DateTimeOffset.UtcNow.AddSeconds(290),
                DurationSeconds = 300,
                DurationMinutes = 5,
                OvertimeSeconds = 60,
                OvertimeMinutes = 1
            });
            await context.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/Game/{game.Id}/Live");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(document.RootElement.GetProperty("recentEvents").EnumerateArray(), eventItem =>
            eventItem.GetProperty("message").GetString()?.StartsWith("Hint #1", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Overtime_ReleasesHintScheduledAtEndOfRegularTimerAfterClockWasMovedForward()
    {
        var game = await TestDataSeeder.CreateGameAsync(factory.Services,
            $"Overtime Hint {TestDataSeeder.RandomName(8)}");
        var challenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id,
            "Overtime Scheduled Hint", "flag{overtime_scheduled_hint}");
        int roundId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var gameEntity = await context.Games.SingleAsync(item => item.Id == game.Id);
            gameEntity.Mode = GameMode.Speedrun;
            var challengeEntity = await context.GameChallenges.SingleAsync(item => item.Id == challenge.Id);
            challengeEntity.Hints = ["Released when the regular thirty-minute clock has elapsed"];
            challengeEntity.SpeedrunHintReleaseSeconds = [1800];
            challengeEntity.SpeedrunHintReleaseMinutes = [30];
            var now = DateTimeOffset.UtcNow;
            var round = new SpeedrunRound
            {
                GameId = game.Id,
                Category = challengeEntity.Category,
                Status = SpeedrunRoundStatus.Overtime,
                // A manually advanced timer can enter overtime before thirty wall-clock minutes pass.
                StartedAtUtc = now,
                EndsAtUtc = now,
                OvertimeEndsAtUtc = now.AddMinutes(5),
                DurationSeconds = 1800,
                DurationMinutes = 30,
                OvertimeSeconds = 300,
                OvertimeMinutes = 5
            };
            context.SpeedrunRounds.Add(round);
            await context.SaveChangesAsync();
            roundId = round.Id;
        }

        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/Game/{game.Id}/Speedrun/State");
        response.EnsureSuccessStatusCode();

        await using var assertionScope = factory.Services.CreateAsyncScope();
        var assertionContext = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await assertionContext.SpeedrunHintReleaseLogs.AsNoTracking()
            .Where(log => log.RoundId == roundId && log.ChallengeId == challenge.Id).ToArrayAsync());
        var notices = await assertionContext.GameNotices.AsNoTracking()
            .Where(notice => notice.GameId == game.Id && notice.Values != null).ToArrayAsync();
        Assert.Single(notices,
            notice => notice.Values!.Contains("Hint #1 released for Overtime Scheduled Hint."));
    }

    private static async Task Login(HttpClient client, string userName, string password)
    {
        var response = await client.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = userName, Password = password });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<int[]> ReadChallengeIds(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.GetProperty("challenges").EnumerateObject()
            .SelectMany(category => category.Value.EnumerateArray())
            .Select(challenge => challenge.GetProperty("id").GetInt32())
            .ToArray();
    }
}
