using NSFinance.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Worker"));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
