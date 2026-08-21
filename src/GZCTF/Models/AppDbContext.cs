using System.Text.Json;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GZCTF.Models;

public class AppDbContext(DbContextOptions<AppDbContext> options) :
    IdentityDbContext<UserInfo, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        TypeInfoResolver = new AppJsonSerializerContext()
    };

    public DbSet<Post> Posts { get; set; } = null!;
    public DbSet<Game> Games { get; set; } = null!;
    public DbSet<Team> Teams { get; set; } = null!;
    public DbSet<Config> Configs { get; set; } = null!;
    public DbSet<LogModel> Logs { get; set; } = null!;
    public DbSet<Division> Divisions { get; set; } = null!;
    public DbSet<LocalFile> Files { get; set; } = null!;
    public DbSet<CheatInfo> CheatInfo { get; set; } = null!;
    public DbSet<Container> Containers { get; set; } = null!;
    public DbSet<GameEvent> GameEvents { get; set; } = null!;
    public DbSet<Submission> Submissions { get; set; } = null!;
    public DbSet<Attachment> Attachments { get; set; } = null!;
    public DbSet<GameNotice> GameNotices { get; set; } = null!;
    public DbSet<FlagContext> FlagContexts { get; set; } = null!;
    public DbSet<Participation> Participations { get; set; } = null!;
    public DbSet<GameInstance> GameInstances { get; set; } = null!;
    public DbSet<GameChallenge> GameChallenges { get; set; } = null!;
    public DbSet<FirstSolve> FirstSolves { get; set; } = null!;
    public DbSet<SpeedrunCategory> SpeedrunCategories { get; set; } = null!;
    public DbSet<SpeedrunRound> SpeedrunRounds { get; set; } = null!;
    public DbSet<SpeedrunHintReleaseLog> SpeedrunHintReleaseLogs { get; set; } = null!;
    public DbSet<GameLiveScoreboardConfig> GameLiveScoreboardConfigs { get; set; } = null!;
    public DbSet<WhitelistJoinAttempt> WhitelistJoinAttempts { get; set; } = null!;
    public DbSet<CaptainOnboardingInvite> CaptainOnboardingInvites { get; set; } = null!;
    public DbSet<ExerciseInstance> ExerciseInstances { get; set; } = null!;
    public DbSet<ExerciseChallenge> ExerciseChallenges { get; set; } = null!;
    public DbSet<UserParticipation> UserParticipations { get; set; } = null!;
    public DbSet<ExerciseDependency> ExerciseDependencies { get; set; } = null!;
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<ApiToken> ApiTokens { get; set; } = null!;

    private static ValueConverter<T?, string> GetJsonConverter<T>() where T : class, new() =>
        new(
            v => JsonSerializer.Serialize(v ?? new(), JsonOptions),
            v => JsonSerializer.Deserialize<T>(v, JsonOptions)
        );

    private static ValueComparer<TList> GetEnumerableComparer<TList, T>()
        where T : notnull
        where TList : IEnumerable<T>, new() =>
        new(
            (c1, c2) => (c1 == null && c2 == null) || (c2 != null && c1 != null && c1.SequenceEqual(c2)),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // var setConverter = GetJsonConverter<HashSet<string>>();
        // var setComparer = GetEnumerableComparer<HashSet<string>, string>();
        var listConverter = GetJsonConverter<List<string>>();
        var listComparer = GetEnumerableComparer<List<string>, string>();
        var intListConverter = GetJsonConverter<List<int>>();
        var intListComparer = GetEnumerableComparer<List<int>, int>();

        builder.Entity<UserInfo>(entity =>
        {
            entity.Property(e => e.Role)
                .HasConversion<int>();

            entity.Property(e => e.UserName)
                .HasMaxLength(16);

            entity.Property(e => e.ExerciseVisible)
                .HasDefaultValue(true);

            entity.HasMany(e => e.Submissions)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Game>(entity =>
        {
            entity.Property(e => e.Mode).HasConversion<byte>().HasDefaultValue(GameMode.Jeopardy);
            entity.Property(e => e.WhitelistOnly).HasDefaultValue(false);
            entity.Property(e => e.SpeedrunDefaultRoundDurationMinutes).HasDefaultValue(30);
            entity.Property(e => e.SpeedrunOvertimeMinutes).HasDefaultValue(5);
            entity.Property(e => e.SpeedrunDefaultRoundDurationSeconds).HasDefaultValue(1800);
            entity.Property(e => e.SpeedrunOvertimeSeconds).HasDefaultValue(300);
            entity.Property(e => e.SpeedrunAllowManualExtend).HasDefaultValue(true);
            entity.Property(e => e.SpeedrunHideInactiveChallenges).HasDefaultValue(true);
            entity.Property(e => e.SpeedrunEmergencyHintEnabled).HasDefaultValue(true);
            entity.Property(e => e.SpeedrunEmergencyHintText).HasDefaultValue(Game.DefaultSpeedrunEmergencyHintText);
            entity.Property(e => e.ScoreboardFrozen).HasDefaultValue(false);
            entity.Property(e => e.BloodNotificationEnabled)
                .HasDefaultValue(false);

            entity.Property(e => e.BloodNotificationMaxRank)
                .HasDefaultValue(1);

            entity.Property(e => e.BloodNotificationTemplate)
                .HasDefaultValue(Game.DefaultBloodNotificationTemplate);

            entity.Property(e => e.BloodNotificationEmbedTitleTemplate)
                .HasDefaultValue(Game.DefaultBloodNotificationEmbedTitleTemplate);

            entity.Property(e => e.BloodNotificationEmbedDescriptionTemplate)
                .HasDefaultValue(Game.DefaultBloodNotificationEmbedDescriptionTemplate);

            entity.Property(e => e.BloodNotificationEmbedFieldsTemplate)
                .HasDefaultValue(Game.DefaultBloodNotificationEmbedFieldsTemplate);

            entity.Property(e => e.BloodNotificationEmbedFooterTemplate)
                .HasDefaultValue(Game.DefaultBloodNotificationEmbedFooterTemplate);

            entity.Property(e => e.BloodNotificationTimeZone)
                .HasDefaultValue(Game.DefaultBloodNotificationTimeZone);

            entity.HasMany(e => e.GameEvents)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.GameNotices)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.Challenges)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.Submissions)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.Divisions)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.SpeedrunCategories)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.SpeedrunRounds)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.SpeedrunHintReleaseLogs)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasOne(e => e.LiveScoreboardConfig)
                .WithOne(e => e.Game)
                .HasForeignKey<GameLiveScoreboardConfig>(e => e.GameId);

            entity.HasMany(e => e.Teams)
                .WithMany(e => e.Games)
                .UsingEntity<Participation>(
                    e => e.HasOne(p => p.Team)
                        .WithMany(t => t.Participations)
                        .HasForeignKey(p => p.TeamId),
                    e => e.HasOne(p => p.Game)
                        .WithMany(g => g.Participations)
                        .HasForeignKey(p => p.GameId)
                );
        });

        builder.Entity<SpeedrunCategory>(entity =>
        {
            entity.Property(e => e.Category).HasConversion<byte>();
            entity.Property(e => e.Included).HasDefaultValue(true);
            entity.HasIndex(e => new { e.GameId, e.Category }).IsUnique();
        });

        builder.Entity<SpeedrunRound>(entity =>
        {
            entity.Property(e => e.Category).HasConversion<byte>();
            entity.Property(e => e.Status).HasConversion<byte>();
            entity.HasIndex(e => new { e.GameId, e.Status });
        });

        builder.Entity<SpeedrunHintReleaseLog>(entity =>
        {
            entity.HasOne(e => e.Round)
                .WithMany()
                .HasForeignKey(e => e.RoundId);
            entity.HasOne(e => e.Challenge)
                .WithMany()
                .HasForeignKey(e => e.ChallengeId);
            entity.HasIndex(e => new { e.RoundId, e.ChallengeId, e.HintIndex }).IsUnique();
        });

        builder.Entity<GameLiveScoreboardConfig>(entity =>
        {
            entity.Property(e => e.VisualIntensity).HasConversion<byte>();
            entity.Property(e => e.Title).HasDefaultValue("ITFest Live Scoreboard");
            entity.Property(e => e.SoundEnabled).HasDefaultValue(true);
            entity.Property(e => e.Volume).HasDefaultValue(0.75);
        });

        builder.Entity<WhitelistJoinAttempt>(entity =>
        {
            entity.HasOne(e => e.Game).WithMany().HasForeignKey(e => e.GameId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Team).WithMany().HasForeignKey(e => e.TeamId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<CaptainOnboardingInvite>(entity =>
        {
            entity.HasOne(e => e.Game).WithMany().HasForeignKey(e => e.GameId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Team).WithMany().HasForeignKey(e => e.TeamId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Post>(entity =>
        {
            entity.HasOne(e => e.Author)
                .WithMany()
                .HasForeignKey(e => e.AuthorId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(e => e.Tags)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.Navigation(e => e.Author).AutoInclude();
        });

        builder.Entity<Team>(entity =>
        {
            entity.HasMany(e => e.Members)
                .WithMany(e => e.Teams);

            entity.HasOne(e => e.Captain)
                .WithMany()
                .HasForeignKey(e => e.CaptainId);

            entity.HasOne(e => e.Captain)
                .WithMany()
                .HasForeignKey(e => e.CaptainId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Participation>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<int>();
            entity.Property(e => e.WhitelistSource)
                .HasConversion<byte>()
                .HasDefaultValue(WhitelistSource.None);

            entity.HasMany(e => e.Instances).WithOne();

            entity.HasMany(e => e.Submissions)
                .WithOne(e => e.Participation)
                .HasForeignKey(e => e.ParticipationId);

            entity.HasMany(e => e.FirstSolves)
                .WithOne(e => e.Participation)
                .HasForeignKey(e => e.ParticipationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Members)
                .WithOne(e => e.Participation)
                .HasForeignKey(e => e.ParticipationId);

            entity.HasOne(e => e.Writeup)
                .WithMany();

            entity.HasOne(e => e.Division)
                .WithMany()
                .HasForeignKey(e => e.DivisionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.Game).AutoInclude();
            entity.Navigation(e => e.Team).AutoInclude();
            entity.Navigation(e => e.Members).AutoInclude();
            entity.Navigation(e => e.Writeup).AutoInclude();

            entity.HasMany(e => e.Challenges)
                .WithMany(e => e.Teams)
                .UsingEntity<GameInstance>(
                    e => e.HasOne(i => i.Challenge)
                        .WithMany(c => c.Instances)
                        .HasForeignKey(i => i.ChallengeId),
                    e => e.HasOne(i => i.Participation)
                        .WithMany(p => p.Instances)
                        .HasForeignKey(i => i.ParticipationId)
                        .OnDelete(DeleteBehavior.Cascade),
                    e => e.HasKey(i => new { i.ChallengeId, i.ParticipationId })
                );
        });

        builder.Entity<GameInstance>(entity =>
        {
            entity.HasOne(e => e.FlagContext)
                .WithMany()
                .HasForeignKey(e => e.FlagId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Container)
                .WithOne(e => e.GameInstance)
                .HasForeignKey<Container>(e => e.GameInstanceId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.Navigation(e => e.Container).AutoInclude();
            entity.Navigation(e => e.Challenge).AutoInclude();
        });

        builder.Entity<ExerciseInstance>(entity =>
        {
            entity.HasOne(e => e.Container)
                .WithOne(e => e.ExerciseInstance)
                .HasForeignKey<Container>(e => e.ExerciseInstanceId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.FlagContext)
                .WithMany()
                .HasForeignKey(e => e.FlagId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.Container).AutoInclude();
            entity.Navigation(e => e.Exercise).AutoInclude();
        });

        builder.Entity<Container>(entity =>
        {
            entity.HasOne(e => e.GameInstance)
                .WithOne(e => e.Container)
                .HasForeignKey<GameInstance>(e => e.ContainerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ExerciseInstance)
                .WithOne(e => e.Container)
                .HasForeignKey<ExerciseInstance>(e => e.ContainerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<GameChallenge>(entity =>
        {
            entity.Property(e => e.RequireSolverUpload).HasDefaultValue(false);

            entity.Property(e => e.Hints)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.Property(e => e.SpeedrunHintReleaseMinutes)
                .HasConversion(intListConverter)
                .Metadata
                .SetValueComparer(intListComparer);

            entity.Property(e => e.SpeedrunHintReleaseSeconds)
                .HasConversion(intListConverter)
                .Metadata
                .SetValueComparer(intListComparer);

            entity.Property(e => e.NetworkMode)
                .HasConversion<byte>()
                .HasDefaultValue(NetworkMode.Open);

            entity.HasMany(e => e.Flags)
                .WithOne(e => e.Challenge)
                .HasForeignKey(e => e.ChallengeId);

            entity.HasMany(e => e.Submissions)
                .WithOne(e => e.GameChallenge)
                .HasForeignKey(e => e.ChallengeId);

            entity.HasMany(e => e.FirstSolves)
                .WithOne(e => e.Challenge)
                .HasForeignKey(e => e.ChallengeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Attachment)
                .WithMany()
                .HasForeignKey(e => e.AttachmentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.TestContainer)
                .WithMany()
                .HasForeignKey(e => e.TestContainerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.Attachment).AutoInclude();
            entity.Navigation(e => e.TestContainer).AutoInclude();

            entity.HasIndex(e => e.GameId);

            entity.HasMany(e => e.DivisionConfigs)
                .WithOne(e => e.Challenge)
                .HasForeignKey(e => e.ChallengeId);
        });

        builder.Entity<Division>(entity =>
        {
            entity.HasMany(e => e.ChallengeConfigs)
                .WithOne(e => e.Division)
                .HasForeignKey(e => e.DivisionId);
        });

        builder.Entity<ExerciseChallenge>(entity =>
        {
            entity.Property(e => e.Hints)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.Property(e => e.NetworkMode)
                .HasConversion<byte>()
                .HasDefaultValue(NetworkMode.Open);

            entity.HasOne(e => e.Attachment)
                .WithMany()
                .HasForeignKey(e => e.AttachmentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(e => e.Tags)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.HasMany(e => e.Flags)
                .WithOne(e => e.Exercise)
                .HasForeignKey(e => e.ExerciseId);

            entity.HasOne(e => e.TestContainer)
                .WithMany()
                .HasForeignKey(e => e.TestContainerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Dependencies)
                .WithMany()
                .UsingEntity<ExerciseDependency>(
                    l => l.HasOne(e => e.Target).WithMany().HasForeignKey(e => e.TargetId),
                    r => r.HasOne(e => e.Source).WithMany().HasForeignKey(e => e.SourceId)
                );

            entity.Navigation(e => e.Attachment).AutoInclude();
            entity.Navigation(e => e.TestContainer).AutoInclude();
        });

        builder.Entity<Submission>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>();

            entity.HasOne(e => e.SolverFile)
                .WithMany()
                .HasForeignKey(e => e.SolverFileId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.Team).AutoInclude();
            entity.Navigation(e => e.User).AutoInclude();
            entity.Navigation(e => e.GameChallenge).AutoInclude();
            entity.Navigation(e => e.SolverFile).AutoInclude();
        });

        builder.Entity<FlagContext>(entity =>
        {
            entity.HasOne(e => e.Attachment)
                .WithMany()
                .HasForeignKey(e => e.AttachmentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.Attachment).AutoInclude();
        });

        builder.Entity<Attachment>(entity =>
        {
            entity.HasOne(e => e.LocalFile)
                .WithMany()
                .HasForeignKey(e => e.LocalFileId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.LocalFile).AutoInclude();
        });

        builder.Entity<GameNotice>(entity =>
        {
            entity.Property(e => e.Values)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);
        });

        builder.Entity<GameEvent>(entity =>
        {
            entity.Property(e => e.Values)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId);

            entity.Navigation(e => e.Team).AutoInclude();
            entity.Navigation(e => e.User).AutoInclude();
        });

        builder.Entity<CheatInfo>(entity =>
        {
            entity.HasOne(e => e.Game)
                .WithMany()
                .HasForeignKey(e => e.GameId);

            entity.HasOne(e => e.SourceTeam)
                .WithMany()
                .HasForeignKey(e => e.SourceTeamId);

            entity.HasOne(e => e.SubmitTeam)
                .WithMany()
                .HasForeignKey(e => e.SubmitTeamId);

            entity.HasOne(e => e.Submission)
                .WithMany()
                .HasForeignKey(e => e.SubmissionId);

            entity.HasKey(e => e.SubmissionId);
        });

        builder.Entity<FirstSolve>(entity =>
        {
            entity.HasKey(e => new { e.ParticipationId, e.ChallengeId });

            entity.HasOne(e => e.Submission)
                .WithMany()
                .HasForeignKey(e => e.SubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserParticipation>(entity =>
        {
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId);

            entity.HasOne(e => e.Game)
                .WithMany()
                .HasForeignKey(e => e.GameId);

            entity.HasKey(e => new { e.GameId, e.TeamId, e.UserId });
        });

        builder.Entity<ApiToken>(entity =>
        {
            entity.HasOne(e => e.Creator)
                .WithMany()
                .HasForeignKey(e => e.CreatorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<LogModel>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(Limits.MaxLogStatusLength);
        });
    }
}
