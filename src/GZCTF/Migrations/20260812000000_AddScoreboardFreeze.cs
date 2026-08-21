using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260812000000_AddScoreboardFreeze")]
public partial class AddScoreboardFreeze : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AcceptedTimeUtc",
            table: "FirstSolves",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "CURRENT_TIMESTAMP");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AcceptedTimeUtc",
            table: "Participations",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE "Participations"
            SET "AcceptedTimeUtc" = CURRENT_TIMESTAMP
            WHERE "Status" = 1
            """);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ScoreboardFreezeTimeUtc",
            table: "Games",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "ScoreboardFrozen",
            table: "Games",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AcceptedTimeUtc", table: "FirstSolves");
        migrationBuilder.DropColumn(name: "AcceptedTimeUtc", table: "Participations");
        migrationBuilder.DropColumn(name: "ScoreboardFreezeTimeUtc", table: "Games");
        migrationBuilder.DropColumn(name: "ScoreboardFrozen", table: "Games");
    }
}
