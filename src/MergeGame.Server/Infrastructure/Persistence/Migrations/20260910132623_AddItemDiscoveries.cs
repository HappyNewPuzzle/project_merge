using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MergeGame.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddItemDiscoveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "item_discoveries",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    chain_id = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "ascii_bin"),
                    level = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_discoveries", x => new { x.player_id, x.chain_id, x.level });
                    table.ForeignKey(
                        name: "FK_item_discoveries_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // 배포 전 보유한 아이템만 보충합니다. 이미 소비된 과거 획득을 추측하지 않습니다.
            // UNION은 같은 정의의 여러 인스턴스 및 보드/보관함 간 중복을 제거합니다.
            migrationBuilder.Sql("""
                INSERT INTO item_discoveries (player_id, chain_id, level)
                SELECT player_id, chain_id, level FROM board_items
                UNION
                SELECT player_id, chain_id, level FROM inventory_items;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_discoveries");
        }
    }
}
