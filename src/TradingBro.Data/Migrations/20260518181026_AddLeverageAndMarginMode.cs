using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBro.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLeverageAndMarginMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Leverage",
                table: "trade_signals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MarginMode",
                table: "trade_signals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Leverage",
                table: "positions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MarginMode",
                table: "positions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Leverage",
                table: "trade_signals");

            migrationBuilder.DropColumn(
                name: "MarginMode",
                table: "trade_signals");

            migrationBuilder.DropColumn(
                name: "Leverage",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "MarginMode",
                table: "positions");
        }
    }
}
