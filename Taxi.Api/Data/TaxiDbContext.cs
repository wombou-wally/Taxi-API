using Microsoft.EntityFrameworkCore;
using System.Net.NetworkInformation;
using Taxi.Api.Models;
namespace Taxi.Api.Data
{

    public class TaxiDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<DriverProfile> DriverProfiles { get; set; }
        public TaxiDbContext(DbContextOptions<TaxiDbContext> options) : base(options)
        {

        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(u => u.Email).IsUnique();
                entity.Property(u => u.Email).HasMaxLength(256);
                entity.Property(u => u.FullName).HasMaxLength(100);
                entity.Property(u => u.Phone).HasMaxLength(30);
                entity.Property(u => u.Role).HasConversion<string>();
            });

            modelBuilder.Entity<DriverProfile>(entity =>
            {
                entity.HasKey(d => d.UserId);

                entity.HasOne(d => d.User)
                      .WithOne(u => u.DriverProfile)
                      .HasForeignKey<DriverProfile>(d => d.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.Property(d => d.Status).HasConversion<string>();
                entity.Property(d => d.LicenceNumber).HasMaxLength(50);
            });
        }
    }
}