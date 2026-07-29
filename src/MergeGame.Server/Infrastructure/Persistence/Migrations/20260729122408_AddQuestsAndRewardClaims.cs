using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MergeGame.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestsAndRewardClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 서버가 확정한 머지 이벤트를 감사 및 퀘스트 처리 근거로 보존합니다.
            migrationBuilder.CreateTable(
                name: "gameplay_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    player_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_type = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "ascii_bin"),
                    board_revision = table.Column<long>(type: "bigint", nullable: false),
                    result_item_level = table.Column<int>(type: "int", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gameplay_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_gameplay_events_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // 플레이어별 퀘스트 진행도와 수령 상태를 revision 동시성 토큰과 함께 저장합니다.
            migrationBuilder.CreateTable(
                name: "player_quests",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    quest_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "ascii_bin"),
                    current_count = table.Column<int>(type: "int", nullable: false),
                    target_count = table.Column<int>(type: "int", nullable: false),
                    reward_coins = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    claimed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_quests", x => new { x.player_id, x.quest_id });
                    table.ForeignKey(
                        name: "FK_player_quests_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // 플레이어+멱등성 키 복합 PK가 동일 보상의 중복 지급을 DB 수준에서 차단합니다.
            migrationBuilder.CreateTable(
                name: "reward_claims",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    idempotency_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "ascii_bin"),
                    quest_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "ascii_bin"),
                    reward_coins = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reward_claims", x => new { x.player_id, x.idempotency_key });
                    table.ForeignKey(
                        name: "FK_reward_claims_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_gameplay_events_player_time",
                table: "gameplay_events",
                columns: new[] { "player_id", "occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 서로 독립적으로 players를 참조하므로 세 기능 테이블을 차례로 제거합니다.
            migrationBuilder.DropTable(
                name: "gameplay_events");

            migrationBuilder.DropTable(
                name: "player_quests");

            migrationBuilder.DropTable(
                name: "reward_claims");
        }
    }
}
