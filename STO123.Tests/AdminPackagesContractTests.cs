using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using STO123.Controllers;
using STO123.DTOs.Admin;
using STO123.Services.Auth;

namespace STO123.Tests;

public sealed class AdminPackagesContractTests
{
    [Theory]
    [InlineData(typeof(AdminPackagesController))]
    [InlineData(typeof(AdminPackageMutationsController))]
    public void Package_controllers_require_management_policy(Type controller)
    {
        var authorize = Assert.Single(controller
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(AdminAuthorization.ManagementPolicy, authorize.Policy);
    }

    [Fact]
    public void Write_contract_rejects_price_snapshots_and_enrollments()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        const string valid = """{"name":"Combo","price":199000,"validDays":90,"description":null,"status":"DANG_MO","courseIds":[1,2]""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AdminPackageWriteRequest>(valid + ",\"registrationPrice\":1}", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AdminPackageWriteRequest>(valid + ",\"enrollments\":[]}", options));
    }
}
