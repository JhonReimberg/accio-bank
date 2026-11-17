using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Bank.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Investments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "guarantee_investment_id",
                table: "loans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "guaranteed_monthly_rate",
                table: "credit_policies",
                type: "numeric(10,6)",
                precision: 10,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "investments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    goal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    cdi_percent = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: false),
                    blocked_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    round_up_card_purchases = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_investments", x => x.id);
                    table.CheckConstraint("ck_investments_blocked_non_negative", "blocked_amount >= 0");
                    table.ForeignKey(
                        name: "fk_investments_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "investment_lot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    investment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    applied_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_investment_lot", x => x.id);
                    table.ForeignKey(
                        name: "fk_investment_lot_investments_investment_id",
                        column: x => x.investment_id,
                        principalTable: "investments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "accounts",
                columns: new[] { "id", "activated_at", "balance", "blocked_balance", "created_at", "number", "overdraft_limit", "status", "status_reason", "system_name", "tier", "type", "user_id" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000005"), null, 0m, 0m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 5L, 0m, "Active", null, "Custódia de investimentos", "Basic", "System", null },
                    { new Guid("00000000-0000-0000-0000-000000000006"), null, 0m, 0m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 6L, 0m, "Active", null, "Despesa com rendimentos", "Basic", "System", null },
                    { new Guid("00000000-0000-0000-0000-000000000007"), null, 0m, 0m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 7L, 0m, "Active", null, "IR a recolher", "Basic", "System", null },
                    { new Guid("00000000-0000-0000-0000-000000000008"), null, 0m, 0m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 8L, 0m, "Active", null, "Liquidação de cartões", "Basic", "System", null }
                });

            migrationBuilder.CreateIndex(
                name: "ix_investment_lot_investment_id",
                table: "investment_lot",
                column: "investment_id");

            migrationBuilder.CreateIndex(
                name: "ix_investments_account_id",
                table: "investments",
                column: "account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "investment_lot");

            migrationBuilder.DropTable(
                name: "investments");

            migrationBuilder.DeleteData(
                table: "accounts",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "accounts",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "accounts",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "accounts",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000008"));

            migrationBuilder.DropColumn(
                name: "guarantee_investment_id",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "guaranteed_monthly_rate",
                table: "credit_policies");
        }
    }
}
