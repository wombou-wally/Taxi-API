using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Taxi.Api.Data;
using Taxi.Api.DTO.Fare;
using Taxi.Api.Infrastructure;
using Taxi.Api.Models;
using Taxi.Api.Services;

namespace Taxi.Api.Controller
{
    [ApiController]
    [Route("api/fare")]
    [Authorize]
    public class FareController : ControllerBase
    {
        private readonly TaxiDbContext _context;
        private readonly IFareService _fareService;
        private readonly IDistanceService _distanceService;

        public FareController(TaxiDbContext context, IFareService fareService, IDistanceService distanceService)
        {
            _context = context;
            _fareService = fareService;
            _distanceService = distanceService;
        }

        [HttpPost("estimate")]
        public async Task<ActionResult<EstimateResponse>> EstimateFare(EstimateRequest request)
        {
            var distance = _distanceService.DistanceKm(
                request.PickupLatitude, request.PickupLongitude,
                request.DropoffLatitude, request.DropoffLongitude);

            if (distance > RideRules.MaxDistanceKm)
            {
                return Problem(statusCode: 400, title: "Trip is too long for the service area");
            }

            double minutes = RideRules.EstimatedMinutes(distance);

            var vehicleTypes = await _context.VehicleTypes
                .AsNoTracking()
                .Where(v => v.IsActive)
                .OrderBy(v => v.BaseFare)
                .ToListAsync();

            var options = vehicleTypes.Select(v => new EstimateOption
            {
                VehicleTypeId = v.Id,
                Seats = v.Seats,
                Name = v.Name,
                EstimatedFare = _fareService.CalculateFare(v, distance, minutes)
            }).ToList();

            var response = new EstimateResponse
            {
                EstimatedMinutes = Convert.ToInt32(minutes),
                Options = options,
                StraightLineKm = Math.Round(distance, 1)
            };

            return Ok(response);
        }
    }
}