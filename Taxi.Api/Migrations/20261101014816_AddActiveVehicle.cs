using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taxi.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveVehicle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActiveVehicleId",
                table: "DriverProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriverProfiles_ActiveVehicleId",
                table: "DriverProfiles",
                column: "ActiveVehicleId");

            migrationBuilder.AddForeignKey(
                name: "FK_DriverProfiles_Vehicles_ActiveVehicleId",
                table: "DriverProfiles",
                column: "ActiveVehicleId",
                principalTable: "Vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DriverProfiles_Vehicles_ActiveVehicleId",
                table: "DriverProfiles");

            migrationBuilder.DropIndex(
                name: "IX_DriverProfiles_ActiveVehicleId",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "ActiveVehicleId",
                table: "DriverProfiles");
        }
    }
}
