using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MergeGame.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerEconomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 플레이어별 에너지, 코인, 충전 기준 시각, 일일 보상 이력과 동시성 revision을 저장합니다.
            migrationBuilder.CreateTable(
                name: "player_economies",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    energy = table.Column<int>(type: "int", nullable: false),
                    coins = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    last_energy_updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_daily_reward_claimed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_economies", x => x.player_id);
                    table.CheckConstraint("ck_player_economies_coins", "`coins` >= 0");
                    table.CheckConstraint("ck_player_economies_energy", "`energy` >= 0 AND `energy` <= 100");
                    table.ForeignKey(
                        name: "FK_player_economies_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 이 단계에서 추가한 경제 상태만 제거하며 플레이어와 보드 데이터는 유지합니다.
            migrationBuilder.DropTable(
                name: "player_economies");
        }
    }
}
