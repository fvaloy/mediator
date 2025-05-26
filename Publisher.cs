using System.Reflection;

namespace MediatorF;

public interface IPublisher
{
    Task Publish(INotification notification, CancellationToken ct);
}

public interface INotification { }

public interface INotificationHandler<T>
    where T : INotification
{
    Task Handle(T notification, CancellationToken ct);
}

public class Publisher(IServiceProvider serviceProvider) : IPublisher
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

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

public static class PublisherExtensions
{
    public static void AddPublisher(this IServiceCollection services, params Assembly[] assemblies)
    {
        var types = assemblies.SelectMany(asm => asm.GetTypes()).ToList();

        (Type notification, Type handler)[] nhTypes = types
           .Where(t => t is { IsInterface: false, IsAbstract: false } && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)))
           .Select(t => (t!.GetInterfaces().First().GenericTypeArguments[0], t))
           .ToArray();

        foreach (var (notificationT, handlerT) in nhTypes)
        {
            var iT = handlerT.GetInterfaces().First(i => (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)));

            services.AddTransient(iT, handlerT);
        }
        services.AddTransient<IPublisher, Publisher>();
    }
}
