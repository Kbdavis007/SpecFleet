using SpecFleet.Api.Features.Authentication;
using SpecFleet.Api.Features.Chat;
using SpecFleet.Api.Features.ExceptionHandling;
using SpecFleet.Api.Features.GitHub;
using SpecFleet.Api.Features.Health;
using SpecFleet.Service.Features.Chat.Artifacts;
using SpecFleet.Service.Features.Chat.Ask;
using SpecFleet.Service.Features.Chat.Conversations;
using SpecFleet.Service.Features.Chat.Providers;
using SpecFleet.Service.Features.GitHubActions;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("SpecFleet")
    ?? builder.Configuration["SpecFleet:ConnectionString"]
    ?? string.Empty;

var configuredTokens = builder.Configuration
    .GetSection("SpecFleet:Auth:Tokens")
    .Get<Dictionary<string, string>>()
    ?? new Dictionary<string, string>(StringComparer.Ordinal);

builder.Services.AddSingleton(new PostgreSqlConversationStore(connectionString));
builder.Services.AddSingleton<ArtifactValidator>();

builder.Services.AddSingleton<IChatCompletionProvider>(services =>
{
    var apiKey = builder.Configuration["Gemini:ApiKey"];
    var model = builder.Configuration["Gemini:Model"];
    if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
    {
        return new UnconfiguredChatCompletionProvider();
    }

    return new GeminiChatCompletionProvider(
        services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GeminiChatCompletionProvider)),
        apiKey,
        model,
        builder.Configuration["Gemini:BaseUrl"] ?? "https://generativelanguage.googleapis.com");
});

builder.Services.AddHttpClient(nameof(GeminiChatCompletionProvider));

var gitHubToken = builder.Configuration["GitHub:AccessToken"] ?? string.Empty;
var allowedWorkflows = builder.Configuration
    .GetSection("GitHub:AllowedWorkflows")
    .Get<string[]>()
    ?? [];

builder.Services.AddHttpClient("github", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["GitHub:BaseUrl"] ?? "https://api.github.com");
});

builder.Services.AddSingleton<IGitHubClient>(services => new HttpGitHubClient(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("github"),
    gitHubToken));

builder.Services.AddSingleton(new GitHubRepositoryPolicy(allowedWorkflows));
builder.Services.AddScoped(services => new GitHubActionHandler(
    services.GetRequiredService<IGitHubClient>(),
    services.GetRequiredService<GitHubRepositoryPolicy>()));

builder.Services.AddScoped(services => new ChatAskHandler(
    services.GetRequiredService<PostgreSqlConversationStore>(),
    services.GetRequiredService<IChatCompletionProvider>(),
    services.GetRequiredService<ArtifactValidator>()));

builder.Services
    .AddAuthentication(BearerTokenAuthenticationOptions.SchemeName)
    .AddScheme<BearerTokenAuthenticationOptions, BearerTokenAuthenticationHandler>(
        BearerTokenAuthenticationOptions.SchemeName,
        options => options.Tokens = configuredTokens);

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseMiddleware<ApiExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapChatEndpoints();
app.MapGitHubActionEndpoints();
app.MapHealthEndpoints();

app.Run();

public partial class Program;
