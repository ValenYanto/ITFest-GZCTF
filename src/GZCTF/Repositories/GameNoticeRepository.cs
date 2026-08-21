using GZCTF.Hubs;
using GZCTF.Hubs.Clients;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Cache;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;


namespace GZCTF.Repositories;

public class GameNoticeRepository(
    CacheHelper cacheHelper,
    ILogger<GameNoticeRepository> logger,
    IHubContext<UserHub, IUserClient> hub,
    AppDbContext context) : RepositoryBase(context), IGameNoticeRepository
{
    public async Task<GameNotice> AddNotice(GameNotice notice, CancellationToken token = default)
    {
        await Context.AddAsync(notice, token);
        await SaveAsync(token);

        await cacheHelper.RemoveAsync(CacheKey.GameNotice(notice.GameId), token);

        var freeze = await Context.Games.AsNoTracking()
            .Where(game => game.Id == notice.GameId)
            .Select(game => new { game.ScoreboardFrozen, game.ScoreboardFreezeTimeUtc })
            .SingleAsync(token);
        var hideTeam = freeze.ScoreboardFrozen &&
                       (freeze.ScoreboardFreezeTimeUtc is null ||
                        notice.PublishTimeUtc >= freeze.ScoreboardFreezeTimeUtc);
        await hub.Clients.Group($"Game_{notice.GameId}")
            .ReceivedGameNotice(hideTeam ? MaskBloodNotice(notice) : notice);

        return notice;
    }

    public Task<GameNotice[]> GetNormalNotices(int gameId, CancellationToken token = default) =>
        Context.GameNotices
            .Where(n => n.GameId == gameId && n.Type == NoticeType.Normal)
            .ToArrayAsync(token);

    public Task<GameNotice?> GetNoticeById(int gameId, int noticeId, CancellationToken token = default) =>
        Context.GameNotices.FirstOrDefaultAsync(e => e.Id == noticeId && e.GameId == gameId, token);

    public Task<DataWithModifiedTime<GameNotice[]>> GetLatestNotices(int gameId, CancellationToken token = default)
        => cacheHelper.GetOrCreateAsync(logger, CacheKey.GameNotice(gameId), async entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromMinutes(30);
            var notices = await Context.GameNotices.Where(e => e.GameId == gameId)
                .OrderByDescending(e => e.Type == NoticeType.Normal ? DateTimeOffset.UtcNow : e.PublishTimeUtc)
                .Take(300).ToArrayAsync(token);
            return new DataWithModifiedTime<GameNotice[]>(notices, DateTimeOffset.UtcNow);
        }, token: token);

    private static GameNotice MaskBloodNotice(GameNotice notice)
    {
        if (notice.Type is not (NoticeType.FirstBlood or NoticeType.SecondBlood or NoticeType.ThirdBlood))
            return notice;

        return new()
        {
            Id = notice.Id,
            Type = notice.Type,
            PublishTimeUtc = notice.PublishTimeUtc,
            Values = ["Anonymous", notice.Values?.ElementAtOrDefault(1) ?? string.Empty]
        };
    }

    public async Task RemoveNotice(GameNotice notice, CancellationToken token = default)
    {
        Context.Remove(notice);
        await SaveAsync(token);

        await cacheHelper.RemoveAsync(CacheKey.GameNotice(notice.GameId), token);
    }

    public async Task<GameNotice> UpdateNotice(GameNotice notice, CancellationToken token = default)
    {
        notice.PublishTimeUtc = DateTimeOffset.UtcNow;
        await SaveAsync(token);

        await cacheHelper.RemoveAsync(CacheKey.GameNotice(notice.GameId), token);

        return notice;
    }
}
