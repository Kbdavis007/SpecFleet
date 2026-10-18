using SpecFleet.Api.Features.ExceptionHandling;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseMiddleware<ApiExceptionMiddleware>();

app.Run();

public partial class Program;
