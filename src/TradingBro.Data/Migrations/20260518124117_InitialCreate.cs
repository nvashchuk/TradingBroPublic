using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBro.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "announcements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Exchange = table.Column<int>(type: "INTEGER", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Market = table.Column<int>(type: "INTEGER", nullable: false),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    AnnouncedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EventAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    BodyText = table.Column<string>(type: "TEXT", nullable: true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TradeSignalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StrategyName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Exchange = table.Column<int>(type: "INTEGER", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Market = table.Column<int>(type: "INTEGER", nullable: false),
                    Side = table.Column<int>(type: "INTEGER", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "TEXT", precision: 28, scale: 12, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 28, scale: 12, nullable: false),
                    StopLossPrice = table.Column<decimal>(type: "TEXT", precision: 28, scale: 12, nullable: true),
                    TakeProfitPrice = table.Column<decimal>(type: "TEXT", precision: 28, scale: 12, nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExitPrice = table.Column<decimal>(type: "TEXT", precision: 28, scale: 12, nullable: true),
                    RealizedPnlUsd = table.Column<decimal>(type: "TEXT", precision: 18, scale: 8, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ExchangeOrderId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsPaper = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_positions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "trade_signals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AnnouncementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetExchange = table.Column<int>(type: "INTEGER", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Market = table.Column<int>(type: "INTEGER", nullable: false),
                    Side = table.Column<int>(type: "INTEGER", nullable: false),
                    QuoteAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 8, nullable: false),
                    StopLossPct = table.Column<decimal>(type: "TEXT", precision: 10, scale: 6, nullable: true),
                    TakeProfitPct = table.Column<decimal>(type: "TEXT", precision: 10, scale: 6, nullable: true),
                    StrategyName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_signals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_announcements_announced_at",
                table: "announcements",
                column: "AnnouncedAt");

            migrationBuilder.CreateIndex(
                name: "ix_announcements_dedup",
                table: "announcements",
                columns: new[] { "Exchange", "Symbol", "Market", "EventType", "AnnouncedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_announcements_external",
                table: "announcements",
                columns: new[] { "Exchange", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_status",
                table: "positions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "ix_positions_symbol_status",
                table: "positions",
                columns: new[] { "Exchange", "Symbol", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_signals_announcement",
                table: "trade_signals",
                column: "AnnouncementId");

            migrationBuilder.CreateIndex(
                name: "ix_signals_strategy",
                table: "trade_signals",
                columns: new[] { "StrategyName", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "announcements");

            migrationBuilder.DropTable(
                name: "positions");

            migrationBuilder.DropTable(
                name: "trade_signals");
        }
    }
}
