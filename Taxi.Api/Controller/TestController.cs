using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Taxi.Api.Data;

namespace Taxi.Api.Controller
{
    [ApiController]
    [Route("test")]
    public class TestController : ControllerBase
    {
        private readonly TaxiDbContext _context;
        public TestController(TaxiDbContext context)
        {
            _context = context;
        }
        [HttpGet]
        public async Task Test()
        {
            throw new Exception("secret details");
        }

        
    }
}
