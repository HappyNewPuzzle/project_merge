using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MergeGame.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandRecurringQuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "event_type",
                table: "player_quests",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                collation: "ascii_bin");

            migrationBuilder.AddColumn<string>(
                name: "period_key",
                table: "player_quests",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "",
                collation: "ascii_bin");

            migrationBuilder.AddColumn<string>(
                name: "period_type",
                table: "player_quests",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "",
                collation: "ascii_bin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "event_type",
                table: "player_quests");

            migrationBuilder.DropColumn(
                name: "period_key",
                table: "player_quests");

            migrationBuilder.DropColumn(
                name: "period_type",
                table: "player_quests");
        }
    }
}
