using System.Diagnostics;

namespace Shroud.Example.Decorators
{
	internal class TimingDecorator<T> : BaseDecorator<T>
	{
		private readonly Stopwatch _stopwatch;

		public TimingDecorator(T decorated) : base(decorated)
		{
			_stopwatch = new Stopwatch();
		}

		protected override void PreAction(string methodName, object[] args)
		{
			_stopwatch.Restart();
			_stopwatch.Start();
			Console.WriteLine($"[Timing] Starting {methodName} with Arguments \"{string.Join(", ", args)}\"");
		}

		protected override Task PreActionAsync(string methodName, object[] args)
		{
			_stopwatch.Restart();
			_stopwatch.Start();
			Console.WriteLine($"[Timing] Starting {methodName} with Arguments \"{string.Join(", ", args)}\"");
			return Task.CompletedTask;
		}

		protected override void PostAction(string methodName, object[] args, object result)
		{
			_stopwatch.Stop();
			Console.WriteLine($"[Timing] Completed {methodName} with Arguments \"{string.Join(", ", args)}\" with Result \"{result}\" in {_stopwatch.ElapsedMilliseconds} ms");
		}

		protected override Task PostActionAsync(string methodName, object[] args, object result)
		{
			_stopwatch.Stop();
			Console.WriteLine($"[Timing] Completed {methodName} with Arguments \"{string.Join(", ", args)}\" with Result \"{result}\" in {_stopwatch.ElapsedMilliseconds} ms");
			return Task.CompletedTask;
		}
	}
}