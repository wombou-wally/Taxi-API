using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taxi.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOneActiveRidePerDriver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rides_DriverProfileUserId",
                table: "Rides");

            migrationBuilder.CreateIndex(
                name: "IX_Rides_DriverProfileUserId",
                table: "Rides",
                column: "DriverProfileUserId",
                unique: true,
                filter: "\"Status\" IN ('Accepted', 'InProgress')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rides_DriverProfileUserId",
                table: "Rides");

            migrationBuilder.CreateIndex(
                name: "IX_Rides_DriverProfileUserId",
                table: "Rides",
                column: "DriverProfileUserId");
        }
    }
}
