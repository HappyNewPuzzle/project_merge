using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MergeGame.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotentBoardActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_board_items_player_slot",
                table: "board_items");

            migrationBuilder.CreateTable(
                name: "board_action_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    player_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    idempotency_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "ascii_bin"),
                    source_slot = table.Column<int>(type: "int", nullable: false),
                    target_slot = table.Column<int>(type: "int", nullable: false),
                    response_json = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_board_action_receipts", x => x.id);
                    table.ForeignKey(
                        name: "FK_board_action_receipts_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_board_items_player_id",
                table: "board_items",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ux_board_action_receipts_player_idempotency",
                table: "board_action_receipts",
                columns: new[] { "player_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "board_action_receipts");

            migrationBuilder.DropIndex(
                name: "IX_board_items_player_id",
                table: "board_items");

            migrationBuilder.CreateIndex(
                name: "ux_board_items_player_slot",
                table: "board_items",
                columns: new[] { "player_id", "slot_index" },
                unique: true);
        }
    }
}
