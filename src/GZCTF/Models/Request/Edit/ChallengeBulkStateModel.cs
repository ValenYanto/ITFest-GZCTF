using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Edit;

public class ChallengeBulkStateModel
{
    /// <summary>
    /// Challenge IDs to update. An empty list means all challenges in the game.
    /// </summary>
    public int[] ChallengeIds { get; set; } = [];

    [Required]
    public bool IsEnabled { get; set; }
}

public class ChallengeLifecycleFailureModel
{
    public int ChallengeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class ChallengeBulkStateResultModel
{
    public int[] ChangedChallengeIds { get; set; } = [];
    public int CreatedInstanceCount { get; set; }
    public ChallengeLifecycleFailureModel[] Failures { get; set; } = [];
}
