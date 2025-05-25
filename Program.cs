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
public class GreetingCommandHandler : IRequestHandler<GreetingCommand>
{
    public Task Handle(GreetingCommand request, CancellationToken ct)
    {
        Console.WriteLine($"Hello {request.Nombre}!");
        return Task.CompletedTask;
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

public class GreetingStrQueryHandler : IRequestHandler<GreetingStrQuery, string>
{
    public Task<string> Handle(GreetingStrQuery request, CancellationToken ct)
    {
        return Task.FromResult($"Hello {request.Nombre}!");
    }
}