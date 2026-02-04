using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shroud;
using Shroud.Example.Services;
using Xunit;

namespace Shroud.Example.Tests;

[Collection("Console")]
public class ExampleServiceTests
{
	[Fact]
	public void Add_ReturnsSum()
	{
		var service = new ExampleService();

		var result = ((IExampleService)service).Add(2, 3);

		Assert.Equal(5, result);
	}

	[Fact]
	public async Task AddAsync_ReturnsSum()
	{
		var service = new ExampleService();

		var result = await ((IExampleService)service).AddAsync(4, 6, CancellationToken.None);

		Assert.Equal(10, result);
	}

	[Fact]
	public void PrintMessage_WritesToConsole()
	{
		var service = new ExampleService();
		var writer = new StringWriter();
		var original = Console.Out;
		Console.SetOut(writer);

		try
		{
			((IExampleService)service).PrintMessage("Hello!");
		}
		finally
		{
			Console.SetOut(original);
		}

		Assert.Contains("Hello!", writer.ToString());
	}

	[Fact]
	public async Task PrintMessageAsync_WritesToConsole()
	{
		var service = new ExampleService();
		var writer = new StringWriter();
		var original = Console.Out;
		Console.SetOut(writer);

		try
		{
			await ((IExampleService)service).PrintMessageAsync("Async hello!");
		}
		finally
		{
			Console.SetOut(original);
		}

		Assert.Contains("Async hello!", writer.ToString());
	}

	[Fact]
	public void OmgException_Throws()
	{
		var service = new ExampleService();

		var ex = Assert.Throws<Exception>(() => ((IExampleService)service).OmgException());

		Assert.Equal("OMG Exception", ex.Message);
	}

	[Fact]
	public async Task OmgExceptionAsync_Throws()
	{
		var service = new ExampleService();

		var ex = await Assert.ThrowsAsync<Exception>(() => ((IExampleService)service).OmgExceptionAsync());

		Assert.Equal("OMG Async Exception", ex.Message);
	}

	[Fact]
	public void Enshroud_WrapsServiceInExpectedDecoratorChain()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<IAuditSink, TestAuditSink>();
		services.AddSingleton<IExampleService, ExampleService>();
		services.Enshroud();

		using var provider = services.BuildServiceProvider();
		var service = provider.GetRequiredService<IExampleService>();

		Assert.IsType<IExampleServiceAuditDecorator>(service);
		var chain = GetDecoratorChain(service);

		Assert.Equal(
			new[]
			{
				nameof(IExampleServiceAuditDecorator),
				nameof(IExampleServiceTimingDecorator),
				nameof(IExampleServiceLoggingDecorator),
				nameof(ExampleService)
			},
			chain);
	}

	[Fact]
	public void LoggingDecorator_RecordsPreAndPostActions()
	{
		var logger = new TestLogger<IExampleService>();
		var decorated = new TrackingExampleService();
		var logging = new IExampleServiceLoggingDecorator(decorated, logger);

		logging.Add(1, 2);

		Assert.Contains(logger.Messages, message => message.Contains("Starting Add", StringComparison.Ordinal));
		Assert.Contains(logger.Messages, message => message.Contains("Completed Add", StringComparison.Ordinal));
	}

	[Fact]
	public void LoggingDecorator_LogsErrors()
	{
		var logger = new TestLogger<IExampleService>();
		var decorated = new TrackingExampleService { ThrowOnOmg = true };
		var logging = new IExampleServiceLoggingDecorator(decorated, logger);

		Assert.Throws<InvalidOperationException>(() => logging.OmgException());

		Assert.Contains(logger.Messages, message => message.Contains("Error in OmgException", StringComparison.Ordinal));
	}

	[Fact]
	public void AuditDecorator_OnlyAppliesToDecoratedMethods()
	{
		var decorated = new TrackingExampleService();
		var sink = new TestAuditSink();
		var audit = new IExampleServiceAuditDecorator(decorated, sink);

		audit.Add(3, 4);
		audit.PrintMessage("Audit me");

		Assert.DoesNotContain("Add", sink.Messages, StringComparer.Ordinal);
		Assert.Contains("[Audit] Calling PrintMessage", sink.Messages, StringComparer.Ordinal);
	}

	[Fact]
	public void TimingDecorator_WritesTimingMessages()
	{
		var decorated = new TrackingExampleService();
		var timing = new IExampleServiceTimingDecorator(decorated);
		var writer = new StringWriter();
		var original = Console.Out;
		Console.SetOut(writer);

		try
		{
			timing.Add(5, 6);
		}
		finally
		{
			Console.SetOut(original);
		}

		var output = writer.ToString();
		Assert.Contains("[Timing] Starting Add", output, StringComparison.Ordinal);
		Assert.Contains("[Timing] Completed Add", output, StringComparison.Ordinal);
	}

	private static IReadOnlyList<string> GetDecoratorChain(IExampleService service)
	{
		var chain = new List<string>();
		object? current = service;

		while (current != null)
		{
			chain.Add(current.GetType().Name);
			var field = current.GetType().GetField("_decorated", BindingFlags.Instance | BindingFlags.NonPublic);
			if (field == null)
			{
				break;
			}
			current = field.GetValue(current);
		}

		return chain;
	}

	private sealed class TrackingExampleService : IExampleService
	{
		public bool ThrowOnOmg { get; set; }

		public int Add(int a, int b) => a + b;

		public Task<int> AddAsync(int a, int b, CancellationToken cancellationToken = default)
			=> Task.FromResult(a + b);

		public void PrintMessage(string message) { }

		public Task PrintMessageAsync(string message) => Task.CompletedTask;

		public void OmgException()
		{
			if (ThrowOnOmg)
			{
				throw new InvalidOperationException("Boom");
			}
		}

		public Task OmgExceptionAsync()
		{
			if (ThrowOnOmg)
			{
				throw new InvalidOperationException("Boom async");
			}

			return Task.CompletedTask;
		}
	}

	private sealed class TestLogger<T> : ILogger<T>
	{
		public List<string> Messages { get; } = new();

		public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NullScope();

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
			Messages.Add(formatter(state, exception));
		}

		private sealed class NullScope : IDisposable
		{
			public void Dispose() { }
		}
	}

	private sealed class TestAuditSink : IAuditSink
	{
		public List<string> Messages { get; } = new();

		public void Write(string message)
		{
			Messages.Add(message);
		}
	}
}
