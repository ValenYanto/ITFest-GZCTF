using GZCTF.Models.Request.Edit;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Cache;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

public class SpeedrunService(AppDbContext context, IGameNoticeRepository noticeRepository, CacheHelper cacheHelper)
{
    private const int AdvisoryLockNamespace = 0x5350524E; // "SPRN"

    public async Task<SpeedrunStateModel> GetState(int gameId, CancellationToken token = default)
    {
        var game = await context.Games.AsNoTracking().SingleOrDefaultAsync(g => g.Id == gameId, token);
        if (game is null || game.Mode != GameMode.Speedrun)
            return new() { IsSpeedrun = false };

        await UpdateExpiredRound(game, token);
        var round = await CurrentRound(gameId, token);
        if (round is { Status: SpeedrunRoundStatus.Running or SpeedrunRoundStatus.Overtime })
            await ReleaseDueHints(game, round, token);
        var categories = await context.SpeedrunCategories.AsNoTracking()
            .Where(c => c.GameId == gameId && c.Included && context.GameChallenges.Any(challenge =>
                challenge.GameId == gameId && challenge.Category == c.Category && challenge.IsEnabled))
            .ToArrayAsync(token);
        var now = DateTimeOffset.UtcNow;
        return new()
        {
            IsSpeedrun = true,
            CurrentRound = round is null ? null : ToModel(round, now),
            UsedCategories = categories.Where(c => c.Used).Select(c => c.Category).ToArray(),
            RemainingCategories = categories.Where(c => !c.Used).Select(c => c.Category).ToArray(),
            Message = round?.Status == SpeedrunRoundStatus.Overtime ? game.SpeedrunEmergencyHintText : null
        };
    }

    public async Task<SpeedrunSettingsModel?> GetSettings(int gameId, CancellationToken token = default)
    {
        var game = await context.Games.AsNoTracking().SingleOrDefaultAsync(g => g.Id == gameId, token);
        if (game is null)
            return null;
        return new()
        {
            DefaultRoundDurationMinutes = game.SpeedrunDefaultRoundDurationMinutes,
            OvertimeMinutes = game.SpeedrunOvertimeMinutes,
            DefaultRoundDurationSeconds = game.SpeedrunDefaultRoundDurationSeconds,
            OvertimeSeconds = game.SpeedrunOvertimeSeconds,
            AllowManualExtend = game.SpeedrunAllowManualExtend,
            HideInactiveChallenges = game.SpeedrunHideInactiveChallenges,
            EmergencyHintEnabled = game.SpeedrunEmergencyHintEnabled,
            EmergencyHintText = game.SpeedrunEmergencyHintText,
            State = await GetState(gameId, token),
            Categories = await context.SpeedrunCategories.AsNoTracking().Where(c => c.GameId == gameId)
                .OrderBy(c => c.Category).Select(c => new SpeedrunCategoryModel
                { Id = c.Id, Category = c.Category, Included = c.Included, Used = c.Used }).ToArrayAsync(token)
        };
    }

    public async Task RefreshCategories(int gameId, CancellationToken token = default)
    {
        var found = await context.GameChallenges.AsNoTracking().Where(c => c.GameId == gameId)
            .Select(c => c.Category).Distinct().ToArrayAsync(token);
        var existing = await context.SpeedrunCategories.Where(c => c.GameId == gameId).ToArrayAsync(token);
        foreach (var category in found.Where(category => existing.All(e => e.Category != category)))
            context.SpeedrunCategories.Add(new() { GameId = gameId, Category = category });
        await context.SaveChangesAsync(token);
    }

    public async Task<SpeedrunRoundModel?> Spin(Game game, Guid? userId, CancellationToken token = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await AcquireGameLock(game.Id, token);
        await UpdateExpiredRoundCore(game, token);
        if (await CurrentRound(game.Id, token) is not null)
        {
            await transaction.RollbackAsync(token);
            return null;
        }
        var categories = await context.SpeedrunCategories.Where(c => c.GameId == game.Id && c.Included && !c.Used &&
            context.GameChallenges.Any(challenge => challenge.GameId == game.Id &&
                challenge.Category == c.Category && challenge.IsEnabled))
            .ToArrayAsync(token);
        if (categories.Length == 0)
        {
            await transaction.RollbackAsync(token);
            return null;
        }
        var selected = categories[Random.Shared.Next(categories.Length)];
        var round = new SpeedrunRound
        {
            GameId = game.Id, Category = selected.Category, Status = SpeedrunRoundStatus.Ready,
            DurationMinutes = game.SpeedrunDefaultRoundDurationMinutes, OvertimeMinutes = game.SpeedrunOvertimeMinutes,
            DurationSeconds = game.SpeedrunDefaultRoundDurationSeconds, OvertimeSeconds = game.SpeedrunOvertimeSeconds,
            SelectedByUserId = userId
        };
        context.SpeedrunRounds.Add(round);
        await context.SaveChangesAsync(token);
        await Announce(game.Id, $"Speedrun category selected: {selected.Category}", token);
        await cacheHelper.FlushScoreboardCache(game.Id, token);
        await transaction.CommitAsync(token);
        return ToModel(round, DateTimeOffset.UtcNow);
    }

    public async Task<bool> Start(Game game, int roundId, CancellationToken token = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await AcquireGameLock(game.Id, token);
        if (await context.SpeedrunRounds.AnyAsync(r => r.GameId == game.Id &&
            (r.Status == SpeedrunRoundStatus.Running || r.Status == SpeedrunRoundStatus.Overtime), token))
            return false;
        var round = await context.SpeedrunRounds.SingleOrDefaultAsync(r => r.Id == roundId && r.GameId == game.Id, token);
        if (round is null || round.Status != SpeedrunRoundStatus.Ready)
            return false;
        if (!await context.GameChallenges.AnyAsync(challenge => challenge.GameId == game.Id &&
                challenge.Category == round.Category && challenge.IsEnabled, token))
            return false;
        var category = await context.SpeedrunCategories.SingleAsync(c => c.GameId == game.Id && c.Category == round.Category, token);
        category.Used = true;
        round.Status = SpeedrunRoundStatus.Running;
        round.StartedAtUtc = DateTimeOffset.UtcNow;
        round.EndsAtUtc = round.StartedAtUtc.Value.AddSeconds(round.DurationSeconds);
        await context.SaveChangesAsync(token);
        await Announce(game.Id, $"Speedrun round started: {round.Category}", token);
        await cacheHelper.FlushScoreboardCache(game.Id, token);
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task<bool> End(int gameId, int roundId, CancellationToken token = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await AcquireGameLock(gameId, token);
        var round = await context.SpeedrunRounds.SingleOrDefaultAsync(r => r.Id == roundId && r.GameId == gameId, token);
        if (round is null || round.Status is SpeedrunRoundStatus.Finished or SpeedrunRoundStatus.Cancelled)
            return false;
        round.Status = SpeedrunRoundStatus.Finished;
        round.FinishedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        await Announce(gameId, "Speedrun round finished.", token);
        await cacheHelper.FlushScoreboardCache(gameId, token);
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task<bool> Extend(Game game, int roundId, int seconds, CancellationToken token = default)
    {
        if (!game.SpeedrunAllowManualExtend)
            return false;
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await AcquireGameLock(game.Id, token);
        var round = await context.SpeedrunRounds.SingleOrDefaultAsync(r => r.Id == roundId && r.GameId == game.Id, token);
        if (round is null || round.Status is not (SpeedrunRoundStatus.Running or SpeedrunRoundStatus.Overtime))
            return false;
        if (round.Status == SpeedrunRoundStatus.Overtime)
            round.OvertimeEndsAtUtc = (round.OvertimeEndsAtUtc ?? DateTimeOffset.UtcNow).AddSeconds(seconds);
        else
            round.EndsAtUtc = (round.EndsAtUtc ?? DateTimeOffset.UtcNow).AddSeconds(seconds);
        round.ManuallyExtendedSeconds += seconds;
        round.ManuallyExtendedMinutes = round.ManuallyExtendedSeconds / 60;
        await context.SaveChangesAsync(token);
        await cacheHelper.FlushScoreboardCache(game.Id, token);
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task<bool> SetRemainingTime(Game game, int roundId, int remainingSeconds,
        CancellationToken token = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await AcquireGameLock(game.Id, token);
        var round = await context.SpeedrunRounds.SingleOrDefaultAsync(r => r.Id == roundId && r.GameId == game.Id,
            token);
        if (round is null || round.Status is not (SpeedrunRoundStatus.Running or SpeedrunRoundStatus.Overtime))
            return false;

        var end = DateTimeOffset.UtcNow.AddSeconds(remainingSeconds);
        if (round.Status == SpeedrunRoundStatus.Overtime)
            round.OvertimeEndsAtUtc = end;
        else
            round.EndsAtUtc = end;
        await context.SaveChangesAsync(token);
        await ReleaseDueHints(game, round, token);
        await cacheHelper.FlushScoreboardCache(game.Id, token);
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task<bool> UpdateCategory(int gameId, int categoryId, bool? used, bool? included,
        CancellationToken token = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await AcquireGameLock(gameId, token);
        var category = await context.SpeedrunCategories.SingleOrDefaultAsync(c => c.Id == categoryId &&
            c.GameId == gameId, token);
        if (category is null)
            return false;

        var active = await CurrentRound(gameId, token);
        if (active?.Category == category.Category)
            return false;

        if (used.HasValue)
            category.Used = used.Value;
        if (included.HasValue)
            category.Included = included.Value;
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task<bool> ResetCategories(int gameId, CancellationToken token = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await AcquireGameLock(gameId, token);
        if (await CurrentRound(gameId, token) is not null)
            return false;
        await context.SpeedrunCategories.Where(c => c.GameId == gameId).ExecuteUpdateAsync(s => s.SetProperty(c => c.Used, false), token);
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task<bool> CanAccessChallenge(Game game, int challengeId, CancellationToken token = default)
    {
        var challenge = await context.GameChallenges.AsNoTracking()
            .Where(item => item.Id == challengeId && item.GameId == game.Id)
            .Select(item => new { item.Category, item.IsEnabled })
            .SingleOrDefaultAsync(token);
        if (challenge is null || !challenge.IsEnabled)
            return false;

        if (game.Mode != GameMode.Speedrun)
            return true;
        await UpdateExpiredRound(game, token);
        var round = await CurrentRound(game.Id, token);
        if (round is null || round.Status is not (SpeedrunRoundStatus.Running or SpeedrunRoundStatus.Overtime))
            return false;
        if (challenge.Category != round.Category)
            return false;
        return round.Status != SpeedrunRoundStatus.Overtime ||
               !await context.FirstSolves.AsNoTracking().AnyAsync(fs => fs.ChallengeId == challengeId, token);
    }

    public Task<bool> IsChallengeInActiveRound(int gameId, int challengeId, CancellationToken token = default) =>
        context.GameChallenges.AsNoTracking().Where(challenge => challenge.GameId == gameId &&
                challenge.Id == challengeId)
            .AnyAsync(challenge => context.SpeedrunRounds.Any(round => round.GameId == gameId &&
                round.Category == challenge.Category &&
                (round.Status == SpeedrunRoundStatus.Running || round.Status == SpeedrunRoundStatus.Overtime)), token);

    public Task<bool> IsCategoryInActiveRound(int gameId, ChallengeCategory category,
        CancellationToken token = default) =>
        context.SpeedrunRounds.AsNoTracking().AnyAsync(round => round.GameId == gameId &&
            round.Category == category &&
            (round.Status == SpeedrunRoundStatus.Running || round.Status == SpeedrunRoundStatus.Overtime), token);

    /// <summary>
    /// Serializes lifecycle mutations with Speedrun round transitions. The caller must already own a database transaction.
    /// </summary>
    public Task LockGameLifecycle(int gameId, CancellationToken token = default) => AcquireGameLock(gameId, token);

    public async Task<List<string>?> GetVisibleHints(Game game, GameChallenge challenge,
        CancellationToken token = default)
    {
        if (game.Mode != GameMode.Speedrun || challenge.Hints is not { Count: > 0 } hints)
            return challenge.Hints;

        var round = await CurrentRound(game.Id, token);
        if (round?.Status is not (SpeedrunRoundStatus.Running or SpeedrunRoundStatus.Overtime) ||
            round.StartedAtUtc is null)
            return [];

        await ReleaseDueHints(game, round, token);
        var releasedIndexes = await context.SpeedrunHintReleaseLogs.AsNoTracking()
            .Where(log => log.RoundId == round.Id && log.ChallengeId == challenge.Id)
            .Select(log => log.HintIndex)
            .ToHashSetAsync(token);
        return hints.Where((_, index) => releasedIndexes.Contains(index)).ToList();
    }

    private async Task UpdateExpiredRound(Game game, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await AcquireGameLock(game.Id, token);
        await UpdateExpiredRoundCore(game, token);
        await transaction.CommitAsync(token);
    }

    private async Task UpdateExpiredRoundCore(Game game, CancellationToken token)
    {
        var round = await context.SpeedrunRounds.SingleOrDefaultAsync(r => r.GameId == game.Id &&
            (r.Status == SpeedrunRoundStatus.Running || r.Status == SpeedrunRoundStatus.Overtime), token);
        if (round is null)
            return;
        var now = DateTimeOffset.UtcNow;
        if (round.Status == SpeedrunRoundStatus.Running && round.EndsAtUtc <= now)
        {
            var challengeIds = context.GameChallenges.Where(c => c.GameId == game.Id && c.Category == round.Category &&
                c.IsEnabled).Select(c => c.Id);
            var hasUnsolved = await challengeIds.AnyAsync(id => !context.FirstSolves.Any(fs => fs.ChallengeId == id), token);
            if (hasUnsolved && round.OvertimeSeconds > 0)
            {
                round.Status = SpeedrunRoundStatus.Overtime;
                round.OvertimeEndsAtUtc = now.AddSeconds(round.OvertimeSeconds);
                if (!round.OvertimeNoticeSent)
                {
                    round.OvertimeNoticeSent = true;
                    await Announce(game.Id, game.SpeedrunEmergencyHintEnabled ? game.SpeedrunEmergencyHintText : "Speedrun overtime started.", token);
                }
            }
            else
            {
                round.Status = SpeedrunRoundStatus.Finished;
                round.FinishedAtUtc = now;
            }
            await context.SaveChangesAsync(token);
            if (round.Status == SpeedrunRoundStatus.Finished)
                await Announce(game.Id, "Speedrun round finished.", token);
            await cacheHelper.FlushScoreboardCache(game.Id, token);
        }
        else if (round.Status == SpeedrunRoundStatus.Overtime && round.OvertimeEndsAtUtc <= now)
        {
            round.Status = SpeedrunRoundStatus.Finished;
            round.FinishedAtUtc = now;
            await context.SaveChangesAsync(token);
            await Announce(game.Id, "Speedrun round finished.", token);
            await cacheHelper.FlushScoreboardCache(game.Id, token);
        }
    }

    private Task<SpeedrunRound?> CurrentRound(int gameId, CancellationToken token) =>
        context.SpeedrunRounds.AsNoTracking().Where(r => r.GameId == gameId &&
            (r.Status == SpeedrunRoundStatus.Ready || r.Status == SpeedrunRoundStatus.Running || r.Status == SpeedrunRoundStatus.Overtime))
            .OrderByDescending(r => r.Id).FirstOrDefaultAsync(token);

    private Task AcquireGameLock(int gameId, CancellationToken token) =>
        context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0}, {1})",
            [AdvisoryLockNamespace, gameId], cancellationToken: token);

    private async Task ReleaseDueHints(Game game, SpeedrunRound round, CancellationToken token)
    {
        if (round.StartedAtUtc is null)
            return;

        var ownsTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await context.Database.BeginTransactionAsync(token)
            : null;
        if (ownsTransaction)
        {
            await AcquireGameLock(game.Id, token);
            var lockedRound = await context.SpeedrunRounds.AsNoTracking().SingleOrDefaultAsync(item =>
                item.Id == round.Id && item.GameId == game.Id &&
                (item.Status == SpeedrunRoundStatus.Running || item.Status == SpeedrunRoundStatus.Overtime), token);
            if (lockedRound is null)
            {
                await transaction!.CommitAsync(token);
                return;
            }
            round = lockedRound;
        }

        var elapsedSeconds = GetHintElapsedSeconds(round, DateTimeOffset.UtcNow);
        var challenges = await context.GameChallenges.AsNoTracking()
            .Where(challenge => challenge.GameId == game.Id && challenge.Category == round.Category &&
                                challenge.IsEnabled)
            .ToArrayAsync(token);
        challenges = challenges.Where(challenge => challenge.Hints is { Count: > 0 }).ToArray();
        var solvedChallengeIds = await context.FirstSolves.AsNoTracking()
            .Where(solve => solve.Challenge.GameId == game.Id && solve.Challenge.Category == round.Category)
            .Select(solve => solve.ChallengeId)
            .Distinct()
            .ToArrayAsync(token);
        challenges = challenges
            .Where(challenge => !solvedChallengeIds.Contains(challenge.Id))
            .ToArray();

        var releasedByHintIndex = new Dictionary<int, List<string>>();

        foreach (var challenge in challenges)
        {
            for (var index = 0; index < challenge.Hints!.Count; index++)
            {
                if (GetHintReleaseSeconds(challenge, index) > elapsedSeconds)
                    continue;

                var released = await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "SpeedrunHintReleaseLogs" ("GameId", "RoundId", "ChallengeId", "HintIndex", "ReleasedAtUtc")
                    SELECT {game.Id}, {round.Id}, {challenge.Id}, {index}, {DateTimeOffset.UtcNow}
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "FirstSolves" WHERE "ChallengeId" = {challenge.Id}
                    )
                    ON CONFLICT ("RoundId", "ChallengeId", "HintIndex") DO NOTHING
                    """, token);
                if (released == 1)
                {
                    if (!releasedByHintIndex.TryGetValue(index, out var titles))
                        releasedByHintIndex[index] = titles = [];
                    titles.Add(challenge.Title);
                }
            }
        }

        foreach (var (index, titles) in releasedByHintIndex.OrderBy(item => item.Key))
            await Announce(game.Id, BuildHintReleaseMessage(index, titles), token);

        if (transaction is not null)
            await transaction.CommitAsync(token);
    }

    private static int GetHintReleaseSeconds(GameChallenge challenge, int index) =>
        challenge.SpeedrunHintReleaseSeconds?.ElementAtOrDefault(index) ??
        (challenge.SpeedrunHintReleaseMinutes?.ElementAtOrDefault(index) ?? 0) * 60;

    internal static int GetHintElapsedSeconds(SpeedrunRound round, DateTimeOffset now)
    {
        if (round.StartedAtUtc is null)
            return 0;

        var wallClockElapsedSeconds = Math.Max(0, (int)(now - round.StartedAtUtc.Value).TotalSeconds);
        var regularDurationSeconds = round.DurationSeconds > 0
            ? round.DurationSeconds
            : round.DurationMinutes * 60;
        var totalRegularSeconds = regularDurationSeconds + round.ManuallyExtendedSeconds;

        if (round.Status == SpeedrunRoundStatus.Overtime && round.OvertimeEndsAtUtc is not null)
        {
            var overtimeDurationSeconds = round.OvertimeSeconds > 0
                ? round.OvertimeSeconds
                : round.OvertimeMinutes * 60;
            var overtimeRemainingSeconds = Math.Max(0,
                (int)Math.Ceiling((round.OvertimeEndsAtUtc.Value - now).TotalSeconds));
            var overtimeElapsedSeconds = Math.Max(0, overtimeDurationSeconds - overtimeRemainingSeconds);
            return totalRegularSeconds + overtimeElapsedSeconds;
        }

        if (round.Status != SpeedrunRoundStatus.Running || round.EndsAtUtc is null)
            return Math.Max(wallClockElapsedSeconds, totalRegularSeconds);

        // The admin timer is authoritative when it is moved forward. Keep wall-clock elapsed time as a
        // lower bound while Running so an extension never makes the hint timeline regress.
        var remainingSeconds = Math.Max(0, (int)Math.Ceiling((round.EndsAtUtc.Value - now).TotalSeconds));
        return Math.Max(wallClockElapsedSeconds, totalRegularSeconds - remainingSeconds);
    }

    internal static string BuildHintReleaseMessage(int hintIndex, IReadOnlyCollection<string> challengeTitles)
    {
        var target = challengeTitles.Count == 1 ? challengeTitles.First() : $"{challengeTitles.Count} challenges";
        return $"Hint #{hintIndex + 1} released for {target}.";
    }

    private async Task Announce(int gameId, string message, CancellationToken token) =>
        await noticeRepository.AddNotice(new() { GameId = gameId, Type = NoticeType.Normal, Values = [message] }, token);

    private static SpeedrunRoundModel ToModel(SpeedrunRound round, DateTimeOffset now)
    {
        var end = round.Status == SpeedrunRoundStatus.Overtime ? round.OvertimeEndsAtUtc : round.EndsAtUtc;
        return new()
        {
            Id = round.Id, Category = round.Category, Status = round.Status, StartedAtUtc = round.StartedAtUtc,
            EndsAtUtc = round.EndsAtUtc, OvertimeEndsAtUtc = round.OvertimeEndsAtUtc,
            TimeLeftSeconds = end is null ? 0 : Math.Max(0, (int)(end.Value - now).TotalSeconds)
        };
    }
}
