using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Shroud.Generator.Utilities;
using System.Text;
using Scriban;

namespace Shroud.Generator
{
	[Generator]
	internal class AttributeGenerator : IIncrementalGenerator
	{
		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			context.RegisterPostInitializationOutput(ctx =>
			{
				var text = Resource.GetEmbeddedResource("Shroud.Generator.Templates.DecoratedAttribute.scriban");

				var template = Template.Parse(text);

				var source = template.Render();

				ctx.AddSource("DecorateAttribute.g.cs", SourceText.From(source, Encoding.UTF8));
			});
		}
	}
}