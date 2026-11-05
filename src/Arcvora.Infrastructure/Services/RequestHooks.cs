using Arcvora.Core.Flows;
using Arcvora.Infrastructure.Persistence;

namespace Arcvora.Infrastructure.Services;

/// <summary>
/// Talep yaşam döngüsüne takılan uzantılar (muhasebe aktarımı). <see cref="OnApprovedAsync"/> talebin onaylandığı işlemde çağrılır ve
/// yalnızca bağlama kayıt ekler (SaveChanges çağırmaz); böylece onay ile muhasebe kaydı aynı işlemde yazılır (transactional outbox).
/// </summary>
public interface IRequestHooks
{
    Task OnApprovedAsync(FlowRequest request, AppDefinition app, FlowDefinition def, IReadOnlyDictionary<string, object?> values, CancellationToken ct);

    /// <summary>Talep eden, atanan veya master admin dışında bir kullanıcının talebi görüp göremeyeceği (ör. muhasebe grubu).</summary>
    Task<bool> CanViewAsync(Guid userId, FlowRequest request, CancellationToken ct);
}

public sealed class NoOpRequestHooks : IRequestHooks
{
    public Task OnApprovedAsync(FlowRequest request, AppDefinition app, FlowDefinition def, IReadOnlyDictionary<string, object?> values, CancellationToken ct) => Task.CompletedTask;
    public Task<bool> CanViewAsync(Guid userId, FlowRequest request, CancellationToken ct) => Task.FromResult(false);
}
