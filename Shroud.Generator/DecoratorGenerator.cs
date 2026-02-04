using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Scriban;
using System.Reflection;
using System.IO;
using Shroud.Generator.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Immutable;
using Shroud.Generator.Utilities;

namespace Shroud.Generator
{
	[Generator]
	internal class DecoratorGenerator : IIncrementalGenerator
	{
		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			var interfaceDeclarations = context.SyntaxProvider
				.CreateSyntaxProvider(
					predicate: static (node, _) =>
						node is InterfaceDeclarationSyntax ids &&
						(ids.AttributeLists.Count > 0 ||
						 ids.Members.OfType<MethodDeclarationSyntax>().Any(m => m.AttributeLists.Count > 0)),
					transform: (ctx, _) =>
					{
						var ids = (InterfaceDeclarationSyntax)ctx.Node;
						var symbol = ctx.SemanticModel.GetDeclaredSymbol(ids) as INamedTypeSymbol;
						if (symbol == null) return ImmutableArray<(INamedTypeSymbol, string, Compilation)>.Empty;

						var compilation = ctx.SemanticModel.Compilation;
						var interfaceDecoratorTypes = GetDecoratorTypes(symbol);
						var methodDecoratorTypes = symbol.GetMembers()
							.OfType<IMethodSymbol>()
							.Where(m => m.MethodKind == MethodKind.Ordinary)
							.SelectMany(GetDecoratorTypes)
							.ToList();

						var allDecoratorTypes = interfaceDecoratorTypes
							.Concat(methodDecoratorTypes)
							.Distinct()
							.ToList();

						if (allDecoratorTypes.Count == 0)
						{
							return ImmutableArray<(INamedTypeSymbol, string, Compilation)>.Empty;
						}

						return allDecoratorTypes
							.Select(decoratorType => (symbol, decoratorType, compilation))
							.ToImmutableArray();
					})
				.SelectMany(static (x, _) => x)
				.Where(x => x != default);

			context.RegisterSourceOutput(interfaceDeclarations, (spc, tuple) =>
			{
				INamedTypeSymbol symbol;
				string decoratorTypeName;
				Compilation compilation;
				(symbol, decoratorTypeName, compilation) = tuple;
				var interfaceName = CleanName(symbol.Name);
				var decoratorName = CleanName(decoratorTypeName);
				var className = decoratorName.EndsWith("Decorator")
					? $"{interfaceName}{decoratorName}"
					: $"{interfaceName}{decoratorName}Decorator";
				var interfaceFullName = symbol.ToDisplayString();
				var ns = symbol.ContainingNamespace.ToDisplayString();

				var decoratorTypeMetadataName = decoratorTypeName.Contains("<")
					? decoratorTypeName.Substring(0, decoratorTypeName.IndexOf('<')) + "`1"
					: decoratorTypeName;
				var decoratorType = compilation.GetTypeByMetadataName(decoratorTypeMetadataName);

				var ctorParams = new List<string>();
				var ctorArgs = new List<string>();
				if (decoratorType != null)
				{
					var ctor = decoratorType.Constructors.FirstOrDefault(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected);
					if (ctor != null)
					{
						foreach (var param in ctor.Parameters)
						{
							var paramType = param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
							var paramName = param.Name;
							if (param.Type is ITypeParameterSymbol tParam && tParam.Name == "T")
							{
								paramType = interfaceFullName;
							}
							else if (paramType.Contains("<T>"))
							{
								paramType = paramType.Replace("<T>", $"<{interfaceFullName}>");
							}
							ctorParams.Add($"{paramType} {paramName}");
							ctorArgs.Add(paramName);
						}
					}
				}

				var cleanDecoratorTypeName = decoratorTypeName.Contains("<")
					? decoratorTypeName.Substring(0, decoratorTypeName.IndexOf('<'))
					: decoratorTypeName;
				var decoratorGeneric = $"{cleanDecoratorTypeName}<{interfaceFullName}>";

				// Prepare method data for template
				var methods = new List<object>();
				var interfaceDecoratorTypes = GetDecoratorTypes(symbol);
				foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>())
				{
					if (member.MethodKind != MethodKind.Ordinary)
						continue;
					var methodName = member.Name;
					var methodDecoratorTypes = GetDecoratorTypes(member);
					var shouldDecorate = interfaceDecoratorTypes.Contains(decoratorTypeName) ||
						methodDecoratorTypes.Contains(decoratorTypeName);
					var parameters = member.Parameters.Select(p => $"{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {p.Name}");
					var paramList = string.Join(", ", parameters);
					var argList = string.Join(", ", member.Parameters.Select(p => p.Name));
					var argsArray = member.Parameters.Length > 0 ? $"new object[] {{ {argList} }}" : "Array.Empty<object>()";
					var returnType = member.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
					var isAsync = returnType.StartsWith("global::System.Threading.Tasks.Task");
					var isTaskOfT = isAsync && member.ReturnType is INamedTypeSymbol nts && nts.TypeArguments.Length == 1;
					var isTask = isAsync && !isTaskOfT;
					var asyncModifier = isAsync && shouldDecorate ? "async " : "";
					var preAction = isAsync ? "await PreActionAsync" : "PreAction";
					var postAction = isAsync ? "await PostActionAsync" : "PostAction";
					var errorAction = isAsync ? "await ErrorActionAsync" : "ErrorAction";
					string resultDecl;
					string callDecorated;
					string postResult;
					string returnResult;
					string plainCall;
					string plainReturn;
					if (member.ReturnsVoid)
					{
						resultDecl = "";
						callDecorated = $"_decorated.{methodName}({argList});";
						postResult = "null";
						returnResult = "";
						plainCall = callDecorated;
						plainReturn = "";
					}
					else if (isTask)
					{
						resultDecl = $"await _decorated.{methodName}({argList});";
						callDecorated = resultDecl;
						postResult = "null";
						returnResult = "return;";
						plainCall = $"return _decorated.{methodName}({argList});";
						plainReturn = "";
					}
					else if (isTaskOfT)
					{
						resultDecl = $"var result = await _decorated.{methodName}({argList});";
						callDecorated = resultDecl;
						postResult = "result";
						returnResult = "return result;";
						plainCall = $"return _decorated.{methodName}({argList});";
						plainReturn = "";
					}
					else
					{
						resultDecl = $"var result = _decorated.{methodName}({argList});";
						callDecorated = resultDecl;
						postResult = "result";
						returnResult = "return result;";
						plainCall = $"return _decorated.{methodName}({argList});";
						plainReturn = "";
					}
					methods.Add(new
					{
						name = methodName,
						param_list = paramList,
						args_array = argsArray,
						return_type = returnType,
						is_async = isAsync,
						should_decorate = shouldDecorate,
						async_modifier = asyncModifier,
						pre_action = preAction,
						post_action = postAction,
						error_action = errorAction,
						call_decorated = callDecorated,
						post_result = postResult,
						return_result = returnResult,
						plain_call = plainCall,
						plain_return = plainReturn
					});
				}

				// Load the Scriban template from embedded resources
				var templateText = Resource.GetEmbeddedResource("Shroud.Generator.Templates.DecoratorClass.scriban");
				var template = Template.Parse(templateText);

				var scribanContext = new
				{
					ns = ns,
					class_name = className,
					base_class = decoratorGeneric,
					interface_full_name = interfaceFullName,
					ctor_params = ctorParams,
					ctor_args = ctorArgs,
					methods = methods
				};

				string source = template.Render(scribanContext);
				spc.AddSource($"{className}.g.cs", SourceText.From(source, Encoding.UTF8));
			});
		}

		private static string CleanName(string name)
		{
			var idx = name.IndexOf('<');
			var baseName = idx >= 0 ? name.Substring(0, idx) : name;
			var lastDot = baseName.LastIndexOf('.');
			return lastDot >= 0 ? baseName.Substring(lastDot + 1) : baseName;
		}

		private static List<string> GetDecoratorTypes(ISymbol symbol)
		{
			var decoratorTypes = new List<string>();
			foreach (var decorateAttr in symbol.GetAttributes().Where(a =>
						 a.AttributeClass?.ToDisplayString() == "Shroud.DecorateAttribute"))
			{
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
			}
			return decoratorTypes;
		}
	}
}
