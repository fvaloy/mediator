using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace MediatorKiller;

/// <summary>
/// Defines a mediator for sending requests to their corresponding handlers.
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Sends a request that does not return a result.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    Task Send(IRequest request, CancellationToken ct);

    /// <summary>
    /// Sends a request and returns a result.
    /// </summary>
    /// <typeparam name="TOut">The type of the result returned by the request.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A task that represents the asynchronous operation, containing the result.</returns>
    Task<TOut> Send<TOut>(IRequest<TOut> request, CancellationToken ct);
}

/// <summary>
/// Marker interface for a request that does not produce a result.
/// </summary>
public interface IRequest { }

/// <summary>
/// Marker interface for a request that produces a result of type <typeparamref name="TOut"/>.
/// </summary>
/// <typeparam name="TOut">The type of the result produced by the request.</typeparam>
public interface IRequest<TOut> : IRequest { }

/// <summary>
/// Defines a handler for a request that produces a result.
/// </summary>
/// <typeparam name="T">The type of request handled.</typeparam>
/// <typeparam name="TOut">The type of the result produced by the handler.</typeparam>
public interface IRequestHandler<T, TOut>
    where T : IRequest<TOut>
{
    /// <summary>
    /// Handles the specified request asynchronously.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A task that represents the asynchronous operation, containing the result.</returns>
    Task<TOut> Handle(T request, CancellationToken ct);
}

/// <summary>
/// Defines a handler for a request that does not produce a result.
/// </summary>
/// <typeparam name="T">The type of request handled.</typeparam>
public interface IRequestHandler<T>
    where T : IRequest
{
    /// <summary>
    /// Handles the specified request asynchronously.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    Task Handle(T request, CancellationToken ct);
}

/// <summary>
/// Default implementation of the <see cref="IMediator"/> interface using dependency injection for resolving handlers.
/// </summary>
public class Mediator(IServiceProvider serviceProvider) : IMediator
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    /// <inheritdoc/>
    public Task Send(IRequest request, CancellationToken ct)
    {
        var requestType = request.GetType();
        var handlerType = typeof(IRequestHandler<>).MakeGenericType(requestType);
        var h = _serviceProvider.GetRequiredService(handlerType);
        var method = handlerType.GetMethod(nameof(IRequestHandler<IRequest>.Handle));
        return (Task)method!.Invoke(h, [request, ct])!;
    }

    /// <inheritdoc/>
    public Task<TOut> Send<TOut>(IRequest<TOut> request, CancellationToken ct)
    {
        var requestType = request.GetType();
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TOut));
        var h = _serviceProvider.GetRequiredService(handlerType);
        var method = handlerType.GetMethod(nameof(IRequestHandler<IRequest>.Handle));
        return (Task<TOut>)method!.Invoke(h, [request, ct])!;
    }
}

/// <summary>
/// Provides extension methods for registering mediator handlers with the dependency injection container.
/// </summary>
public static class MediatorExtensions
{
    /// <summary>
    /// Scans the specified assemblies for implementations of <see cref="IRequestHandler{T}"/> and <see cref="IRequestHandler{T, TOut}"/> 
    /// and registers them with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to add the handlers to.</param>
    /// <param name="assemblies">Assemblies to scan for handler implementations.</param>
    public static void AddMediatorHandlers(this IServiceCollection services, params Assembly[] assemblies)
    {
        var fht = typeof(IRequestHandler<,>);
        var pht = typeof(IRequestHandler<>);
        var types = assemblies.SelectMany(asm => asm.GetTypes()).ToList();

        (Type request, Type handler)[] rhTypes = types
           .Where(t => t is { IsInterface: false, IsAbstract: false } && t.GetInterfaces().Any(i => i.IsGenericType && (i.GetGenericTypeDefinition() == pht || i.GetGenericTypeDefinition() == fht)))
           .Select(t => (t!.GetInterfaces().First().GenericTypeArguments[0], t))
           .ToArray();

        foreach (var (requestT, handlerT) in rhTypes)
        {
            var iT = handlerT.GetInterfaces().First(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
                || i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<>));

            services.AddTransient(iT, handlerT);
        }

        services.AddTransient<Mediator>();
    }
}

/// <summary>
/// Marker interface for a decorator implementation of <see cref="IMediator"/>.
/// </summary>
public interface IMediatorDecorator : IMediator { }