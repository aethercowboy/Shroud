using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shroud.Example;
using Shroud.Example.Decorators;
using Shroud.Example.Services;
using Shroud;

var builder = Host.CreateApplicationBuilder(args);

// configure services here
builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<IExampleService, ExampleService>();
builder.Services.AddSingleton<IAuditSink, ConsoleAuditSink>();
builder.Services.RegisterDecorator<GlobalDecorator<>, IExampleService>();
builder.Services.Enshroud();

var host = builder.Build();

await host.RunAsync();
