using FluentValidation;
using MediatorF;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

var asm = typeof(Program).Assembly;
builder.Services.AddValidatorsFromAssembly(asm);
builder.Services.AddMediatorHandlers(asm);
builder.Services.AddPublisher(asm);
builder.Services.AddTransient<IMediator>(p =>
{
    var logger = p.GetRequiredService<ILogger<LoggingMediatorDecorator>>();
    var mediator = p.GetRequiredService<Mediator>();
    return new ValidatorMediatorDecorator(p, new LoggingMediatorDecorator(logger, mediator));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseExceptionHandler(cfg =>
{
    cfg.Run(async ctx =>
    {
        var ctxFeature = ctx.Features.Get<IExceptionHandlerFeature>();
        if (ctxFeature is not null)
        {
            switch (ctxFeature.Error)
            {
                case MediatorF.ValidationException:
                    var exception = (MediatorF.ValidationException)ctxFeature.Error;
                    await Results.UnprocessableEntity(new ValidationProblemDetails(exception.Errors)).ExecuteAsync(ctx);
                    break;
                default:
                    await Results.Problem(detail: ctxFeature.Error.Message).ExecuteAsync(ctx);
                    break;
            }
        }
    });
});

app.MapPost("/command",
    async ([FromServices] IMediator mediator,
    [FromQuery] string? nombre,
    CancellationToken ct)
    => await mediator.Send(new GreetingCommand(nombre), ct));

app.MapGet("/query",
    async ([FromServices] IMediator mediator,
    [FromQuery] string? nombre,
    CancellationToken ct) =>
{
    string str = await mediator.Send(new GreetingStrQuery(nombre), ct);
    return Results.Ok(str);
});

app.Run();

public record GreetingCommand(string? Nombre) : IRequest;
public class GreetingCommandHandler(IPublisher publisher) : IRequestHandler<GreetingCommand>
{
    private readonly IPublisher _publisher = publisher;

    public async Task Handle(GreetingCommand request, CancellationToken ct)
    {
        Console.WriteLine($"Hello {request.Nombre}!");
        await _publisher.Publish(new GreetedNotification(), ct);
    }
}

public class GreetingCommandValidation : AbstractValidator<GreetingCommand>
{
    public GreetingCommandValidation()
    {
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("Nombre requerido command");
    }
}

public class GreetingStrQueryValidation : AbstractValidator<GreetingStrQuery>
{
    public GreetingStrQueryValidation()
    {
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("Nombre requerido query");
    }
}

public record GreetingStrQuery(string? Nombre) : IRequest<string>;

public class GreetingStrQueryHandler(IPublisher publisher) : IRequestHandler<GreetingStrQuery, string>
{
    private readonly IPublisher _publisher = publisher;

    public async Task<string> Handle(GreetingStrQuery request, CancellationToken ct)
    {
        await _publisher.Publish(new OtherNotification(), ct);
        return $"Hello {request.Nombre}!";
    }
}

public record GreetedNotification : INotification;
public record OtherNotification : INotification;

public class GreetedEvent1NotificationHandler : INotificationHandler<GreetedNotification>
{
    public Task Handle(GreetedNotification notification, CancellationToken ct)
    {
        Console.WriteLine("Event 1");
        return Task.CompletedTask;
    }
}

public class GreetedEvent2NotificationHandler : INotificationHandler<GreetedNotification>
{
    public Task Handle(GreetedNotification notification, CancellationToken ct)
    {
        Console.WriteLine("Event 2");
        return Task.CompletedTask;
    }
}

public class OtherNotificationHandler : INotificationHandler<OtherNotification>
{
    public Task Handle(OtherNotification notification, CancellationToken ct)
    {
        Console.WriteLine("Random Event");
        return Task.CompletedTask;
    }
}

