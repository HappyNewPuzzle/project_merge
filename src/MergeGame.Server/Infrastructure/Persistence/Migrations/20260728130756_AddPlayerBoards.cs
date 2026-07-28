using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MergeGame.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerBoards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 플레이어당 하나의 보드 상태와 동시성 제어용 revision을 저장합니다.
            migrationBuilder.CreateTable(
                name: "player_boards",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_boards", x => x.player_id);
                    table.ForeignKey(
                        name: "FK_player_boards_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // 보드 아이템은 독립 행으로 저장해 슬롯별 조회와 머지 시 삭제/레벨 변경을 명확히 수행합니다.
            migrationBuilder.CreateTable(
                name: "board_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    player_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    slot_index = table.Column<int>(type: "int", nullable: false),
                    chain_id = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "ascii_bin"),
                    level = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_board_items", x => x.id);
                    // 애플리케이션 검증을 우회한 데이터도 5×7 보드 범위를 벗어나지 못하게 합니다.
                    table.CheckConstraint("ck_board_items_slot_index", "`slot_index` >= 0 AND `slot_index` < 35");
                    table.ForeignKey(
                        name: "FK_board_items_player_boards_player_id",
                        column: x => x.player_id,
                        principalTable: "player_boards",
                        principalColumn: "player_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // 같은 보드 슬롯에 두 아이템이 저장되는 경쟁 조건을 DB 고유 인덱스로 최종 차단합니다.
            migrationBuilder.CreateIndex(
                name: "ux_board_items_player_slot",
                table: "board_items",
                columns: new[] { "player_id", "slot_index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // FK 의존 순서에 따라 아이템을 먼저 제거한 뒤 보드 테이블을 제거합니다.
            migrationBuilder.DropTable(
                name: "board_items");

            migrationBuilder.DropTable(
                name: "player_boards");
        }
    }
}
