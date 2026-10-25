using Microsoft.EntityFrameworkCore;
using Taxi.Api.Models;
namespace Taxi.Api.Data
{

    public class TaxiDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public TaxiDbContext(DbContextOptions<TaxiDbContext> options) : base(options)
        {

        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();
            modelBuilder.Entity<User>()
                .Property(u => u.Email)
                .HasMaxLength(256);
            modelBuilder.Entity<User>()
                .Property(u => u.Role)
                .HasConversion<string>();
            modelBuilder.Entity<User>()
                .Property(u => u.FullName)
                .HasMaxLength(100);
            modelBuilder.Entity<User>()
                .Property(u => u.Phone)
                .HasMaxLength (30);

        }
    }
}