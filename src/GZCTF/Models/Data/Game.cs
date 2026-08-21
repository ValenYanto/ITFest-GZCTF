using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using GZCTF.Models.Request.Edit;
using MemoryPack;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Encoders;

namespace GZCTF.Models.Data;

[MemoryPackable]
public partial class Game
{
    public const string DefaultBloodNotificationTemplate =
        "Challenge **{challenge}** has been blooded!";
    public const string DefaultBloodNotificationEmbedTitleTemplate = "{emoji} {blood} BLOOD!";
    public const string DefaultBloodNotificationEmbedDescriptionTemplate =
        "**{team}** conquered **{challenge}** and claimed rank **#{rank}**!";
    public const string DefaultBloodNotificationEmbedFieldsTemplate =
        "👤 User / Team|{team}|true\n" +
        "🏁 Challenge|{challenge}|true\n" +
        "📂 Category|{category}|true\n" +
        "💯 Points|{score}|true\n" +
        "🎮 Game|{game}|true\n" +
        "🏆 Rank|#{rank}|true";
    public const string DefaultBloodNotificationEmbedFooterTemplate = "Solved at {time} • ITFest CTF";
    public const string DefaultBloodNotificationTimeZone = "Asia/Jakarta";
    public const string DefaultSpeedrunEmergencyHintText =
        "Overtime unlocked! Unsolved challenges remain available for 5 more minutes.";

    [Key]
    [Required]
    public int Id { get; set; }

    /// <summary>
    /// Game title
    /// </summary>
    [Required]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Token signature public key
    /// </summary>
    [Required]
    [MaxLength(Limits.GameKeyLength)]
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Token signature private key
    /// </summary>
    [Required]
    [MaxLength(Limits.GameKeyLength)]
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>
    /// Whether to hide
    /// </summary>
    [Required]
    public bool Hidden { get; set; }

    /// <summary>
    /// Whether the game is in practice mode (most operations can still be performed after the game ends)
    /// </summary>
    public bool PracticeMode { get; set; } = true;

    public GameMode Mode { get; set; } = GameMode.Jeopardy;
    public int SpeedrunDefaultRoundDurationMinutes { get; set; } = 30;
    public int SpeedrunOvertimeMinutes { get; set; } = 5;
    public int SpeedrunDefaultRoundDurationSeconds { get; set; } = 1800;
    public int SpeedrunOvertimeSeconds { get; set; } = 300;
    public bool SpeedrunAllowManualExtend { get; set; } = true;
    public bool SpeedrunHideInactiveChallenges { get; set; } = true;
    public bool SpeedrunEmergencyHintEnabled { get; set; } = true;
    [MaxLength(1000)]
    public string SpeedrunEmergencyHintText { get; set; } = DefaultSpeedrunEmergencyHintText;
    public bool ScoreboardFrozen { get; set; }
    public DateTimeOffset? ScoreboardFreezeTimeUtc { get; set; }

    /// <summary>
    /// Poster hash
    /// </summary>
    [MaxLength(Limits.FileHashLength)]
    public string? PosterHash { get; set; }

    /// <summary>
    /// Game description
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Detailed introduction of the game
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Teams can join without review
    /// </summary>
    public bool AcceptWithoutReview { get; set; }

    /// <summary>
    /// Only pre-approved teams may join this game
    /// </summary>
    public bool WhitelistOnly { get; set; }

    /// <summary>
    /// Whether writeup is required
    /// </summary>
    public bool WriteupRequired { get; set; }

    /// <summary>
    /// Game invitation code
    /// </summary>
    [MaxLength(Limits.InviteTokenLength)]
    public string? InviteCode { get; set; }

    /// <summary>
    /// Limit on the number of team members, 0 means no limit
    /// </summary>
    public int TeamMemberCountLimit { get; set; }

    /// <summary>
    /// Limit on the number of containers a team can have simultaneously
    /// </summary>
    public int ContainerCountLimit { get; set; } = 3;

    /// <summary>
    /// Start time
    /// </summary>
    [Required]
    [JsonPropertyName("start")]
    public DateTimeOffset StartTimeUtc { get; set; } = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>
    /// End time
    /// </summary>
    [Required]
    [JsonPropertyName("end")]
    public DateTimeOffset EndTimeUtc { get; set; } = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>
    /// Writeup submission deadline
    /// </summary>
    [Required]
    public DateTimeOffset WriteupDeadline { get; set; } = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>
    /// Additional notes for writeup
    /// </summary>
    [Required]
    public string WriteupNote { get; set; } = string.Empty;

    [JsonIgnore]
    [Column(nameof(BloodBonus))]
    public long BloodBonusValue { get; set; } = BloodBonus.DefaultValue;

    /// <summary>
    /// Blood bonus
    /// </summary>
    [NotMapped]
    [Required]
    [MemoryPackIgnore]
    public BloodBonus BloodBonus
    {
        get => BloodBonus.FromValue(BloodBonusValue);
        set => BloodBonusValue = value.Val;
    }

    /// <summary>
    /// Whether Discord blood notifications are enabled
    /// </summary>
    public bool BloodNotificationEnabled { get; set; }

    /// <summary>
    /// Discord webhook URL used for blood notifications
    /// </summary>
    [MaxLength(512)]
    public string? BloodDiscordWebhookUrl { get; set; }

    /// <summary>
    /// Maximum first-solve rank to notify, or 0 for all first-solves
    /// </summary>
    public int BloodNotificationMaxRank { get; set; } = 1;

    /// <summary>
    /// Discord blood notification embed description template
    /// </summary>
    [MaxLength(2000)]
    public string BloodNotificationTemplate { get; set; } = DefaultBloodNotificationTemplate;

    [MaxLength(512)]
    public string BloodNotificationEmbedTitleTemplate { get; set; } = DefaultBloodNotificationEmbedTitleTemplate;

    [MaxLength(2000)]
    public string BloodNotificationEmbedDescriptionTemplate { get; set; } =
        DefaultBloodNotificationEmbedDescriptionTemplate;

    [MaxLength(16)]
    public string? BloodNotificationEmbedColor { get; set; }

    [MaxLength(4000)]
    public string BloodNotificationEmbedFieldsTemplate { get; set; } = DefaultBloodNotificationEmbedFieldsTemplate;

    [MaxLength(512)]
    public string BloodNotificationEmbedFooterTemplate { get; set; } = DefaultBloodNotificationEmbedFooterTemplate;

    [MaxLength(64)]
    public string BloodNotificationTimeZone { get; set; } = DefaultBloodNotificationTimeZone;

    /// <summary>
    /// Whether the game is active
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    [MemoryPackIgnore]
    public bool IsActive => StartTimeUtc <= DateTimeOffset.Now && DateTimeOffset.Now <= EndTimeUtc;

    /// <summary>
    /// Poster URL
    /// </summary>
    [NotMapped]
    [MemoryPackIgnore]
    public string? PosterUrl => GetPosterUrl(PosterHash);

    /// <summary>
    /// Team hash salt
    /// </summary>
    [NotMapped]
    [MemoryPackIgnore]
    public string TeamHashSalt => $"GZCTF@{PrivateKey}@PK".ToSHA256String();

    internal static string? GetPosterUrl(string? hash) => hash is null ? null : $"/assets/{hash}/poster";

    internal void GenerateKeyPair(byte[]? xorKey)
    {
        SecureRandom sr = new();
        Ed25519KeyPairGenerator kpg = new();
        kpg.Init(new Ed25519KeyGenerationParameters(sr));
        var kp = kpg.GenerateKeyPair();
        var privateKey = (Ed25519PrivateKeyParameters)kp.Private;
        var publicKey = (Ed25519PublicKeyParameters)kp.Public;

        PrivateKey =
            Base64.ToBase64String(xorKey is null
                ? privateKey.GetEncoded()
                : Codec.Xor(privateKey.GetEncoded(), xorKey));

        PublicKey = Base64.ToBase64String(publicKey.GetEncoded());
    }

    internal string Sign(string str, byte[]? xorKey)
    {
        Ed25519PrivateKeyParameters privateKey;
        if (xorKey is null)
            privateKey = new(Codec.Base64.DecodeToBytes(PrivateKey), 0);
        else
            privateKey = new(Codec.Xor(Codec.Base64.DecodeToBytes(PrivateKey), xorKey), 0);

        return CryptoUtils.GenerateSignature(str, privateKey, SignAlgorithm.Ed25519);
    }

    internal Game Update(GameInfoModel model)
    {
        Title = model.Title;
        Content = model.Content;
        Summary = model.Summary;
        Hidden = model.Hidden;
        PracticeMode = model.PracticeMode;
        AcceptWithoutReview = model.AcceptWithoutReview;
        WhitelistOnly = model.WhitelistOnly;
        InviteCode = model.InviteCode;
        EndTimeUtc = model.EndTimeUtc;
        StartTimeUtc = model.StartTimeUtc;
        TeamMemberCountLimit = model.TeamMemberCountLimit;
        ContainerCountLimit = model.ContainerCountLimit;
        WriteupNote = model.WriteupNote;
        WriteupRequired = model.WriteupRequired;
        WriteupDeadline = model.WriteupDeadline;
        BloodBonus = BloodBonus.FromValue(model.BloodBonusValue);
        Mode = model.Mode;
        SpeedrunDefaultRoundDurationSeconds =
            model.SpeedrunDefaultRoundDurationSeconds ?? model.SpeedrunDefaultRoundDurationMinutes * 60;
        SpeedrunOvertimeSeconds = model.SpeedrunOvertimeSeconds ?? model.SpeedrunOvertimeMinutes * 60;
        SpeedrunDefaultRoundDurationMinutes = SpeedrunDefaultRoundDurationSeconds / 60;
        SpeedrunOvertimeMinutes = SpeedrunOvertimeSeconds / 60;

        return this;
    }

    internal Game Update(BloodNotificationModel model)
    {
        BloodNotificationEnabled = model.Enabled;
        BloodDiscordWebhookUrl = string.IsNullOrWhiteSpace(model.DiscordWebhookUrl)
            ? null
            : model.DiscordWebhookUrl.Trim();
        BloodNotificationMaxRank = model.MaxRank;
        BloodNotificationTemplate = model.Template ?? DefaultBloodNotificationTemplate;
        BloodNotificationEmbedTitleTemplate = string.IsNullOrWhiteSpace(model.EmbedTitleTemplate)
            ? DefaultBloodNotificationEmbedTitleTemplate
            : model.EmbedTitleTemplate;
        BloodNotificationEmbedDescriptionTemplate = model.EmbedDescriptionTemplate ?? string.Empty;
        BloodNotificationEmbedColor = string.IsNullOrWhiteSpace(model.EmbedColor) ? null : model.EmbedColor.Trim();
        BloodNotificationEmbedFieldsTemplate = model.EmbedFieldsTemplate ?? string.Empty;
        BloodNotificationEmbedFooterTemplate = string.IsNullOrWhiteSpace(model.EmbedFooterTemplate)
            ? DefaultBloodNotificationEmbedFooterTemplate
            : model.EmbedFooterTemplate;
        BloodNotificationTimeZone = string.IsNullOrWhiteSpace(model.TimeZone)
            ? DefaultBloodNotificationTimeZone
            : model.TimeZone.Trim();

        return this;
    }

    #region Db Relationship

    /// <summary>
    /// Game events
    /// </summary>
    [JsonIgnore]
    public List<GameEvent> GameEvents { get; set; } = [];

    /// <summary>
    /// Game notices
    /// </summary>
    [JsonIgnore]
    public List<GameNotice> GameNotices { get; set; } = [];

    /// <summary>
    /// Game submissions
    /// </summary>
    [JsonIgnore]
    public List<Submission> Submissions { get; set; } = [];

    /// <summary>
    /// Game challenges
    /// </summary>
    [JsonIgnore]
    public HashSet<GameChallenge> Challenges { get; set; } = [];

    /// <summary>
    /// Game participations
    /// </summary>
    [JsonIgnore]
    public HashSet<Participation> Participations { get; set; } = [];

    /// <summary>
    /// Game teams
    /// </summary>
    [JsonIgnore]
    public HashSet<Team>? Teams { get; set; }

    /// <summary>
    /// List of divisions for the game
    /// </summary>
    public HashSet<Division>? Divisions { get; set; }
    public List<SpeedrunCategory> SpeedrunCategories { get; set; } = [];
    public List<SpeedrunRound> SpeedrunRounds { get; set; } = [];
    public List<SpeedrunHintReleaseLog> SpeedrunHintReleaseLogs { get; set; } = [];
    [MemoryPackIgnore]
    public GameLiveScoreboardConfig? LiveScoreboardConfig { get; set; }

    #endregion Db Relationship
}
