using Arcvora.Core.Ai;
using Arcvora.Infrastructure.Ai;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Jobs;
using Arcvora.Infrastructure.Persistence;
using Arcvora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arcvora.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddArcvoraInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.Configure<AiOptions>(config.GetSection("Ai"));
        services.Configure<JobOptions>(config.GetSection("Jobs"));

        var connection = config.GetConnectionString("Arcvora")
                         ?? throw new InvalidOperationException("ConnectionStrings:Arcvora yapılandırılmamış.");
        services.AddDbContext<ArcvoraDbContext>(o => o.UseNpgsql(connection));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PasswordHasher>();
        services.AddSingleton<TokenService>();

        services.AddScoped<AuditLog>();
        services.AddScoped<AuthService>();
        services.AddScoped<ProjectAccessService>();
        services.AddScoped<VersionStore>();
        services.AddScoped<ProjectService>();
        services.AddScoped<ProposalService>();
        services.AddScoped<JobService>();
        services.AddScoped<ExportService>();
        services.AddScoped<SampleService>();
        services.AddScoped<MemberService>();
        services.AddScoped<GenerationJobHandler>();
        services.AddSingleton<JobRunner>();

        // Üretici seçimi: yanlış yapılandırma sessizce AI'ya düşmez, başlangıçta yüksek sesle hata verir.
        var ai = config.GetSection("Ai").Get<AiOptions>() ?? new AiOptions();
        switch (ai.Provider.ToLowerInvariant())
        {
            case "anthropic":
                if (string.IsNullOrWhiteSpace(ai.Anthropic.ApiKey))
                    throw new InvalidOperationException("Ai:Provider=anthropic için Ai:Anthropic:ApiKey gerekli.");
                services.AddHttpClient<ILlmClient, AnthropicLlmClient>(c => c.Timeout = TimeSpan.FromSeconds(ai.TimeoutSeconds + 30));
                services.AddSingleton<IArchitectureGenerator>(sp =>
                    new LlmArchitectureGenerator(sp.GetRequiredService<ILlmClient>(), ai.Anthropic.MaxOutputTokens));
                break;
            case "local":
                if (string.IsNullOrWhiteSpace(ai.Local.Model))
                    throw new InvalidOperationException("Ai:Provider=local için Ai:Local:Model gerekli (örn. qwen2.5:7b-instruct).");
                services.AddHttpClient<ILlmClient, OpenAiCompatibleLlmClient>(c => c.Timeout = TimeSpan.FromSeconds(ai.TimeoutSeconds + 30));
                services.AddSingleton<IArchitectureGenerator>(sp =>
                    new LlmArchitectureGenerator(sp.GetRequiredService<ILlmClient>(), ai.Local.MaxOutputTokens, DraftSchema.Get()));
                break;
            case "heuristic":
                services.AddSingleton<IArchitectureGenerator, HeuristicGenerator>();
                break;
            default:
                throw new InvalidOperationException($"Bilinmeyen Ai:Provider '{ai.Provider}' (heuristic | anthropic | local).");
        }

        return services;
    }
}
