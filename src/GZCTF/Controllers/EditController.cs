using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using GZCTF.Extensions;
using GZCTF.Middlewares;
using GZCTF.Models.Request.Edit;
using GZCTF.Models.Request.Game;
using GZCTF.Models.Request.Info;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Cache;
using GZCTF.Services.Container.Manager;
using GZCTF.Services.Integrations;
using GZCTF.Services.Transfer;
using GZCTF.Storage.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NSwag.Annotations;

namespace GZCTF.Controllers;

/// <summary>
/// Data Modification APIs
/// </summary>
[RequireAdmin]
[ApiController]
[Route("api/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class EditController(
    CacheHelper cacheHelper,
    UserManager<UserInfo> userManager,
    ILogger<EditController> logger,
    IPostRepository postRepository,
    IContainerRepository containerRepository,
    IGameChallengeRepository challengeRepository,
    IGameInstanceRepository instanceRepository,
    IGameNoticeRepository gameNoticeRepository,
    IGameRepository gameRepository,
    IContainerManager containerService,
    IBlobRepository blobService,
    IBlobStorage blobStorage,
    GameExportService exportService,
    GameImportService importService,
    DiscordWebhookService discordWebhookService,
    SpeedrunService speedrunService,
    AppDbContext dbContext,
    IDivisionRepository divisionRepository,
    IStringLocalizer<Program> localizer) : Controller
{
    /// <summary>
    /// Get AI usage disclosures submitted for a game
    /// </summary>
    /// <remarks>
    /// Retrieving AI usage disclosures requires administrator privileges
    /// </remarks>
    [HttpGet("Games/{id:int}/AiDisclosures")]
    [ProducesResponseType(typeof(AiUsageDisclosureModel[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGameAiDisclosures([FromRoute] int id,
        [FromQuery][Range(1, 500)] int count = 200, [FromQuery][Range(0, int.MaxValue)] int skip = 0,
        CancellationToken token = default)
    {
        if (!await dbContext.Games.AnyAsync(game => game.Id == id, token))
            return NotFound(new RequestResponse("Game not found.", StatusCodes.Status404NotFound));

        var disclosures = await dbContext.Submissions.AsNoTracking()
            .Where(submission => submission.GameId == id
                                 && submission.AiUsageDisclosure != null
                                 && submission.AiUsageDisclosure != string.Empty)
            .OrderByDescending(submission => submission.SubmitTimeUtc)
            .Skip(skip)
            .Take(count)
            .Select(submission => new AiUsageDisclosureModel
            {
                SubmissionId = submission.Id,
                SubmitTimeUtc = submission.SubmitTimeUtc,
                Team = submission.Team != null ? submission.Team.Name : string.Empty,
                User = submission.User != null ? submission.User.UserName ?? string.Empty : string.Empty,
                Challenge = submission.GameChallenge != null ? submission.GameChallenge.Title : string.Empty,
                Answer = submission.Answer,
                Status = submission.Status,
                AiUsageDisclosure = submission.AiUsageDisclosure!,
                SolverFileName = submission.SolverFileName,
                SolverFileSize = submission.SolverFile != null ? submission.SolverFile.FileSize : null,
                HasSolverFile = submission.SolverFileId != null
            })
            .ToArrayAsync(token);

        return Ok(disclosures);
    }

    /// <summary>
    /// Download the solver file attached to a submission
    /// </summary>
    [HttpGet("Games/{id:int}/Submissions/{submissionId:int}/Solver")]
    [Produces(MediaTypeNames.Application.Octet)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadSubmissionSolver([FromRoute] int id, [FromRoute] int submissionId,
        CancellationToken token)
    {
        var submission = await dbContext.Submissions.AsNoTracking()
            .Include(item => item.SolverFile)
            .SingleOrDefaultAsync(item => item.GameId == id && item.Id == submissionId, token);

        if (submission?.SolverFile is null)
            return NotFound(new RequestResponse("Solver file not found.", StatusCodes.Status404NotFound));

        var path = StoragePath.Combine(PathHelper.Uploads, submission.SolverFile.Location,
            submission.SolverFile.Hash);
        if (!await blobStorage.ExistsAsync(path, token))
            return NotFound(new RequestResponse("Solver file not found.", StatusCodes.Status404NotFound));

        var stream = await blobStorage.OpenReadAsync(path, token);
        var downloadName = string.Concat(
            Path.GetFileName(submission.SolverFileName ?? submission.SolverFile.Name)
                .Where(character => !char.IsControl(character))).Trim();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stream, MediaTypeNames.Application.Octet,
            string.IsNullOrWhiteSpace(downloadName) ? "solver.bin" : downloadName);
    }

    [HttpGet("Games/{id:int}/LiveScoreboard")]
    public async Task<IActionResult> GetLiveScoreboardConfig(int id, CancellationToken token)
    {
        if (!await dbContext.Games.AnyAsync(game => game.Id == id, token))
            return NotFound(new RequestResponse("Game not found."));
        var config = await dbContext.GameLiveScoreboardConfigs.AsNoTracking()
            .SingleOrDefaultAsync(item => item.GameId == id, token);
        return Ok(LiveScoreboardConfigModel.FromConfig(config));
    }

    [HttpPut("Games/{id:int}/LiveScoreboard")]
    public async Task<IActionResult> UpdateLiveScoreboardConfig(int id, [FromBody] LiveScoreboardConfigModel model,
        CancellationToken token)
    {
        if (!await dbContext.Games.AnyAsync(game => game.Id == id, token))
            return NotFound(new RequestResponse("Game not found."));
        var config = await dbContext.GameLiveScoreboardConfigs.SingleOrDefaultAsync(item => item.GameId == id, token);
        if (config is null)
        {
            config = new() { GameId = id };
            dbContext.GameLiveScoreboardConfigs.Add(config);
        }
        model.Apply(config);
        await dbContext.SaveChangesAsync(token);
        return Ok(LiveScoreboardConfigModel.FromConfig(config));
    }

    [HttpPost("Games/{id:int}/LiveScoreboard/ResetSounds")]
    public async Task<IActionResult> ResetLiveScoreboardSounds(int id, CancellationToken token)
    {
        var config = await dbContext.GameLiveScoreboardConfigs.SingleOrDefaultAsync(item => item.GameId == id, token);
        if (config is null)
            return NotFound(new RequestResponse("Live Scoreboard configuration not found."));
        new LiveScoreboardSoundModel().Apply(config);
        await dbContext.SaveChangesAsync(token);
        return Ok(LiveScoreboardConfigModel.FromConfig(config));
    }
    /// <summary>
    /// Add Post
    /// </summary>
    /// <remarks>
    /// Adding a post requires administrator privileges
    /// </remarks>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully added post</response>
    [HttpPost("Posts")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddPost([FromBody] PostEditModel model, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        var res = await postRepository.CreatePost(new Post().Update(model, user!), token);
        return Ok(res.Id);
    }

    /// <summary>
    /// Update Post
    /// </summary>
    /// <remarks>
    /// Updating a post requires administrator privileges
    /// </remarks>
    /// <param name="id">Post ID</param>
    /// <param name="token"></param>
    /// <param name="model"></param>
    /// <response code="200">Successfully updated post</response>
    /// <response code="404">Post not found</response>
    [HttpPut("Posts/{id}")]
    [ProducesResponseType(typeof(PostDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePost(string id, [FromBody] PostEditModel model, CancellationToken token)
    {
        var post = await postRepository.GetPostById(id, token);

        if (post is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Post_NotFound)],
                StatusCodes.Status404NotFound));

        var user = await userManager.GetUserAsync(User);

        await postRepository.UpdatePost(post.Update(model, user!), token);

        return Ok(PostDetailModel.FromPost(post));
    }

    /// <summary>
    /// Delete Post
    /// </summary>
    /// <remarks>
    /// Deleting a post requires administrator privileges
    /// </remarks>
    /// <param name="id">Post ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted post</response>
    /// <response code="404">Post not found</response>
    [HttpDelete("Posts/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePost(string id, CancellationToken token)
    {
        var post = await postRepository.GetPostById(id, token);

        if (post is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Post_NotFound)],
                StatusCodes.Status404NotFound));

        await postRepository.RemovePost(post, token);

        return Ok();
    }

    /// <summary>
    /// Add Game
    /// </summary>
    /// <remarks>
    /// Adding a game requires administrator privileges
    /// </remarks>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully added game</response>
    [HttpPost("Games")]
    [ProducesResponseType(typeof(GameInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddGame([FromBody] GameInfoModel model, CancellationToken token)
    {
        var game = await gameRepository.CreateGame(new Game().Update(model), token);

        if (game is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_CreationFailed)]));

        await cacheHelper.FlushRecentGamesCache(token);

        return Ok(GameInfoModel.FromGame(game));
    }

    /// <summary>
    /// Get Game List
    /// </summary>
    /// <remarks>
    /// Retrieving the game list requires administrator privileges
    /// </remarks>
    /// <param name="count"></param>
    /// <param name="skip"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game list</response>
    [HttpGet("Games")]
    [ProducesResponseType(typeof(ArrayResponse<GameInfoModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGames([FromQuery][Range(0, 100)] int count, [FromQuery] int skip,
        CancellationToken token) =>
        Ok((await gameRepository.GetGames(count, skip, token))
            .Select(GameInfoModel.FromGame)
            .ToResponse(await gameRepository.CountAsync(token)));

    /// <summary>
    /// Get Game
    /// </summary>
    /// <remarks>
    /// Retrieving a game requires administrator privileges
    /// </remarks>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game</response>
    [HttpGet("Games/{id:int}")]
    [ProducesResponseType(typeof(GameInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGame([FromRoute] int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        return Ok(GameInfoModel.FromGame(game));
    }

    /// <summary>
    /// Get Game Hash Salt
    /// </summary>
    /// <remarks>
    /// Retrieving the game hash salt requires administrator privileges
    /// </remarks>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game hash salt</response>
    [OpenApiIgnore]
    [HttpGet("Games/{id:int}/HashSalt")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHashSalt([FromRoute] int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        return Ok(game.TeamHashSalt);
    }

    /// <summary>
    /// Update Game
    /// </summary>
    /// <remarks>
    /// Updating a game requires administrator privileges
    /// </remarks>
    /// <param name="id"></param>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully updated game</response>
    [HttpPut("Games/{id:int}")]
    [ProducesResponseType(typeof(GameInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateGame([FromRoute] int id, [FromBody] GameInfoModel model,
        CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        if (model.ScoreboardFrozen && !game.ScoreboardFrozen)
            game.ScoreboardFreezeTimeUtc = DateTimeOffset.UtcNow;
        else if (!model.ScoreboardFrozen)
            game.ScoreboardFreezeTimeUtc = null;

        game.ScoreboardFrozen = model.ScoreboardFrozen;
        game.Update(model);
        await gameRepository.UpdateGame(game, token);

        return Ok(GameInfoModel.FromGame(game));
    }

    /// <summary>
    /// Get a game's Discord blood notification settings
    /// </summary>
    [HttpGet("Games/{id:int}/BloodNotification")]
    [ProducesResponseType(typeof(BloodNotificationModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGameBloodNotification([FromRoute] int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        return Ok(BloodNotificationModel.FromGame(game));
    }

    /// <summary>
    /// Update a game's Discord blood notification settings
    /// </summary>
    [HttpPut("Games/{id:int}/BloodNotification")]
    [ProducesResponseType(typeof(BloodNotificationModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateGameBloodNotification([FromRoute] int id,
        [FromBody] BloodNotificationModel model, CancellationToken token)
    {
        if (model.Validate() is { } error)
            return BadRequest(new RequestResponse(error, StatusCodes.Status400BadRequest));

        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        game.Update(model);
        await gameRepository.UpdateGame(game, token);

        return Ok(BloodNotificationModel.FromGame(game));
    }

    /// <summary>
    /// Test a game's Discord blood notification webhook
    /// </summary>
    [HttpPost("Games/{id:int}/BloodNotification/Test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TestGameBloodNotification([FromRoute] int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BloodNotificationModel? model, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        model ??= BloodNotificationModel.FromGame(game);
        if (string.IsNullOrWhiteSpace(model.DiscordWebhookUrl))
            model.DiscordWebhookUrl = game.BloodDiscordWebhookUrl;

        if (model.Validate(requireWebhook: true) is { } error)
            return BadRequest(new RequestResponse(error, StatusCodes.Status400BadRequest));

        return await discordWebhookService.SendTestWebhook(model.DiscordWebhookUrl!, model, game.Title, token)
            ? Ok()
            : BadRequest(new RequestResponse("Discord webhook test failed.", StatusCodes.Status400BadRequest));
    }

    [HttpGet("Games/{id:int}/Speedrun")]
    public async Task<IActionResult> GetSpeedrunSettings(int id, CancellationToken token) =>
        await speedrunService.GetSettings(id, token) is { } settings
            ? Ok(settings)
            : NotFound(new RequestResponse("Game not found.", StatusCodes.Status404NotFound));

    [HttpPut("Games/{id:int}/Speedrun")]
    public async Task<IActionResult> UpdateSpeedrunSettings(int id, [FromBody] SpeedrunSettingsModel model,
        CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);
        if (game is null)
            return NotFound(new RequestResponse("Game not found.", StatusCodes.Status404NotFound));
        game.SpeedrunDefaultRoundDurationSeconds =
            model.DefaultRoundDurationSeconds ?? model.DefaultRoundDurationMinutes * 60;
        game.SpeedrunOvertimeSeconds = model.OvertimeSeconds ?? model.OvertimeMinutes * 60;
        game.SpeedrunDefaultRoundDurationMinutes = game.SpeedrunDefaultRoundDurationSeconds / 60;
        game.SpeedrunOvertimeMinutes = game.SpeedrunOvertimeSeconds / 60;
        game.SpeedrunAllowManualExtend = model.AllowManualExtend;
        game.SpeedrunHideInactiveChallenges = model.HideInactiveChallenges;
        game.SpeedrunEmergencyHintEnabled = model.EmergencyHintEnabled;
        game.SpeedrunEmergencyHintText = model.EmergencyHintText;
        await gameRepository.UpdateGame(game, token);
        return Ok(await speedrunService.GetSettings(id, token));
    }

    [HttpPost("Games/{id:int}/Speedrun/RefreshCategories")]
    public async Task<IActionResult> RefreshSpeedrunCategories(int id, CancellationToken token)
    {
        await speedrunService.RefreshCategories(id, token);
        return Ok(await speedrunService.GetSettings(id, token));
    }

    [HttpPost("Games/{id:int}/Speedrun/Spin")]
    public async Task<IActionResult> SpinSpeedrun(int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);
        if (game is null || game.Mode != GameMode.Speedrun)
            return BadRequest(new RequestResponse("Speedrun mode is not enabled."));
        var user = await userManager.GetUserAsync(User);
        return await speedrunService.Spin(game, user?.Id, token) is { } round
            ? Ok(round)
            : BadRequest(new RequestResponse("No unused category is available or another round is active."));
    }

    [HttpPost("Games/{id:int}/Speedrun/Rounds/{roundId:int}/Start")]
    public async Task<IActionResult> StartSpeedrunRound(int id, int roundId, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);
        return game is not null && game.Mode == GameMode.Speedrun && await speedrunService.Start(game, roundId, token)
            ? Ok(await speedrunService.GetSettings(id, token))
            : BadRequest(new RequestResponse("Speedrun round cannot be started."));
    }

    [HttpPost("Games/{id:int}/Speedrun/Rounds/{roundId:int}/End")]
    public async Task<IActionResult> EndSpeedrunRound(int id, int roundId, CancellationToken token) =>
        await speedrunService.End(id, roundId, token)
            ? Ok(await speedrunService.GetSettings(id, token))
            : BadRequest(new RequestResponse("Speedrun round cannot be ended."));

    [HttpPost("Games/{id:int}/Speedrun/Rounds/{roundId:int}/Extend")]
    public async Task<IActionResult> ExtendSpeedrunRound(int id, int roundId, [FromBody] SpeedrunExtendModel model,
        CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);
        return game is not null && game.Mode == GameMode.Speedrun &&
               await speedrunService.Extend(game, roundId, model.GetSeconds(), token)
            ? Ok(await speedrunService.GetSettings(id, token))
            : BadRequest(new RequestResponse("Speedrun round cannot be extended."));
    }

    [HttpPost("Games/{id:int}/Speedrun/Rounds/{roundId:int}/SetTimer")]
    public async Task<IActionResult> SetSpeedrunRoundTimer(int id, int roundId,
        [FromBody] SpeedrunSetTimerModel model, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);
        return game is not null && game.Mode == GameMode.Speedrun &&
               await speedrunService.SetRemainingTime(game, roundId, model.GetSeconds(), token)
            ? Ok(await speedrunService.GetSettings(id, token))
            : BadRequest(new RequestResponse("Speedrun round timer cannot be updated."));
    }

    [HttpPost("Games/{id:int}/Speedrun/Categories/{categoryId:int}/Available")]
    public async Task<IActionResult> MakeSpeedrunCategoryAvailable(int id, int categoryId, CancellationToken token) =>
        await UpdateSpeedrunCategory(id, categoryId, used: false, included: null, token);

    [HttpPost("Games/{id:int}/Speedrun/Categories/{categoryId:int}/Used")]
    public async Task<IActionResult> MarkSpeedrunCategoryUsed(int id, int categoryId, CancellationToken token) =>
        await UpdateSpeedrunCategory(id, categoryId, used: true, included: null, token);

    [HttpPost("Games/{id:int}/Speedrun/Categories/{categoryId:int}/Enable")]
    public async Task<IActionResult> EnableSpeedrunCategory(int id, int categoryId, CancellationToken token) =>
        await UpdateSpeedrunCategory(id, categoryId, used: null, included: true, token);

    [HttpPost("Games/{id:int}/Speedrun/Categories/{categoryId:int}/Disable")]
    public async Task<IActionResult> DisableSpeedrunCategory(int id, int categoryId, CancellationToken token) =>
        await UpdateSpeedrunCategory(id, categoryId, used: null, included: false, token);

    private async Task<IActionResult> UpdateSpeedrunCategory(int id, int categoryId, bool? used, bool? included,
        CancellationToken token) =>
        await speedrunService.UpdateCategory(id, categoryId, used, included, token)
            ? Ok(await speedrunService.GetSettings(id, token))
            : BadRequest(new RequestResponse("Speedrun category cannot be updated while it is selected."));

    [HttpPost("Games/{id:int}/Speedrun/ResetCategories")]
    public async Task<IActionResult> ResetSpeedrunCategories(int id, CancellationToken token) =>
        await speedrunService.ResetCategories(id, token)
            ? Ok(await speedrunService.GetSettings(id, token))
            : BadRequest(new RequestResponse("Categories cannot be reset while a round is active."));

    /// <summary>
    /// Delete Game
    /// </summary>
    /// <remarks>
    /// Deleting a game requires administrator privileges
    /// </remarks>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted game</response>
    [HttpDelete("Games/{id:int}")]
    [ProducesResponseType(typeof(GameInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteGame([FromRoute] int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        return await gameRepository.DeleteGame(game, token) switch
        {
            TaskStatus.Success => Ok(),
            _ => BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_DeletionFailed)]))
        };
    }

    /// <summary>
    /// Delete All WriteUps
    /// </summary>
    /// <remarks>
    /// Deleting all WriteUps for a game requires administrator privileges
    /// </remarks>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted game WriteUps</response>
    [HttpDelete("Games/{id:int}/WriteUps")]
    [ProducesResponseType(typeof(GameInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteGameWriteUps([FromRoute] int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        await gameRepository.DeleteAllWriteUps(game, token);

        return Ok();
    }

    /// <summary>
    /// Update Game Poster
    /// </summary>
    /// <remarks>
    /// Use this endpoint to update the game poster; administrator privileges required
    /// </remarks>
    /// <response code="200">Game poster URL</response>
    /// <response code="400">Invalid request</response>
    /// <response code="401">Unauthorized user</response>
    [HttpPut("Games/{id:int}/Poster")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateGamePoster([FromRoute] int id, IFormFile file, CancellationToken token)
    {
        switch (file.Length)
        {
            case 0:
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeZero)]));
            case > 3 * 1024 * 1024:
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeTooLarge)]));
        }

        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var poster = await blobService.CreateOrUpdateImage(file, "poster", 0, token);

        if (poster is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_CreationFailed)]));

        game.PosterHash = poster.Hash;
        await gameRepository.UpdateGame(game, token);

        return Ok(poster.Url());
    }

    /// <summary>
    /// Add Game Notice
    /// </summary>
    /// <remarks>
    /// Adding a game notice requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="model">Notice content</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully added game notice</response>
    [HttpPost("Games/{id:int}/Notices")]
    [ProducesResponseType(typeof(GameNotice), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddGameNotice([FromRoute] int id, [FromBody] GameNoticeModel model,
        CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var res = await gameNoticeRepository.AddNotice(
            new()
            {
                Values = [model.Content],
                GameId = game.Id,
                Type = NoticeType.Normal,
                PublishTimeUtc = DateTimeOffset.UtcNow
            }, token);

        return Ok(res);
    }

    /// <summary>
    /// Get Game Notices
    /// </summary>
    /// <remarks>
    /// Retrieving game notices requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game notices</response>
    [HttpGet("Games/{id:int}/Notices")]
    [ProducesResponseType(typeof(GameNotice[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGameNotices([FromRoute] int id, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        return Ok(await gameNoticeRepository.GetNormalNotices(id, token));
    }

    /// <summary>
    /// Update Game Notice
    /// </summary>
    /// <remarks>
    /// Updating a game notice requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="noticeId">Notice ID</param>
    /// <param name="model">Notice content</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully updated notice</response>
    [HttpPut("Games/{id:int}/Notices/{noticeId:int}")]
    [ProducesResponseType(typeof(GameNotice), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateGameNotice([FromRoute] int id, [FromRoute] int noticeId,
        [FromBody] GameNoticeModel model, CancellationToken token = default)
    {
        var notice = await gameNoticeRepository.GetNoticeById(id, noticeId, token);

        if (notice is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Notification_NotFound)],
                StatusCodes.Status404NotFound));

        if (notice.Type != NoticeType.Normal)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Notification_SystemNotEditable)]));

        notice.Values = [model.Content];
        return Ok(await gameNoticeRepository.UpdateNotice(notice, token));
    }

    /// <summary>
    /// Delete Game Notice
    /// </summary>
    /// <remarks>
    /// Deleting a game notice requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="noticeId">Post ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted post</response>
    /// <response code="404">Post not found</response>
    [HttpDelete("Games/{id:int}/Notices/{noticeId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteGameNotice([FromRoute] int id, [FromRoute] int noticeId,
        CancellationToken token)
    {
        var notice = await gameNoticeRepository.GetNoticeById(id, noticeId, token);

        if (notice is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Notification_SystemNotEditable)],
                StatusCodes.Status404NotFound));

        if (notice.Type != NoticeType.Normal)
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Notification_SystemNotDeletable)]));

        await gameNoticeRepository.RemoveNotice(notice, token);

        return Ok();
    }


    /// <summary>
    /// Create Division
    /// </summary>
    /// <remarks>
    /// Add a new division for a game; requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="model">Division information</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully created division</response>
    [HttpPost("Games/{id:int}/Divisions")]
    [ProducesResponseType(typeof(Division), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateDivision([FromRoute] int id, [FromBody] DivisionCreateModel model,
        CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);
        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var division = await divisionRepository.CreateDivision(game, model, token);

        return Ok(division);
    }

    /// <summary>
    /// Get Divisions
    /// </summary>
    /// <remarks>
    /// Retrieve all divisions for a game; requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved divisions</response>
    [HttpGet("Games/{id:int}/Divisions")]
    [ProducesResponseType(typeof(Division[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDivisions([FromRoute] int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);
        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var divisions = await divisionRepository.GetDivisions(id, token);
        return Ok(divisions);
    }

    /// <summary>
    /// Update Division
    /// </summary>
    /// <remarks>
    /// Update a division for a game; requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="divisionId">Division ID</param>
    /// <param name="model">Division information</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully updated division</response>
    [HttpPut("Games/{id:int}/Divisions/{divisionId:int}")]
    [ProducesResponseType(typeof(Division), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateDivision([FromRoute] int id, [FromRoute] int divisionId,
        [FromBody] DivisionEditModel model, CancellationToken token)
    {
        var division = await divisionRepository.GetDivision(id, divisionId, token);
        if (division is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Division_NotFound)],
                StatusCodes.Status404NotFound));

        await divisionRepository.UpdateDivision(division, model, token);

        return Ok(division);
    }

    /// <summary>
    /// Delete Division
    /// </summary>
    /// <remarks>
    /// Delete a division for a game; requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="divisionId">Division ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted division</response>
    [HttpDelete("Games/{id:int}/Divisions/{divisionId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDivision([FromRoute] int id, [FromRoute] int divisionId,
        CancellationToken token)
    {
        var division = await divisionRepository.GetDivision(id, divisionId, token);
        if (division is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Division_NotFound)],
                StatusCodes.Status404NotFound));

        await divisionRepository.RemoveDivision(division, token);

        return Ok();
    }

    /// <summary>
    /// Add Game Challenge
    /// </summary>
    /// <remarks>
    /// Adding a game challenge requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully added game challenge</response>
    [HttpPost("Games/{id:int}/Challenges")]
    [ProducesResponseType(typeof(ChallengeEditDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddGameChallenge([FromRoute] int id, [FromBody] ChallengeInfoModel model,
        CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var res = await challengeRepository.CreateChallenge(game,
            new GameChallenge { Title = model.Title, Type = model.Type, Category = model.Category }, token);

        return Ok(ChallengeEditDetailModel.FromChallenge(res));
    }

    /// <summary>
    /// Get All Game Challenges
    /// </summary>
    /// <remarks>
    /// Retrieving all game challenges requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game challenges</response>
    [HttpGet("Games/{id:int}/Challenges")]
    [ProducesResponseType(typeof(ChallengeInfoModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGameChallenges([FromRoute] int id, CancellationToken token)
    {
        var challenges = await challengeRepository.GetChallenges(id, token);

        var scoreboard = await gameRepository.TryGetScoreboard(id, token);

        var result = challenges.Select(c =>
        {
            var model = ChallengeInfoModel.FromChallenge(c);
            if (scoreboard is not null && scoreboard.ChallengeMap.TryGetValue(c.Id, out var challengeInfo))
                model.Score = challengeInfo.Score;
            return model;
        });

        return Ok(result);
    }

    /// <summary>
    /// Flush Scoreboard Cache
    /// </summary>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200"></response>
    [HttpPost("Games/{id:int}/Scoreboard/Flush")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> FlushScoreboardCache([FromRoute] int id, CancellationToken token)
    {
        await cacheHelper.FlushScoreboardCache(id, token);
        return Ok();
    }

    /// <summary>
    /// Get Game Challenge
    /// </summary>
    /// <remarks>
    /// Retrieving a game challenge requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game challenge</response>
    [HttpGet("Games/{id:int}/Challenges/{cId:int}")]
    [ProducesResponseType(typeof(ChallengeEditDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGameChallenge([FromRoute] int id, [FromRoute] int cId, CancellationToken token)
    {
        var challenge = await challengeRepository.GetChallenge(id, cId, token);

        if (challenge is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        // Do not load flags for dynamic containers
        if (challenge.Type != ChallengeType.DynamicContainer)
            await challengeRepository.LoadFlags(challenge, token);

        var result = ChallengeEditDetailModel.FromChallenge(challenge);
        var scoreboard = await gameRepository.TryGetScoreboard(id, token);

        if (scoreboard is not null && scoreboard.ChallengeMap.TryGetValue(cId, out var challengeInfo))
            result.AcceptedCount = challengeInfo.SolvedCount;

        return Ok(result);
    }

    /// <summary>
    /// Update Game Challenge Information
    /// </summary>
    /// <remarks>
    /// Updating a game challenge, requires administrator privileges. Flags are not affected; use Flag-related APIs to modify
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="model">Challenge information</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully updated game challenge</response>
    [HttpPut("Games/{id:int}/Challenges/{cId:int}")]
    [ProducesResponseType(typeof(ChallengeEditDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateGameChallenge([FromRoute] int id, [FromRoute] int cId,
        [FromBody] ChallengeUpdateModel model, CancellationToken token)
    {
        await using var transaction = await challengeRepository.BeginTransactionAsync(token);

        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        await speedrunService.LockGameLifecycle(id, token);

        var res = await challengeRepository.GetChallenge(id, cId, token);

        if (res is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        if (ChangesActiveSpeedrunChallenge(model) &&
            (await speedrunService.IsChallengeInActiveRound(id, cId, token) ||
             model.Category is not null &&
             await speedrunService.IsCategoryInActiveRound(id, model.Category.Value, token)))
        {
            return Conflict(new RequestResponse(
                "This challenge belongs to the active Speedrun round. End or cancel the round before changing its category, enabled state, flag, or hint schedule.",
                StatusCodes.Status409Conflict));
        }

        // NOTE: IsEnabled can only be updated outside the edit page
        if (model.IsEnabled is true && !res.IsEnabled)
        {
            var validationError = await ValidateChallengeForEnable(res, token);
            if (validationError is not null)
                return BadRequest(new RequestResponse(validationError));
        }

        if (model.EnableTrafficCapture is true && !res.Type.IsContainer())
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Challenge_CaptureNotAllowed)]));

        if (model.FileName is not null && string.IsNullOrWhiteSpace(model.FileName))
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Challenge_DynamicAssetsNotNullable)]));

        var scheduleError = ValidateHintSchedule(model.Hints ?? res.Hints,
            model.SpeedrunHintReleaseSeconds ??
            (model.SpeedrunHintReleaseMinutes is null ? res.SpeedrunHintReleaseSeconds : null),
            model.SpeedrunHintReleaseMinutes ??
            (model.SpeedrunHintReleaseSeconds is null ? res.SpeedrunHintReleaseMinutes : null));
        if (scheduleError is not null)
            return BadRequest(new RequestResponse(scheduleError));

        var hintUpdated = model.IsHintUpdated(res.Hints?.GetSetHashCode());

        if (!string.IsNullOrWhiteSpace(model.FlagTemplate) && res.Type == ChallengeType.DynamicContainer &&
            !model.IsValidFlagTemplate())
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Challenge_FlagTooTrivial)]));

        res.Update(model);

        switch (model.IsEnabled)
        {
            case true:
                {
                    // Will also update IsEnabled
                    await challengeRepository.EnsureInstances(res, game, token);

                    if (game.IsActive && game.Mode != GameMode.Speedrun)
                        await gameNoticeRepository.AddNotice(
                            new() { Game = game, Type = NoticeType.NewChallenge, Values = [res.Title] }, token);
                    break;
                }
            case false when res.Type.IsContainer():
                await instanceRepository.DestroyAllContainers(res, token);
                break;
            case null:
                // do nothing
                break;
        }

        if (game.IsActive && game.Mode != GameMode.Speedrun && res.IsEnabled && hintUpdated)
            await gameNoticeRepository.AddNotice(
                new() { Game = game, Type = NoticeType.NewHint, Values = [res.Title] },
                token);

        await challengeRepository.SaveAsync(token);

        await transaction.CommitAsync(token);

        // Always flush scoreboard
        await cacheHelper.FlushScoreboardCache(game.Id, token);

        return Ok(ChallengeEditDetailModel.FromChallenge(res));
    }

    /// <summary>
    /// Enable or disable multiple game challenges in one lifecycle transaction.
    /// Invalid challenges remain disabled and are returned with validation reasons.
    /// </summary>
    [HttpPut("Games/{id:int}/Challenges/BulkState")]
    [ProducesResponseType(typeof(ChallengeBulkStateResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> BulkUpdateChallengeState([FromRoute] int id,
        [FromBody] ChallengeBulkStateModel model, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);
        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        await using var transaction = await challengeRepository.BeginTransactionAsync(token);
        await speedrunService.LockGameLifecycle(id, token);

        var requestedIds = model.ChallengeIds.Distinct().ToArray();
        var challenges = await challengeRepository.GetChallenges(id, token);
        if (requestedIds.Length > 0)
        {
            var foundIds = challenges.Select(challenge => challenge.Id).ToHashSet();
            var missing = requestedIds.Where(challengeId => !foundIds.Contains(challengeId)).ToArray();
            if (missing.Length > 0)
                return NotFound(new RequestResponse($"Challenges not found in this game: {string.Join(", ", missing)}",
                    StatusCodes.Status404NotFound));
            challenges = challenges.Where(challenge => requestedIds.Contains(challenge.Id)).ToArray();
        }

        foreach (var challenge in challenges)
        {
            if (challenge.IsEnabled == model.IsEnabled)
                continue;
            if (await speedrunService.IsChallengeInActiveRound(id, challenge.Id, token))
            {
                return Conflict(new RequestResponse(
                    $"Challenge '{challenge.Title}' belongs to the active Speedrun round. End or cancel the round before changing its enabled state.",
                    StatusCodes.Status409Conflict));
            }
        }

        var failures = new List<ChallengeLifecycleFailureModel>();
        var changed = new List<GameChallenge>();
        var validEnableIds = new List<int>();
        if (model.IsEnabled)
        {
            foreach (var challenge in challenges)
            {
                var error = await ValidateChallengeForEnable(challenge, token);
                if (error is not null)
                {
                    failures.Add(new()
                    {
                        ChallengeId = challenge.Id,
                        Title = challenge.Title,
                        Reason = error
                    });
                    continue;
                }
                validEnableIds.Add(challenge.Id);
                if (!challenge.IsEnabled)
                    changed.Add(challenge);
            }
        }
        else
        {
            changed.AddRange(challenges.Where(challenge => challenge.IsEnabled));
        }

        foreach (var challenge in changed)
            challenge.IsEnabled = model.IsEnabled;
        await challengeRepository.SaveAsync(token);

        var createdInstanceCount = 0;
        if (model.IsEnabled && validEnableIds.Count > 0)
        {
            createdInstanceCount = await challengeRepository.ReconcileInstances(id,
                validEnableIds, token);

            if (changed.Count > 0 && game.IsActive && game.Mode != GameMode.Speedrun)
                foreach (var challenge in changed)
                    await gameNoticeRepository.AddNotice(
                        new() { Game = game, Type = NoticeType.NewChallenge, Values = [challenge.Title] }, token);
        }
        else if (!model.IsEnabled)
        {
            foreach (var challenge in changed.Where(challenge => challenge.Type.IsContainer()))
                await instanceRepository.DestroyAllContainers(challenge, token);
        }

        await transaction.CommitAsync(token);
        await cacheHelper.FlushScoreboardCache(id, token);

        return Ok(new ChallengeBulkStateResultModel
        {
            ChangedChallengeIds = changed.Select(challenge => challenge.Id).ToArray(),
            CreatedInstanceCount = createdInstanceCount,
            Failures = failures.ToArray()
        });
    }

    private static bool ChangesActiveSpeedrunChallenge(ChallengeUpdateModel model) =>
        model.Category is not null || model.IsEnabled is not null || model.FlagTemplate is not null ||
        model.Hints is not null || model.SpeedrunHintReleaseSeconds is not null ||
        model.SpeedrunHintReleaseMinutes is not null;

    private async Task<string?> ValidateChallengeForEnable(GameChallenge challenge, CancellationToken token)
    {
        if (challenge.Type != ChallengeType.DynamicContainer)
        {
            await challengeRepository.LoadFlags(challenge, token);
            if (challenge.Flags.Count == 0)
                return localizer[nameof(Resources.Program.Challenge_NoFlag)];
        }

        if (challenge.Type.IsContainer())
        {
            if (string.IsNullOrWhiteSpace(challenge.ContainerImage))
                return "Container image is required before this challenge can be enabled.";
            if (challenge.ExposePort is null or < 1 or > 65535)
                return "A valid exposed container port (1-65535) is required before this challenge can be enabled.";
        }

        if (challenge.Type == ChallengeType.DynamicContainer &&
            (string.IsNullOrWhiteSpace(challenge.FlagTemplate) ||
             !new DynamicFlagGenerator(challenge.FlagTemplate).IsValid()))
        {
            return "A valid, non-trivial dynamic flag template is required before this challenge can be enabled.";
        }

        return ValidateHintSchedule(challenge.Hints, challenge.SpeedrunHintReleaseSeconds,
            challenge.SpeedrunHintReleaseMinutes);
    }

    private static string? ValidateHintSchedule(IReadOnlyCollection<string>? hints,
        IReadOnlyList<int>? seconds, IReadOnlyList<int>? minutes)
    {
        var schedule = seconds ?? minutes?.Select(value => value * 60).ToArray();
        if (schedule is null)
            return null;
        if (schedule.Count != (hints?.Count ?? 0))
            return "Speedrun hint schedule count must match the number of hints.";
        if (schedule.Any(value => value < 0))
            return "Speedrun hint schedule values must be zero or greater.";
        if (schedule.Zip(schedule.Skip(1)).Any(pair => pair.First > pair.Second))
            return "Speedrun hint schedule must be ordered from earliest to latest.";
        return null;
    }

    /// <summary>
    /// Test Game Challenge Container
    /// </summary>
    /// <remarks>
    /// Testing a game challenge container requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully started game challenge container</response>
    [HttpPost("Games/{id:int}/Challenges/{cId:int}/Container")]
    [ProducesResponseType(typeof(ContainerInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateTestContainer([FromRoute] int id, [FromRoute] int cId,
        CancellationToken token)
    {
        var challenge = await challengeRepository.GetChallenge(id, cId, token);

        if (challenge is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        if (!challenge.Type.IsContainer())
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerCreationNotAllowed)]));

        if (challenge.ContainerImage is null || challenge.ExposePort is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Container_ConfigError)]));

        var user = await userManager.GetUserAsync(User);

        var container = await containerService.CreateContainerAsync(
            new()
            {
                TeamId = "admin",
                UserId = user!.Id,
                ChallengeId = challenge.Id,
                GameId = challenge.GameId,
                Flag = challenge.Type.IsDynamic() ? challenge.GenerateTestFlag() : null,
                Image = challenge.ContainerImage,
                CPUCount = challenge.CPUCount ?? 1,
                MemoryLimit = challenge.MemoryLimit ?? 64,
                StorageLimit = challenge.StorageLimit ?? 256,
                NetworkMode = challenge.NetworkMode ?? NetworkMode.Open,
                ExposedPort = challenge.ExposePort.Value,
            }, token);

        if (container is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Container_CreationFailed)]));

        challenge.TestContainer = container;
        await challengeRepository.SaveAsync(token);

        logger.Log(
            StaticLocalizer[nameof(Resources.Program.Container_TestContainerCreated), container.LogId],
            user,
            TaskStatus.Success);

        return Ok(ContainerInfoModel.FromContainer(container));
    }

    /// <summary>
    /// Destroy Test Game Challenge Container
    /// </summary>
    /// <remarks>
    /// Destroying a test game challenge container requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully destroyed game challenge container</response>
    [HttpDelete("Games/{id:int}/Challenges/{cId:int}/Container")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DestroyTestContainer([FromRoute] int id, [FromRoute] int cId,
        CancellationToken token)
    {
        var challenge = await challengeRepository.GetChallenge(id, cId, token);

        if (challenge is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        if (challenge.TestContainer is null)
            return Ok();

        await containerRepository.DestroyContainer(challenge.TestContainer, token);

        return Ok();
    }

    /// <summary>
    /// Delete Game Challenge
    /// </summary>
    /// <remarks>
    /// Deleting a game challenge requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted game challenge</response>
    [HttpDelete("Games/{id:int}/Challenges/{cId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveGameChallenge([FromRoute] int id, [FromRoute] int cId,
        CancellationToken token)
    {
        var res = await challengeRepository.GetChallenge(id, cId, token);

        if (res is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        await challengeRepository.RemoveChallenge(res, true, token);

        // Always flush scoreboard
        await cacheHelper.FlushScoreboardCache(id, token);

        return Ok();
    }

    /// <summary>
    /// Update Game Challenge Attachment
    /// </summary>
    /// <remarks>
    /// Updating a game challenge attachment requires administrator privileges; only for non-dynamic attachment challenges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully updated game challenge</response>
    [HttpPost("Games/{id:int}/Challenges/{cId:int}/Attachment")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAttachment([FromRoute] int id, [FromRoute] int cId,
        [FromBody] AttachmentCreateModel model, CancellationToken token)
    {
        var challenge = await challengeRepository.GetChallenge(id, cId, token);

        if (challenge is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        if (challenge.Type == ChallengeType.DynamicAttachment)
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Challenge_UseAssetsApiForDynamic)]));

        await challengeRepository.UpdateAttachment(challenge, model, token);

        return Ok();
    }

    /// <summary>
    /// Add Game Challenge Flag
    /// </summary>
    /// <remarks>
    /// Adding a game challenge flag requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="models"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully added game challenge flags</response>
    [HttpPost("Games/{id:int}/Challenges/{cId:int}/Flags")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddFlags([FromRoute] int id, [FromRoute] int cId,
        [FromBody] FlagCreateModel[] models, CancellationToken token)
    {
        await using var transaction = await challengeRepository.BeginTransactionAsync(token);
        await speedrunService.LockGameLifecycle(id, token);
        var challenge = await challengeRepository.GetChallenge(id, cId, token);

        if (challenge is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        if (await speedrunService.IsChallengeInActiveRound(id, cId, token))
            return Conflict(new RequestResponse(
                "Flags cannot be changed while this challenge belongs to a Running or Overtime Speedrun round. End or cancel the round first.",
                StatusCodes.Status409Conflict));

        await challengeRepository.AddFlags(challenge, models, token);
        await transaction.CommitAsync(token);

        return Ok();
    }

    /// <summary>
    /// Delete Game Challenge Flag
    /// </summary>
    /// <remarks>
    /// Deleting a game challenge flag requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="fId">Flag ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted game challenge flag</response>
    [HttpDelete("Games/{id:int}/Challenges/{cId:int}/Flags/{fId:int}")]
    [ProducesResponseType(typeof(TaskStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveFlag([FromRoute] int id, [FromRoute] int cId, [FromRoute] int fId,
        CancellationToken token)
    {
        await using var transaction = await challengeRepository.BeginTransactionAsync(token);
        await speedrunService.LockGameLifecycle(id, token);
        var challenge = await challengeRepository.GetChallenge(id, cId, token);

        if (challenge is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        if (await speedrunService.IsChallengeInActiveRound(id, cId, token))
            return Conflict(new RequestResponse(
                "Flags cannot be changed while this challenge belongs to a Running or Overtime Speedrun round. End or cancel the round first.",
                StatusCodes.Status409Conflict));

        var status = await challengeRepository.RemoveFlag(challenge, fId, token);
        await transaction.CommitAsync(token);
        return Ok(status);
    }


    /// <summary>
    /// Export game package
    /// </summary>
    /// <remarks>
    /// Export game with all challenges, divisions, and attachments as a ZIP file; requires Admin permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully exported game package</response>
    /// <response code="400">Invalid operation</response>
    /// <response code="404">Game not found</response>
    /// <response code="500">Internal server error during export</response>
    [HttpPost("Games/{id:int}/Export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExportGame([FromRoute] int id, CancellationToken token = default)
    {
        try
        {
            var result = await exportService.ExportGameAsync(id, token);

            if (result is null)
            {
                logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.Game_NotFound)],
                    TaskStatus.NotFound,
                    LogLevel.Warning);
                return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                    StatusCodes.Status404NotFound));
            }

            var fileName = $"{result.Game.Title}-export-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip";

            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.Game_Exported), result.Game.Title, fileName]);

            var fileStream = new FileStream(
                result.ZipFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.DeleteOnClose | FileOptions.SequentialScan | FileOptions.Asynchronous);

            return File(fileStream, "application/zip", fileName, enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.Game_ExportFailed), id],
                TaskStatus.Failed,
                LogLevel.Error);
            logger.LogError(ex, "Failed to export game {GameId}", id);
            return RequestResponse.Result(localizer[nameof(Resources.Program.Error_InternalServerError)],
                StatusCodes.Status500InternalServerError);
        }
    }

    private static readonly string[] AllowedImportContentTypes = ["application/zip", "application/x-zip-compressed"];

    /// <summary>
    /// Import game package
    /// </summary>
    /// <remarks>
    /// Import game from a ZIP package; requires Admin permission
    /// </remarks>
    /// <param name="file">Game package ZIP file</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully imported game, returns game ID</response>
    /// <response code="400">Invalid package or import failed</response>
    /// <response code="500">Internal server error during import</response>
    [HttpPost("Games/Import")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status500InternalServerError)]
    [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<IActionResult> ImportGame(IFormFile file, CancellationToken token = default)
    {
        switch (file.Length)
        {
            case 0:
                logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.File_SizeZero)],
                    TaskStatus.Failed,
                    LogLevel.Warning);
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeZero)]));
            case > 512 * 1024 * 1024:
                // 512MB limit
                logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.File_SizeTooLarge), file.FileName],
                    TaskStatus.Failed,
                    LogLevel.Warning);
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeTooLarge)]));
        }

        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            !AllowedImportContentTypes.Contains(file.ContentType))
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.File_TypeNotSupported), file.FileName],
                TaskStatus.Failed,
                LogLevel.Warning);
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_TypeNotSupported)]));
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var gameId = await importService.ImportGameAsync(stream, token);

            if (gameId is null)
            {
                logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.Game_ImportFailed), file.FileName],
                    TaskStatus.Failed,
                    LogLevel.Warning);
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_ImportFailed)]));
            }

            await cacheHelper.FlushRecentGamesCache(token);

            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.Game_Imported), gameId, file.FileName]);

            return Ok(gameId);
        }
        catch (InvalidOperationException)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.Game_ImportInvalidPackage), file.FileName],
                TaskStatus.Failed,
                LogLevel.Warning);
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_ImportInvalidPackage)]));
        }
        catch (Exception ex)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.Game_ImportFailed), file.FileName],
                TaskStatus.Failed,
                LogLevel.Error);
            logger.LogError(ex, "Failed to import game from file {FileName}: {ErrorMessage}", file.FileName,
                ex.Message);
            return RequestResponse.Result(localizer[nameof(Resources.Program.Error_InternalServerError)],
                StatusCodes.Status500InternalServerError);
        }
    }
}
