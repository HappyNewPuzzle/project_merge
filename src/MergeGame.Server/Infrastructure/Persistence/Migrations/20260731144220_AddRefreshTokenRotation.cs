using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MergeGame.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "refresh_token_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    player_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    family_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    token_hash = table.Column<string>(type: "char(64)", nullable: false, collation: "ascii_bin"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    replaced_by_session_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    revocation_reason = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_token_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_refresh_token_sessions_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_sessions_player_family",
                table: "refresh_token_sessions",
                columns: new[] { "player_id", "family_id" });

            migrationBuilder.CreateIndex(
                name: "ux_refresh_sessions_token_hash",
                table: "refresh_token_sessions",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refresh_token_sessions");
        }
    }
}
