using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Taxi.Api.Data;
using Taxi.Api.DTO.Driver;
using Taxi.Api.Infrastructure;
using Taxi.Api.Models;

namespace Taxi.Api.Controller
{
    [ApiController]
    [Route("api/driver")]
    [Authorize(Roles = nameof(Role.Driver))]
    public class VehicleController : ControllerBase
    {
        private readonly TaxiDbContext _context;

        public VehicleController(TaxiDbContext context)
        {
            _context = context;
        }

        [HttpPost("vehicle")]
        public async Task<ActionResult<DriversVehicleResponse>> CreateVehicle(DriversVehicleRequest request)
        {
            var plate = NormalizePlate(request.PlateNumber);

            var error = await ValidateAsync(request, plate);
            if (error != null)
            {
                return error;
            }

            var vehicle = new Vehicle
            {
                DriverProfileUserId = User.GetUserId(),
                PlateNumber = plate,
                Model = request.Model.Trim(),
                InsuranceNumber = request.InsuranceNumber.Trim(),
                InsuranceExpiryDate = request.InsuranceExpiryDate,
                VehicleTypeId = request.VehicleTypeId,
            };
            _context.Vehicles.Add(vehicle);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Problem(statusCode: 409, title: "Plate already exists");
            }

            return StatusCode(StatusCodes.Status201Created, ToResponse(vehicle));
        }

        [HttpGet("vehicle")]
        public async Task<ActionResult<IEnumerable<DriversVehicleResponse>>> ListVehicle()
        {
            var driverId = User.GetUserId();

            var list = await _context.Vehicles
                .Where(v => v.DriverProfileUserId == driverId)
                .Select(v => new DriversVehicleResponse
                {
                    Id = v.Id,
                    InsuranceExpiryDate = v.InsuranceExpiryDate,
                    VehicleTypeId = v.VehicleTypeId,
                    InsuranceNumber = v.InsuranceNumber,
                    PlateNumber = v.PlateNumber,
                    Model = v.Model
                })
                .ToListAsync();

            return Ok(list);
        }

        [HttpPut("vehicle/{id:int}")]
        public async Task<ActionResult<DriversVehicleResponse>> Update(int id, DriversVehicleRequest request)
        {
            var vehicle = await FindMyVehicleAsync(id);
            if (vehicle == null)
            {
                return Problem(statusCode: 404, title: "Vehicle does not exist");
            }

            var plate = NormalizePlate(request.PlateNumber);

            var error = await ValidateAsync(request, plate, id);
            if (error != null)
            {
                return error;
            }

            vehicle.PlateNumber = plate;
            vehicle.Model = request.Model.Trim();
            vehicle.InsuranceNumber = request.InsuranceNumber.Trim();
            vehicle.InsuranceExpiryDate = request.InsuranceExpiryDate;
            vehicle.VehicleTypeId = request.VehicleTypeId;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Problem(statusCode: 409, title: "Plate already exists");
            }

            return Ok(ToResponse(vehicle));
        }

        [HttpDelete("vehicle/{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            int userid = User.GetUserId();
            var vehicle = await FindMyVehicleAsync(id);
            var driver = await _context.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == userid);
            if (vehicle == null)
            {
                return Problem(statusCode: 404, title: "Vehicle does not exist");
            }
            if (driver == null)
            {
                return Problem(statusCode: 409, title: "Not found driver");
            }
            if(vehicle.Id == driver.ActiveVehicleId)
            {
                return Problem(statusCode: 409, title: "Go offline before deleting this vehicle");
            }

            _context.Vehicles.Remove(vehicle);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // Loads a vehicle only if it belongs to the logged-in driver.
        private async Task<Vehicle?> FindMyVehicleAsync(int vehicleId)
        {
            var driverId = User.GetUserId();
            return await _context.Vehicles
                .FirstOrDefaultAsync(v => v.Id == vehicleId && v.DriverProfileUserId == driverId);
        }

        private static string NormalizePlate(string plate) => plate.Trim().ToUpperInvariant();

        // Shared checks for create and update. Returns an error response, or null when everything is valid.
        // excludeVehicleId is the vehicle being edited (0 when creating), so it doesn't conflict with itself.
        private async Task<ActionResult?> ValidateAsync(DriversVehicleRequest request, string plate, int excludeVehicleId = 0)
        {
            var plateTaken = await _context.Vehicles
                .AnyAsync(v => v.PlateNumber == plate && v.Id != excludeVehicleId);
            if (plateTaken)
            {
                return Problem(statusCode: 409, title: "Plate already exists");
            }

            if (request.InsuranceExpiryDate <= DateOnly.FromDateTime(DateTime.UtcNow))
            {
                return Problem(statusCode: 400, title: "Insurance expiry date must be in the future");
            }

            var typeExists = await _context.VehicleTypes
                .AnyAsync(t => t.Id == request.VehicleTypeId && t.IsActive);
            if (!typeExists)
            {
                return Problem(statusCode: 400, title: "Vehicle type does not exist or is not active");
            }

            return null;
        }

        private static DriversVehicleResponse ToResponse(Vehicle vehicle) => new()
        {
            Id = vehicle.Id,
            PlateNumber = vehicle.PlateNumber,
            Model = vehicle.Model,
            InsuranceNumber = vehicle.InsuranceNumber,
            InsuranceExpiryDate = vehicle.InsuranceExpiryDate,
            VehicleTypeId = vehicle.VehicleTypeId
        };
    }
}