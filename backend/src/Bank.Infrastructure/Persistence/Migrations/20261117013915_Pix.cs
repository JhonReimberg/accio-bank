using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pix_contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nickname = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    key_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    key_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pix_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_pix_contacts_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pix_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pix_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_pix_keys_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pix_limits",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    daytime_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    nighttime_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    pending_daytime_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    pending_nighttime_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    pending_effective_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pix_limits", x => x.account_id);
                    table.ForeignKey(
                        name: "fk_pix_limits_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pix_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payer_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    key_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    description = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    frequency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    first_date = table.Column<DateOnly>(type: "date", nullable: false),
                    next_run_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    executions = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pix_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_pix_schedules_accounts_payer_account_id",
                        column: x => x.payer_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pix_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    end_to_end_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payer_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payee_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    description = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    key_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    key_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ledger_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refunded_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    schedule_occurrence = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pix_transfers", x => x.id);
                    table.CheckConstraint("ck_pix_transfers_refund_within_amount", "refunded_amount >= 0 AND refunded_amount <= amount");
                    table.ForeignKey(
                        name: "fk_pix_transfers_accounts_payee_account_id",
                        column: x => x.payee_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pix_transfers_accounts_payer_account_id",
                        column: x => x.payer_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pix_refund",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pix_transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    end_to_end_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    ledger_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pix_refund", x => x.id);
                    table.ForeignKey(
                        name: "fk_pix_refund_pix_transfers_pix_transfer_id",
                        column: x => x.pix_transfer_id,
                        principalTable: "pix_transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pix_contacts_owner_user_id_key_value",
                table: "pix_contacts",
                columns: new[] { "owner_user_id", "key_value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pix_keys_account_id",
                table: "pix_keys",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_pix_keys_value",
                table: "pix_keys",
                column: "value",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pix_refund_end_to_end_id",
                table: "pix_refund",
                column: "end_to_end_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pix_refund_ledger_transaction_id",
                table: "pix_refund",
                column: "ledger_transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pix_refund_pix_transfer_id",
                table: "pix_refund",
                column: "pix_transfer_id");

            migrationBuilder.CreateIndex(
                name: "ix_pix_schedules_payer_account_id",
                table: "pix_schedules",
                column: "payer_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_pix_schedules_status_next_run_date",
                table: "pix_schedules",
                columns: new[] { "status", "next_run_date" });

            migrationBuilder.CreateIndex(
                name: "ix_pix_transfers_end_to_end_id",
                table: "pix_transfers",
                column: "end_to_end_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pix_transfers_ledger_transaction_id",
                table: "pix_transfers",
                column: "ledger_transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pix_transfers_payee_account_id",
                table: "pix_transfers",
                column: "payee_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_pix_transfers_payer_account_id_created_at",
                table: "pix_transfers",
                columns: new[] { "payer_account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pix_transfers_schedule_id_schedule_occurrence",
                table: "pix_transfers",
                columns: new[] { "schedule_id", "schedule_occurrence" },
                unique: true,
                filter: "schedule_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pix_contacts");

            migrationBuilder.DropTable(
                name: "pix_keys");

            migrationBuilder.DropTable(
                name: "pix_limits");

            migrationBuilder.DropTable(
                name: "pix_refund");

            migrationBuilder.DropTable(
                name: "pix_schedules");

            migrationBuilder.DropTable(
                name: "pix_transfers");
        }
    }
}
