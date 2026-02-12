using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shroud.Generator;
using Xunit;

namespace Shroud.Generator.Tests;

public class GeneratorTests
{
    private const string AttributeSource = """
    using System;

    namespace Shroud;

    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class DecorateAttribute : Attribute
    {
        public DecorateAttribute(params Type[] types) { }
    }

    namespace Microsoft.Extensions.DependencyInjection
    {
        public interface IServiceCollection { }
    }

    namespace Shroud
    {
        public static class ShroudExtensions
        {
            public static Microsoft.Extensions.DependencyInjection.IServiceCollection RegisterDecorator<TDecorator, TService>(
                this Microsoft.Extensions.DependencyInjection.IServiceCollection services) => services;
        }
    }
    """;

    private const string DecoratorSource = """
using System;
using System.Threading.Tasks;
using Shroud;

namespace TestDecorators
{
	public class LoggingDecorator<T>
	{
		public LoggingDecorator(T decorated) { }
	}

	public class TimingDecorator<T>
	{
		public TimingDecorator(T decorated) { }
	}

	public class AuditDecorator<T>
	{
		public AuditDecorator(T decorated, string label) { }
	}
}

namespace Test
{
	public interface IReporter
	{
		void Report(string message);
	}

	public class Startup
	{
		public void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
		{
			services.RegisterDecorator<TestDecorators.AuditDecorator<>, IReporter>();
		}
	}

	[Decorate(typeof(TestDecorators.LoggingDecorator<>), typeof(TestDecorators.TimingDecorator<>))]
	public interface ICalculator
	{
		int Add(int a, int b);

		[Decorate(typeof(TestDecorators.AuditDecorator<>))]
		void Log(string message);

		Task<int> AddAsync(int a, int b);
	}

	[Decorate(typeof(TestDecorators.LoggingDecorator<>))]
	public interface IClock
	{
		DateTime Now();
	}

	[Decorate(typeof(TestDecorators.LoggingDecorator<>))]
	public interface IntrospectionService
	{
		void Trace(string message);
	}

	public partial class CalculatorLoggingDecorator
	{
		public int Add(int a, int b)
		{
			return a + b + 1;
		}
	}
}
""";

    [Fact]
    public void DecoratorGenerator_EmitsExpectedDecorators()
    {
        var runResult = RunGenerator(new DecoratorGenerator(), AttributeSource + DecoratorSource);
        var loggingSource = GetGeneratedSource(runResult, "CalculatorLoggingDecorator.g.cs");
        var auditSource = GetGeneratedSource(runResult, "CalculatorAuditDecorator.g.cs");
        var reporterSource = GetGeneratedSource(runResult, "ReporterAuditDecorator.g.cs");
        var introspectionSource = GetGeneratedSource(runResult, "IntrospectionServiceLoggingDecorator.g.cs");

        Assert.Contains("internal partial class CalculatorLoggingDecorator", loggingSource);
        Assert.DoesNotContain("int Add(", loggingSource);
        Assert.Contains("PreAction(\"Log\"", loggingSource);
        Assert.Contains("PostAction(\"AddAsync\"", loggingSource);

        Assert.Contains("internal partial class CalculatorAuditDecorator", auditSource);
        Assert.DoesNotContain("PreAction(\"Add\"", auditSource);
        Assert.Contains("PreAction(\"Log\"", auditSource);
        Assert.Contains("Test.ICalculator decorated", auditSource);
        Assert.Contains("string label", auditSource);
        Assert.Contains("internal partial class ReporterAuditDecorator", reporterSource);
        Assert.Contains("internal partial class IntrospectionServiceLoggingDecorator", introspectionSource);
    }

    [Fact]
    public void ShroudExtensionGenerator_EmitsDecoratorChainInOrder()
    {
        var runResult = RunGenerator(new ShroudExtensionGenerator(), AttributeSource + DecoratorSource);
        var extensionsSource = GetGeneratedSource(runResult, "ShroudExtensions.g.cs");

        var loggingIndex = extensionsSource.IndexOf("CalculatorLoggingDecorator", StringComparison.Ordinal);
        var timingIndex = extensionsSource.IndexOf("CalculatorTimingDecorator", StringComparison.Ordinal);
        var auditIndex = extensionsSource.IndexOf("CalculatorAuditDecorator", StringComparison.Ordinal);
        var reporterIndex = extensionsSource.IndexOf("ReporterAuditDecorator", StringComparison.Ordinal);

        Assert.True(loggingIndex >= 0, "Logging decorator was not generated.");
        Assert.True(timingIndex > loggingIndex, "Timing decorator should follow logging.");
        Assert.True(auditIndex > timingIndex, "Audit decorator should be last in the chain.");
        Assert.True(reporterIndex >= 0, "Reporter decorator was not generated.");
        Assert.Contains("ActivatorUtilities.CreateInstance(sp, typeof", extensionsSource);
        Assert.Contains("// Decorator stack for global::Shroud.Test.ICalculator", extensionsSource);
        Assert.Contains("// Decorator stack for global::Shroud.Test.IClock", extensionsSource);
    }

    [Fact]
    public void AttributeGenerator_EmitsDecorateAttribute()
    {
        var runResult = RunGenerator(new AttributeGenerator(), string.Empty);
        var source = GetGeneratedSource(runResult, "DecorateAttribute.g.cs");

        Assert.Contains("public sealed class DecorateAttribute", source);
        Assert.Contains("AttributeUsage", source);
    }

    [Fact]
    public void BaseDecoratorGenerator_EmitsBaseDecorator()
    {
        var runResult = RunGenerator(new BaseDecoratorGenerator(), string.Empty);
        var source = GetGeneratedSource(runResult, "BaseDecorator.g.cs");

        Assert.Contains("public abstract class BaseDecorator<T>", source);
        Assert.Contains("protected virtual Task PreActionAsync", source);
    }

    private static GeneratorDriverRunResult RunGenerator(IIncrementalGenerator generator, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ImmutableArray).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator.AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static string GetGeneratedSource(GeneratorDriverRunResult runResult, string hintName)
    {
        var source = runResult.GeneratedTrees
            .Select(tree => (tree.FilePath, Text: tree.GetText().ToString()))
            .FirstOrDefault(entry => entry.FilePath.EndsWith(hintName, StringComparison.Ordinal));

        Assert.False(string.IsNullOrEmpty(source.Text), $"Generated source '{hintName}' not found.");
        return source.Text;
    }
}
