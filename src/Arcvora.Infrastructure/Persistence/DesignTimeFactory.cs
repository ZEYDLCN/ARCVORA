using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Arcvora.Infrastructure.Persistence;

/// <summary>`dotnet ef` araçları için.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<ArcvoraDbContext>
{
    public ArcvoraDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ARCVORA_CONNECTION")
                 ?? "Host=localhost;Database=arcvora_design;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<ArcvoraDbContext>().UseNpgsql(cs).Options;
        return new ArcvoraDbContext(options);
    }
}
