using System.Diagnostics;
using System.Reflection;
using FluentValidation;
using FluentValidation.Results;

namespace MediatorF;

public interface IMediator
{
    Task Send(IRequest request, CancellationToken ct);
    Task<TOut> Send<TOut>(IRequest<TOut> request, CancellationToken ct);
}

public interface IRequest { }

public interface IRequest<TOut> : IRequest { }

public interface IRequestHandler<T, TOut>
    where T : IRequest<TOut>
{
    Task<TOut> Handle(T request, CancellationToken ct);
}

public interface IRequestHandler<T>
    where T : IRequest
{
    Task Handle(T request, CancellationToken ct);
}

public class Mediator(IServiceProvider serviceProvider) : IMediator
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public Task Send(IRequest request, CancellationToken ct)
    {
        var requestType = request.GetType();
        var handlerType = typeof(IRequestHandler<>).MakeGenericType(requestType);
        var h = (dynamic)_serviceProvider.GetRequiredService(handlerType);
        return h!.Handle((dynamic)request, ct);
    }

    public Task<TOut> Send<TOut>(IRequest<TOut> request, CancellationToken ct)
    {
        var requestType = request.GetType();
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TOut));
        var h = (dynamic)_serviceProvider.GetRequiredService(handlerType);
        return h!.Handle((dynamic)request, ct);
    }
}

public static class MediatorExtensions
{
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
            string keyName = requestT.Name;

            var iT = handlerT.GetInterfaces().First(i =>
                (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<>)));

            services.AddTransient(iT, handlerT);
        }

        services.AddTransient<Mediator>();
    }
}

public interface IMediatorDecorator : IMediator { }

public class LoggingMediatorDecorator(
    ILogger<LoggingMediatorDecorator> logger,
    IMediator mediator) : IMediatorDecorator
{
    private readonly ILogger<LoggingMediatorDecorator> _logger = logger;
    private readonly IMediator _mediator = mediator;

    public async Task Send(IRequest request, CancellationToken ct)
    {
        var stopWatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting execution for {Request}", request);
        await _mediator.Send(request, ct);
        stopWatch.Stop();
        _logger.LogInformation("Finished execution for {Request} in {ElapsedMilliseconds} ms", request, stopWatch.ElapsedMilliseconds);
    }

    public async Task<TOut> Send<TOut>(IRequest<TOut> request, CancellationToken ct)
    {
        var stopWatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting execution for {Request}", request);
        var response = await _mediator.Send(request, ct);
        stopWatch.Stop();
        _logger.LogInformation("Finished execution for {Request} in {ElapsedMilliseconds} ms", request, stopWatch.ElapsedMilliseconds);
        return response;
    }
}

public class ValidatorMediatorDecorator(
    IServiceProvider serviceProvider,
    IMediator mediator) : IMediator
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IMediator _mediator = mediator;

    public async Task Send(IRequest request, CancellationToken ct)
    {
        await ValidateAsync(request, ct);
        await _mediator.Send(request, ct);
    }

    public async Task<TOut> Send<TOut>(IRequest<TOut> request, CancellationToken ct)
    {
        await ValidateAsync(request, ct);
        return await _mediator.Send(request, ct);
    }

    public async Task ValidateAsync(IRequest request, CancellationToken ct)
    {
        var validatorType = typeof(IValidator<>).MakeGenericType(request.GetType());
        var validators = _serviceProvider.GetServices(validatorType);
        if (validators.Any())
        {
            var context = new ValidationContext<object>(request);
            var validationResults = await Task.WhenAll(validators.Select(v => ((IValidator)v!).ValidateAsync(context, ct)));
            var failures = validationResults
                .Where(vr => vr.Errors.Count != 0)
                .SelectMany(vr => vr.Errors)
                .ToList();
            if (failures.Count != 0)
                throw new MediatorF.ValidationException(failures);
        }
    }
}

public class ValidationException(IEnumerable<ValidationFailure> failures) : Exception
{
    public IDictionary<string, string[]> Errors { get; } = failures
        .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
        .ToDictionary(failureGroup => failureGroup.Key, failureGroup => failureGroup.ToArray());
}
