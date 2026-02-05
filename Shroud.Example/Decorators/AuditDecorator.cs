using Shroud.Example.Services;

namespace Shroud.Example.Decorators
{
	internal class AuditDecorator<T> : BaseDecorator<T>
	{
		private readonly IAuditSink _auditSink;

		public AuditDecorator(T decorated, IAuditSink auditSink) : base(decorated)
		{
			_auditSink = auditSink;
		}

		protected override void PreAction(string methodName, object[] args)
		{
			_auditSink.Write($"[Audit] Calling {methodName} with Arguments \"{string.Join(", ", args)}\"");
		}

		protected override Task PreActionAsync(string methodName, object[] args)
		{
			_auditSink.Write($"[Audit] Calling {methodName} with Arguments \"{string.Join(", ", args)}\"");
			return Task.CompletedTask;
		}
	}
}
