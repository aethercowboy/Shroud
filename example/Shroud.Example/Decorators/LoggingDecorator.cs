using Microsoft.Extensions.Logging;

namespace Shroud.Example.Decorators
{
    public abstract class LoggingDecorator<T> : BaseDecorator<T>
    {
        protected readonly ILogger _logger;

        protected LoggingDecorator(T decorated, ILogger<T> logger) : base(decorated)
        {
            _logger = logger;
        }

        protected override void ErrorAction(string methodName, object[] args, Exception ex)
        {
            _logger.LogError(ex, "Error in {MethodName} with Arguments {Arguments}", methodName, args);
        }

        protected override Task ErrorActionAsync(string methodName, object[] args, Exception ex)
        {
            _logger.LogError(ex, "Error in {MethodName} with Arguments {Arguments}", methodName, args);
            return Task.CompletedTask;
        }

        protected override void PreAction(string methodName, object[] args)
        {
            _logger.LogInformation("Starting {MethodName} with Arguments {Arguments}", methodName, args);
        }

        protected override Task PreActionAsync(string methodName, object[] args)
        {
            _logger.LogInformation("Starting {MethodName} with Arguments {Arguments}", methodName, args);
            return Task.CompletedTask;
        }

        protected override void PostAction(string methodName, object[] args, object result)
        {
            _logger.LogInformation("Completed {MethodName} with Arguments {Arguments} with Result {Result}", methodName, args, result);
        }

        protected override Task PostActionAsync(string methodName, object[] args, object result)
        {
            _logger.LogInformation("Completed {MethodName} with Arguments {Arguments} with Result {Result}", methodName, args, result);
            return Task.CompletedTask;
        }
    }
}