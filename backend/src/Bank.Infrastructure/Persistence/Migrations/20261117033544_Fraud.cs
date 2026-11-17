using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Fraud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fraud_policies",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    average_multiplier = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    average_rule_min_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    new_device_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    velocity_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fraud_policies", x => x.id);
                    table.CheckConstraint("ck_fraud_policies_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "fraud_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payee_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payee_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    key_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    key_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    hold_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reasons = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    pix_transfer_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fraud_reviews", x => x.id);
                    table.ForeignKey(
                        name: "fk_fraud_reviews_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fraud_reviews_account_id",
                table: "fraud_reviews",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_fraud_reviews_status_created_at",
                table: "fraud_reviews",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_fraud_reviews_user_id",
                table: "fraud_reviews",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fraud_policies");

            migrationBuilder.DropTable(
                name: "fraud_reviews");
        }
    }
}
