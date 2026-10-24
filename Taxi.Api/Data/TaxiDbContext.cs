namespace Taxi.Api.Data;

using Microsoft.EntityFrameworkCore;
public class TaxiDbContext : DbContext
{
    public TaxiDbContext(DbContextOptions<TaxiDbContext> options) : base(options)
    {

    }
}