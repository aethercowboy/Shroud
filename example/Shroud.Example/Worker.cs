using Microsoft.Extensions.Hosting;
using Shroud.Example.Services;

namespace Shroud.Example;

internal class Worker : BackgroundService
{
    private readonly IExampleService _exampleService;
    private readonly ISecondaryService _secondaryService;

    public Worker(IExampleService exampleService, ISecondaryService secondaryService)
    {
        _exampleService = exampleService;
        _secondaryService = secondaryService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var x = 0;

        for (var i = 1; i <= 100; i++)
        {
            x = _exampleService.Add(x, i);
        }

        _exampleService.PrintMessage($"The Sum is {x}");

        try
        {
            _exampleService.OmgException();
        }
        catch (Exception) { }

        var y = 0;

        for (var i = 1; i <= 100; i++)
        {
            y = await _exampleService.AddAsync(y, i, stoppingToken);
        }

        await _exampleService.PrintMessageAsync($"The sum is {y}");

        try
        {
            await _exampleService.OmgExceptionAsync();
        }
        catch (Exception) { }

        _secondaryService.Echo("Hello from the secondary service!");
    }
}