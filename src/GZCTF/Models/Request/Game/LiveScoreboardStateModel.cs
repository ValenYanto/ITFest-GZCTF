using GZCTF.Models.Request.Edit;

namespace GZCTF.Models.Request.Game;

public class LiveScoreboardStateModel
{
    public int GameId { get; set; }
    public string GameTitle { get; set; } = string.Empty;
    public GameMode GameMode { get; set; }
    public bool ScoreboardFrozen { get; set; }
    public DateTimeOffset ServerTimeUtc { get; set; } = DateTimeOffset.UtcNow;
    public LiveScoreboardConfigModel Config { get; set; } = new();
    public SpeedrunStateModel SpeedrunState { get; set; } = new();
    public LiveScoreboardTeamModel[] TopTeams { get; set; } = [];
    public LiveScoreboardEventModel[] RecentEvents { get; set; } = [];
}

public class LiveScoreboardTeamModel
{
    public int Id { get; set; }
    public int Rank { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Score { get; set; }
    public int SolvedCount { get; set; }
}

public class LiveScoreboardEventModel
{
    public string Id { get; set; } = string.Empty;
    public NoticeType Type { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? TeamId { get; set; }
    public string? TeamName { get; set; }
    public string? ChallengeTitle { get; set; }
}
