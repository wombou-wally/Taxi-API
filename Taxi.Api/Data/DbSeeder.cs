using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Taxi.Api.Models;

namespace Taxi.Api.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAdminAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var services = scope.ServiceProvider;

            var context = services.GetRequiredService<TaxiDbContext>();
            var configuration = services.GetRequiredService<IConfiguration>();
            var hasher = services.GetRequiredService<IPasswordHasher<User>>();
            var loggerFactory = services.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("DbSeeder");
            string? email = configuration["Seed:AdminEmail"];
            string? password = configuration["Seed:AdminPassword"];
            if(string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password) || password.Length < 8)
            {
                logger.LogWarning("Email and/or Password is null or Password length is lesser than 8 Admin not created ");
                return;
            }
            email = email.Trim().ToLowerInvariant();
            var exists = await context.Users.AnyAsync(u  => u.Email == email);
            if(!exists)
            {
                var user = new User
                {
                    Email = email,
                    PasswordHash = "",
                    FullName = "Adminitrator",
                    CreatedAt = DateTime.UtcNow,
                    Role = Role.Admin,
                    Phone = "",
                };
                user.PasswordHash = hasher.HashPassword(user, password);
                context.Add(user);
                await context.SaveChangesAsync();
            }
        }
    }
}
