using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace MediatorKiller;

/// <summary>
/// Defines a publisher for sending notifications to their corresponding handlers.
/// </summary>
public interface IPublisher
{
    /// <summary>
    /// Publishes a notification to all registered handlers.
    /// </summary>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    Task Publish(INotification notification, CancellationToken ct);
}

/// <summary>
/// Marker interface representing a notification to be published.
/// </summary>
public interface INotification { }

/// <summary>
/// Defines a handler for processing a specific type of notification.
/// </summary>
/// <typeparam name="T">The type of notification handled.</typeparam>
public interface INotificationHandler<T>
    where T : INotification
{
    /// <summary>
    /// Handles the specified notification asynchronously.
    /// </summary>
    /// <param name="notification">The notification to handle.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    Task Handle(T notification, CancellationToken ct);
}

/// <summary>
/// Default implementation of <see cref="IPublisher"/> using dependency injection to resolve handlers.
/// </summary>
/// <param name="serviceProvider">Service provider used for resolving handlers.</param>
public class Publisher(IServiceProvider serviceProvider) : IPublisher
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    /// <inheritdoc/>
    public async Task Publish(INotification notification, CancellationToken ct)
    {
        var notificationT = notification.GetType();
        var handlerType = typeof(INotificationHandler<>).MakeGenericType(notificationT);
        var hs = _serviceProvider.GetServices(handlerType);
        foreach (var h in hs)
        {
            var method = handlerType.GetMethod(nameof(INotificationHandler<INotification>.Handle));
            await (Task)method!.Invoke(h, [notification, ct])!;
        }
    }
}

/// <summary>
/// Provides extension methods for registering notification handlers and the publisher with the dependency injection container.
/// </summary>
public static class PublisherExtensions
{
    /// <summary>
    /// Scans the specified assemblies for implementations of <see cref="INotificationHandler{T}"/>
    /// and registers them with the dependency injection container, along with the <see cref="IPublisher"/> implementation.
    /// </summary>
    /// <param name="services">The service collection to add the handlers and publisher to.</param>
    /// <param name="assemblies">Assemblies to scan for handler implementations.</param>
    public static void AddPublisher(this IServiceCollection services, params Assembly[] assemblies)
    {
        var types = assemblies.SelectMany(asm => asm.GetTypes()).ToList();

        (Type notification, Type handler)[] nhTypes = types
           .Where(t => t is { IsInterface: false, IsAbstract: false } && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)))
           .Select(t => (t!.GetInterfaces().First().GenericTypeArguments[0], t))
           .ToArray();

        foreach (var (notificationT, handlerT) in nhTypes)
        {
            var iT = handlerT.GetInterfaces().First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>));

            services.AddTransient(iT, handlerT);
        }
        services.AddTransient<IPublisher, Publisher>();
    }
}
