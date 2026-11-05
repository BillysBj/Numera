using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <summary>
    /// Creates the tenant-independent, single-row <c>platform_belege_mailbox</c> table that holds the
    /// shared receipt-mailbox configuration (one IMAP inbox + catch-all domain the operator runs for
    /// all tenants; each tenant gets a <c>belege-{token}@domain</c> alias). It carries NO RLS and is
    /// deliberately NOT mapped in <c>NumeraDbContext</c> (so the per-tenant export never leaks it);
    /// the API and worker read/write it over the app connection, which holds the default DML grant
    /// (db-roles.sql ALTER DEFAULT PRIVILEGES). The IMAP password is stored encrypted
    /// (DataProtection), never in plaintext. A boolean primary key enforces the single row.
    /// </summary>
    public partial class AddPlatformBelegeMailbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE platform_belege_mailbox (
                    singleton       boolean     NOT NULL PRIMARY KEY DEFAULT true,
                    host            text        NOT NULL,
                    port            integer     NOT NULL,
                    use_ssl         boolean     NOT NULL,
                    username        text        NOT NULL,
                    password_cipher text        NULL,
                    domain          text        NOT NULL,
                    is_active       boolean     NOT NULL,
                    updated_at      timestamptz NOT NULL,
                    CONSTRAINT ck_platform_belege_mailbox_singleton CHECK (singleton)
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS platform_belege_mailbox;");
        }
    }
}
