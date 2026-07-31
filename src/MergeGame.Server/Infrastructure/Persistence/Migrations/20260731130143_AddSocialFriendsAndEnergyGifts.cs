using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MergeGame.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialFriendsAndEnergyGifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "energy_gifts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    sender_player_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    recipient_player_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    gift_date_utc = table.Column<DateTime>(type: "date", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_energy_gifts", x => x.id);
                    table.CheckConstraint("ck_energy_gifts_distinct_players", "`sender_player_id` <> `recipient_player_id`");
                    table.ForeignKey(
                        name: "FK_energy_gifts_players_recipient_player_id",
                        column: x => x.recipient_player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_energy_gifts_players_sender_player_id",
                        column: x => x.sender_player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "friendships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    player_low_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    player_high_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_friendships", x => x.id);
                    table.CheckConstraint("ck_friendships_distinct_players", "`player_low_id` <> `player_high_id`");
                    table.ForeignKey(
                        name: "FK_friendships_players_player_high_id",
                        column: x => x.player_high_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_friendships_players_player_low_id",
                        column: x => x.player_low_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "player_social_profiles",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    friend_code = table.Column<string>(type: "char(8)", nullable: false, collation: "ascii_general_ci"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_social_profiles", x => x.player_id);
                    table.ForeignKey(
                        name: "FK_player_social_profiles_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_energy_gifts_recipient_player_id",
                table: "energy_gifts",
                column: "recipient_player_id");

            migrationBuilder.CreateIndex(
                name: "ux_energy_gifts_daily_sender_recipient",
                table: "energy_gifts",
                columns: new[] { "sender_player_id", "recipient_player_id", "gift_date_utc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_friendships_player_high_id",
                table: "friendships",
                column: "player_high_id");

            migrationBuilder.CreateIndex(
                name: "ux_friendships_player_pair",
                table: "friendships",
                columns: new[] { "player_low_id", "player_high_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_social_profiles_friend_code",
                table: "player_social_profiles",
                column: "friend_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "energy_gifts");

            migrationBuilder.DropTable(
                name: "friendships");

            migrationBuilder.DropTable(
                name: "player_social_profiles");
        }
    }
}
