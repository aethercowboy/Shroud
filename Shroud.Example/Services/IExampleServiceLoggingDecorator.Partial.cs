namespace Shroud.Example.Services
{
	internal partial class IExampleServiceLoggingDecorator
	{
		public int Add(int a, int b)
		{
			var args = new object[] { a, b };
			PreAction(nameof(Add), args);
			var result = _decorated.Add(a, b);
			PostAction(nameof(Add), args, result);
			return result + 1;
		}
	}
}
