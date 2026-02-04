namespace Shroud.Example.Services
{
	public interface IAuditSink
	{
		void Write(string message);
	}

	internal sealed class ConsoleAuditSink : IAuditSink
	{
		public void Write(string message)
		{
			Console.WriteLine(message);
		}
	}
}
