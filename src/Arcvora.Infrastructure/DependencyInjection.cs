using Arcvora.Core.Ai;
using Arcvora.Infrastructure.Ai;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Files;
using Arcvora.Infrastructure.Jobs;
using Arcvora.Infrastructure.Mail;
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
        services.Configure<AuthOptions>(config.GetSection("Auth"));
        services.Configure<AccountingOptions>(config.GetSection("Accounting"));
        services.Configure<AttachmentOptions>(config.GetSection("Attachments"));
        services.Configure<MaintenanceOptions>(config.GetSection("Maintenance"));
        services.Configure<DocumentOptions>(config.GetSection("Documents"));
        services.Configure<LocaleOptions>(config.GetSection("Locale"));
        services.Configure<EmailOptions>(config.GetSection("Email"));
        var email = config.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
        if (email.Enabled)
        {
            if (string.IsNullOrWhiteSpace(email.Host) || string.IsNullOrWhiteSpace(email.From))
                throw new InvalidOperationException("Email:Enabled=true için Email:Host ve Email:From gerekli.");
            if (email.Security.ToLowerInvariant() is not ("starttls" or "ssl" or "none"))
                throw new InvalidOperationException("Email:Security 'starttls', 'ssl' veya 'none' olmalı.");
        }
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<EmailDispatcher>();
        services.AddSingleton<MaintenanceService>();

        // DATABASE_URL, barındırıcıların (Render, Koyeb, Fly…) yerleşik kuralıdır ve verilmişse önceliklidir; postgres:// adresi de kabul edilir.
        var connection = ConnectionStringNormalizer.Normalize(
            config["DATABASE_URL"] is { Length: > 0 } url ? url
            : config.GetConnectionString("Arcvora") ?? throw new InvalidOperationException("ConnectionStrings:Arcvora yapılandırılmamış."));
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
        services.AddScoped<GroupService>();
        services.AddScoped<AppAccessService>();
        services.AddScoped<AppDesignService>();
        services.AddScoped<AttachmentService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<DelegationService>();
        services.AddScoped<DemoDataService>();
        services.AddScoped<RequestVisibility>();
        services.AddScoped<RequestService>();
        services.AddScoped<IRequestHooks, AccountingHooks>();
        services.AddScoped<AccountingService>();
        var attachments = config.GetSection("Attachments").Get<AttachmentOptions>() ?? new AttachmentOptions();
        if (attachments.MaxSizeMb is < 1 or > 25) throw new InvalidOperationException("Attachments:MaxSizeMb 1–25 arasında olmalı.");
        switch (attachments.Store.ToLowerInvariant())
        {
            case "database": services.AddScoped<IFileStore, DbFileStore>(); break;
            case "disk":
                if (string.IsNullOrWhiteSpace(attachments.Path)) throw new InvalidOperationException("Attachments:Store=disk için Attachments:Path gerekli.");
                services.AddSingleton<IFileStore>(new DiskFileStore(Path.GetFullPath(attachments.Path)));
                break;
            default: throw new InvalidOperationException($"Bilinmeyen Attachments:Store '{attachments.Store}' (database | disk).");
        }
        services.AddHttpClient(AccountingDispatcher.ClientName, c => c.Timeout = TimeSpan.FromSeconds(config.GetValue("Accounting:TimeoutSeconds", 15)));
        services.AddSingleton<AccountingDispatcher>();
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
