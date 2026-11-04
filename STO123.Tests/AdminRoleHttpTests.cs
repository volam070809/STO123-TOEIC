using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using STO123.Controllers;
using STO123.Services.Auth;

namespace STO123.Tests;

public sealed class AdminRoleHttpTests
{
    [Fact]
    public async Task CrossRoleAndLearnerWriteRequestsReturnHttp403()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddControllers().AddApplicationPart(typeof(AdminLearnersController).Assembly);
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, RoleHandler>("Test", _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy(AdminAuthorization.ManagementPolicy,
            policy => policy.RequireRole(AdminAuthorization.ManagementRole)));
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        async Task AssertForbidden(string role, HttpMethod method, string path)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Test-Role", role);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        await AssertForbidden("ADMIN_NOI_DUNG", HttpMethod.Get, "/api/admin/learners");
        await AssertForbidden("ADMIN_QUAN_LY", HttpMethod.Get, "/api/admin/courses");
        await AssertForbidden("ADMIN_QUAN_LY", HttpMethod.Get, "/api/admin/content/questions/units");
        await AssertForbidden("ADMIN_NOI_DUNG", HttpMethod.Get, "/api/admin/payments");
        await AssertForbidden("ADMIN_QUAN_LY", HttpMethod.Put, "/api/tu-vung/1/progress");
    }

    private sealed class RoleHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) :
        AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (string.IsNullOrWhiteSpace(role)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1"),
                new Claim(ClaimTypes.Role, role)], "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(identity), "Test")));
        }
    }
}
