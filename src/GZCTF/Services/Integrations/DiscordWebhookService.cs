using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using GZCTF.Models.Request.Edit;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Integrations;

public class DiscordWebhookService(
    HttpClient httpClient,
    AppDbContext context,
    ILogger<DiscordWebhookService> logger)
{
    private const int FirstBloodColor = 0xDC143C;
    private const int SecondBloodColor = 0xC0C0C0;
    private const int ThirdBloodColor = 0xCD7F32;
    private const int OtherBloodColor = 0x3498DB;

    public async Task SendBloodNotification(int submissionId, int firstSolveRank, CancellationToken token = default)
    {
        if (firstSolveRank < 1)
            return;

        try
        {
            await SendBloodNotificationCore(submissionId, token);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Discord blood notification failed for submission {SubmissionId}: {ExceptionType}.",
                submissionId, ex.GetType().Name);
        }
    }

    private async Task SendBloodNotificationCore(int submissionId, CancellationToken token)
    {
        var solve = await context.Submissions.AsNoTracking()
            .Where(s => s.Id == submissionId)
            .Select(s => new
            {
                s.Id,
                s.SubmitTimeUtc,
                Team = s.Team!.Name,
                Challenge = s.GameChallenge!.Title,
                s.GameChallenge.Category,
                s.GameChallenge.OriginalScore,
                s.GameChallenge.MinScoreRate,
                s.GameChallenge.Difficulty,
                Game = s.Game!.Title,
                s.Game.ScoreboardFrozen,
                s.Game.ScoreboardFreezeTimeUtc,
                s.Game.BloodNotificationEnabled,
                s.Game.BloodDiscordWebhookUrl,
                s.Game.BloodNotificationMaxRank,
                s.Game.BloodNotificationTemplate,
                s.Game.BloodNotificationEmbedTitleTemplate,
                s.Game.BloodNotificationEmbedDescriptionTemplate,
                s.Game.BloodNotificationEmbedColor,
                s.Game.BloodNotificationEmbedFieldsTemplate,
                s.Game.BloodNotificationEmbedFooterTemplate,
                s.Game.BloodNotificationTimeZone,
                s.Game.StartTimeUtc,
                s.Game.EndTimeUtc,
                s.ChallengeId,
                s.GameId
            })
            .SingleOrDefaultAsync(token);

        if (solve is null || !solve.BloodNotificationEnabled || string.IsNullOrWhiteSpace(solve.BloodDiscordWebhookUrl))
            return;

        if (solve.BloodNotificationMaxRank != 0 &&
            (solve.SubmitTimeUtc < solve.StartTimeUtc || solve.SubmitTimeUtc >= solve.EndTimeUtc))
            return;

        var acceptedTimeUtc = await context.FirstSolves.AsNoTracking()
            .Where(firstSolve => firstSolve.SubmissionId == submissionId)
            .Select(firstSolve => (DateTimeOffset?)firstSolve.AcceptedTimeUtc)
            .SingleOrDefaultAsync(token);

        var rank = await (
            from fs in context.FirstSolves.AsNoTracking()
            join submission in context.Submissions.AsNoTracking() on fs.SubmissionId equals submission.Id
            where fs.ChallengeId == solve.ChallengeId
                  && (solve.BloodNotificationMaxRank == 0 ||
                      submission.SubmitTimeUtc >= solve.StartTimeUtc && submission.SubmitTimeUtc < solve.EndTimeUtc)
                  && (submission.SubmitTimeUtc < solve.SubmitTimeUtc ||
                      submission.SubmitTimeUtc == solve.SubmitTimeUtc && submission.Id <= submissionId)
            select fs
        ).CountAsync(token);

        if (rank < 1 || solve.BloodNotificationMaxRank != 0 && rank > solve.BloodNotificationMaxRank)
            return;

        var solveCount = await context.FirstSolves.AsNoTracking().CountAsync(fs => fs.ChallengeId == solve.ChallengeId,
            token);
        var score = GameChallenge.CalculateChallengeScore(solve.OriginalScore, solve.MinScoreRate, solve.Difficulty,
            solveCount);

        var settings = new EmbedSettings(
            solve.BloodNotificationEmbedTitleTemplate,
            string.IsNullOrWhiteSpace(solve.BloodNotificationEmbedDescriptionTemplate)
                ? solve.BloodNotificationTemplate
                : solve.BloodNotificationEmbedDescriptionTemplate,
            solve.BloodNotificationEmbedColor,
            solve.BloodNotificationEmbedFieldsTemplate,
            solve.BloodNotificationEmbedFooterTemplate,
            solve.BloodNotificationTimeZone);
        var hideTeam = solve.ScoreboardFrozen &&
                       (solve.ScoreboardFreezeTimeUtc is null ||
                        acceptedTimeUtc is null ||
                        acceptedTimeUtc >= solve.ScoreboardFreezeTimeUtc);
        var values = new RenderValues(rank, hideTeam ? "Anonymous" : solve.Team, solve.Challenge,
            solve.Category.ToString(), score.ToString(), solve.Game, solve.SubmitTimeUtc, solve.Id, solve.ChallengeId,
            solve.GameId);

        await Send(solve.BloodDiscordWebhookUrl, new([BuildEmbed(settings, values)]), token);
    }

    public Task<bool> SendTestWebhook(string webhookUrl, BloodNotificationModel model, string gameTitle,
        CancellationToken token = default)
    {
        var settings = new EmbedSettings(
            model.EmbedTitleTemplate,
            string.IsNullOrWhiteSpace(model.EmbedDescriptionTemplate) ? model.Template : model.EmbedDescriptionTemplate,
            model.EmbedColor,
            model.EmbedFieldsTemplate,
            model.EmbedFooterTemplate,
            model.TimeZone);
        var values = new RenderValues(1, "test", "test3", "Misc", "1000", gameTitle, DateTimeOffset.UtcNow,
            1, 1, 1);

        return Send(webhookUrl, new([BuildEmbed(settings, values)]), token);
    }

    private DiscordEmbed BuildEmbed(EmbedSettings settings, RenderValues values)
    {
        var (emoji, blood, automaticColor) = values.Rank switch
        {
            1 => ("🥇", "FIRST", FirstBloodColor),
            2 => ("🥈", "SECOND", SecondBloodColor),
            3 => ("🥉", "THIRD", ThirdBloodColor),
            _ => ("🩸", $"#{values.Rank}", OtherBloodColor)
        };
        var time = FormatTime(values.TimeUtc, settings.TimeZone);
        var placeholders = new Dictionary<string, string>
        {
            ["{emoji}"] = emoji,
            ["{blood}"] = blood,
            ["{rank}"] = values.Rank.ToString(),
            ["{team}"] = values.Team,
            ["{challenge}"] = values.Challenge,
            ["{category}"] = values.Category,
            ["{score}"] = values.Score,
            ["{game}"] = values.Game,
            ["{time}"] = time,
            ["{submissionId}"] = values.SubmissionId.ToString(),
            ["{challengeId}"] = values.ChallengeId.ToString(),
            ["{gameId}"] = values.GameId.ToString()
        };

        var color = automaticColor;
        if (!string.IsNullOrWhiteSpace(settings.Color) &&
            int.TryParse(settings.Color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var custom))
            color = custom;

        return new(
            Truncate(Render(settings.Title, placeholders), 256),
            Truncate(Render(settings.Description, placeholders), 4096),
            color,
            ParseFields(settings.Fields, placeholders),
            new(Truncate(Render(settings.Footer, placeholders), 2048)),
            values.TimeUtc);
    }

    private string FormatTime(DateTimeOffset utcTime, string timeZoneId)
    {
        try
        {
            var time = TimeZoneInfo.ConvertTime(utcTime, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
            return timeZoneId == Game.DefaultBloodNotificationTimeZone
                ? time.ToString("yyyy-MM-dd HH:mm:ss 'WIB'")
                : time.ToString("yyyy-MM-dd HH:mm:ss zzz");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning("Blood notification time zone {TimeZone} is unavailable; falling back to UTC.", timeZoneId);
            return utcTime.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
        }
    }

    private static DiscordEmbedField[] ParseFields(string template, IReadOnlyDictionary<string, string> placeholders)
    {
        List<DiscordEmbedField> fields = [];
        foreach (var line in template.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Take(25))
        {
            var parts = line.Split('|');
            if (parts.Length is < 2 or > 3 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                continue;

            fields.Add(new(
                Truncate(Render(parts[0].Trim(), placeholders), 256),
                Truncate(Render(parts[1].Trim(), placeholders), 1024),
                parts.Length < 3 || !bool.TryParse(parts[2].Trim(), out var inline) || inline));
        }

        return fields.ToArray();
    }

    private static string Render(string template, IReadOnlyDictionary<string, string> placeholders)
    {
        foreach (var (placeholder, value) in placeholders)
            template = template.Replace(placeholder, value, StringComparison.Ordinal);
        return template;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        var length = maxLength;
        if (char.IsHighSurrogate(value[length - 1]))
            length--;
        return value[..length];
    }

    private async Task<bool> Send(string webhookUrl, DiscordWebhookPayload payload, CancellationToken token)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(webhookUrl, payload, token);
            if (response.IsSuccessStatusCode)
                return true;

            logger.LogWarning("Discord webhook request failed with status code {StatusCode}.",
                (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Discord webhook request failed: {ExceptionType}.", ex.GetType().Name);
        }

        return false;
    }

    private sealed record EmbedSettings(string Title, string Description, string? Color, string Fields, string Footer,
        string TimeZone);

    private sealed record RenderValues(int Rank, string Team, string Challenge, string Category, string Score,
        string Game, DateTimeOffset TimeUtc, int SubmissionId, int ChallengeId, int GameId);

    private sealed record DiscordWebhookPayload(
        [property: JsonPropertyName("embeds")] DiscordEmbed[] Embeds);

    private sealed record DiscordEmbed(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("color")] int Color,
        [property: JsonPropertyName("fields")] DiscordEmbedField[] Fields,
        [property: JsonPropertyName("footer")] DiscordEmbedFooter Footer,
        [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp);

    private sealed record DiscordEmbedField(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("inline")] bool Inline);

    private sealed record DiscordEmbedFooter(
        [property: JsonPropertyName("text")] string Text);
}
