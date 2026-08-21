using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Edit;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public class ChallengeLifecycleTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task BulkEnable_IsIdempotent_CreatesOneInstancePerParticipationAndRefreshesChallengeList()
    {
        const string adminPassword = "Lifecycle@Admin123";
        const string playerPassword = "Lifecycle@Player123";
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(),
            adminPassword, role: Role.Admin);
        var player = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(),
            playerPassword);
        var secondPlayer = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(),
            playerPassword);
        var playerTeam = await TestDataSeeder.CreateTeamAsync(factory.Services, player.Id,
            $"Lifecycle {TestDataSeeder.RandomName(8)}");
        var secondTeam = await TestDataSeeder.CreateTeamAsync(factory.Services, secondPlayer.Id,
            $"Lifecycle {TestDataSeeder.RandomName(8)}");
        var game = await TestDataSeeder.CreateGameAsync(factory.Services,
            $"Lifecycle {TestDataSeeder.RandomName(8)}");
        var challenges = new[]
        {
            await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id, "Lifecycle One",
                "flag{lifecycle_one}"),
            await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id, "Lifecycle Two",
                "flag{lifecycle_two}"),
            await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id, "Lifecycle Three",
                "flag{lifecycle_three}")
        };
        var challengeIds = challenges.Select(challenge => challenge.Id).ToArray();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.GameChallenges.Where(challenge => challengeIds.Contains(challenge.Id))
                .ExecuteUpdateAsync(update => update.SetProperty(challenge => challenge.IsEnabled, false));
        }

        var firstParticipation = await TestDataSeeder.JoinGameAsync(factory.Services, game.Id, playerTeam.Id,
            player.Id);
        var secondParticipation = await TestDataSeeder.JoinGameAsync(factory.Services, game.Id, secondTeam.Id,
            secondPlayer.Id);

        using var adminClient = factory.CreateClient();
        await Login(adminClient, admin.UserName, adminPassword);
        using var playerClient = factory.CreateClient();
        await Login(playerClient, player.UserName, playerPassword);

        var enableResponse = await adminClient.PutAsJsonAsync($"/api/Edit/Games/{game.Id}/Challenges/BulkState",
            new ChallengeBulkStateModel { ChallengeIds = challengeIds, IsEnabled = true });
        enableResponse.EnsureSuccessStatusCode();
        var enableResult = await enableResponse.Content.ReadFromJsonAsync<ChallengeBulkStateResultModel>();
        Assert.NotNull(enableResult);
        Assert.Empty(enableResult.Failures);
        Assert.Equal(6, enableResult.CreatedInstanceCount);
        Assert.Equal(challengeIds.Order(), enableResult.ChangedChallengeIds.Order());

        await AssertInstances(challengeIds, [firstParticipation.Id, secondParticipation.Id], expectedCount: 6);

        var repeatResponse = await adminClient.PutAsJsonAsync($"/api/Edit/Games/{game.Id}/Challenges/BulkState",
            new ChallengeBulkStateModel { ChallengeIds = challengeIds, IsEnabled = true });
        repeatResponse.EnsureSuccessStatusCode();
        var repeatResult = await repeatResponse.Content.ReadFromJsonAsync<ChallengeBulkStateResultModel>();
        Assert.NotNull(repeatResult);
        Assert.Empty(repeatResult.ChangedChallengeIds);
        Assert.Equal(0, repeatResult.CreatedInstanceCount);
        await AssertInstances(challengeIds, [firstParticipation.Id, secondParticipation.Id], expectedCount: 6);

        var enabledDetails = await playerClient.GetAsync($"/api/Game/{game.Id}/Details");
        enabledDetails.EnsureSuccessStatusCode();
        Assert.Equal(challengeIds.Order(), (await ReadChallengeIds(enabledDetails)).Order());

        var disableResponse = await adminClient.PutAsJsonAsync($"/api/Edit/Games/{game.Id}/Challenges/BulkState",
            new ChallengeBulkStateModel { ChallengeIds = challengeIds, IsEnabled = false });
        disableResponse.EnsureSuccessStatusCode();

        var disabledDetails = await playerClient.GetAsync($"/api/Game/{game.Id}/Details");
        disabledDetails.EnsureSuccessStatusCode();
        Assert.Empty(await ReadChallengeIds(disabledDetails));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.GameInstances.Where(instance => instance.ChallengeId == challengeIds[0] &&
                instance.ParticipationId == firstParticipation.Id).ExecuteDeleteAsync();
        }

        var singleEnable = await adminClient.PutAsJsonAsync(
            $"/api/Edit/Games/{game.Id}/Challenges/{challengeIds[0]}",
            new ChallengeUpdateModel { IsEnabled = true });
        singleEnable.EnsureSuccessStatusCode();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await context.GameInstances.AnyAsync(instance =>
                instance.ChallengeId == challengeIds[0] && instance.ParticipationId == firstParticipation.Id));
        }
    }

    [Fact]
    public async Task ParallelChallengeDetails_SelfHealWithoutDuplicates_AndNeverReturnStaleContent()
    {
        const string password = "Lifecycle@Parallel123";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password);
        var team = await TestDataSeeder.CreateTeamAsync(factory.Services, user.Id,
            $"Parallel {TestDataSeeder.RandomName(8)}");
        var game = await TestDataSeeder.CreateGameAsync(factory.Services,
            $"Parallel {TestDataSeeder.RandomName(8)}");
        var first = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id,
            "Parallel First", "flag{parallel_first}");
        var second = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id,
            "Parallel Second", "flag{parallel_second}");
        var participation = await TestDataSeeder.JoinGameAsync(factory.Services, game.Id, team.Id, user.Id);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var firstEntity = await context.GameChallenges.SingleAsync(challenge => challenge.Id == first.Id);
            var secondEntity = await context.GameChallenges.SingleAsync(challenge => challenge.Id == second.Id);
            firstEntity.Content = "Content belonging only to the first challenge";
            secondEntity.Content = "Content belonging only to the second challenge";
            await context.SaveChangesAsync();
            await context.GameInstances.Where(instance => instance.ParticipationId == participation.Id &&
                    (instance.ChallengeId == first.Id || instance.ChallengeId == second.Id))
                .ExecuteDeleteAsync();
        }

        using var client = factory.CreateClient();
        await Login(client, user.UserName, password);

        var parallelResponses = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => client.GetAsync($"/api/Game/{game.Id}/Challenges/{first.Id}")));
        Assert.All(parallelResponses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var firstDetails = await Task.WhenAll(parallelResponses.Select(response =>
            response.Content.ReadFromJsonAsync<ChallengeDetailModel>()));
        Assert.All(firstDetails, detail =>
        {
            Assert.NotNull(detail);
            Assert.Equal(first.Id, detail.Id);
            Assert.Equal("Parallel First", detail.Title);
            Assert.Equal("Content belonging only to the first challenge", detail.Content);
        });

        var secondResponse = await client.GetAsync($"/api/Game/{game.Id}/Challenges/{second.Id}");
        secondResponse.EnsureSuccessStatusCode();
        var secondDetail = await secondResponse.Content.ReadFromJsonAsync<ChallengeDetailModel>();
        Assert.NotNull(secondDetail);
        Assert.Equal(second.Id, secondDetail.Id);
        Assert.Equal("Parallel Second", secondDetail.Title);
        Assert.Equal("Content belonging only to the second challenge", secondDetail.Content);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await context.GameInstances.CountAsync(instance =>
                instance.ParticipationId == participation.Id &&
                (instance.ChallengeId == first.Id || instance.ChallengeId == second.Id)));

            await context.GameChallenges.Where(challenge => challenge.Id == second.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(challenge => challenge.IsEnabled, false));
            await context.GameInstances.Where(instance => instance.ParticipationId == participation.Id &&
                instance.ChallengeId == second.Id).ExecuteDeleteAsync();
        }

        var disabledResponse = await client.GetAsync($"/api/Game/{game.Id}/Challenges/{second.Id}");
        Assert.Equal(HttpStatusCode.NotFound, disabledResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await context.GameInstances.AnyAsync(instance =>
                instance.ParticipationId == participation.Id && instance.ChallengeId == second.Id));
        }
    }

    [Fact]
    public async Task DynamicFlagAndContainer_StillUseNormalFirstLoadLifecycle()
    {
        const string password = "Lifecycle@Dynamic123";
        var game = await TestDataSeeder.CreateGameAsync(factory.Services,
            $"Dynamic Lifecycle {TestDataSeeder.RandomName(8)}");
        int challengeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var gameRepository = scope.ServiceProvider.GetRequiredService<IGameRepository>();
            var challengeRepository = scope.ServiceProvider.GetRequiredService<IGameChallengeRepository>();
            var gameEntity = await gameRepository.GetGameById(game.Id) ?? throw new InvalidOperationException();
            var challenge = new GameChallenge
            {
                GameId = game.Id,
                Game = gameEntity,
                Title = "Dynamic lifecycle",
                Content = "Dynamic lifecycle content",
                Category = ChallengeCategory.Misc,
                Type = ChallengeType.DynamicContainer,
                ContainerImage = "ghcr.io/gzctf/challenge-base/echo:latest",
                ExposePort = 70,
                FlagTemplate = "flag{lifecycle_[GUID]}",
                Hints = [],
                IsEnabled = true,
                OriginalScore = 100,
                MinScoreRate = 0.5,
                Difficulty = 1
            };
            await challengeRepository.CreateChallenge(gameEntity, challenge);
            challengeId = challenge.Id;
        }

        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password);
        var team = await TestDataSeeder.CreateTeamAsync(factory.Services, user.Id,
            $"Dynamic {TestDataSeeder.RandomName(8)}");
        var participation = await TestDataSeeder.JoinGameAsync(factory.Services, game.Id, team.Id, user.Id);
        using var client = factory.CreateClient();
        await Login(client, user.UserName, password);

        var detailResponse = await client.GetAsync($"/api/Game/{game.Id}/Challenges/{challengeId}");
        detailResponse.EnsureSuccessStatusCode();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var instance = await context.GameInstances.AsNoTracking().Include(item => item.FlagContext)
                .SingleAsync(item => item.ParticipationId == participation.Id && item.ChallengeId == challengeId);
            Assert.True(instance.IsLoaded);
            Assert.NotNull(instance.FlagContext);
            Assert.StartsWith("flag{lifecycle_", instance.FlagContext.Flag);
            Assert.Null(instance.ContainerId);
        }

        var createResponse = await client.PostAsync($"/api/Game/{game.Id}/Container/{challengeId}", null);
        createResponse.EnsureSuccessStatusCode();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var instance = await context.GameInstances.Include(item => item.Container).Include(item => item.FlagContext)
                .SingleAsync(item => item.ParticipationId == participation.Id && item.ChallengeId == challengeId);
            Assert.NotNull(instance.Container);
            Assert.StartsWith("flag{lifecycle_", instance.FlagContext!.Flag);
            instance.LastContainerOperation = DateTimeOffset.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();
        }

        var deleteResponse = await client.DeleteAsync($"/api/Game/{game.Id}/Container/{challengeId}");
        deleteResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task BulkEnable_ReportsEveryInvalidChallengeAndEnablesOnlyValidOnes()
    {
        const string password = "Lifecycle@Validation123";
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password,
            role: Role.Admin);
        var player = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password);
        var team = await TestDataSeeder.CreateTeamAsync(factory.Services, player.Id,
            $"Validation {TestDataSeeder.RandomName(8)}");
        var game = await TestDataSeeder.CreateGameAsync(factory.Services,
            $"Validation {TestDataSeeder.RandomName(8)}");
        var valid = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id,
            "Valid challenge", "flag{valid_bulk}");
        int[] invalidIds;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var validEntity = await context.GameChallenges.SingleAsync(challenge => challenge.Id == valid.Id);
            validEntity.IsEnabled = false;

            GameChallenge Invalid(string title, ChallengeType type) => new()
            {
                GameId = game.Id,
                Title = title,
                Content = title,
                Category = ChallengeCategory.Misc,
                Type = type,
                Hints = [],
                IsEnabled = false,
                OriginalScore = 100,
                MinScoreRate = 0.5,
                Difficulty = 1
            };

            var noFlag = Invalid("No static flag", ChallengeType.StaticAttachment);
            var noImage = Invalid("No container image", ChallengeType.DynamicContainer);
            noImage.ExposePort = 80;
            noImage.FlagTemplate = "flag{[GUID]}";
            var noPort = Invalid("No exposed port", ChallengeType.DynamicContainer);
            noPort.ContainerImage = "example/valid:latest";
            noPort.ExposePort = 0;
            noPort.FlagTemplate = "flag{[GUID]}";
            var badTemplate = Invalid("Invalid flag template", ChallengeType.DynamicContainer);
            badTemplate.ContainerImage = "example/valid:latest";
            badTemplate.ExposePort = 80;
            badTemplate.FlagTemplate = "flag{constant}";
            context.GameChallenges.AddRange(noFlag, noImage, noPort, badTemplate);
            await context.SaveChangesAsync();
            invalidIds = [noFlag.Id, noImage.Id, noPort.Id, badTemplate.Id];
        }

        var participation = await TestDataSeeder.JoinGameAsync(factory.Services, game.Id, team.Id, player.Id);
        using var adminClient = factory.CreateClient();
        await Login(adminClient, admin.UserName, password);
        var response = await adminClient.PutAsJsonAsync($"/api/Edit/Games/{game.Id}/Challenges/BulkState",
            new ChallengeBulkStateModel
            {
                ChallengeIds = [valid.Id, ..invalidIds],
                IsEnabled = true
            });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ChallengeBulkStateResultModel>();
        Assert.NotNull(result);
        Assert.Equal([valid.Id], result.ChangedChallengeIds);
        Assert.Equal(4, result.Failures.Length);
        Assert.Contains(result.Failures, failure => failure.ChallengeId == invalidIds[0] &&
            failure.Reason.Contains("flag", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Failures, failure => failure.ChallengeId == invalidIds[1] &&
            failure.Reason.Contains("image", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Failures, failure => failure.ChallengeId == invalidIds[2] &&
            failure.Reason.Contains("port", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Failures, failure => failure.ChallengeId == invalidIds[3] &&
            failure.Reason.Contains("template", StringComparison.OrdinalIgnoreCase));

        await using var assertionScope = factory.Services.CreateAsyncScope();
        var assertionContext = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await assertionContext.GameChallenges.Where(challenge => challenge.Id == valid.Id)
            .Select(challenge => challenge.IsEnabled).SingleAsync());
        Assert.All(await assertionContext.GameChallenges.Where(challenge => invalidIds.Contains(challenge.Id))
            .ToArrayAsync(), challenge => Assert.False(challenge.IsEnabled));
        Assert.Single(await assertionContext.GameInstances.Where(instance =>
            instance.ParticipationId == participation.Id && instance.ChallengeId == valid.Id).ToArrayAsync());
        Assert.False(await assertionContext.GameInstances.AnyAsync(instance =>
            instance.ParticipationId == participation.Id && invalidIds.Contains(instance.ChallengeId)));
    }

    private async Task AssertInstances(int[] challengeIds, int[] participationIds, int expectedCount)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instances = await context.GameInstances.AsNoTracking()
            .Where(instance => challengeIds.Contains(instance.ChallengeId) &&
                               participationIds.Contains(instance.ParticipationId))
            .ToArrayAsync();
        Assert.Equal(expectedCount, instances.Length);
        Assert.All(instances.GroupBy(instance => new { instance.ParticipationId, instance.ChallengeId }),
            group => Assert.Single(group));
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
