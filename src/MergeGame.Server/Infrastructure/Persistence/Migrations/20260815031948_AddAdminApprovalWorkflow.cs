using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MergeGame.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_approval_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    requested_by = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "ascii_bin"),
                    idempotency_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "ascii_bin"),
                    target_player_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expected_economy_revision = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "ascii_bin"),
                    approved_by = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "ascii_bin"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    approved_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_approval_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_admin_approval_requests_players_target_player_id",
                        column: x => x.target_player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_admin_approval_requests_target_player_id",
                table: "admin_approval_requests",
                column: "target_player_id");

            migrationBuilder.CreateIndex(
                name: "ux_admin_approvals_requester_idempotency",
                table: "admin_approval_requests",
                columns: new[] { "requested_by", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_approval_requests");
        }
    }
}
