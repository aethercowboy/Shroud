using Microsoft.Extensions.Logging;

namespace Shroud.Example.Services
{
    internal partial class ExampleServiceLoggingDecorator
    {
        public decimal Divide(decimal a, decimal b)
        {
            if (b == 0)
            {
                _logger.LogError("Attempted to divid by zero");
                return decimal.Zero;
            }

            try
            {
                PreAction("Divide", new object[] { a, b });

                var result = _decorated.Divide(a, b);

                PostAction("Divide", new object[] { a, b }, result);

                return result;
            }
            catch (Exception e)
            {
                ErrorAction("Divide", new object[] { a, b }, e);

                throw;
            }
        }
    }
}
