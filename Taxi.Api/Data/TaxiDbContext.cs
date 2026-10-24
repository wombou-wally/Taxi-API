using Microsoft.EntityFrameworkCore;
namespace Taxi.Api.Data
{

    public class TaxiDbContext : DbContext
    {
        public TaxiDbContext(DbContextOptions<TaxiDbContext> options) : base(options)
        {

        }
    }
}