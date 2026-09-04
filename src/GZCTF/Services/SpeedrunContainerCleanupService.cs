using System.Threading.Channels;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

/// <summary>
/// Removes dynamic containers left by completed Speedrun rounds without blocking the round lifecycle request.
/// Cleanup is restricted to containers that existed before the round finished, so a delayed job cannot remove
/// a container created by a later round that happens to reuse the same category.
/// </summary>
public sealed class SpeedrunContainerCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<SpeedrunContainerCleanupService> logger) : BackgroundService
{
    private const int MaxParallelDestructions = 4;
    private readonly Channel<CleanupRequest> _queue = Channel.CreateUnbounded<CleanupRequest>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public void Queue(int gameId, ChallengeCategory category, DateTimeOffset cutoffUtc)
    {
        if (!_queue.Writer.TryWrite(new(gameId, category, cutoffUtc)))
            logger.LogWarning("Failed to queue dynamic-container cleanup for Speedrun game {GameId}.", gameId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await Cleanup(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Unexpected dynamic-container cleanup failure for Speedrun game {GameId}, category {Category}.",
                    request.GameId, request.Category);
            }
        }
    }

    private async Task Cleanup(CleanupRequest request, CancellationToken token)
    {
        Guid[] containerIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            containerIds = await context.Containers.AsNoTracking()
                .Where(container => container.GameInstance != null &&
                                    container.GameInstance.Challenge.GameId == request.GameId &&
                                    container.GameInstance.Challenge.Category == request.Category &&
                                    container.GameInstance.Challenge.Type == ChallengeType.DynamicContainer &&
                                    container.StartedAt <= request.CutoffUtc)
                .Select(container => container.Id)
                .ToArrayAsync(token);
        }

        if (containerIds.Length == 0)
            return;

        var destroyed = 0;
        var failed = 0;
        await Parallel.ForEachAsync(containerIds,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDestructions, CancellationToken = token },
            async (containerId, cancellationToken) =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IContainerRepository>();
                var container = await repository.GetContainerWithInstanceById(containerId, cancellationToken);

                // Recheck the scope and cutoff after loading: another operation may have replaced the relation
                // between the initial query and this bounded-parallel destruction.
                if (container?.GameInstance?.Challenge is not { } challenge ||
                    challenge.GameId != request.GameId ||
                    challenge.Category != request.Category ||
                    challenge.Type != ChallengeType.DynamicContainer ||
                    container.StartedAt > request.CutoffUtc)
                    return;

                if (await repository.DestroyContainer(container, cancellationToken))
                    Interlocked.Increment(ref destroyed);
                else
                    Interlocked.Increment(ref failed);
            });

        logger.LogInformation(
            "Speedrun container cleanup completed for game {GameId}, category {Category}: {Destroyed} destroyed, {Failed} failed.",
            request.GameId, request.Category, destroyed, failed);
    }

    private readonly record struct CleanupRequest(
        int GameId,
        ChallengeCategory Category,
        DateTimeOffset CutoffUtc);
}
