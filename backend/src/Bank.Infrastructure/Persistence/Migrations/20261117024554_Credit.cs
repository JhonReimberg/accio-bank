using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Bank.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Credit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_blocked_balance_valid",
                table: "accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_customer_balance_non_negative",
                table: "accounts");

            migrationBuilder.AddColumn<decimal>(
                name: "overdraft_limit",
                table: "accounts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "credit_policies",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    min_account_age_days = table.Column<int>(type: "integer", nullable: false),
                    min_movement30days = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    min_score = table.Column<int>(type: "integer", nullable: false),
                    max_income_commitment = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false),
                    min_installments = table.Column<int>(type: "integer", nullable: false),
                    max_installments = table.Column<int>(type: "integer", nullable: false),
                    min_loan_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    late_fee_rate = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false),
                    daily_late_interest_rate = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false),
                    overdraft_monthly_rate = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false),
                    cdi_annual_rate = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    bands = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_policies", x => x.id);
                    table.CheckConstraint("ck_credit_policies_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "credit_scores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    factors = table.Column<string>(type: "jsonb", nullable: false),
                    calculated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_scores", x => x.id);
                    table.ForeignKey(
                        name: "fk_credit_scores_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "loans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    monthly_rate = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false),
                    installment_count = table.Column<int>(type: "integer", nullable: false),
                    installment_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iof = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cet_monthly = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false),
                    cet_yearly = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false),
                    score_at_contract = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    disbursement_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contracted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loans", x => x.id);
                    table.ForeignKey(
                        name: "fk_loans_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "loan_installments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    interest = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    principal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    was_late = table.Column<bool>(type: "boolean", nullable: false),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    paid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    late_fee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    late_interest = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_transaction_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_installments", x => x.id);
                    table.ForeignKey(
                        name: "fk_loan_installments_loans_loan_id",
                        column: x => x.loan_id,
                        principalTable: "loans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "accounts",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "overdraft_limit",
                value: 0m);

            migrationBuilder.InsertData(
                table: "accounts",
                columns: new[] { "id", "activated_at", "balance", "blocked_balance", "created_at", "number", "overdraft_limit", "status", "status_reason", "system_name", "tier", "type", "user_id" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000002"), null, 0m, 0m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2L, 0m, "Active", null, "Carteira de crédito", "Basic", "System", null },
                    { new Guid("00000000-0000-0000-0000-000000000003"), null, 0m, 0m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 3L, 0m, "Active", null, "Receitas de crédito", "Basic", "System", null },
                    { new Guid("00000000-0000-0000-0000-000000000004"), null, 0m, 0m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 4L, 0m, "Active", null, "IOF a recolher", "Basic", "System", null }
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_blocked_balance_valid",
                table: "accounts",
                sql: "blocked_balance >= 0 AND overdraft_limit >= 0 AND (type <> 'Customer' OR blocked_balance <= balance + overdraft_limit)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_customer_balance_within_overdraft",
                table: "accounts",
                sql: "type <> 'Customer' OR balance >= -overdraft_limit");

            migrationBuilder.CreateIndex(
                name: "ix_credit_scores_user_id_calculated_at",
                table: "credit_scores",
                columns: new[] { "user_id", "calculated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_loan_installments_loan_id_number",
                table: "loan_installments",
                columns: new[] { "loan_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loan_installments_status_due_date",
                table: "loan_installments",
                columns: new[] { "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_loans_account_id",
                table: "loans",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_loans_disbursement_transaction_id",
                table: "loans",
                column: "disbursement_transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loans_user_id_status",
                table: "loans",
                columns: new[] { "user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_policies");

            migrationBuilder.DropTable(
                name: "credit_scores");

            migrationBuilder.DropTable(
                name: "loan_installments");

            migrationBuilder.DropTable(
                name: "loans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_blocked_balance_valid",
                table: "accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_customer_balance_within_overdraft",
                table: "accounts");

            migrationBuilder.DeleteData(
                table: "accounts",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "accounts",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "accounts",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000004"));

            migrationBuilder.DropColumn(
                name: "overdraft_limit",
                table: "accounts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_blocked_balance_valid",
                table: "accounts",
                sql: "blocked_balance >= 0 AND (type <> 'Customer' OR blocked_balance <= balance)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_customer_balance_non_negative",
                table: "accounts",
                sql: "type <> 'Customer' OR balance >= 0");
        }
    }
}
