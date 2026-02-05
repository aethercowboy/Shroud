using Shroud.Example.Decorators;

namespace Shroud.Example.Services
{
	[Decorate(typeof(LoggingDecorator<>), typeof(TimingDecorator<>))]
	public interface IExampleService
	{
		int Add(int a, int b);

		Task<int> AddAsync(int a, int b, CancellationToken cancellationToken = default);

		[Decorate(typeof(AuditDecorator<>))]
		void PrintMessage(string message);

		Task PrintMessageAsync(string message);

		void OmgException();

		Task OmgExceptionAsync();
	}

	internal class ExampleService : IExampleService
	{
		int IExampleService.Add(int a, int b)
		{
			return a + b;
		}

		Task<int> IExampleService.AddAsync(int a, int b, CancellationToken cancellationToken)
		{
			return Task.FromResult(a + b);
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
		}

		Task IExampleService.PrintMessageAsync(string message)
		{
			Console.WriteLine(message);
			return Task.CompletedTask;
		}
	}
}
