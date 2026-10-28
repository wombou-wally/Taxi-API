using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System;
using System.Collections.Generic;
using System.Text;

namespace Taxi.Api.Tests
{
    public class TaxiApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            string? con = Environment.GetEnvironmentVariable("TAXI_TEST_CONNECTION");

            if(string.IsNullOrWhiteSpace(con))
            {
                throw new InvalidOperationException("Environment variable TAXI_TEST_CONNECTION is not set");
                
            }
            builder.UseSetting("ConnectionStrings:DefaultConnection", con);
            builder.UseSetting("Jwt:Key","My-name-is-fred-and-i-am-learning-asp.netCore.because-I-love-it-i-hpoe-this-make-32-characters-already");
            builder.UseSetting("Jwt:Issuer", "heyllo");
            builder.UseSetting("Jwt:Audience", "Yessir");
            builder.UseSetting("Jwt:ExpiryMinutes", "60");
        }
    }
}
