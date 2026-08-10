using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations;

/// <inheritdoc />
public partial class Billing : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "current_period_end",
            table: "tenants",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "stripe_customer_id",
            table: "tenants",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "stripe_subscription_id",
            table: "tenants",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "subscription_status",
            table: "tenants",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "trial_ends_at",
            table: "tenants",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "processed_stripe_event",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<string>(type: "text", nullable: false),
                event_type = table.Column<string>(type: "text", nullable: false),
                processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_processed_stripe_event", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_processed_stripe_event_event_id",
            table: "processed_stripe_event",
            column: "event_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_tenants_stripe_customer_id",
            table: "tenants",
            column: "stripe_customer_id",
            unique: true,
            filter: "stripe_customer_id IS NOT NULL");

        migrationBuilder.Sql("GRANT SELECT, INSERT ON processed_stripe_event TO numera_app;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_processed_stripe_event_event_id",
            table: "processed_stripe_event");

        migrationBuilder.DropTable(
            name: "processed_stripe_event");

        migrationBuilder.DropIndex(
            name: "ix_tenants_stripe_customer_id",
            table: "tenants");

        migrationBuilder.DropColumn(
            name: "current_period_end",
            table: "tenants");

        migrationBuilder.DropColumn(
            name: "stripe_customer_id",
            table: "tenants");

        migrationBuilder.DropColumn(
            name: "stripe_subscription_id",
            table: "tenants");

        migrationBuilder.DropColumn(
            name: "subscription_status",
            table: "tenants");

        migrationBuilder.DropColumn(
            name: "trial_ends_at",
            table: "tenants");
    }
}
