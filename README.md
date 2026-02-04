![Shroud Logo](logo.png)

# Shroud
Decorators for Lazy People :)

Shroud is a C# library that makes decorating classes easier and more intuitive. 
Some decorator patterns require you to implement the decorated interface on the decorator class.
However, Shroud, using Code Generation, only requires that you define the decorator class, and 
it handles the rest for you.

# Installing

Install the pre-release NuGet package:

```bash
dotnet add package Shroud --prerelease
```

# Usage

(See `Shroud.Examples` for a working example)

```cs
using Shroud;
```

The first step is to create a decorator. The `BaseDecorator` provides a few points for you to override
to provide your custom logic. They are:

- `PreAction`: Called before the decorated method is called.
- `PostAction`: Called after the decorated method is called.
- `ErrorAction`: Called if the decorated method throws an exception.
- `PreActionAsync`: Called before the decorated async method is called.
- `PostActionAsync`: Called after the decorated async method is called.
- `ErrorActionAsync`: Called if the decorated async method throws an exception.

The Async methods are only called for decorated async methods.

Creating a Decorator method is simple. Here's a common one you might use:

```cs
public abstract class LoggingDecorator<T> : BaseDecorator<T>
{
    private readonly ILogger _logger;

    protected LoggingDecorator(T decorated, ILogger logger) : base(decorated)
    {
        _logger = logger;
    }

    // ... 
}
```

Then, you can override the methods you want to use:

```cs
    protected override void PreAction(MethodInfo methodInfo, object[] args)
    {
        _logger.LogInformation($"Calling method {methodInfo.Name} with arguments {string.Join(", ", args)}");
    }

    protected override void PostAction(MethodInfo methodInfo, object[] args, object result)
    {
        _logger.LogInformation($"Method {methodInfo.Name} returned {result}");
    }

    protected override void ErrorAction(MethodInfo methodInfo, object[] args, Exception exception)
    {
        _logger.LogError(exception, $"Method {methodInfo.Name} threw an exception");
    }
```

Adding a decorator to an interface is as simple as adding the `Decorate` attribute to the interface:

```cs
[Decorate(typeof(LoggingDecorator<>))]
public interface IMyService
{
    void DoSomething(string message);
    Task<string> DoSomethingAsync(string message);
}
```

This will generate a decorator class that implements the interface and extends the LoggingDecorator.
The attribute accepts multiple decorators.

You can also decorate specific methods directly:

```cs
public interface IMyService
{
    [Decorate(typeof(LoggingDecorator<>))]
    void DoSomething(string message);
}
```

Finally, you must register the decorators.

```cs
// register your services as normal
builder.Services.AddScoped<IMyService, MyService>();
// ...

// register a decorator for a specific interface
builder.Services.RegisterDecorator(typeof(AuditDecorator<>), typeof(IMyService));

// register shroud
builder.Services.Enshroud(); 
```

This will take all your decorated interfaces and wrap them in the decorators in the order you
specified.

> Note: `RegisterDecorator` is picked up by the source generator at build time. The call itself is
> intentionally a no-op at runtime; it exists to declare which decorators should be generated and
> applied by `Enshroud`.

## Constructor dependencies in decorators

Decorators can take additional constructor dependencies, and Shroud will resolve them from the
service provider when building the decorator chain. For example:

```cs
public interface IAuditSink
{
    void Write(string message);
}

public sealed class ConsoleAuditSink : IAuditSink
{
    public void Write(string message) => Console.WriteLine(message);
}

public sealed class AuditDecorator<T> : BaseDecorator<T>
{
    private readonly IAuditSink _auditSink;

    public AuditDecorator(T decorated, IAuditSink auditSink) : base(decorated)
    {
        _auditSink = auditSink;
    }

    protected override void PreAction(string methodName, object[] args)
    {
        _auditSink.Write($"[Audit] {methodName}");
    }
}
```

```cs
builder.Services.AddSingleton<IAuditSink, ConsoleAuditSink>();
```

# Things Shroud Does Not (Currently) Do

* **Support partials** You cannot create a partial decorator with special logic for a specific method.

If any of these are desired features, please open an issue.
