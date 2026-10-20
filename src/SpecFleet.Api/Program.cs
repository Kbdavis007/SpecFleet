using SpecFleet.Api.Features.Authentication;
using SpecFleet.Api.Features.Chat;
using SpecFleet.Api.Features.ExceptionHandling;
using SpecFleet.Api.Features.GitHub;
using SpecFleet.Api.Features.Health;
using SpecFleet.Api.Features.OpenSpecOrchestration;
using SpecFleet.Service.Features.Chat.Actions;
using SpecFleet.Service.Features.Chat.Artifacts;
using SpecFleet.Service.Features.Chat.Ask;
using SpecFleet.Service.Features.Chat.Conversations;
using SpecFleet.Service.Features.Chat.Providers;
using SpecFleet.Service.Features.GitHubActions;
using SpecFleet.Service.Features.OpenSpecOrchestration;
using SpecFleet.Service.Features.TaskOrchestration;
using SpecFleet.Service.Features.TaskQueue;
using SpecFleet.Service.Features.WorkerTasks;

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
builder.Services.AddSingleton(new PostgreSqlArtifactActionSink(connectionString));
builder.Services.AddScoped(services => new ArtifactActionHandler(
    services.GetRequiredService<PostgreSqlArtifactActionSink>()));

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

var openSpecWorkspaceRoot = builder.Configuration["OpenSpec:WorkspaceRoot"]
    ?? Directory.GetCurrentDirectory();
builder.Services.AddSingleton(new OpenSpecWorkspaceGuard(openSpecWorkspaceRoot));
builder.Services.AddSingleton<IOpenSpecCommandRunner>(new ProcessOpenSpecCommandRunner(
    builder.Configuration["OpenSpec:Executable"] ?? "openspec"));
builder.Services.AddScoped(services => new OpenSpecOperationHandler(
    services.GetRequiredService<IOpenSpecCommandRunner>(),
    services.GetRequiredService<OpenSpecWorkspaceGuard>()));

builder.Services.AddSingleton(new PostgreSqlTaskQueue(connectionString));
builder.Services.AddSingleton<WorkerTaskPayloadValidator>();
builder.Services.AddScoped(services => new WorkerTaskOrchestrator(
    services.GetRequiredService<PostgreSqlTaskQueue>()));
builder.Services.AddScoped(services => new WorkerTaskHandler(
    services.GetRequiredService<WorkerTaskOrchestrator>(),
    services.GetRequiredService<WorkerTaskPayloadValidator>(),
    services.GetRequiredService<PostgreSqlTaskQueue>()));

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

// The UI is deployed independently from this API, so cross-origin calls are expected.
// Only origins listed in SpecFleet:Ui:AllowedOrigins are trusted.
var uiOrigins = builder.Configuration
    .GetSection("SpecFleet:Ui:AllowedOrigins")
    .Get<string[]>()
    ?? [];

builder.Services.AddCors(options =>
    options.AddPolicy(UiCorsPolicy, policy =>
    {
        if (uiOrigins.Length > 0)
        {
            policy
                .WithOrigins(uiOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    }));

// Serialize enums as readable strings so API responses such as worker task status are stable
// and self-describing for the UI.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

await app.Services.GetRequiredService<PostgreSqlTaskQueue>().InitializeAsync();
await app.Services.GetRequiredService<PostgreSqlArtifactActionSink>().InitializeAsync();

app.UseMiddleware<ApiExceptionMiddleware>();
app.UseCors(UiCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapChatEndpoints();
app.MapGitHubActionEndpoints();
app.MapOpenSpecOrchestrationEndpoints();
app.MapHealthEndpoints();

app.Run();

public partial class Program
{
    /// <summary>Name of the CORS policy applied to the independently hosted UI.</summary>
    public const string UiCorsPolicy = "specfleet-ui";
}
