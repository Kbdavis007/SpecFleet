using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SpecFleet.Api.Features.ExceptionHandling;

namespace SpecFleet.Test.Api;

public sealed class ApiExceptionMiddlewareTests
{
    [Fact]
    public async Task Unexpected_exception_returns_generic_problem_and_does_not_leak_details()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.MapGet("/unexpected", ThrowUnexpected);
        await app.StartAsync();

        try
        {
            var address = app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!
                .Addresses
                .Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            using var response = await client.GetAsync("/unexpected");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("An unexpected error occurred.", body);
            Assert.DoesNotContain("internal-secret", body);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static IResult ThrowUnexpected() =>
        throw new InvalidOperationException("internal-secret");
}
