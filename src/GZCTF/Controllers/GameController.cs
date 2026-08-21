using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Mime;
using System.Security.Claims;
using System.Threading.Channels;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Admin;
using GZCTF.Models.Request.Edit;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Cache;
using GZCTF.Services.Config;
using GZCTF.Storage.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace GZCTF.Controllers;

/// <summary>
/// Game related APIs
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class GameController(
    ILogger<GameController> logger,
    UserManager<UserInfo> userManager,
    ChannelWriter<Submission> channelWriter,
    CacheHelper cacheHelper,
    IBlobStorage storage,
    IConfigService configService,
    IBlobRepository blobService,
    IGameRepository gameRepository,
    ITeamRepository teamRepository,
    IDivisionRepository divisionRepository,
    IGameEventRepository eventRepository,
    IGameNoticeRepository noticeRepository,
    ICheatInfoRepository cheatInfoRepository,
    IContainerRepository containerRepository,
    IGameEventRepository gameEventRepository,
    ISubmissionRepository submissionRepository,
    IGameChallengeRepository challengeRepository,
    IGameInstanceRepository gameInstanceRepository,
    IParticipationRepository participationRepository,
    SpeedrunService speedrunService,
    AppDbContext dbContext,
    IOptionsSnapshot<ContainerPolicy> containerPolicy,
    IStringLocalizer<Program> localizer) : ControllerBase
{
    [HttpGet("{id:int}/Live")]
    [ProducesResponseType(typeof(LiveScoreboardStateModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> LiveScoreboard(int id, CancellationToken token)
    {
        var game = await dbContext.Games.AsNoTracking().Include(g => g.LiveScoreboardConfig)
            .SingleOrDefaultAsync(g => g.Id == id, token);
        if (game is null)
            return NotFound(new RequestResponse("Game not found.", StatusCodes.Status404NotFound));

        var scoreboard = game.ScoreboardFrozen
            ? await gameRepository.GetFrozenScoreboard(game, token)
            : await gameRepository.TryGetScoreboard(id, token) ?? await gameRepository.GetScoreboard(game, token);
        var teamIds = await dbContext.Participations.AsNoTracking()
            .Where(participation => participation.GameId == id &&
                                    participation.Status == ParticipationStatus.Accepted)
            .Select(participation => new { participation.Team.Name, participation.TeamId })
            .ToDictionaryAsync(team => team.Name, team => team.TeamId, token);
        (var notices, _) = await noticeRepository.GetLatestNotices(id, token);
        return Ok(new LiveScoreboardStateModel
        {
            GameId = id,
            GameTitle = game.Title,
            GameMode = game.Mode,
            ScoreboardFrozen = game.ScoreboardFrozen,
            Config = LiveScoreboardConfigModel.FromConfig(game.LiveScoreboardConfig),
            SpeedrunState = await speedrunService.GetState(id, token),
            TopTeams = scoreboard.ItemList.OrderBy(team => team.Rank).Select(team => new LiveScoreboardTeamModel
            {
                Id = team.Id,
                Rank = team.Rank,
                Name = game.ScoreboardFrozen ? "????" : team.Name,
                Score = game.ScoreboardFrozen ? 0 : team.Score,
                SolvedCount = game.ScoreboardFrozen ? 0 : team.SolvedCount
            }).ToArray(),
            RecentEvents = notices.OrderByDescending(notice => notice.PublishTimeUtc).Take(20).Select(notice =>
            {
                var blood = notice.Type is NoticeType.FirstBlood or NoticeType.SecondBlood or NoticeType.ThirdBlood;
                var teamName = blood ? notice.Values?.ElementAtOrDefault(0) : null;
                var challengeTitle = blood ? notice.Values?.ElementAtOrDefault(1) : null;
                var hideTeam = blood && ShouldMaskBloodNotice(game, notice);
                var displayedTeamName = hideTeam ? "????" : teamName;
                return new LiveScoreboardEventModel
                {
                    Id = $"notice-{notice.Id}",
                    Type = notice.Type,
                    CreatedAt = notice.PublishTimeUtc,
                    TeamId = teamName is not null && teamIds.TryGetValue(teamName, out var teamId) ? teamId : null,
                    TeamName = blood ? displayedTeamName : null,
                    ChallengeTitle = challengeTitle,
                    Message = blood
                        ? $"{displayedTeamName} solved {challengeTitle}"
                        : notice.Values is { Count: > 0 }
                            ? string.Join(" ", notice.Values)
                            : notice.Type.ToString()
                };
            }).ToArray()
        });
    }
    /// <summary>
    /// Get the recent games
    /// </summary>
    /// <remarks>
    /// Retrieves recent game in three weeks
    /// </remarks>
    /// <param name="limit">Limit of the number of games</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game information</response>
    [HttpGet("Recent")]
    [ProducesResponseType(typeof(BasicGameInfoModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> RecentGames(
        [FromQuery][Range(0, 50)] int limit,
        CancellationToken token)
    {
        (var games, var lastModified) = await gameRepository.GetRecentGames(token);
        var eTag = $"\"{lastModified.ToUnixTimeSeconds():X}-{limit}\"";
        if (ContextHelper.IsNotModified(Request, Response, eTag, lastModified))
            return StatusCode(StatusCodes.Status304NotModified);

        return Ok(limit > 0 ? games.Take(limit) : games);
    }

    /// <summary>
    /// Get games
    /// </summary>
    /// <remarks>
    /// Retrieves game information in specified range
    /// </remarks>
    /// <param name="count"></param>
    /// <param name="skip"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game notices</response>
    /// <response code="400">Game not found</response>
    [HttpGet]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Query))]
    [ResponseCache(VaryByQueryKeys = ["count", "skip"], Duration = 60)]
    [ProducesResponseType(typeof(ArrayResponse<BasicGameInfoModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Games([FromQuery][Range(0, 50)] int count = 10,
        [FromQuery] int skip = 0, CancellationToken token = default)
        => Ok(await gameRepository.GetGameInfo(count, skip, token));

    /// <summary>
    /// Get detailed game information
    /// </summary>
    /// <remarks>
    /// Retrieves detailed information about the game
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game information</response>
    /// <response code="404">Game not found</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(DetailedGameInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Game(int id, CancellationToken token)
    {
        var gameInfo = await gameRepository.GetDetailedGameInfo(id, token);

        if (gameInfo is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var count = await participationRepository.GetParticipationCount(id, token);

        Participation? part = null;
        if (await userManager.GetUserAsync(User) is { } user)
            part = await participationRepository.GetParticipation(user.Id, id, token);

        return Ok(gameInfo.WithParticipation(part, count));
    }

    [HttpGet("{id:int}/Speedrun/State")]
    [ProducesResponseType(typeof(SpeedrunStateModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSpeedrunState(int id, CancellationToken token) =>
        Ok(await speedrunService.GetState(id, token));

    /// <summary>
    /// Get check info for joining a game
    /// </summary>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    [RequireUser]
    [HttpGet("{id:int}/Check")]
    [ProducesResponseType(typeof(GameJoinCheckInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGameJoinCheckInfo(int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var user = await userManager.GetUserAsync(User);

        return Ok(await gameRepository.GetCheckInfo(game, user!, token));
    }

    /// <summary>
    /// Join a game
    /// </summary>
    /// <remarks>
    /// Join a game; requires User permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully joined the game</response>
    /// <response code="403">Unauthorized operation or invalid operation</response>
    /// <response code="404">Game not found</response>
    [RequireUser]
    [HttpPost("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> JoinGame(int id, [FromBody] GameJoinModel model, CancellationToken token)
    {
        await using var transaction = await gameRepository.BeginTransactionAsync(token);
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        if (!game.PracticeMode && game.EndTimeUtc < DateTimeOffset.UtcNow)
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_Ended)], ErrorCodes.GameEnded));

        var user = await userManager.GetUserAsync(User);
        var team = await teamRepository.GetTeamById(model.TeamId, token);

        if (team is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Team_NotFound)],
                StatusCodes.Status404NotFound));

        if (team.Members.All(u => u.Id != user!.Id))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotMemberOfTeam)]));

        var part = await participationRepository.GetParticipation(team, game, token);
        if (game.WhitelistOnly && part?.WhitelistSource is null or WhitelistSource.None)
        {
            dbContext.WhitelistJoinAttempts.Add(new()
            {
                GameId = game.Id,
                TeamId = team.Id,
                UserId = user!.Id,
                AttemptedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            logger.LogWarning("Whitelist join rejected for TeamId {TeamId}, GameId {GameId}, UserId {UserId}",
                team.Id, game.Id, user.Id);
            return StatusCode(StatusCodes.Status403Forbidden,
                new RequestResponse(
                    "Tim ini belum terdaftar di whitelist kompetisi ini. Hubungi panitia jika sudah melakukan pembayaran.",
                    StatusCodes.Status403Forbidden));
        }

        // =============== Validate division and permissions ===============

        var joinableDivisionIds = await divisionRepository.GetJoinableDivisionIds(id, token);

        Division? div = null;
        if (joinableDivisionIds is { Count: > 0 })
        {
            // We don't allow joining a game with joinable divisions without specifying a division
            if (model.DivisionId is null)
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_DivisionRequired)]));

            // Division must allow joining
            if (!joinableDivisionIds.Contains(model.DivisionId.Value))
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_InvalidDivision)]));

            div = await divisionRepository.GetDivision(id, model.DivisionId.Value, token);
            if (div is null)
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_InvalidDivision)]));

            // just redundant check, it takes little cost
            if (!div.DefaultPermissions.HasFlag(GamePermission.JoinGame))
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_InvalidDivision)]));
        }

        // =============== Validate invitation code ===============

        string? requiredInviteCode;
        if (div is not null)
            requiredInviteCode = string.IsNullOrEmpty(div.InviteCode) ? null : div.InviteCode;
        else
            requiredInviteCode = string.IsNullOrEmpty(game.InviteCode) ? null : game.InviteCode;

        if (requiredInviteCode is not null && requiredInviteCode != model.InviteCode)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_InvalidInvitationCode)]));

        // =============== Check and handle participation state ===============

        // Check if user is already in this game through a different team (exclude rejected participations)
        if (await participationRepository.CheckRepeatParticipation(user!, game, token))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_InOtherTeam)]));

        // Always clean up rejected user participations in this game
        await participationRepository.RemoveUserParticipations(user!, game, token);

        if (part is null)
        {
            // Create new participation
            part = new() { Game = game, Team = team, Division = div, Token = gameRepository.GetToken(game, team) };
            participationRepository.Add(part);
        }
        else if (part.Status == ParticipationStatus.Rejected)
        {
            // Allow changing division when re-joining after rejection
            part.Division = div;
            part.Status = ParticipationStatus.Pending;
        }
        else if (part.WhitelistSource != WhitelistSource.None && part.Members.Count == 0 &&
                 part.DivisionId is null)
        {
            // Pre-approved onboarding/manual whitelist entries choose their division on first join.
            part.Division = div;
        }
        else if (part.DivisionId != model.DivisionId)
        {
            // If trying to change division, reject
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_InvalidDivision)]));
        }

        // =============== Verify team member count limit ===============

        if (game.TeamMemberCountLimit > 0 && part.Members.Count >= game.TeamMemberCountLimit)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_TeamMemberLimitExceeded)]));

        // =============== All checks passed, add user to the team ===============

        part.Members.Add(new(user!, game, team));

        await participationRepository.SaveAsync(token);

        var shouldAcceptWithoutReview = div is null
            ? game.AcceptWithoutReview
            : !div.DefaultPermissions.HasFlag(GamePermission.RequireReview);
        if (shouldAcceptWithoutReview)
            await participationRepository.UpdateParticipationStatus(part, ParticipationStatus.Accepted, token);

        await transaction.CommitAsync(token);
        await cacheHelper.FlushScoreboardCache(part.GameId, token);

        logger.Log(StaticLocalizer[nameof(Resources.Program.Game_JoinSucceeded), team.Name, game.Title], user,
            TaskStatus.Success);

        return Ok();
    }

    /// <summary>
    /// Leave a game
    /// </summary>
    /// <remarks>
    /// Leave a game; requires User permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully left the game</response>
    /// <response code="403">Unauthorized operation or invalid operation</response>
    /// <response code="404">Game not found</response>
    [RequireUser]
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> LeaveGame(int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var user = await userManager.GetUserAsync(User);

        var part = await participationRepository.GetParticipation(user!.Id, game.Id, token);

        if (part is null || part.Members.All(u => u.UserId != user.Id))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_CannotLeaveWithoutJoin)]));

        if (part.Status != ParticipationStatus.Pending && part.Status != ParticipationStatus.Rejected)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_CannotLeaveAfterApproval)]));

        // FIXME: After approval, new users can be added, but cannot exit?
        part.Members.RemoveWhere(u => u.UserId == user.Id);

        if (part.Members.Count == 0)
            await participationRepository.RemoveParticipation(part, true, token);
        else
            await participationRepository.SaveAsync(token);

        return Ok();
    }

    /// <summary>
    /// Get the scoreboard
    /// </summary>
    /// <remarks>
    /// Retrieves the scoreboard data
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game information</response>
    /// <response code="400">Game not found</response>
    [HttpGet("{id:int}/Scoreboard")]
    [ProducesResponseType(typeof(ScoreboardModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Scoreboard([FromRoute] int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)]));

        var scoreboard = game.ScoreboardFrozen
            ? await gameRepository.GetFrozenScoreboard(game, token)
            : await gameRepository.TryGetScoreboard(id, token);
        string eTag;
        if (scoreboard is not null)
        {
            eTag = GameETag(id, scoreboard.UpdateTimeUtc);
            if (ContextHelper.IsNotModified(Request, Response, eTag, scoreboard.UpdateTimeUtc))
                return StatusCode(StatusCodes.Status304NotModified);

            return Ok(scoreboard);
        }

        scoreboard = await gameRepository.GetScoreboard(game, token);
        var lastModified = scoreboard.UpdateTimeUtc;
        eTag = GameETag(game.Id, lastModified);
        ContextHelper.SetCacheHeaders(Response, eTag, lastModified);

        return Ok(scoreboard);
    }

    /// <summary>
    /// Get game notices
    /// </summary>
    /// <remarks>
    /// Retrieves game notice data
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="count"></param>
    /// <param name="skip"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game notices</response>
    /// <response code="400">Game not found</response>
    [HttpGet("{id:int}/Notices")]
    [ProducesResponseType(typeof(GameNotice[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Notices([FromRoute] int id, [FromQuery][Range(0, 100)] int count = 100,
        [FromQuery][Range(0, 300)] int skip = 0, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)]));

        (var data, var lastModified) = await noticeRepository.GetLatestNotices(game.Id, token);
        var freezeTicks = game.ScoreboardFreezeTimeUtc?.UtcTicks ?? 0;
        var eTag =
            $"\"{game.Id}-{lastModified.ToUnixTimeSeconds():X}-{skip}-{count}-{game.ScoreboardFrozen}-{freezeTicks:X}\"";
        if (ContextHelper.IsNotModified(Request, Response, eTag, lastModified))
            return StatusCode(StatusCodes.Status304NotModified);

        var notices = data.Skip(skip).Take(count);
        return Ok(game.ScoreboardFrozen
            ? notices.Select(notice => ShouldMaskBloodNotice(game, notice) ? MaskBloodNotice(notice) : notice)
            : notices);
    }

    /// <summary>
    /// Get game events
    /// </summary>
    /// <remarks>
    /// Retrieves game event data; requires Monitor permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="count"></param>
    /// <param name="hideContainer">Hide container events</param>
    /// <param name="skip"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game events</response>
    /// <response code="400">Game not found</response>
    [RequireMonitor]
    [HttpGet("{id:int}/Events")]
    [ProducesResponseType(typeof(GameEvent[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Events([FromRoute] int id, [FromQuery] bool hideContainer = false,
        [FromQuery][Range(0, 100)] int count = 100, [FromQuery] int skip = 0, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)]));

        return Ok(await eventRepository.GetEvents(game.Id, hideContainer, count, skip, token));
    }

    /// <summary>
    /// Get game submissions
    /// </summary>
    /// <remarks>
    /// Retrieves game submission data; requires Monitor permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="type">Submission type</param>
    /// <param name="count"></param>
    /// <param name="skip"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game submissions</response>
    /// <response code="400">Game not found</response>
    [RequireMonitor]
    [HttpGet("{id:int}/Submissions")]
    [ProducesResponseType(typeof(Submission[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Submissions([FromRoute] int id, [FromQuery] AnswerResult? type = null,
        [FromQuery][Range(0, 100)] int count = 100, [FromQuery] int skip = 0, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)]));

        return Ok(await submissionRepository.GetSubmissions(game, type, count, skip, token));
    }

    /// <summary>
    /// Get game cheat information
    /// </summary>
    /// <remarks>
    /// Retrieves game cheat data; requires Monitor permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game cheat data</response>
    /// <response code="400">Game not found</response>
    [RequireMonitor]
    [HttpGet("{id:int}/CheatInfo")]
    [ProducesResponseType(typeof(CheatInfoModel[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheatInfo([FromRoute] int id, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)]));

        return Ok((await cheatInfoRepository.GetCheatInfoByGameId(game.Id, token))
            .Select(CheatInfoModel.FromCheatInfo));
    }

    /// <summary>
    /// Get challenges with traffic capturing enabled
    /// </summary>
    /// <remarks>
    /// Retrieves challenges with traffic capturing enabled; requires Monitor permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved challenge list</response>
    /// <response code="404">Capture information not found</response>
    [RequireMonitor]
    [HttpGet("Games/{id:int}/Captures")]
    [ProducesResponseType(typeof(ChallengeTrafficModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChallengesWithTrafficCapturing([FromRoute] int id, CancellationToken token)
    {
        var challenges = await challengeRepository.GetChallengesWithTrafficCapturing(id, token);

        var results = await Task.WhenAll(
            challenges.Select(c => ChallengeTrafficModel.FromChallengeAsync(c, storage, token))
        );

        return Ok(results);
    }

    /// <summary>
    /// Get team captures in a challenge
    /// </summary>
    /// <remarks>
    /// Retrieves the list of captured teams for a game challenge; requires Monitor permission
    /// </remarks>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved file list</response>
    /// <response code="404">Capture information not found</response>
    [RequireMonitor]
    [HttpGet("Captures/{challengeId:int}")]
    [ProducesResponseType(typeof(TeamTrafficModel[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChallengeTraffic([FromRoute] int challengeId, CancellationToken token)
    {
        var path = StoragePath.Combine(PathHelper.Capture, $"{challengeId}");

        var entries = await storage.ListAsync(path, cancellationToken: token);
        var participationIds = entries.Select(e => int.TryParse(e.Name, out var id) ? id : -1)
            .Where(id => id > 0).ToArray();

        if (participationIds.Length == 0)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_CaptureNotFound)],
                StatusCodes.Status404NotFound));

        var participation = await participationRepository.GetParticipationsByIds(participationIds, token);

        var results = await Task.WhenAll(
            participation.Select(p => TeamTrafficModel.FromParticipationAsync(p, challengeId, storage, token))
        );

        return Ok(results);
    }

    /// <summary>
    /// Get traffic files
    /// </summary>
    /// <remarks>
    /// Retrieves traffic packet files for a team and challenge; requires Monitor permission
    /// </remarks>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="partId">Team participation ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved file list</response>
    /// <response code="404">Capture information not found</response>
    [RequireMonitor]
    [HttpGet("Captures/{challengeId:int}/{partId:int}")]
    [ProducesResponseType(typeof(FileRecord[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeamTraffic([FromRoute] int challengeId, [FromRoute] int partId,
        CancellationToken token)
    {
        var path = StoragePath.Combine(PathHelper.Capture, $"{challengeId}", $"{partId}");

        var blobs = await storage.ListAsync(path, cancellationToken: token);

        var results = blobs.Select(blob => new FileRecord
        {
            FileName = blob.Name,
            Size = blob.Size ?? 0,
            UpdateTime = blob.LastModificationTime ?? DateTimeOffset.MinValue
        }).ToArray();

        return Ok(results);
    }

    /// <summary>
    /// Download all traffic files
    /// </summary>
    /// <remarks>
    /// Downloads all traffic packet files for a team and challenge; requires Monitor permission
    /// </remarks>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="partId">Team participation ID</param>
    /// <param name="token">Token</param>
    /// <response code="200">Successfully retrieved files</response>
    /// <response code="404">Capture information not found</response>
    [RequireMonitor]
    [HttpGet("Captures/{challengeId:int}/{partId:int}/All")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetAllTeamTraffic([FromRoute] int challengeId, [FromRoute] int partId,
        CancellationToken token)
    {
        var path = StoragePath.Combine(PathHelper.Capture, $"{challengeId}", $"{partId}");

        var filename = $"Capture-{challengeId}-{partId}-{DateTimeOffset.UtcNow:yyyyMMdd-HH.mm.ssZ}";

        return new TarDirectoryResult(storage, path, filename, token);
    }

    /// <summary>
    /// Deletes all traffic files
    /// </summary>
    /// <remarks>
    /// Deletes a team's traffic packet files for a challenge; requires Monitor permission
    /// </remarks>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="partId">Team participation ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted files</response>
    /// <response code="404">Capture information not found</response>
    [RequireMonitor]
    [HttpDelete("Captures/{challengeId:int}/{partId:int}/All")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteAllTeamTraffic([FromRoute] int challengeId, [FromRoute] int partId,
        CancellationToken token)
    {
        try
        {
            var path = StoragePath.Combine(PathHelper.Capture, $"{challengeId}", $"{partId}");

            await storage.DeleteAsync(path, token);

            return Ok();
        }
        catch (Exception e)
        {
            return BadRequest(new RequestResponse(e.Message));
        }
    }

    /// <summary>
    /// Get a traffic file
    /// </summary>
    /// <remarks>
    /// Retrieves a traffic packet file; requires Monitor permission
    /// </remarks>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="partId">Team participation ID</param>
    /// <param name="filename">Traffic packet filename</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved file</response>
    /// <response code="404">Capture information not found</response>
    [RequireMonitor]
    [HttpGet("Captures/{challengeId:int}/{partId:int}/{filename}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTeamTraffic([FromRoute] int challengeId, [FromRoute] int partId,
        [FromRoute] string filename, CancellationToken token)
    {
        try
        {
            var path = StoragePath.Combine(PathHelper.Capture, $"{challengeId}", $"{partId}", filename);

            if (!await storage.ExistsAsync(path, token))
                return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_CaptureNotFound)]));

            var stream = await storage.OpenReadAsync(path, token);

            return File(stream, MediaTypeNames.Application.Octet, filename);
        }
        catch
        {
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_CaptureNotFound)]));
        }
    }

    /// <summary>
    /// Deletes a traffic file
    /// </summary>
    /// <remarks>
    /// Deletes a traffic packet file; requires Monitor permission
    /// </remarks>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="partId">Team participation ID</param>
    /// <param name="filename">Traffic packet filename</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted file</response>
    /// <response code="404">Capture information not found</response>
    [RequireMonitor]
    [HttpDelete("Captures/{challengeId:int}/{partId:int}/{filename}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTeamTraffic([FromRoute] int challengeId, [FromRoute] int partId,
        [FromRoute] string filename, CancellationToken token)
    {
        try
        {
            var path = StoragePath.Combine(PathHelper.Capture, $"{challengeId}", $"{partId}", filename);

            if (!await storage.ExistsAsync(path, token))
                return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_CaptureNotFound)]));

            await storage.DeleteAsync(path, token);

            return Ok();
        }
        catch
        {
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_CaptureNotFound)]));
        }
    }

    /// <summary>
    /// Get team details in a game
    /// </summary>
    /// <remarks>
    /// Retrieves all challenges of the game; requires User permission and active team participation
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game challenge information</response>
    /// <response code="400">Invalid operation</response>
    /// <response code="404">Game not found</response>
    [RequireUser]
    [HttpGet("{id:int}/Details")]
    [ProducesResponseType(typeof(GameDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChallengesWithTeamInfo([FromRoute] int id, CancellationToken token)
    {
        var gameClosed = await gameRepository.IsGameClosed(id, token);
        if (gameClosed)
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_Ended)], ErrorCodes.GameEnded));

        var isSpeedrun = (await gameRepository.GetGameById(id, token))?.Mode == GameMode.Speedrun;
        var scoreboard = await gameRepository.TryGetScoreboard(id, token);
        string eTag;
        if (scoreboard is not null)
        {
            eTag = GameETag(id, scoreboard.UpdateTimeUtc);
            if (!isSpeedrun && ContextHelper.IsNotModified(Request, Response, eTag, scoreboard.UpdateTimeUtc, true))
                return StatusCode(StatusCodes.Status304NotModified);
        }

        var context = await GetContextInfo(id, token: token);

        if (context.Result is not null)
            return context.Result;

        scoreboard ??= await gameRepository.GetScoreboard(context.Game!, token);
        var lastModified = scoreboard.UpdateTimeUtc;
        eTag = GameETag(context.Game!.Id, lastModified);
        ContextHelper.SetCacheHeaders(Response, eTag, lastModified, true);

        var challenges = scoreboard.Challenges;
        if (context.Participation!.DivisionId is { } divId &&
            scoreboard.Divisions.TryGetValue(divId, out var division))
        {
            // filter out challenges is can be viewed by division permission
            challenges = FilterChallengesByPermission(scoreboard.Challenges, division);
        }

        if (context.Game!.Mode == GameMode.Speedrun)
        {
            // Filter only the participant challenge-card response. The normal scoreboard remains unchanged.
            var state = await speedrunService.GetState(id, token);
            if (state.CurrentRound is not { Status: SpeedrunRoundStatus.Running or SpeedrunRoundStatus.Overtime } round)
                challenges = [];
            else
            {
                challenges = challenges
                    .Where(pair => pair.Key == round.Category)
                    .ToDictionary(pair => pair.Key,
                        pair => round.Status == SpeedrunRoundStatus.Overtime
                            ? pair.Value.Where(challenge => challenge.SolvedCount == 0)
                            : pair.Value);
            }
        }

        var boardItem = scoreboard.Items.TryGetValue(context.Participation!.TeamId, out var item)
            ? item
            : new()
            {
                Avatar = context.Participation!.Team.AvatarUrl,
                Rank = 0,
                Name = context.Participation!.Team.Name,
                Id = context.Participation!.TeamId
            };

        return Ok(new GameDetailModel
        {
            ScoreboardItem = boardItem,
            TeamToken = context.Participation!.Token,
            Challenges = challenges,
            ChallengeCount = challenges.Count,
            WriteupRequired = context.Game!.WriteupRequired,
            WriteupDeadline = context.Game!.WriteupDeadline
        });
    }

    /// <summary>
    /// Get all game participations
    /// </summary>
    /// <remarks>
    /// Retrieves all participation information of the game; requires Admin permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game participation information</response>
    /// <response code="400">Invalid operation</response>
    /// <response code="404">Game not found</response>
    [RequireAdmin]
    [HttpGet("{id:int}/Participations")]
    [ProducesResponseType(typeof(ParticipationInfoModel[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Participations([FromRoute] int id, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)]));

        return Ok((await participationRepository.GetParticipations(game, token))
            .Select(ParticipationInfoModel.FromParticipation));
    }

    /// <summary>
    /// Downloads the scoreboard
    /// </summary>
    /// <remarks>
    /// Downloads the game scoreboard; requires Monitor permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="excelHelper"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully downloaded game scoreboard</response>
    /// <response code="400">Invalid operation</response>
    /// <response code="404">Game not found</response>
    [RequireMonitor]
    [HttpGet("{id:int}/ScoreboardSheet")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [SuppressMessage("ReSharper", "StringLiteralTypo")]
    public async Task<IActionResult> ScoreboardSheet([FromRoute] int id, [FromServices] ExcelHelper excelHelper,
        CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)]));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)]));

        try
        {
            var scoreboard = await gameRepository.GetScoreboardWithMembers(game, token);
            var stream = excelHelper.GetScoreboardExcel(scoreboard);
            stream.Seek(0, SeekOrigin.Begin);

            return File(stream,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"{game.Title}-Scoreboard-{DateTimeOffset.Now:yyyyMMdd-HH.mm.ssZ}.xlsx");
        }
        catch (Exception ex)
        {
            logger.SystemLog(StaticLocalizer[nameof(Resources.Program.Game_ScoreboardDownloadFailed)],
                TaskStatus.Failed, LogLevel.Error);
            logger.LogErrorMessage(ex, ex.Message);
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_ScoreboardDownloadFailed)]));
        }
    }

    /// <summary>
    /// Downloads all submissions
    /// </summary>
    /// <remarks>
    /// Downloads all submissions of the game; requires Monitor permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="excelHelper"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully downloaded all game submissions</response>
    /// <response code="400">Invalid operation</response>
    /// <response code="404">Game not found</response>
    [RequireMonitor]
    [HttpGet("{id:int}/SubmissionSheet")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [SuppressMessage("ReSharper", "StringLiteralTypo")]
    public async Task<IActionResult> SubmissionSheet([FromRoute] int id, [FromServices] ExcelHelper excelHelper,
        CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)]));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)]));

        var submissions = await submissionRepository.GetSubmissions(game, count: 0, token: token);

        var stream = excelHelper.GetSubmissionExcel(submissions);
        stream.Seek(0, SeekOrigin.Begin);

        return File(stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{game.Title}_Submissions_{DateTimeOffset.Now:yyyyMMddHHmmss}.xlsx");
    }

    /// <summary>
    /// Get challenge information
    /// </summary>
    /// <remarks>
    /// Retrieves challenge information; requires User permission and active team participation
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game challenge information</response>
    /// <response code="400">Invalid operation</response>
    /// <response code="404">Game not found</response>
    [RequireUser]
    [HttpGet("{id:int}/Challenges/{challengeId:int}")]
    [ProducesResponseType(typeof(ChallengeDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChallenge([FromRoute] int id, [FromRoute] int challengeId,
        CancellationToken token)
    {
        if (id <= 0 || challengeId <= 0)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var context = await GetContextInfo(id, token: token);

        if (context.Result is not null)
            return context.Result;

        if (!await speedrunService.CanAccessChallenge(context.Game!, challengeId, token))
            return NotFound(new RequestResponse("Challenge is not active in the current Speedrun round.",
                StatusCodes.Status404NotFound));

        var permission = await divisionRepository.GetPermission(context.Participation?.DivisionId, challengeId, token);

        if (!permission.HasFlag(GamePermission.ViewChallenge))
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var instance = await gameInstanceRepository.GetInstance(context.Participation!, challengeId, token);

        if (instance is null)
        {
            // Imported/cloned games may contain an enabled challenge whose lifecycle rows were never
            // reconciled. Repair only the missing database row, then let GetInstance perform the
            // normal first-load lifecycle (including dynamic flag allocation) exactly once.
            await challengeRepository.ReconcileInstances(id, [challengeId], token);
            instance = await gameInstanceRepository.GetInstance(context.Participation!, challengeId, token);
        }

        if (instance is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_ChallengeNotFound)],
                StatusCodes.Status404NotFound));

        var scoreboard = await gameRepository.GetScoreboard(context.Game!, token);
        var scoreboardChallenge =
            scoreboard.ChallengeMap.TryGetValue(challengeId, out var challenge) ? challenge : null;

        var attempts = await submissionRepository.CountSubmissions(context.Participation!.Id, challengeId, token);

        var detail = ChallengeDetailModel.FromInstance(instance, attempts, scoreboardChallenge);
        detail.Hints = await speedrunService.GetVisibleHints(context.Game!, instance.Challenge, token);
        return Ok(detail);
    }

    /// <summary>
    /// Submits a flag
    /// </summary>
    /// <remarks>
    /// Submits a flag; requires User permission and active team participation
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="model">Flag submission</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game challenge information</response>
    /// <response code="400">Invalid operation</response>
    /// <response code="404">Game not found</response>
    [RequireUser]
    [HttpPost("{id:int}/Challenges/{challengeId:int}")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Submit))]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Submit([FromRoute] int id, [FromRoute] int challengeId,
        [FromBody] FlagSubmitModel model, CancellationToken token) =>
        SubmitCore(id, challengeId, model, null, token);

    /// <summary>
    /// Submits a flag with a solver file
    /// </summary>
    [RequireUser]
    [HttpPost("{id:int}/Challenges/{challengeId:int}/WithSolver")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Submit))]
    [Consumes("multipart/form-data")]
    [RequestFormLimits(MultipartBodyLengthLimit = Limits.MaxSolverFileSize + 1024 * 1024)]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> SubmitWithSolver([FromRoute] int id, [FromRoute] int challengeId,
        [FromForm] FlagSubmitWithSolverModel model, CancellationToken token) =>
        SubmitCore(id, challengeId, model, model.SolverFile, token);

    private async Task<IActionResult> SubmitCore(int id, int challengeId, FlagSubmitModel model,
        IFormFile? solverFile, CancellationToken token)
    {
        var submitTime = DateTimeOffset.UtcNow;
        var answer = configService.DecryptApiData(model.Flag);
        if (string.IsNullOrWhiteSpace(answer))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Model_FlagRequired)]));

        if (answer.Length > Limits.MaxFlagLength)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Model_FlagTooLong)]));

        string? solverFileName = null;
        if (solverFile is not null)
        {
            solverFileName = string.Concat(Path.GetFileName(solverFile.FileName.Replace('\\', '/'))
                .Where(character => !char.IsControl(character))).Trim();
            if (solverFile.Length == 0)
                return BadRequest(new RequestResponse("File solver tidak boleh kosong."));
            if (solverFile.Length > Limits.MaxSolverFileSize)
                return BadRequest(new RequestResponse("Ukuran file solver maksimal 10 MB."));
            if (string.IsNullOrWhiteSpace(solverFileName) ||
                solverFileName.Length > Limits.MaxSolverFileNameLength)
            {
                return BadRequest(new RequestResponse(
                    $"Nama file solver wajib diisi dan maksimal {Limits.MaxSolverFileNameLength} karakter."));
            }
        }

        var context = await GetContextInfo(id, token: token);

        if (context.Result is not null)
            return context.Result;

        var aiUsageDisclosure = model.AiUsageDisclosure?.Trim();

        if (context.Game!.Mode == GameMode.Jeopardy && string.IsNullOrWhiteSpace(aiUsageDisclosure))
        {
            return BadRequest(new RequestResponse(AiUsageDisclosureValidator.RequiredMessage));
        }

        if (aiUsageDisclosure?.Length > Limits.MaxAiUsageDisclosureLength)
        {
            return BadRequest(new RequestResponse(
                $"Link AI atau pernyataan penggunaan AI tidak boleh melebihi {Limits.MaxAiUsageDisclosureLength} karakter."));
        }

        if (context.Game.Mode == GameMode.Jeopardy && !AiUsageDisclosureValidator.IsValid(aiUsageDisclosure))
            return BadRequest(new RequestResponse(AiUsageDisclosureValidator.InvalidFormatMessage));

        if (!await speedrunService.CanAccessChallenge(context.Game!, challengeId, token))
            return BadRequest(new RequestResponse("Challenge is not submittable in the current Speedrun round."));

        // Valid Speedrun submissions continue through the normal submission queue and VerifyAnswer pipeline.
        const int maxRetries = 3;
        for (var retry = 0; retry < maxRetries; retry++)
        {
            await using var transaction = await gameInstanceRepository.BeginTransactionAsync(token);

            var instance =
                await gameInstanceRepository.GetInstanceForSubmission(context.Participation!, challengeId, token);

            if (instance is null)
            {
                await challengeRepository.ReconcileInstances(id, [challengeId], token);
                instance = await gameInstanceRepository.GetInstanceForSubmission(
                    context.Participation!, challengeId, token);
            }

            if (instance is null)
                return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_ChallengeNotFound)],
                    StatusCodes.Status404NotFound));

            if (!instance.Challenge.IsEnabled)
                return BadRequest(new RequestResponse("Challenge is disabled and cannot receive submissions."));

            if (context.Game.Mode == GameMode.Jeopardy && instance.Challenge.RequireSolverUpload &&
                solverFile is null)
            {
                return BadRequest(new RequestResponse(
                    "Upload file solver sebelum mengirim flag untuk challenge ini."));
            }

            // Check if submission exceeds challenge deadline (only reject in non-practice mode)
            var hasExceededDeadline = instance.Challenge.DeadlineUtc is { } deadline && submitTime > deadline;
            if (hasExceededDeadline && !context.Game!.PracticeMode)
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Challenge_DeadlinePassed)]));

            var permission =
                await divisionRepository.GetPermission(context.Participation?.DivisionId, challengeId, token);

            if (!permission.HasFlag(GamePermission.ViewChallenge | GamePermission.SubmitFlags))
                return BadRequest(
                    new RequestResponse(localizer[nameof(Resources.Program.Challenge_SubmissionNoPermission)]));

            var currentAttempts =
                await submissionRepository.CountSubmissions(context.Participation!.Id, challengeId, token);

            if (instance.Challenge.SubmissionLimit > 0 && currentAttempts >= instance.Challenge.SubmissionLimit)
            {
                return BadRequest(
                    new RequestResponse(localizer[nameof(Resources.Program.Challenge_SubmissionLimitExceeded)]));
            }

            try
            {
                LocalFile? solverBlob = null;
                if (solverFile is not null)
                    solverBlob = await blobService.CreateOrUpdateBlob(solverFile, solverFileName, token);

                Submission submission = new()
                {
                    Game = context.Game!,
                    User = context.User!,
                    GameChallenge = instance.Challenge,
                    Team = context.Participation!.Team,
                    Participation = context.Participation!,
                    Status = AnswerResult.FlagSubmitted,
                    SubmitTimeUtc = submitTime,
                    Answer = answer,
                    AiUsageDisclosure = aiUsageDisclosure,
                    SolverFile = solverBlob,
                    SolverFileName = solverFileName
                };

                submission = await submissionRepository.AddSubmission(submission, token);
                await transaction.CommitAsync(token);

                await channelWriter.WriteAsync(submission, token);
                return Ok(submission.Id);
            }
            catch (DbUpdateConcurrencyException) when (retry < maxRetries - 1)
            {
                await transaction.RollbackAsync(token);
                await Task.Delay((retry + 1) * 100, token);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(token);
                logger.LogErrorMessage(ex, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new RequestResponse(localizer[nameof(Resources.Program.Error_InternalServerError)],
                        StatusCodes.Status500InternalServerError));
            }
        }

        return StatusCode(StatusCodes.Status409Conflict,
            new RequestResponse(localizer[nameof(Resources.Program.Error_InternalServerError)],
                StatusCodes.Status409Conflict));
    }

    /// <summary>
    /// Queries flag status
    /// </summary>
    /// <remarks>
    /// Queries flag status; requires User permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="submitId">Submission ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved submission status</response>
    /// <response code="404">Submission not found</response>
    [RequireUser]
    [HttpGet("{id:int}/Challenges/{challengeId:int}/Status/{submitId:int}")]
    [ProducesResponseType(typeof(AnswerResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Status([FromRoute] int id, [FromRoute] int challengeId, [FromRoute] int submitId,
        CancellationToken token)
    {
        var claimId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (claimId is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_SubmissionNotFound)],
                StatusCodes.Status404NotFound));

        var submission =
            await submissionRepository.GetSubmission(id, challengeId, Guid.Parse(claimId), submitId, token);

        if (submission is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Submission_NotFound)],
                StatusCodes.Status404NotFound));

        return Ok(submission.Status switch
        {
            AnswerResult.CheatDetected => AnswerResult.WrongAnswer,
            var x => x
        });
    }

    /// <summary>
    /// Get writeup information
    /// </summary>
    /// <remarks>
    /// Retrieves post-game writeup submission information; requires User permission
    /// </remarks>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully submitted writeup</response>
    /// <response code="400">Submission does not meet requirements</response>
    /// <response code="404">Game not found</response>
    [RequireUser]
    [HttpGet("{id:int}/Writeup")]
    [ProducesResponseType(typeof(BasicWriteupInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetWriteup([FromRoute] int id, CancellationToken token)
    {
        var context = await GetContextInfo(id, denyAfterEnded: false, token: token);

        if (context.Result is not null)
            return context.Result;

        return Ok(BasicWriteupInfoModel.FromParticipation(context.Participation!));
    }

    /// <summary>
    /// Submits a writeup
    /// </summary>
    /// <remarks>
    /// Submits a post-game writeup; requires User permission
    /// </remarks>
    /// <param name="id"></param>
    /// <param name="file">File</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully submitted writeup</response>
    /// <response code="400">Submission does not meet requirements</response>
    /// <response code="404">Game not found</response>
    [RequireUser]
    [HttpPost("{id:int}/Writeup")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitWriteup([FromRoute] int id, IFormFile file, CancellationToken token)
    {
        var current = DateTimeOffset.UtcNow;
        switch (file.Length)
        {
            case 0:
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeZero)]));
            case > 20 * 1024 * 1024:
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeTooLarge)]));
        }

        if (file.ContentType != "application/pdf" || Path.GetExtension(file.FileName) != ".pdf")
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_PdfOnly)]));

        var context = await GetContextInfo(id, denyAfterEnded: false, token: token);

        if (context.Result is not null)
            return context.Result;

        var game = context.Game!;

        if (!game.WriteupRequired)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_WriteupNotNeeded)]));

        var part = context.Participation!;
        var team = part.Team;

        if (current > game.WriteupDeadline)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_DeadlineExpired)]));

        var wp = context.Participation!.Writeup;

        if (wp is not null)
            await blobService.DeleteBlob(wp, token);

        part.Writeup = await blobService.CreateOrUpdateBlob(file,
            $"Writeup-{game.Id}-{team.Id}-{current:yyyyMMdd-HH.mm.ss}Z.pdf", token);

        await participationRepository.SaveAsync(token);

        logger.Log(StaticLocalizer[nameof(Resources.Program.Game_WriteupSubmitted), team.Name, game.Title],
            context.User!,
            TaskStatus.Success);

        return Ok();
    }

    /// <summary>
    /// Creates a container
    /// </summary>
    /// <remarks>
    /// Creates a container; requires User permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game challenge information</response>
    /// <response code="404">Challenge not found</response>
    /// <response code="400">Container creation not allowed for challenge</response>
    [RequireUser]
    [HttpPost("{id:int}/Container/{challengeId:int}")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Container))]
    [ProducesResponseType(typeof(ContainerInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateContainer([FromRoute] int id, [FromRoute] int challengeId,
        CancellationToken token)
    {
        var context = await GetContextInfo(id, token: token);

        if (context.Result is not null)
            return context.Result;

        if (!await speedrunService.CanAccessChallenge(context.Game!, challengeId, token))
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var permission = await divisionRepository.GetPermission(context.Participation?.DivisionId, challengeId, token);

        if (!permission.HasFlag(GamePermission.ViewChallenge))
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var instance = await gameInstanceRepository.GetInstance(context.Participation!, challengeId, token);
        if (instance is null)
        {
            await challengeRepository.ReconcileInstances(id, [challengeId], token);
            instance = await gameInstanceRepository.GetInstance(context.Participation!, challengeId, token);
        }

        if (instance is null || !instance.Challenge.IsEnabled)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        if (!instance.Challenge.Type.IsContainer())
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerCreationNotAllowed)]));

        if (instance.IsContainerOperationTooFrequent)
            return RequestResponse.Result(localizer[nameof(Resources.Program.Game_OperationTooFrequent)],
                StatusCodes.Status429TooManyRequests);

        if (instance.Container is not null)
        {
            if (instance.Container.Status == ContainerStatus.Running)
                return BadRequest(
                    new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerAlreadyCreated)]));

            await containerRepository.DestroyContainer(instance.Container, token);
        }

        return await gameInstanceRepository.CreateContainer(instance, context.Participation!.Team, context.User!,
                context.Game!, token) switch
        {
            null or (TaskStatus.Failed, null) => BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerCreationFailed)])),
            (TaskStatus.Denied, null) => BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerNumberLimitExceeded),
                    context.Game!.ContainerCountLimit])),
            (TaskStatus.Success, var x) => Ok(ContainerInfoModel.FromContainer(x!)),
            _ => throw new UnreachableException()
        };
    }

    /// <summary>
    /// Extends container lifetime
    /// </summary>
    /// <remarks>
    /// Extends container lifetime; requires User permission and can only be extended two hours within ten minutes before expiration
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game challenge container information</response>
    /// <response code="404">Challenge not found</response>
    /// <response code="400">Container not created or cannot be extended</response>
    [RequireUser]
    [HttpPost("{id:int}/Container/{challengeId:int}/Extend")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Container))]
    [ProducesResponseType(typeof(ContainerInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExtendContainerLifetime([FromRoute] int id, [FromRoute] int challengeId,
        CancellationToken token)
    {
        var context = await GetContextInfo(id, token: token);

        if (context.Result is not null)
            return context.Result;

        if (!await speedrunService.CanAccessChallenge(context.Game!, challengeId, token))
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var instance = await gameInstanceRepository.GetInstance(context.Participation!, challengeId, token);

        if (instance is null || !instance.Challenge.IsEnabled)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        if (!instance.Challenge.Type.IsContainer())
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerCreationNotAllowed)]));

        if (instance.Container is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerNotCreated)]));

        if (instance.Container.ExpectStopAt - DateTimeOffset.UtcNow >
            TimeSpan.FromMinutes(containerPolicy.Value.RenewalWindow))
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerExtensionNotAvailable)]));

        await containerRepository.ExtendLifetime(instance.Container,
            TimeSpan.FromMinutes(containerPolicy.Value.ExtensionDuration), token);

        return Ok(ContainerInfoModel.FromContainer(instance.Container));
    }

    /// <summary>
    /// Deletes a container
    /// </summary>
    /// <remarks>
    /// Deletes a container; requires User permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted container</response>
    /// <response code="404">Challenge not found</response>
    /// <response code="400">Container creation not allowed for challenge</response>
    [RequireUser]
    [HttpDelete("{id:int}/Container/{challengeId:int}")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Container))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> DeleteContainer([FromRoute] int id, [FromRoute] int challengeId,
        CancellationToken token)
    {
        var context = await GetContextInfo(id, token: token);

        if (context.Result is not null)
            return context.Result;

        if (!await speedrunService.CanAccessChallenge(context.Game!, challengeId, token))
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var instance = await gameInstanceRepository.GetInstance(context.Participation!, challengeId, token);

        if (instance is null || !instance.Challenge.IsEnabled)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        if (!instance.Challenge.Type.IsContainer())
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerCreationNotAllowed)]));

        if (instance.Container is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerNotCreated)]));

        if (instance.IsContainerOperationTooFrequent)
            return RequestResponse.Result(localizer[nameof(Resources.Program.Game_OperationTooFrequent)],
                StatusCodes.Status429TooManyRequests);

        var destroyId = instance.Container.LogId;

        if (!await containerRepository.DestroyContainer(instance.Container, token))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerDeletionFailed)]));

        instance.LastContainerOperation = DateTimeOffset.UtcNow;

        await gameEventRepository.AddEvent(
            new()
            {
                Type = EventType.ContainerDestroy,
                GameId = context.Game!.Id,
                TeamId = context.Participation!.TeamId,
                UserId = context.User!.Id,
                Values = [instance.Challenge.Id.ToString(), instance.Challenge.Title]
            }, token);

        logger.Log(
            StaticLocalizer[nameof(Resources.Program.Game_ContainerDeleted), context.Participation!.Team.Name,
                instance.Challenge.Title,
                destroyId],
            context.User, TaskStatus.Success);

        return Ok();
    }

    private async Task<ContextInfo> GetContextInfo(int id, bool denyAfterEnded = true,
        CancellationToken token = default)
    {
        ContextInfo res = new()
        {
            User = await userManager.GetUserAsync(User),
            Game = await gameRepository.GetGameById(id, token)
        };

        if (res.Game is null)
            return res.WithResult(NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound)));

        var part = await participationRepository.GetParticipation(res.User!.Id, res.Game.Id, token);

        if (part is null)
            return res.WithResult(
                BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotParticipated)])));

        res.Participation = part;

        if (part.Status != ParticipationStatus.Accepted)
            return res.WithResult(
                BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_ParticipationNotAccepted)])));

        if (DateTimeOffset.UtcNow < res.Game.StartTimeUtc)
            return res.WithResult(
                BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted),
                    ErrorCodes.GameNotStarted])));

        if (denyAfterEnded && !res.Game.PracticeMode && res.Game.EndTimeUtc < DateTimeOffset.UtcNow)
            return res.WithResult(
                BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_Ended)], ErrorCodes.GameEnded)));

        return res;
    }

    private static string GameETag(int gameId, DateTimeOffset lastModified) =>
        $"\"{gameId}-{lastModified.ToUnixTimeSeconds():X}\"";

    private static bool ShouldMaskBloodNotice(Game game, GameNotice notice) =>
        game.ScoreboardFrozen &&
        notice.Type is NoticeType.FirstBlood or NoticeType.SecondBlood or NoticeType.ThirdBlood &&
        (game.ScoreboardFreezeTimeUtc is null || notice.PublishTimeUtc >= game.ScoreboardFreezeTimeUtc);

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

    private static Dictionary<ChallengeCategory, IEnumerable<ChallengeInfo>> FilterChallengesByPermission(
        Dictionary<ChallengeCategory, IEnumerable<ChallengeInfo>> challenges,
        DivisionItem division)
    {
        var res = new Dictionary<ChallengeCategory, IEnumerable<ChallengeInfo>>();

        foreach ((var cat, var chs) in challenges)
        {
            var infos = chs.Where(chal =>
                division.ChallengeConfigs.TryGetValue(chal.Id, out var config)
                    ? config.Permissions.HasFlag(GamePermission.ViewChallenge)
                    : division.DefaultPermissions.HasFlag(GamePermission.ViewChallenge)
            ).ToArray();

            if (infos.Length > 0)
                res[cat] = infos;
        }

        return res;
    }

    private class ContextInfo
    {
        public Game? Game;
        public Participation? Participation;
        public UserInfo? User;

        /// <summary>
        /// The result to be returned.
        /// If this is not null, the action should return this result directly.
        /// </summary>
        public IActionResult? Result;

        public ContextInfo WithResult(IActionResult res)
        {
            Result = res;
            return this;
        }
    }
}
