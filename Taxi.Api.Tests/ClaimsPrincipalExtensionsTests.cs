using System.Security.Claims;
using Taxi.Api.Infrastructure;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    [Theory]
    [InlineData("Customer", Role.Customer)]
    [InlineData("Driver", Role.Driver)]
    [InlineData("Admin", Role.Admin)]
    [InlineData("driver", Role.Driver)]
    public void GetUserRole_ReadsValidRoles(string claimValue, Role expected)
    {
        var principal = PrincipalWith(new Claim(ClaimTypes.Role, claimValue));

        Assert.Equal(expected, principal.GetUserRole());
    }

    [Theory]
    [InlineData("Superuser")]
    [InlineData("99")]
    [InlineData("")]
    public void GetUserRole_InvalidValue_Throws(string claimValue)
    {
        var principal = PrincipalWith(new Claim(ClaimTypes.Role, claimValue));

        Assert.Throws<InvalidOperationException>(() => principal.GetUserRole());
    }

    [Fact]
    public void GetUserRole_MissingClaim_Throws()
    {
        var principal = PrincipalWith();

        Assert.Throws<InvalidOperationException>(() => principal.GetUserRole());
    }

    [Fact]
    public void GetUserId_ReadsTheNumber()
    {
        var principal = PrincipalWith(new Claim(ClaimTypes.NameIdentifier, "42"));

        Assert.Equal(42, principal.GetUserId());
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    public void GetUserId_InvalidValue_Throws(string claimValue)
    {
        var principal = PrincipalWith(new Claim(ClaimTypes.NameIdentifier, claimValue));

        Assert.Throws<InvalidOperationException>(() => principal.GetUserId());
    }

    [Fact]
    public void GetUserId_MissingClaim_Throws()
    {
        var principal = PrincipalWith();

        Assert.Throws<InvalidOperationException>(() => principal.GetUserId());
    }
}