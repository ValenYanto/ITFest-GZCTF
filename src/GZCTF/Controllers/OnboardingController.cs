using System.Data;
using System.Net.Mime;
using GZCTF.Extensions;
using GZCTF.Middlewares;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Admin;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Config;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GZCTF.Controllers;

/// <summary>
/// Public, token-protected captain onboarding APIs.
/// Normal account registration and team invitation APIs are intentionally separate.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
public class OnboardingController(
    AppDbContext dbContext,
    UserManager<UserInfo> userManager,
    SignInManager<UserInfo> signInManager,
    IConfigService configService,
    IGameRepository gameRepository,
    ILogger<OnboardingController> logger,
    IStringLocalizer<Program> localizer) : ControllerBase
{
    [HttpGet("{token}")]
    [ProducesResponseType(typeof(CaptainOnboardingInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInfo([FromRoute] string token, CancellationToken cancellationToken)
    {
        var invite = await FindInvite(token, cancellationToken);
        if (invite is null)
            return NotFound(new RequestResponse("Invalid onboarding link.", StatusCodes.Status404NotFound));

        if (invite.ConsumedAtUtc.HasValue)
            return BadRequest(new RequestResponse("This onboarding link has already been used."));

        if (invite.RevokedAtUtc.HasValue)
            return BadRequest(new RequestResponse("This onboarding link has been revoked by the committee."));

        if (invite.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            return BadRequest(new RequestResponse("This onboarding link has expired."));

        if (!invite.OpenedAtUtc.HasValue)
        {
            invite.OpenedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var titles = await GetGameTitles(invite, cancellationToken);

        return Ok(new CaptainOnboardingInfoModel
        {
            TeamName = invite.TeamName,
            CaptainEmail = invite.CaptainEmail,
            GameTitle = titles.FirstOrDefault() ?? string.Empty,
            GameTitles = titles,
            ExpiresAtUtc = invite.ExpiresAtUtc
        });
    }

    [HttpPost("{token}/Redeem")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Register))]
    [ProducesResponseType(typeof(CaptainOnboardingRedeemResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Redeem([FromRoute] string token,
        [FromBody] CaptainOnboardingRedeemModel model, CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var invite = await FindInvite(token, cancellationToken);
        if (invite is null)
            return NotFound(new RequestResponse("Invalid onboarding link.", StatusCodes.Status404NotFound));

        if (invite.ConsumedAtUtc.HasValue || invite.TeamId.HasValue)
            return BadRequest(new RequestResponse("This onboarding link has already been used."));

        if (invite.RevokedAtUtc.HasValue)
            return BadRequest(new RequestResponse("This onboarding link has been revoked by the committee."));

        if (invite.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            return BadRequest(new RequestResponse("This onboarding link has expired."));

        if (await userManager.FindByEmailAsync(invite.CaptainEmail) is not null)
            return BadRequest(new RequestResponse(
                "An account already exists for this captain email. Contact the committee for assistance."));

        if (await dbContext.Teams.AnyAsync(team => team.Name.ToLower() == invite.TeamName.ToLower(),
                cancellationToken))
            return BadRequest(new RequestResponse(
                "A team with this name already exists. Contact the committee for assistance."));

        var password = configService.DecryptApiData(model.Password);
        if (string.IsNullOrWhiteSpace(password))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Model_PasswordRequired)]));

        var now = DateTimeOffset.UtcNow;
        var user = new UserInfo
        {
            UserName = model.UserName.Trim(),
            Email = invite.CaptainEmail,
            Role = Role.User,
            EmailConfirmed = true,
            RegisterTimeUtc = now,
            LastSignedInUtc = now
        };
        user.UpdateByHttpContext(HttpContext);

        var identityResult = await userManager.CreateAsync(user, password);
        if (!identityResult.Succeeded)
            return BadRequest(new RequestResponse(identityResult.Errors.FirstOrDefault()?.Description ??
                                                   localizer[nameof(Resources.Program.Identity_UnknownError)]));

        var team = new Team
        {
            Name = invite.TeamName,
            Captain = user,
            Locked = false
        };
        team.Members.Add(user);
        dbContext.Teams.Add(team);
        await dbContext.SaveChangesAsync(cancellationToken);

        var gameIds = invite.GetGameIds().Distinct().ToArray();
        var games = await dbContext.Games
            .Where(game => gameIds.Contains(game.Id))
            .ToArrayAsync(cancellationToken);

        if (games.Length != gameIds.Length)
            return BadRequest(new RequestResponse(
                "One or more games assigned to this onboarding link no longer exist."));

        foreach (var game in games)
        {
            dbContext.Participations.Add(new Participation
            {
                GameId = game.Id,
                TeamId = team.Id,
                Status = ParticipationStatus.Accepted,
                AcceptedTimeUtc = now,
                WhitelistSource = WhitelistSource.BulkOnboarding,
                Token = gameRepository.GetToken(game, team),
                Division = null
            });
        }

        invite.ConsumedAtUtc = now;
        invite.TeamId = team.Id;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await signInManager.SignInAsync(user, true);

        var orderedTitles = gameIds
            .Select(id => games.First(game => game.Id == id).Title)
            .ToArray();

        logger.LogInformation(
            "Captain {UserId} redeemed onboarding for team {TeamId} and games {GameIds}",
            user.Id, team.Id, gameIds);

        return Ok(new CaptainOnboardingRedeemResultModel
        {
            TeamId = team.Id,
            TeamName = team.Name,
            InviteCode = team.InviteCode,
            GameTitles = orderedTitles
        });
    }

    private Task<CaptainOnboardingInvite?> FindInvite(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
            return Task.FromResult<CaptainOnboardingInvite?>(null);

        var tokenHash = token.ToSHA256String();
        return dbContext.CaptainOnboardingInvites
            .FirstOrDefaultAsync(invite => invite.TokenHash == tokenHash, cancellationToken);
    }

    private async Task<string[]> GetGameTitles(CaptainOnboardingInvite invite,
        CancellationToken cancellationToken)
    {
        var gameIds = invite.GetGameIds().Distinct().ToArray();
        var games = await dbContext.Games
            .Where(game => gameIds.Contains(game.Id))
            .Select(game => new { game.Id, game.Title })
            .ToArrayAsync(cancellationToken);

        return gameIds
            .Select(id => games.FirstOrDefault(game => game.Id == id)?.Title)
            .Where(title => title is not null)
            .Cast<string>()
            .ToArray();
    }
}
