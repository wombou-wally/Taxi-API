using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Taxi.Api.Data;
using Taxi.Api.Infrastructure;
using Taxi.Api.Models;

namespace Taxi.Api.Controller
{
    [ApiController]
    [Route("api/roletest")]
    public class TestController : ControllerBase
    {
        [HttpGet("me")]
        [Authorize]
        public ActionResult Get()
        {
            return Ok(new { id = User.GetUserId(), role = User.FindFirstValue(ClaimTypes.Role) });
        }

        [HttpGet("customer")]
        [Authorize(Roles = nameof(Role.Customer))]
        public ActionResult GetCustomer()
        {
            return Ok("Customer valid");
        }
    }
}
