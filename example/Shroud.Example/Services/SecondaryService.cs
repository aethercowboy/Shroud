using Shroud.Example.Decorators;

namespace Shroud.Example.Services
{
    [Decorate(typeof(LoggingDecorator<>))]
    public interface ISecondaryService
    {
        string Echo(string message);
    }

    internal class SecondaryService : ISecondaryService
    {
        public string Echo(string message)
        {
            return message;
        }
    }
}