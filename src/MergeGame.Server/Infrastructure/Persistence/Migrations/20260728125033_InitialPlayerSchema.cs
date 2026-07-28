using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MergeGame.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlayerSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 모든 문자열의 기본 문자 집합을 utf8mb4로 설정해 한글 이름과 이모지를 안전하게 저장합니다.
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            // 플레이어 식별 정보와 게스트 인증 해시를 저장하는 최초 테이블을 만듭니다.
            migrationBuilder.CreateTable(
                name: "players",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    display_name = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    guest_token_hash = table.Column<string>(type: "char(64)", nullable: false, collation: "ascii_bin"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_players", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // 인증 조회 성능을 확보하고 서로 다른 계정에 같은 토큰 해시가 저장되는 것을 차단합니다.
            migrationBuilder.CreateIndex(
                name: "ux_players_guest_token_hash",
                table: "players",
                column: "guest_token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 롤백 시 이 단계에서 만든 players 테이블과 소속 인덱스를 함께 제거합니다.
            migrationBuilder.DropTable(
                name: "players");
        }
    }
}
