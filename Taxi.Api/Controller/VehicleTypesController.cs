using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Taxi.Api.Data;
using Taxi.Api.DTO.Vehicles;
using Taxi.Api.Models;

namespace Taxi.Api.Controller
{
    [ApiController]
    [Route("api/vehicle-types")]
    [Authorize(Roles = nameof(Role.Admin))]
    public class VehicleTypesController : ControllerBase
    {
        private readonly TaxiDbContext _context;
        public VehicleTypesController(TaxiDbContext context)
        {
            _context = context;
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<VehicleResponse>> Update(VehicleRequest request, int id)
        {
            if (!(request.MinimumFare >= request.BaseFare))
            {
                return Problem(statusCode: 400, title: "Minimum fare must be at least the base fare");
            }

            var existing = await _context.VehicleTypes.FirstOrDefaultAsync(u => u.Id == id);

            if (existing == null)
            {
                return Problem(statusCode: 404, title: "Vehicle Not Found");
            }

            var permin = Math.Round(request.PricePerMinute, 2, MidpointRounding.AwayFromZero);
            var minimum = Math.Round(request.MinimumFare, 2, MidpointRounding.AwayFromZero);
            var perKm = Math.Round(request.PricePerKm, 2, MidpointRounding.AwayFromZero);
            var basefare = Math.Round(request.BaseFare, 2, MidpointRounding.AwayFromZero);

            existing.PricePerMinute = permin;
            existing.BaseFare = basefare;
            existing.MinimumFare = minimum;
            existing.PricePerKm = perKm;
            existing.IsActive = request.IsActive;

            await _context.SaveChangesAsync();

            var response = new VehicleResponse
            {
                Id = id,
                Name = existing.Name,
                BaseFare = existing.BaseFare,
                MinimumFare = existing.MinimumFare,
                PricePerMinute = existing.PricePerMinute,
                IsActive = existing.IsActive,
                PricePerKm = existing.PricePerKm,

            };

            return Ok(response);
        }
    }
}
