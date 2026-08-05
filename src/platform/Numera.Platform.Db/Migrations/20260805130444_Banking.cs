using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class Banking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_connection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    fin_api_user_id = table.Column<string>(type: "text", nullable: true),
                    fin_api_user_secret = table.Column<string>(type: "text", nullable: true),
                    access_token_cipher = table.Column<string>(type: "text", nullable: true),
                    consent_status = table.Column<int>(type: "integer", nullable: false),
                    consent_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    web_form_id = table.Column<string>(type: "text", nullable: true),
                    web_form_status = table.Column<string>(type: "text", nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_connection", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bank_account",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_connection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    iban = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    fin_api_account_id = table.Column<string>(type: "text", nullable: true),
                    sync_cursor = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_account", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_account_bank_connection_bank_connection_id",
                        column: x => x.bank_connection_id,
                        principalTable: "bank_connection",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "bank_transaction",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dedupe_key = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    value_date = table.Column<DateOnly>(type: "date", nullable: false),
                    booking_date = table.Column<DateOnly>(type: "date", nullable: true),
                    purpose = table.Column<string>(type: "text", nullable: true),
                    counterparty_name = table.Column<string>(type: "text", nullable: true),
                    counterparty_iban = table.Column<string>(type: "text", nullable: true),
                    end_to_end_id = table.Column<string>(type: "text", nullable: true),
                    match_status = table.Column<int>(type: "integer", nullable: false),
                    confidence_score = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    matched_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_transaction", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_transaction_bank_account_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "bank_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_account_bank_connection_id",
                table: "bank_account",
                column: "bank_connection_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_account_tenant_id_id",
                table: "bank_account",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_connection_tenant_id_id",
                table: "bank_connection",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_transaction_bank_account_id",
                table: "bank_transaction",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_transaction_tenant_id_bank_account_id_dedupe_key",
                table: "bank_transaction",
                columns: new[] { "tenant_id", "bank_account_id", "dedupe_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_transaction_tenant_id_match_status",
                table: "bank_transaction",
                columns: new[] { "tenant_id", "match_status" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_transaction_tenant_id_value_date",
                table: "bank_transaction",
                columns: new[] { "tenant_id", "value_date" });

            migrationBuilder.Sql("ALTER TABLE bank_connection ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE bank_connection FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                """
                CREATE POLICY tenant_isolation ON bank_connection
                USING (tenant_id = current_setting('app.current_tenant')::uuid)
                WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);
                """);

            migrationBuilder.Sql("ALTER TABLE bank_account ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE bank_account FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                """
                CREATE POLICY tenant_isolation ON bank_account
                USING (tenant_id = current_setting('app.current_tenant')::uuid)
                WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);
                """);

            migrationBuilder.Sql("ALTER TABLE bank_transaction ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE bank_transaction FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                """
                CREATE POLICY tenant_isolation ON bank_transaction
                USING (tenant_id = current_setting('app.current_tenant')::uuid)
                WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON bank_transaction;");
            migrationBuilder.Sql("ALTER TABLE bank_transaction NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE bank_transaction DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON bank_account;");
            migrationBuilder.Sql("ALTER TABLE bank_account NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE bank_account DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON bank_connection;");
            migrationBuilder.Sql("ALTER TABLE bank_connection NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE bank_connection DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "bank_transaction");

            migrationBuilder.DropTable(
                name: "bank_account");

            migrationBuilder.DropTable(
                name: "bank_connection");
        }
    }
}
