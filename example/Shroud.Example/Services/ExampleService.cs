using Shroud.Example.Decorators;

namespace Shroud.Example.Services
{
    [Decorate(typeof(LoggingDecorator<>), typeof(TimingDecorator<>))]
    public interface IExampleService
    {
        string ServiceName { get; set; }

        event EventHandler? MessagePrinted;

        int Add(int a, int b);

        Task<int> AddAsync(int a, int b, CancellationToken cancellationToken = default);

        decimal Divide(decimal a, decimal b);

        [Decorate(typeof(AuditDecorator<>))]
        void PrintMessage(string message);

        void RaiseMessagePrinted();

        Task PrintMessageAsync(string message);

        void OmgException();

        Task OmgExceptionAsync();
    }

    internal class ExampleService : IExampleService
    {
        private string _serviceName = "ExampleService";

        private event EventHandler? _messagePrinted;

        string IExampleService.ServiceName
        {
            get => _serviceName;
            set => _serviceName = value;
        }

        event EventHandler? IExampleService.MessagePrinted
        {
            add => _messagePrinted += value;
            remove => _messagePrinted -= value;
        }

        int IExampleService.Add(int a, int b)
        {
            return a + b;
        }

        Task<int> IExampleService.AddAsync(int a, int b, CancellationToken cancellationToken)
        {
            return Task.FromResult(a + b);
        }

        decimal IExampleService.Divide(decimal a, decimal b)
        {
            return a / b;
        }

        void IExampleService.OmgException()
        {
            throw new Exception("OMG Exception");
        }

        Task IExampleService.OmgExceptionAsync()
        {
            throw new Exception("OMG Async Exception");
        }

        void IExampleService.PrintMessage(string message)
        {
            Console.WriteLine(message);
            _messagePrinted?.Invoke(this, EventArgs.Empty);
        }

        void IExampleService.RaiseMessagePrinted()
        {
            _messagePrinted?.Invoke(this, EventArgs.Empty);
        }

        Task IExampleService.PrintMessageAsync(string message)
        {
            Console.WriteLine(message);
            _messagePrinted?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }
}
