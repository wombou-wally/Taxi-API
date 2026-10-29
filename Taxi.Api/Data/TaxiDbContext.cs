using Microsoft.EntityFrameworkCore;
using System.Net.NetworkInformation;
using Taxi.Api.Models;
namespace Taxi.Api.Data
{

    public class TaxiDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<DriverProfile> DriverProfiles { get; set; }
        public DbSet<VehicleType> VehicleTypes { get; set; }
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

            modelBuilder.Entity<VehicleType>(entity =>
            {
                entity.HasIndex(v => v.Name).IsUnique();
                entity.Property(v => v.Name).HasMaxLength(50);

                entity.Property(v => v.BaseFare).HasPrecision(10, 2);
                entity.Property(v => v.PricePerKm).HasPrecision(10, 2);
                entity.Property(v => v.PricePerMinute).HasPrecision(10, 2);
                entity.Property(v => v.MinimumFare).HasPrecision(10, 2);

                entity.HasData(
                    new VehicleType { Id = 1, Name = "Standard", Seats = 4, BaseFare = 4.00m, PricePerKm = 1.80m, PricePerMinute = 0.35m, MinimumFare = 7.00m, IsActive = true },
                    new VehicleType { Id = 2, Name = "Comfort", Seats = 4, BaseFare = 5.00m, PricePerKm = 2.20m, PricePerMinute = 0.45m, MinimumFare = 9.00m, IsActive = true },
                    new VehicleType { Id = 3, Name = "XL", Seats = 6, BaseFare = 6.50m, PricePerKm = 2.80m, PricePerMinute = 0.55m, MinimumFare = 12.00m, IsActive = true }
                );
            });
        }
    }
}