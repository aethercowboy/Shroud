using System;
using System.Threading.Tasks;

namespace Shroud.Example.Decorators
{
	internal class GlobalDecorator<T> : BaseDecorator<T>
	{
		public GlobalDecorator(T decorated) : base(decorated)
		{
		}

		protected override void PreAction(string methodName, object[] args)
		{
			Console.WriteLine($"[Global] Calling {methodName}");
		}

		protected override Task PreActionAsync(string methodName, object[] args)
		{
			Console.WriteLine($"[Global] Calling {methodName}");
			return Task.CompletedTask;
		}
	}
}
