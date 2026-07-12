using Microsoft.Extensions.DependencyInjection;

namespace Numera.Modules.Sales.Events;

/// <summary>
/// The in-process domain-event dispatch seam (RESEARCH.md Q8, ledger-readiness floor).
/// Finalize does its authoritative work — numbering, snapshots, VAT, open item, audit —
/// INSIDE the transaction; this publisher fires only AFTER commit for downstream
/// side-effects (Phase-4 PDF render, Phase-5 e-invoice, Phase-6 ledger posting). In v1
/// there are no handlers registered, so publishing is a no-op — but the seam exists so a
/// later phase adds a handler without a finalize rewrite.
/// </summary>
public interface IDomainEventPublisher
{
    /// <summary>Dispatches <paramref name="domainEvent"/> to every registered handler for its type.</summary>
    Task PublishAsync(object domainEvent, CancellationToken ct);
}

/// <summary>
/// Handles a domain event of type <typeparamref name="T"/>. Contravariant so a handler for
/// a base type also receives derived events. Register in DI to hook into finalize.
/// </summary>
/// <typeparam name="T">The domain-event type handled.</typeparam>
public interface IDomainEventHandler<in T>
{
    /// <summary>Reacts to the dispatched event.</summary>
    Task HandleAsync(T domainEvent, CancellationToken ct);
}

/// <summary>
/// The default <see cref="IDomainEventPublisher"/> — resolves every
/// <see cref="IDomainEventHandler{T}"/> for the concrete event type from the request scope
/// and invokes them in turn. Deliberately tiny (NO MediatR — commercial per STACK.md).
/// With zero handlers registered it is a silent no-op, which is the v1 floor.
/// </summary>
public sealed class InProcessDomainEventPublisher : IDomainEventPublisher
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the publisher over the request-scoped service provider.</summary>
    public InProcessDomainEventPublisher(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    public async Task PublishAsync(object domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
        var handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<object>.HandleAsync))!;

        foreach (var handler in _services.GetServices(handlerType))
        {
            if (handler is null)
            {
                continue;
            }

            await ((Task)handleMethod.Invoke(handler, [domainEvent, ct])!).ConfigureAwait(false);
        }
    }
}
