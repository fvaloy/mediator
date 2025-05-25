using System.Reflection;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

var ft = typeof(IRequestHandler<,>);
var pt = typeof(IRequestHandler<>);
var types = AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(asm =>
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }).ToList();

var hts = types
   .Where(t => !t!.IsInterface && !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == pt)
   || !t!.IsInterface && !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == ft))
   .ToArray();

foreach (var ht in hts)
{
    builder.Services.AddScoped(ht!);
}

builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<IMediator, Mediator>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/command", async ([FromServices] IMediator mediator, CancellationToken ct) => await mediator.Send(new GreetingCommand(), ct));
app.MapGet("/query", async ([FromServices] IMediator mediator, CancellationToken ct) =>
{
    string str = await mediator.Send(new GreetingStrQuery(), ct);
    return Results.Ok(str);
});

app.Run();

public record GreetingCommand : IRequest;
public class GreetingCommandHandler : IRequestHandler<GreetingCommand>
{
    public Task Handle(GreetingCommand request, CancellationToken ct)
    {
        Console.WriteLine("Hello world!");
        return Task.CompletedTask;
    }
}

public record GreetingStrQuery : IRequest<string>;

public class GreetingStrQueryHandler : IRequestHandler<GreetingStrQuery, string>
{
    public Task<string> Handle(GreetingStrQuery request, CancellationToken ct)
    {
        return Task.FromResult("Hello world!");
    }
}

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

public class Mediator : IMediator
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDictionary<Type, Type> _procHandlers = new Dictionary<Type, Type>();
    private readonly IDictionary<Type, Type> _funcHandlers = new Dictionary<Type, Type>();

    public Mediator(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
        var funcT = typeof(IRequestHandler<,>);
        var procT = typeof(IRequestHandler<>);
        var ats = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try
                    {
                        return a.GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        return e.Types.Where(t => t != null);
                    }
                }).ToList();

        (Type request, Type handler)[] procHandlers = ats
           .Where(t => !t!.IsInterface && !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == procT))
           .Select(t => (t!.GetInterfaces().First().GenericTypeArguments[0], t))
           .ToArray();

        (Type request, Type handler)[] funcHandlers = ats
           .Where(t => !t!.IsInterface && !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == funcT))
           .Select(t => (t!.GetInterfaces().First().GenericTypeArguments[0], t))
           .ToArray();

        foreach ((Type request, Type handler) in procHandlers)
            _procHandlers[request] = handler;

        foreach ((Type request, Type handler) in funcHandlers)
            _funcHandlers[request] = handler;
    }

    public Task Send(IRequest request, CancellationToken ct)
    {
        var ht = _procHandlers[request.GetType()];
        var h = (dynamic)_httpContextAccessor.HttpContext!.RequestServices.GetRequiredService(ht);
        return h.Handle((dynamic)request, ct);
    }

    public Task<TOut> Send<TOut>(IRequest<TOut> request, CancellationToken ct)
    {
        var ht = _funcHandlers[request.GetType()];
        var h = (dynamic)_httpContextAccessor.HttpContext!.RequestServices.GetRequiredService(ht);
        return h.Handle((dynamic)request, ct);
    }
}
