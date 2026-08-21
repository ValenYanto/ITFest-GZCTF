using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Net.Mime;
using GZCTF.Extensions;
using GZCTF.Middlewares;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Admin;
using GZCTF.Models.Request.Info;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Cache;
using GZCTF.Services.Config;
using GZCTF.Services.Mail;
using GZCTF.Storage.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace GZCTF.Controllers;

/// <summary>
/// Administration APIs
/// </summary>
[RequireAdmin]
[ApiController]
[Route("api/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class AdminController(
    UserManager<UserInfo> userManager,
    ILogger<AdminController> logger,
    IBlobStorage storage,
    CacheHelper cacheHelper,
    IBlobRepository blobService,
    ILogRepository logRepository,
    IConfigService configService,
    IGameRepository gameRepository,
    ITeamRepository teamRepository,
    IContainerRepository containerRepository,
    IServiceProvider serviceProvider,
    IParticipationRepository participationRepository,
    IMailSender mailSender,
    AppDbContext dbContext,
    IStringLocalizer<Program> localizer) : ControllerBase
{
    /// <summary>
    /// Get configuration
    /// </summary>
    /// <remarks>
    /// Use this API to get global settings, requires Admin permission
    /// </remarks>
    /// <response code="200">Global configuration</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpGet("Config")]
    [ProducesResponseType(typeof(ConfigEditModel), StatusCodes.Status200OK)]
    public IActionResult GetConfigs()
    {
        // always reload, ensure latest
        configService.ReloadConfig();

        ConfigEditModel config = new()
        {
            AccountPolicy = serviceProvider.GetRequiredService<IOptionsSnapshot<AccountPolicy>>().Value,
            GlobalConfig = serviceProvider.GetRequiredService<IOptionsSnapshot<GlobalConfig>>().Value,
            ContainerPolicy = serviceProvider.GetRequiredService<IOptionsSnapshot<ContainerPolicy>>().Value
        };

        return Ok(config);
    }

    /// <summary>
    /// Change configuration
    /// </summary>
    /// <remarks>
    /// Use this API to change global settings, requires Admin permission
    /// </remarks>
    /// <response code="200">Update successful</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpPut("Config")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateConfigs([FromBody] ConfigEditModel model, CancellationToken token)
    {
        // handle api encryption config
        var global = serviceProvider.GetRequiredService<IOptionsSnapshot<GlobalConfig>>().Value;
        if (!global.ApiEncryption && model.GlobalConfig?.ApiEncryption is true)
            await configService.UpdateApiEncryptionKey(token);

        // save all config properties
        foreach (var prop in typeof(ConfigEditModel).GetProperties())
        {
            var value = prop.GetValue(model);

            if (value is null)
                continue;

            await configService.SaveConfig(prop.PropertyType, value, token);
        }

        return Ok();
    }

    /// <summary>
    /// Change platform Logo
    /// </summary>
    /// <remarks>
    /// Use this API to change the platform Logo, requires Admin permission
    /// </remarks>
    /// <response code="200">Update successful</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpPost("Config/Logo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateLogo(IFormFile file, CancellationToken token)
    {
        switch (file.Length)
        {
            case 0:
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeZero)]));
            case > 3 * 1024 * 1024:
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeTooLarge)]));
        }

        if (!await DeleteCurrentLogo(token))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Admin_LogoUpdateFailed)]));

        var logo = await blobService.CreateOrUpdateImage(file, "logo", 640, token);
        if (logo is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Admin_LogoUpdateFailed)]));

        var favicon = await blobService.CreateOrUpdateImage(file, "favicon", 256, token);
        if (favicon is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Admin_LogoUpdateFailed)]));

        HashSet<Config> configSet =
        [
            new($"{nameof(GlobalConfig)}:{nameof(GlobalConfig.LogoHash)}", logo.Hash, [CacheKey.ClientConfig]),
            new($"{nameof(GlobalConfig)}:{nameof(GlobalConfig.FaviconHash)}", favicon.Hash, [CacheKey.Favicon])
        ];

        await configService.SaveConfigSet(configSet, token);

        return Ok();
    }

    /// <summary>
    /// Reset platform Logo
    /// </summary>
    /// <remarks>
    /// Use this API to reset the platform Logo, requires Admin permission
    /// </remarks>
    /// <response code="200">Updated successfully</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpDelete("Config/Logo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetLogo(CancellationToken token)
    {
        if (!await DeleteCurrentLogo(token))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Admin_LogoUpdateFailed)]));

        HashSet<Config> configSet =
        [
            new($"{nameof(GlobalConfig)}:{nameof(GlobalConfig.LogoHash)}", string.Empty, [CacheKey.ClientConfig]),
            new($"{nameof(GlobalConfig)}:{nameof(GlobalConfig.FaviconHash)}", string.Empty, [CacheKey.Favicon])
        ];

        await configService.SaveConfigSet(configSet, token);

        return Ok();
    }

    private async Task<bool> DeleteCurrentLogo(CancellationToken token)
    {
        var globalConfig = serviceProvider.GetRequiredService<IOptionsSnapshot<GlobalConfig>>().Value;

        return await DeleteByHash(globalConfig.LogoHash, token) &&
               await DeleteByHash(globalConfig.FaviconHash, token);
    }

    private async Task<bool> DeleteByHash(string? hash, CancellationToken token)
    {
        if (hash is not null && Codec.FileHashRegex().IsMatch(hash))
            return await blobService.DeleteBlobByHash(hash, token) switch
            {
                TaskStatus.Success or TaskStatus.NotFound => true,
                _ => false
            };

        return true;
    }

    /// <summary>
    /// Get all users
    /// </summary>
    /// <remarks>
    /// Use this API to get all users, requires Admin permission
    /// </remarks>
    /// <response code="200">User list</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpGet("Users")]
    [ProducesResponseType(typeof(ArrayResponse<UserInfoModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Users([FromQuery][Range(0, 500)] int count = 100, [FromQuery] int skip = 0,
        CancellationToken token = default) =>
        Ok((await userManager.Users.OrderBy(e => e.Id).Skip(skip).Take(count)
                .Select(u => UserInfoModel.FromUserInfo(u))
                .ToArrayAsync(token))
            .ToResponse(await userManager.Users.CountAsync(token)));

    /// <summary>
    /// Add users in batch
    /// </summary>
    /// <remarks>
    /// Use this API to add users in batch, requires Admin permission
    /// </remarks>
    /// <response code="200">Successfully added</response>
    /// <response code="400">User validation failed</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpPost("Users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddUsers([FromBody] UserCreateModel[] model, CancellationToken token = default)
    {
        var currentUser = await userManager.GetUserAsync(User);
        var trans = await teamRepository.BeginTransactionAsync(token);

        try
        {
            var users = new List<(UserInfo, string?)>(model.Length);
            foreach (var user in model)
            {
                var userInfo = user.ToUserInfo();
                var result = await userManager.CreateAsync(userInfo, user.Password);

                if (result.Succeeded)
                {
                    users.Add((userInfo, user.TeamName));
                    continue;
                }

                userInfo = result.Errors.FirstOrDefault()?.Code switch
                {
                    "DuplicateEmail" => await userManager.FindByEmailAsync(user.Email),
                    "DuplicateUserName" => await userManager.FindByNameAsync(user.UserName),
                    _ => null
                };

                if (userInfo is null)
                {
                    await trans.RollbackAsync(token);
                    return HandleIdentityError(result.Errors);
                }

                userInfo.UpdateUserInfo(user);
                var code = await userManager.GeneratePasswordResetTokenAsync(userInfo);
                await userManager.ResetPasswordAsync(userInfo, code, user.Password);

                users.Add((userInfo, user.TeamName));
            }

            var teams = new List<Team>();
            foreach (var (user, teamName) in users)
            {
                if (teamName is null)
                    continue;

                var team = teams.Find(team => team.Name == teamName);
                if (team is null)
                {
                    team = await teamRepository.CreateTeam(new() { Name = teamName }, user, token);
                    teams.Add(team);
                }
                else
                {
                    team.Members.Add(user);
                }
            }

            await teamRepository.SaveAsync(token);
            await trans.CommitAsync(token);

            logger.Log(StaticLocalizer[nameof(Resources.Program.Admin_UserBatchAdded), users.Count],
                currentUser, TaskStatus.Success);

            return Ok();
        }
        catch
        {
            await trans.RollbackAsync(token);
            throw;
        }
    }

    /// <summary>
    /// Search users
    /// </summary>
    /// <remarks>
    /// Use this API to search users, requires Admin permission
    /// </remarks>
    /// <response code="200">User list</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpPost("Users/Search")]
    [ProducesResponseType(typeof(ArrayResponse<UserInfoModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchUsers([FromQuery] string hint, CancellationToken token = default)
    {
        var loweredHint = hint.ToLower();
        var data = await userManager.Users.Where(item =>
            item.UserName!.ToLower().Contains(loweredHint) ||
            item.StdNumber.ToLower().Contains(loweredHint) ||
            item.Email!.ToLower().Contains(loweredHint) ||
            item.PhoneNumber!.ToLower().Contains(loweredHint) ||
            item.Id.ToString().ToLower().Contains(loweredHint) ||
            item.RealName.ToLower().Contains(loweredHint)
        ).OrderBy(e => e.Id).Take(30).ToArrayAsync(token);

        return Ok(data.Select(UserInfoModel.FromUserInfo).ToResponse());
    }

    /// <summary>
    /// Get all team information
    /// </summary>
    /// <remarks>
    /// Use this API to get all teams, requires Admin permission
    /// </remarks>
    /// <response code="200">User list</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpGet("Teams")]
    [ProducesResponseType(typeof(ArrayResponse<TeamInfoModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Teams([FromQuery][Range(0, 500)] int count = 100, [FromQuery] int skip = 0,
        CancellationToken token = default) =>
        Ok((await teamRepository.GetTeams(count, skip, token)).Select(team => TeamInfoModel.FromTeam(team))
            .ToResponse(await teamRepository.CountAsync(token)));

    /// <summary>
    /// Search teams
    /// </summary>
    /// <remarks>
    /// Use this API to search teams, requires Admin permission
    /// </remarks>
    /// <response code="200">User list</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpPost("Teams/Search")]
    [ProducesResponseType(typeof(ArrayResponse<TeamInfoModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchTeams([FromQuery] string hint, CancellationToken token = default) =>
        Ok((await teamRepository.SearchTeams(hint, token))
            .Select(team => TeamInfoModel.FromTeam(team))
            .ToResponse());

    /// <summary>
    /// Modify team information
    /// </summary>
    /// <remarks>
    /// Use this API to modify team information, requires Admin permission
    /// </remarks>
    /// <response code="200">Successfully updated</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">Team not found</response>
    [HttpPut("Teams/{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTeam([FromRoute] int id, [FromBody] AdminTeamModel model,
        CancellationToken token = default)
    {
        var team = await teamRepository.GetTeamById(id, token);

        if (team is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Team_NotFound)]));

        team.UpdateInfo(model);
        await teamRepository.SaveAsync(token);

        return Ok();
    }

    /// <summary>
    /// Modify user information
    /// </summary>
    /// <remarks>
    /// Use this API to modify user information, requires Admin permission
    /// </remarks>
    /// <response code="200">Successfully updated</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">User not found</response>
    [HttpPut("Users/{userid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUserInfo(string userid, [FromBody] AdminUserInfoModel model)
    {
        var user = await userManager.FindByIdAsync(userid);

        if (user is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Admin_UserNotFound)],
                StatusCodes.Status404NotFound));

        if (model.UserName is not null && model.UserName != user.UserName)
        {
            var result = await userManager.SetUserNameAsync(user, model.UserName);

            if (!result.Succeeded)
                return HandleIdentityError(result.Errors);
        }

        if (model.Email is not null && model.Email != user.Email)
        {
            var result = await userManager.SetEmailAsync(user, model.Email);

            if (!result.Succeeded)
                return HandleIdentityError(result.Errors);
        }

        user.UpdateUserInfo(model);
        await userManager.UpdateAsync(user);

        return Ok();
    }

    /// <summary>
    /// Reset user password
    /// </summary>
    /// <remarks>
    /// Use this API to reset user password, requires Admin permission
    /// </remarks>
    /// <response code="200">Successfully retrieved</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">User not found</response>
    [HttpDelete("Users/{userid:guid}/Password")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(string userid)
    {
        var user = await userManager.FindByIdAsync(userid);

        if (user is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Admin_UserNotFound)],
                StatusCodes.Status404NotFound));

        var pwd = Codec.RandomPassword(16);
        var code = await userManager.GeneratePasswordResetTokenAsync(user);
        await userManager.ResetPasswordAsync(user, code, pwd);

        return Ok(pwd);
    }

    /// <summary>
    /// Delete user
    /// </summary>
    /// <remarks>
    /// Use this API to delete user, requires Admin permission
    /// </remarks>
    /// <response code="200">Successfully retrieved</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">User not found</response>
    [HttpDelete("Users/{userid:guid}")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUser(Guid userid, CancellationToken token = default)
    {
        var user = await userManager.GetUserAsync(User);

        if (user!.Id == userid)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Admin_SelfDeletionNotAllowed)]));

        user = await userManager.FindByIdAsync(userid.ToString());

        if (user is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Admin_UserNotFound)],
                StatusCodes.Status404NotFound));

        if (await teamRepository.CheckIsCaptain(user, token))
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Admin_CaptainDeletionNotAllowed)]));

        await userManager.DeleteAsync(user);

        return Ok();
    }

    /// <summary>
    /// Delete team
    /// </summary>
    /// <remarks>
    /// Use this API to delete team, requires Admin permission
    /// </remarks>
    /// <response code="200">Successfully retrieved</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">User not found</response>
    [HttpDelete("Teams/{id:int}")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTeam(int id, CancellationToken token = default)
    {
        var team = await teamRepository.GetTeamById(id, token);

        if (team is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Team_NotFound)],
                StatusCodes.Status404NotFound));

        await teamRepository.DeleteTeam(team, token);

        return Ok();
    }

    /// <summary>
    /// Get user information
    /// </summary>
    /// <remarks>
    /// Use this API to get user information, requires Admin permission
    /// </remarks>
    /// <response code="200">User object</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpGet("Users/{userid:guid}")]
    [ProducesResponseType(typeof(ProfileUserInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UserInfo(string userid)
    {
        var user = await userManager.FindByIdAsync(userid);

        if (user is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Admin_UserNotFound)],
                StatusCodes.Status404NotFound));

        return Ok(ProfileUserInfoModel.FromUserInfo(user));
    }

    /// <summary>
    /// Get all logs
    /// </summary>
    /// <remarks>
    /// Use this API to get all logs, requires Admin permission
    /// </remarks>
    /// <response code="200">Log list</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpGet("Logs")]
    [ProducesResponseType(typeof(LogMessageModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logs([FromQuery] string? level = "All",
        [FromQuery][Range(0, 1000)] int count = 50,
        [FromQuery] int skip = 0, CancellationToken token = default) =>
        Ok(await logRepository.GetLogs(skip, count, level, token));

    /// <summary>
    /// Update participation status
    /// </summary>
    /// <remarks>
    /// Use this API to update team participation status, review application, requires Admin permission
    /// </remarks>
    /// <response code="200">Update successful</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">Participation object not found</response>
    [HttpPut("Participation/{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Participation(int id, [FromBody] ParticipationEditModel model,
        CancellationToken token = default)
    {
        await using var transaction = await participationRepository.BeginTransactionAsync(token);

        var participation = await participationRepository.GetParticipationById(id, token);

        if (participation is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Admin_ParticipationNotFound)],
                StatusCodes.Status404NotFound));

        await participationRepository.UpdateParticipation(participation, model, token);

        await transaction.CommitAsync(token);
        await cacheHelper.FlushScoreboardCache(participation.GameId, token);

        return Ok();
    }

    /// <summary>
    /// Get all Writeup basic information
    /// </summary>
    /// <remarks>
    /// Use this API to get Writeup basic information, requires Admin permission
    /// </remarks>
    /// <response code="200">Update successful</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">Game not found</response>
    [HttpGet("Writeups/{id:int}")]
    [ProducesResponseType(typeof(WriteupInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Writeups(int id, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        return Ok(await participationRepository.GetWriteups(game, token));
    }

    /// <summary>
    /// Download all Writeups
    /// </summary>
    /// <remarks>
    /// Use this API to download all Writeups, requires Admin permission
    /// </remarks>
    /// <response code="200">Downloaded successfully</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">Game not found</response>
    [HttpGet("Writeups/{id:int}/All")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAllWriteups(int id, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var into = await participationRepository.GetWriteups(game, token);
        var filename = $"Writeups-{game.Title}-{DateTimeOffset.UtcNow:yyyyMMdd-HH.mm.ss}Z";

        return new TarFilesResult(storage, into.Writeups.Select(p => p.File), PathHelper.Uploads, filename, token);
    }

    /// <summary>
    /// Get all container instances
    /// </summary>
    /// <remarks>
    /// Use this API to get all container instances, requires Admin permission
    /// </remarks>
    /// <response code="200">Instance list</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpGet("Instances")]
    [ProducesResponseType(typeof(ArrayResponse<ContainerInstanceModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Instances(CancellationToken token = default) =>
        Ok(new ArrayResponse<ContainerInstanceModel>(await containerRepository.GetContainerInstances(token)));

    /// <summary>
    /// Delete container instance
    /// </summary>
    /// <remarks>
    /// Use this API to forcibly delete container instance, requires Admin permission
    /// </remarks>
    /// <response code="200">Successfully retrieved</response>
    /// <response code="400">Container instance destruction failed</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">Container instance not found</response>
    [HttpDelete("Instances/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [SuppressMessage("ReSharper", "RouteTemplates.ParameterTypeCanBeMadeStricter")]
    public async Task<IActionResult> DestroyInstance(Guid id, CancellationToken token = default)
    {
        var container = await containerRepository.GetContainerById(id, token);

        if (container is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Admin_ContainerInstanceNotFound)],
                StatusCodes.Status404NotFound));

        if (await containerRepository.DestroyContainer(container, token))
            return Ok();

        return BadRequest(
            new RequestResponse(localizer[nameof(Resources.Program.Admin_ContainerInstanceDestroyFailed)]));
    }

    /// <summary>
    /// Get all files
    /// </summary>
    /// <remarks>
    /// Use this API to get all files, requires Admin permission
    /// </remarks>
    /// <response code="200">File list</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpGet("Files")]
    [ProducesResponseType(typeof(ArrayResponse<LocalFile>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Files([FromQuery][Range(0, 500)] int count = 50, [FromQuery] int skip = 0,
        CancellationToken token = default) =>
        Ok(new ArrayResponse<LocalFile>(await blobService.GetBlobs(count, skip, token)));

    #region Whitelist

    /// <summary>
    /// Search teams for whitelist (excludes already whitelisted teams for the game)
    /// </summary>
    [HttpGet("Games/{gameId:int}/Whitelist/search-teams")]
    [ProducesResponseType(typeof(TeamWithDetailedUserInfo[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchTeamsForWhitelist([FromRoute] int gameId,
        [FromQuery] string query, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)]));

        var whitelistedTeamIds = await dbContext.Participations
            .Where(p => p.GameId == gameId && p.WhitelistSource != WhitelistSource.None)
            .Select(p => p.TeamId)
            .ToArrayAsync(token);

        var loweredHint = query.Trim().ToLower();
        var teams = await dbContext.Teams
            .Include(t => t.Members)
            .Include(t => t.Captain)
            .Where(t => !whitelistedTeamIds.Contains(t.Id) &&
                        (t.Name.ToLower().Contains(loweredHint) ||
                         (t.Captain != null && t.Captain.Email != null &&
                          t.Captain.Email.ToLower().Contains(loweredHint))))
            .OrderBy(t => t.Id)
            .Take(30)
            .ToArrayAsync(token);

        return Ok(teams.Select(TeamWithDetailedUserInfo.FromTeam));
    }

    /// <summary>
    /// Get whitelisted teams for a game
    /// </summary>
    [HttpGet("Games/{gameId:int}/Whitelist")]
    [ProducesResponseType(typeof(WhitelistTeamModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWhitelist([FromRoute] int gameId, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)]));

        var whitelist = await dbContext.Participations
            .Where(p => p.GameId == gameId && p.WhitelistSource != WhitelistSource.None)
            .Include(p => p.Team)
                .ThenInclude(t => t.Captain)
            .Include(p => p.Team)
                .ThenInclude(t => t.Members)
            .OrderBy(p => p.Team.Name)
            .Select(p => new WhitelistTeamModel
            {
                TeamId = p.TeamId,
                TeamName = p.Team.Name,
                CaptainEmail = p.Team.Captain != null ? p.Team.Captain.Email : null,
                Source = p.WhitelistSource,
                Status = p.Status
            })
            .ToArrayAsync(token);

        return Ok(whitelist);
    }

    /// <summary>
    /// Get audit log of whitelist join attempts for a game
    /// </summary>
    [HttpGet("Games/{gameId:int}/Whitelist/join-attempts")]
    [ProducesResponseType(typeof(WhitelistJoinAttemptModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWhitelistJoinAttempts([FromRoute] int gameId, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)]));

        var attempts = await dbContext.WhitelistJoinAttempts
            .Where(a => a.GameId == gameId)
            .Include(a => a.Team)
            .OrderByDescending(a => a.AttemptedAtUtc)
            .Take(100)
            .Select(a => new WhitelistJoinAttemptModel
            {
                Id = a.Id,
                TeamId = a.TeamId,
                TeamName = a.Team.Name,
                AttemptedAtUtc = a.AttemptedAtUtc
            })
            .ToArrayAsync(token);

        return Ok(attempts);
    }

    /// <summary>
    /// Add teams to whitelist
    /// </summary>
    [HttpPost("Games/{gameId:int}/Whitelist")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddWhitelist([FromRoute] int gameId,
        [FromBody] WhitelistTeamsRequest request, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)]));

        foreach (var teamId in request.TeamIds)
        {
            var existing = await dbContext.Participations
                .FirstOrDefaultAsync(p => p.GameId == gameId && p.TeamId == teamId, token);

            if (existing is not null)
            {
                existing.WhitelistSource = WhitelistSource.ManualWhitelist;
                if (existing.Status != ParticipationStatus.Accepted)
                {
                    existing.Status = ParticipationStatus.Accepted;
                    existing.AcceptedTimeUtc = DateTimeOffset.UtcNow;
                }
            }
            else
            {
                var team = await dbContext.Teams.FindAsync([teamId], token);
                if (team is null) continue;

                var participation = new Participation
                {
                    GameId = gameId,
                    TeamId = teamId,
                    Status = ParticipationStatus.Accepted,
                    AcceptedTimeUtc = DateTimeOffset.UtcNow,
                    WhitelistSource = WhitelistSource.ManualWhitelist,
                    Token = gameRepository.GetToken(game, team),
                    Division = null
                };
                dbContext.Participations.Add(participation);
            }
        }

        await dbContext.SaveChangesAsync(token);
        await cacheHelper.FlushScoreboardCache(gameId, token);

        logger.LogInformation("{Count} teams whitelisted for {Title}", request.TeamIds.Length, game.Title);

        return Ok();
    }

    /// <summary>
    /// Remove a team from whitelist (reject their participation)
    /// </summary>
    [HttpDelete("Games/{gameId:int}/Whitelist/{teamId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveWhitelist([FromRoute] int gameId, [FromRoute] int teamId,
        CancellationToken token)
    {
        var participation = await dbContext.Participations
            .FirstOrDefaultAsync(p => p.GameId == gameId && p.TeamId == teamId, token);

        if (participation is null)
            return NotFound(new RequestResponse("Team is not in whitelist."));

        participation.WhitelistSource = WhitelistSource.None;
        participation.Status = ParticipationStatus.Rejected;

        await dbContext.SaveChangesAsync(token);
        await cacheHelper.FlushScoreboardCache(gameId, token);

        logger.LogInformation("Team {TeamId} removed from whitelist for game {GameId}", teamId, gameId);

        return Ok();
    }

    #endregion

    #region CaptainOnboarding

    /// <summary>
    /// Get persistent captain onboarding history and current status.
    /// Raw onboarding tokens are never returned by this endpoint.
    /// </summary>
    [HttpGet("Onboarding")]
    [ProducesResponseType(typeof(CaptainOnboardingRecordModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOnboardingHistory(
        [FromQuery][Range(1, 500)] int count = 200,
        [FromQuery][Range(0, int.MaxValue)] int skip = 0,
        [FromQuery] string? query = null,
        CancellationToken token = default)
    {
        var invitesQuery = dbContext.CaptainOnboardingInvites.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var loweredQuery = query.Trim().ToLower();
            invitesQuery = invitesQuery.Where(invite =>
                invite.TeamName.ToLower().Contains(loweredQuery) ||
                invite.CaptainEmail.ToLower().Contains(loweredQuery));
        }

        var invites = await invitesQuery
            .OrderByDescending(invite => invite.CreatedAtUtc)
            .Skip(skip)
            .Take(count)
            .ToArrayAsync(token);

        var gameIds = invites.SelectMany(invite => invite.GetGameIds()).Distinct().ToArray();
        var gameTitles = await dbContext.Games
            .Where(game => gameIds.Contains(game.Id))
            .ToDictionaryAsync(game => game.Id, game => game.Title, token);
        var now = DateTimeOffset.UtcNow;

        return Ok(invites.Select(invite => new CaptainOnboardingRecordModel
        {
            InviteId = invite.Id,
            TeamName = invite.TeamName,
            CaptainEmail = invite.CaptainEmail,
            GameTitles = GetOnboardingGameTitles(invite, gameTitles),
            Status = GetOnboardingStatus(invite, now),
            CreatedAtUtc = invite.CreatedAtUtc,
            ExpiresAtUtc = invite.ExpiresAtUtc,
            OpenedAtUtc = invite.OpenedAtUtc,
            ConsumedAtUtc = invite.ConsumedAtUtc,
            RevokedAtUtc = invite.RevokedAtUtc,
            LastSentAtUtc = invite.LastSentAtUtc,
            SendCount = invite.SendCount,
            LastEmailQueued = invite.LastEmailQueued,
            TeamId = invite.TeamId
        }).ToArray());
    }

    /// <summary>
    /// Revoke an onboarding link while retaining its audit history.
    /// </summary>
    [HttpDelete("Onboarding/{inviteId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeOnboarding([FromRoute] int inviteId, CancellationToken token)
    {
        var invite = await dbContext.CaptainOnboardingInvites.FindAsync([inviteId], token);
        if (invite is null)
            return NotFound(new RequestResponse("Onboarding invitation was not found."));

        if (invite.ConsumedAtUtc.HasValue)
            return BadRequest(new RequestResponse(
                "A redeemed invitation cannot be revoked. Remove the team from each game whitelist instead."));

        if (!invite.RevokedAtUtc.HasValue)
        {
            invite.RevokedAtUtc = DateTimeOffset.UtcNow;
            invite.TokenHash = (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")).ToSHA256String();
            await dbContext.SaveChangesAsync(token);
        }

        logger.LogInformation("Captain onboarding invite {InviteId} was revoked", invite.Id);
        return Ok();
    }

    /// <summary>
    /// Send a replacement onboarding link. The previous link becomes invalid immediately.
    /// </summary>
    [HttpPost("Onboarding/{inviteId:int}/Resend")]
    [ProducesResponseType(typeof(CaptainOnboardingCreatedModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResendOnboarding([FromRoute] int inviteId,
        [FromBody] CaptainOnboardingResendModel model, CancellationToken token)
    {
        var invite = await dbContext.CaptainOnboardingInvites.FindAsync([inviteId], token);
        if (invite is null)
            return NotFound(new RequestResponse("Onboarding invitation was not found."));

        if (invite.ConsumedAtUtc.HasValue || invite.TeamId.HasValue)
            return BadRequest(new RequestResponse("A redeemed invitation cannot be sent again."));

        var assignedGameIds = invite.GetGameIds().Distinct().ToArray();
        var games = await dbContext.Games
            .Where(game => assignedGameIds.Contains(game.Id))
            .ToDictionaryAsync(game => game.Id, game => game.Title, token);
        var gameTitles = GetOnboardingGameTitles(invite, games);
        if (gameTitles.Length != assignedGameIds.Length)
            return BadRequest(new RequestResponse(
                "One or more games assigned to this onboarding invitation no longer exist."));

        var rawToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        invite.TokenHash = rawToken.ToSHA256String();
        invite.ExpiresAtUtc = now.AddHours(model.ExpiresInHours);
        invite.OpenedAtUtc = null;
        invite.RevokedAtUtc = null;

        var onboardingUrl = BuildOnboardingUrl(rawToken);
        var emailQueued = mailSender.SendCaptainOnboardingUrl(
            invite.TeamName, invite.CaptainEmail, onboardingUrl, gameTitles, invite.ExpiresAtUtc,
            localizer, serviceProvider.GetRequiredService<IOptionsSnapshot<GlobalConfig>>());

        invite.LastSentAtUtc = now;
        invite.SendCount++;
        invite.LastEmailQueued = emailQueued;
        await dbContext.SaveChangesAsync(token);

        logger.LogInformation(
            "Captain onboarding invite {InviteId} replacement link queued: {EmailQueued}",
            invite.Id, emailQueued);

        return Ok(new CaptainOnboardingCreatedModel
        {
            InviteId = invite.Id,
            TeamName = invite.TeamName,
            CaptainEmail = invite.CaptainEmail,
            OnboardingUrl = onboardingUrl,
            EmailQueued = emailQueued,
            GameTitles = gameTitles
        });
    }

    /// <summary>
    /// Bulk-create captain onboarding invites from the global admin page
    /// </summary>
    [HttpPost("Onboarding")]
    [ProducesResponseType(typeof(CaptainOnboardingCreatedModel[]), StatusCodes.Status200OK)]
    public Task<IActionResult> BulkCreateOnboarding(
        [FromBody] CaptainOnboardingBatchModel model, CancellationToken token) =>
        CreateOnboardingInvites(model.GameIds, model, token);

    /// <summary>
    /// Backwards-compatible per-game endpoint. GameIds may include extra games.
    /// </summary>
    [HttpPost("Games/{gameId:int}/Onboarding")]
    [ProducesResponseType(typeof(CaptainOnboardingCreatedModel[]), StatusCodes.Status200OK)]
    public Task<IActionResult> BulkCreateGameOnboarding([FromRoute] int gameId,
        [FromBody] CaptainOnboardingBatchModel model, CancellationToken token) =>
        CreateOnboardingInvites([gameId, .. model.GameIds], model, token);

    private async Task<IActionResult> CreateOnboardingInvites(int[] requestedGameIds,
        CaptainOnboardingBatchModel model, CancellationToken token)
    {
        var gameIds = requestedGameIds.Where(id => id > 0).Distinct().ToArray();
        if (gameIds.Length == 0)
            return BadRequest(new RequestResponse("Select at least one game for the onboarding batch."));

        var unorderedGames = await dbContext.Games
            .Where(game => gameIds.Contains(game.Id))
            .ToArrayAsync(token);

        if (unorderedGames.Length != gameIds.Length)
            return NotFound(new RequestResponse("One or more selected games do not exist."));

        var games = gameIds.Select(id => unorderedGames.First(game => game.Id == id)).ToArray();

        var entries = model.Entries
            .Select(entry => new CaptainOnboardingEntryModel
            {
                TeamName = entry.TeamName.Trim(),
                CaptainEmail = entry.CaptainEmail.Trim().ToLowerInvariant()
            })
            .ToArray();

        if (entries.Length > 1000)
            return BadRequest(new RequestResponse("An onboarding batch may contain at most 1000 teams."));

        if (entries.Any(entry => string.IsNullOrWhiteSpace(entry.TeamName) ||
                                 string.IsNullOrWhiteSpace(entry.CaptainEmail)))
            return BadRequest(new RequestResponse("Every line must contain a team name and captain email."));

        if (entries.Select(entry => entry.TeamName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
            return BadRequest(new RequestResponse("Duplicate team names were found in the onboarding batch."));

        if (entries.Select(entry => entry.CaptainEmail).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            entries.Length)
            return BadRequest(new RequestResponse("Duplicate captain emails were found in the onboarding batch."));

        var loweredTeamNames = entries.Select(entry => entry.TeamName.ToLower()).ToArray();
        if (await dbContext.Teams.AnyAsync(team => loweredTeamNames.Contains(team.Name.ToLower()), token))
            return BadRequest(new RequestResponse("A team with one of the submitted names already exists."));

        var normalizedEmails = entries.Select(entry => entry.CaptainEmail.ToUpperInvariant()).ToArray();
        if (await userManager.Users.AnyAsync(user =>
                user.NormalizedEmail != null && normalizedEmails.Contains(user.NormalizedEmail), token))
            return BadRequest(new RequestResponse("An account with one of the captain emails already exists."));

        var captainEmails = entries.Select(entry => entry.CaptainEmail).ToArray();
        if (await dbContext.CaptainOnboardingInvites.AnyAsync(invite =>
                invite.ConsumedAtUtc == null &&
                invite.RevokedAtUtc == null &&
                invite.ExpiresAtUtc > DateTimeOffset.UtcNow &&
                (captainEmails.Contains(invite.CaptainEmail) ||
                 loweredTeamNames.Contains(invite.TeamName.ToLower())), token))
            return BadRequest(new RequestResponse(
                "An active onboarding invitation already exists for one of the submitted teams or emails."));

        var results = new List<CaptainOnboardingCreatedModel>();
        var pendingInvites = new List<(CaptainOnboardingInvite Invite, string RawToken)>();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(Math.Max(1, model.ExpiresInHours));

        foreach (var entry in entries)
        {
            var rawToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var tokenHash = rawToken.ToSHA256String();

            var invite = new CaptainOnboardingInvite
            {
                GameId = gameIds[0],
                AdditionalGameIds = gameIds.Skip(1).ToArray(),
                TeamName = entry.TeamName,
                CaptainEmail = entry.CaptainEmail,
                TokenHash = tokenHash,
                ExpiresAtUtc = expiresAt,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            dbContext.CaptainOnboardingInvites.Add(invite);
            pendingInvites.Add((invite, rawToken));
        }

        await dbContext.SaveChangesAsync(token);

        foreach (var (invite, rawToken) in pendingInvites)
        {
            var onboardingUrl = BuildOnboardingUrl(rawToken);
            var gameTitles = games.Select(game => game.Title).ToArray();

            var emailQueued = mailSender.SendCaptainOnboardingUrl(
                invite.TeamName, invite.CaptainEmail, onboardingUrl, gameTitles, invite.ExpiresAtUtc,
                localizer, serviceProvider.GetRequiredService<IOptionsSnapshot<GlobalConfig>>());

            invite.LastSentAtUtc = DateTimeOffset.UtcNow;
            invite.SendCount = 1;
            invite.LastEmailQueued = emailQueued;

            results.Add(new CaptainOnboardingCreatedModel
            {
                InviteId = invite.Id,
                TeamName = invite.TeamName,
                CaptainEmail = invite.CaptainEmail,
                OnboardingUrl = onboardingUrl,
                EmailQueued = emailQueued,
                GameTitles = gameTitles
            });
        }

        await dbContext.SaveChangesAsync(token);

        logger.LogInformation("{Count} onboarding invites created for games {GameIds}",
            entries.Length, gameIds);

        return Ok(results);
    }

    private string BuildOnboardingUrl(string rawToken) =>
        $"{Request.Scheme}://{Request.Host}/onboarding?token={rawToken}";

    private static string[] GetOnboardingGameTitles(CaptainOnboardingInvite invite,
        IReadOnlyDictionary<int, string> gameTitles) =>
        invite.GetGameIds()
            .Distinct()
            .Where(gameTitles.ContainsKey)
            .Select(gameId => gameTitles[gameId])
            .ToArray();

    private static CaptainOnboardingStatus GetOnboardingStatus(CaptainOnboardingInvite invite,
        DateTimeOffset now)
    {
        if (invite.ConsumedAtUtc.HasValue)
            return CaptainOnboardingStatus.Redeemed;
        if (invite.RevokedAtUtc.HasValue)
            return CaptainOnboardingStatus.Revoked;
        if (invite.ExpiresAtUtc <= now)
            return CaptainOnboardingStatus.Expired;
        return invite.OpenedAtUtc.HasValue
            ? CaptainOnboardingStatus.Opened
            : CaptainOnboardingStatus.Pending;
    }

    #endregion

    private IActionResult HandleIdentityError(IEnumerable<IdentityError> errors) =>
        BadRequest(new RequestResponse(errors.FirstOrDefault()?.Description ??
                                       localizer[nameof(Resources.Program.Identity_UnknownError)]));
}
