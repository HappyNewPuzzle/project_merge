using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MergeGame.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEconomyLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "economy_ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    player_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    resource = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "ascii_bin"),
                    reason = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "ascii_bin"),
                    delta = table.Column<long>(type: "bigint", nullable: false),
                    balance_after = table.Column<long>(type: "bigint", nullable: false),
                    economy_revision = table.Column<long>(type: "bigint", nullable: false),
                    reference_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "ascii_bin"),
                    occurred_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_economy_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_economy_ledger_nonzero_delta", "`delta` <> 0");
                    table.ForeignKey(
                        name: "FK_economy_ledger_entries_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_economy_ledger_player_occurred",
                table: "economy_ledger_entries",
                columns: new[] { "player_id", "occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "economy_ledger_entries");
        }
    }
}
