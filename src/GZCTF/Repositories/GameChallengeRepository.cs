using GZCTF.Models.Request.Edit;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Repositories;

public class GameChallengeRepository(
    AppDbContext context,
    IBlobRepository blobRepository
) : RepositoryBase(context),
    IGameChallengeRepository
{
    public async Task AddFlags(GameChallenge challenge, FlagCreateModel[] models, CancellationToken token = default)
    {
        foreach (var model in models)
        {
            var attachment = model.ToAttachment(await blobRepository.GetBlobByHash(model.FileHash, token));

            challenge.Flags.Add(new() { Flag = model.Flag, Challenge = challenge, Attachment = attachment });
        }

        await SaveAsync(token);
    }

    public async Task<GameChallenge> CreateChallenge(Game game, GameChallenge challenge,
        CancellationToken token = default)
    {
        await Context.AddAsync(challenge, token);
        game.Challenges.Add(challenge);
        await SaveAsync(token);
        return challenge;
    }

    public async Task<bool> EnsureInstances(GameChallenge challenge, Game game, CancellationToken token = default)
    {
        if (!challenge.IsEnabled || challenge.GameId != game.Id)
            return false;

        // Preserve the original lifecycle contract: callers may have just toggled the tracked
        // challenge to enabled. Persist that state before the set-based INSERT reads it back.
        await SaveAsync(token);
        return await ReconcileInstances(game.Id, [challenge.Id], token) > 0;
    }

    public Task<int> ReconcileInstances(int gameId, IReadOnlyCollection<int>? challengeIds = null,
        CancellationToken token = default)
    {
        var accepted = (byte)ParticipationStatus.Accepted;
        var minimumOperationTime = DateTimeOffset.MinValue;
        var ids = challengeIds?.Distinct().ToArray();

        return ids is { Length: > 0 }
            ? Context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "GameInstances"
                    ("ChallengeId", "ParticipationId", "IsLoaded", "LastContainerOperation")
                SELECT challenge."Id", participation."Id", FALSE, {minimumOperationTime}
                FROM "GameChallenges" AS challenge
                CROSS JOIN "Participations" AS participation
                WHERE challenge."GameId" = {gameId}
                  AND participation."GameId" = {gameId}
                  AND participation."Status" = {accepted}
                  AND challenge."IsEnabled" = TRUE
                  AND challenge."Id" = ANY ({ids})
                ON CONFLICT ("ChallengeId", "ParticipationId") DO NOTHING
                """, token)
            : Context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "GameInstances"
                    ("ChallengeId", "ParticipationId", "IsLoaded", "LastContainerOperation")
                SELECT challenge."Id", participation."Id", FALSE, {minimumOperationTime}
                FROM "GameChallenges" AS challenge
                CROSS JOIN "Participations" AS participation
                WHERE challenge."GameId" = {gameId}
                  AND participation."GameId" = {gameId}
                  AND participation."Status" = {accepted}
                  AND challenge."IsEnabled" = TRUE
                ON CONFLICT ("ChallengeId", "ParticipationId") DO NOTHING
                """, token);
    }

    public Task<GameChallenge?> GetChallenge(int gameId, int id, CancellationToken token = default)
        => Context.GameChallenges
            .Where(c => c.Id == id && c.GameId == gameId).FirstOrDefaultAsync(token);

    public Task LoadFlags(GameChallenge challenge, CancellationToken token = default) =>
        Context.Entry(challenge).Collection(c => c.Flags).LoadAsync(token);

    public Task<GameChallenge[]> GetChallenges(int gameId, CancellationToken token = default) =>
        Context.GameChallenges
            .Where(c => c.GameId == gameId).OrderBy(c => c.Id).ToArrayAsync(token);

    public Task<GameChallenge[]> GetChallengesWithTrafficCapturing(int gameId, CancellationToken token = default) =>
        Context.GameChallenges.IgnoreAutoIncludes().Where(c => c.GameId == gameId && c.EnableTrafficCapture)
            .ToArrayAsync(token);

    public async Task RemoveChallenge(GameChallenge challenge, bool save = true, CancellationToken token = default)
    {
        await blobRepository.DeleteAttachment(challenge.Attachment, token);

        await LoadFlags(challenge, token);

        // only dynamic attachment challenge's flag contexts have attachment
        if (challenge.Type == ChallengeType.DynamicAttachment)
            foreach (var flag in challenge.Flags)
                await blobRepository.DeleteAttachment(flag.Attachment, token);

        Context.RemoveRange(challenge.Flags);
        Context.Remove(challenge);

        if (save)
            await SaveAsync(token);
    }

    public async Task<TaskStatus> RemoveFlag(GameChallenge challenge, int flagId, CancellationToken token = default)
    {
        var flag = await Context.FlagContexts
            .FirstOrDefaultAsync(f => f.Challenge == challenge && f.Id == flagId, token);

        if (flag is null)
            return TaskStatus.NotFound;

        await blobRepository.DeleteAttachment(flag.Attachment, token);

        Context.Remove(flag);

        await SaveAsync(token);

        // If there are no more flags, disable the challenge
        if (!await Context.FlagContexts.AnyAsync(f => f.Challenge == challenge, token))
        {
            challenge.IsEnabled = false;
            await SaveAsync(token);
        }

        return TaskStatus.Success;
    }

    public async Task UpdateAttachment(GameChallenge challenge, AttachmentCreateModel model,
        CancellationToken token = default)
    {
        var attachment = model.ToAttachment(await blobRepository.GetBlobByHash(model.FileHash, token));

        await blobRepository.DeleteAttachment(challenge.Attachment, token);

        if (attachment is not null)
            await Context.AddAsync(attachment, token);

        challenge.Attachment = attachment;

        await SaveAsync(token);
    }
}
