namespace Shroud.Example.Decorators
{
	internal class AuditDecorator<T> : BaseDecorator<T>
	{
		public AuditDecorator(T decorated) : base(decorated)
		{
		}

		protected override void PreAction(string methodName, object[] args)
		{
			Console.WriteLine($"[Audit] Calling {methodName} with Arguments \"{string.Join(", ", args)}\"");
		}

		protected override Task PreActionAsync(string methodName, object[] args)
		{
			Console.WriteLine($"[Audit] Calling {methodName} with Arguments \"{string.Join(", ", args)}\"");
			return Task.CompletedTask;
		}
	}
}
