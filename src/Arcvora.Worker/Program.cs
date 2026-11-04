using Arcvora.Infrastructure;
using Arcvora.Infrastructure.Jobs;

// Üretim işleyicisi: API'den ayrı süreç. Aynı veritabanındaki dayanıklı iş kayıtlarını tüketir (§13.1).
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddArcvoraInfrastructure(builder.Configuration);
builder.Services.AddHostedService<JobWorkerService>();

using var host = builder.Build();
await host.RunAsync();
