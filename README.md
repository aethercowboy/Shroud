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
dotnet add package Shroud
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
    protected readonly ILogger _logger;

    protected LoggingDecorator(T decorated, ILogger logger) : base(decorated)
    {
        _logger = logger;
    }

    // ... 
}
```

Then, you can override the methods you want to use:

```cs
    protected override void PreAction(string methodName, object[] args)
    {
        _logger.LogInformation($"Calling method {methodName} with arguments {string.Join(", ", args)}");
    }

    protected override void PostAction(string methodName, object[] args, object result)
    {
        _logger.LogInformation($"Method {methodName} returned {result}");
    }

    protected override void ErrorAction(string methodName, object[] args, Exception exception)
    {
        _logger.LogError(exception, $"Method {methodName} threw an exception");
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

Properties and events declared on decorated interfaces are also generated and forwarded directly to
`_decorated` automatically, so you do not need to create partials just to satisfy those members.

> Note: Generated decorator class names drop a leading `I` prefix when the interface name starts
> with `I` followed by another capital letter (for example `IMyService` becomes
> `MyServiceLoggingDecorator`). Interfaces like `IntrospectionService` keep the leading `I`.

## Partial implementations

Decorators can be partially implemented by declaring a partial class that matches the generated
decorator type. Any methods you implement in the partial class will be left out of the generated
decorator so you can provide custom logic.

```cs
namespace MyApp.Services
{
    public partial class MyServiceLoggingDecorator
    {
        public int Add(int a, int b)
        {
            if (a == b) 
            {
                _logger.LogWarning("Adding two identical numbers");
            }

            var args = new object[] { a, b };

            try
            {
                PreAction(nameof(Add), args);

                var result = _decorated.Add(a, b);
            
                PostAction(nameof(Add), args, result);
            
                return result;
            }
            catch (Exception ex)
            {
                ErrorAction(nameof(Add), args, ex);
                
                throw;
            }
        }
    }
}
```

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

// register shroud
builder.Services.Enshroud(); 
```

This will take all your decorated interfaces and wrap them in the decorators in the order you
specified.

Additionally, You can apply a decorator to all implementations of a base interface:

```cs
public interface IBaseService
{
}

public class ServiceA : IBaseService
{
}

public class ServiceB : IBaseService
{
}
```

Then in `Program.cs`

```cs
builder.Services.AddScoped<IBaseService, ServiceA>();
builder.Services.AddScoped<IBaseService, ServiceB>();

builder.Services.RegisterDecorator(typeof(LoggingDecorator<>), typeof(IBaseService));

builder.Services.Enshroud();
```

This will apply the `LoggingDecorator` to all implementations of `IBaseService`, which in this case are `ServiceA` and `ServiceB`. All decorators registered this way will be added after any decorators specified using the `Decorate` attribute.

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

- Property/Event-specific decoration hooks (members are forwarded, but not decorated with pre/post/error actions yet)
- Generic Constraints on Decorators (e.g. `LoggingDecorator<T> where T : IMyService`)
- Conditional Decoration (e.g. only decorate methods with a certain attribute, or only decorate interfaces in a certain namespace)
- Fine-Grained Order Control (e.g. specify that `LoggingDecorator` should be applied before `AuditDecorator`)
- AOP Integration (e.g. using Shroud with an AOP library like PostSharp or AspectInjector)
- Support for non-interface types (e.g. decorating concrete classes or abstract classes)
- Support for struct types (e.g. decorating value types)
- Support for record types (e.g. decorating record classes or record structs)
- Support for decorating methods with ref or out parameters

If any of these (or anything else not specified here) are desired features, please open an issue.
