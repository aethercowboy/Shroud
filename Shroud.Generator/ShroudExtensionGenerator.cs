using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using Scriban;
using Shroud.Generator.Utilities;

namespace Shroud.Generator
{
	[Generator]
	internal class ShroudExtensionGenerator : IIncrementalGenerator
	{
		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			var interfaceDeclarations = context.SyntaxProvider
				.CreateSyntaxProvider(
					predicate: static (node, _) => node is InterfaceDeclarationSyntax ids && ids.AttributeLists.Count > 0,
					transform: (ctx, _) =>
					{
						var ids = (InterfaceDeclarationSyntax)ctx.Node;
						var symbol = ctx.SemanticModel.GetDeclaredSymbol(ids) as INamedTypeSymbol;
						var decorateAttr = symbol?.GetAttributes().FirstOrDefault(a =>
							a.AttributeClass?.ToDisplayString() == "Shroud.DecorateAttribute");
						if (decorateAttr == null) return (null, (List<string>)null, ctx.SemanticModel.Compilation);

						var decoratorTypes = new List<string>();
						if (decorateAttr.ConstructorArguments.Length > 0)
						{
							var arg = decorateAttr.ConstructorArguments[0];
							if (arg.Kind == TypedConstantKind.Array)
							{
								foreach (var v in arg.Values)
								{
									var typeStr = v.Value?.ToString();
									if (!string.IsNullOrEmpty(typeStr))
										decoratorTypes.Add(typeStr);
								}
							}
							else
							{
								var typeStr = arg.Value?.ToString();
								if (!string.IsNullOrEmpty(typeStr))
									decoratorTypes.Add(typeStr);
							}
						}
						return (symbol, decoratorTypes, ctx.SemanticModel.Compilation);
					})
				.Where(x => x.symbol != null)
				.Collect();

			context.RegisterSourceOutput(interfaceDeclarations, (spc, interfaces) =>
			{
				var scribanInterfaces = new List<object>();
				foreach (var entry in interfaces)
				{
					var symbol = (INamedTypeSymbol)entry.symbol;
					var decoratorTypes = (List<string>)entry.Item2;
					var interfaceType = symbol.ToDisplayString();
					var interfaceTypeShort = symbol.Name;
					var interfaceNamespace = symbol.ContainingNamespace.ToDisplayString();

					var decorators = new List<object>();
					foreach (var decoratorType in decoratorTypes)
					{
						// Remove generic markers for type name construction
						var decoratorTypeSimple = decoratorType.Split('.').Last().Replace("Decorator", "");
						if (decoratorTypeSimple.Contains("<"))
						{
							decoratorTypeSimple = decoratorTypeSimple.Substring(0, decoratorTypeSimple.IndexOf('<'));
						}
						var concreteDecoratorTypeName = $"{interfaceNamespace}.{interfaceTypeShort}{decoratorTypeSimple}Decorator";
						var decoratorArgs = new List<string> { $"({interfaceType})current" };
						if (decoratorTypeSimple == "Logging")
						{
							decoratorArgs.Add($"sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<{interfaceType}>>()");
						}
						var argList = string.Join(", ", decoratorArgs);
						decorators.Add(new {
							type_name = concreteDecoratorTypeName,
							args = argList
						});
					}

					scribanInterfaces.Add(new
					{
						interface_type = interfaceType,
						interface_type_short = interfaceTypeShort,
						interface_namespace = interfaceNamespace,
						decorators = decorators
					});
				}

				var scribanContext = new
				{
					interfaces = scribanInterfaces
				};

				var templateText = Resource.GetEmbeddedResource("Shroud.Generator.Templates.ShroudExtensionsClass.scriban");
				var template = Template.Parse(templateText);
				string source = template.Render(scribanContext);
				spc.AddSource("ShroudExtensions.g.cs", SourceText.From(source, Encoding.UTF8));
			});
		}
	}
}