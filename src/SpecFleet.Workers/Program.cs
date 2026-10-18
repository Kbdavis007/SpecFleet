var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<HostOptions>(options =>
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);

var host = builder.Build();
host.Run();
