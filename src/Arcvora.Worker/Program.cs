var builder = Host.CreateApplicationBuilder(args);
using var host = builder.Build();
await host.RunAsync();
