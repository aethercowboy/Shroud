using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Scriban;
using Shroud.Generator.Utilities;
using System.Text;

namespace Shroud.Generator
{
	[Generator]
	internal class BaseDecoratorGenerator : IIncrementalGenerator
	{
		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			context.RegisterPostInitializationOutput(ctx =>
			{
				var text = Resource.GetEmbeddedResource("Shroud.Generator.Templates.BaseDecorator.scriban");

				var template = Template.Parse(text);

				var source = template.Render();

				ctx.AddSource("BaseDecorator.g.cs", SourceText.From(source, Encoding.UTF8));
			});
		}
	}
}