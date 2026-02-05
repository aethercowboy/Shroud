using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Scriban;
using System;
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
        private sealed record DecoratorRegistrationInfo(INamedTypeSymbol DecoratorType, INamedTypeSymbol? ServiceType);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var registrationCalls = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsRegisterDecoratorInvocation(node),
                    transform: static (ctx, _) => GetRegistrationInfo(ctx))
                .Where(static registration => registration != null)
                .Collect();

            var interfaceDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                    transform: static (ctx, _) =>
                    {
                        var ids = (InterfaceDeclarationSyntax)ctx.Node;
                        var symbol = ctx.SemanticModel.GetDeclaredSymbol(ids) as INamedTypeSymbol;
                        return (symbol, ctx.SemanticModel.Compilation);
                    })
                .Where(x => x != default);

            var interfaceWithRegistrations = interfaceDeclarations.Combine(registrationCalls);

            var interfaceDecorators = interfaceWithRegistrations.SelectMany<
                (// left: (INamedTypeSymbol? symbol, Compilation Compilation), right: ImmutableArray<DecoratorRegistrationInfo>
                    (INamedTypeSymbol? symbol, Compilation Compilation), ImmutableArray<DecoratorRegistrationInfo>),
                (INamedTypeSymbol, string, Compilation, IEnumerable<string>)
            >(
            (entry, _) =>
            {
                var (interfaceInfo, registrations) = entry;
                var symbol = interfaceInfo.symbol;
                if (symbol == null)
                {
                    return ImmutableArray<(INamedTypeSymbol, string, Compilation, IEnumerable<string>)>.Empty;
                }

                var compilation = interfaceInfo.Compilation;
                var interfaceDecoratorTypes = GetDecoratorTypes(symbol);
                var methodDecoratorTypes = symbol.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(m => m.MethodKind == MethodKind.Ordinary)
                    .SelectMany(GetDecoratorTypes)
                    .ToList();
                var registrationDecoratorTypes = GetRegistrationDecoratorTypes(symbol, registrations).ToList();

                var allDecoratorTypes = interfaceDecoratorTypes
                    .Concat(methodDecoratorTypes)
                    .Concat(registrationDecoratorTypes)
                    .Distinct()
                    .ToList();

                if (allDecoratorTypes.Count == 0)
                {
                    return ImmutableArray<(INamedTypeSymbol, string, Compilation, IEnumerable<string>)>.Empty;
                }

                return allDecoratorTypes
                    .Select(decoratorType => (symbol, decoratorType, compilation, (IEnumerable<string>)registrationDecoratorTypes))
                    .ToImmutableArray();
            });

            context.RegisterSourceOutput(interfaceDecorators, (spc, tuple) =>
            {
                var symbol = tuple.Item1;
                var decoratorTypeName = tuple.Item2;
                var compilation = tuple.Item3;
                var registrationDecoratorTypes = tuple.Item4;
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
                var existingMethodKeys = GetExistingMethodKeys(compilation, ns, className);

                var methods = new List<object>();
                var interfaceDecoratorTypes = GetDecoratorTypes(symbol);
                foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>())
                {
                    if (member.MethodKind != MethodKind.Ordinary)
                        continue;
                    if (existingMethodKeys.Contains(GetMethodKey(member)))
                    {
                        continue;
                    }

                    var methodName = member.Name;
                    var methodDecoratorTypes = GetDecoratorTypes(member);
                    // Always decorate if this decorator is from registration
                    var isRegisteredDecorator = registrationDecoratorTypes.Contains(decoratorTypeName);
                    var shouldDecorate = isRegisteredDecorator || interfaceDecoratorTypes.Contains(decoratorTypeName) ||
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

        private static HashSet<string> GetExistingMethodKeys(Compilation compilation, string ns, string className)
        {
            var existingType = compilation.GetTypeByMetadataName($"{ns}.{className}");
            if (existingType == null)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in existingType.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary || method.IsImplicitlyDeclared)
                {
                    continue;
                }

                keys.Add(GetMethodKey(method));
            }

            return keys;
        }

        private static string GetMethodKey(IMethodSymbol method)
        {
            var parameters = string.Join(",",
                method.Parameters.Select(p =>
                    $"{p.RefKind}:{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}"));
            return $"{method.Name}`{method.Arity}({parameters})";
        }

        private static IEnumerable<string> GetRegistrationDecoratorTypes(
            INamedTypeSymbol interfaceSymbol,
            ImmutableArray<DecoratorRegistrationInfo> registrations)
        {
            foreach (var registration in registrations)
            {
                if (registration.ServiceType == null)
                {
                    continue;
                }

                if (registration.ServiceType.TypeKind != TypeKind.Interface)
                {
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(interfaceSymbol, registration.ServiceType) ||
                    interfaceSymbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, registration.ServiceType)))
                {
                    yield return ToDisplayStringNoGlobal(registration.DecoratorType);
                }
            }
        }

        private static bool IsRegisterDecoratorInvocation(SyntaxNode node)
        {
            if (node is not InvocationExpressionSyntax invocation)
            {
                return false;
            }

            var nameSyntax = invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
                IdentifierNameSyntax idNameRegisterDecorator => idNameRegisterDecorator,
                _ => null
            };

            // Accept both generic and non-generic RegisterDecorator
            if (nameSyntax is GenericNameSyntax generic)
            {
                return generic.Identifier.Text == "RegisterDecorator" && generic.TypeArgumentList.Arguments.Count == 2;
            }
            if (nameSyntax is IdentifierNameSyntax idName)
            {
                if (idName.Identifier.Text != "RegisterDecorator") return false;
                // Check for two arguments, both typeof expressions
                if (invocation.ArgumentList.Arguments.Count == 2)
                {
                    var arg0 = invocation.ArgumentList.Arguments[0].Expression;
                    var arg1 = invocation.ArgumentList.Arguments[1].Expression;
                    return arg0 is TypeOfExpressionSyntax && arg1 is TypeOfExpressionSyntax;
                }
            }
            return false;
        }

        private static DecoratorRegistrationInfo? GetRegistrationInfo(GeneratorSyntaxContext context)
        {
            if (context.Node is not InvocationExpressionSyntax invocation)
            {
                return null;
            }

            var nameSyntax = invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
                GenericNameSyntax gName => gName,
                IdentifierNameSyntax idNameRegisterDecorator3 => idNameRegisterDecorator3,
                _ => null
            };

            if (nameSyntax is GenericNameSyntax genericName)
            {
                var typeArguments = genericName.TypeArgumentList.Arguments;
                if (typeArguments.Count != 2)
                {
                    return null;
                }
                var decoratorType = context.SemanticModel.GetTypeInfo(typeArguments[0]).Type as INamedTypeSymbol;
                if (decoratorType == null)
                {
                    return null;
                }
                var serviceType = context.SemanticModel.GetTypeInfo(typeArguments[1]).Type as INamedTypeSymbol;
                return new DecoratorRegistrationInfo(decoratorType, serviceType);
            }
            if (nameSyntax is IdentifierNameSyntax idNameRegisterDecorator2 && idNameRegisterDecorator2.Identifier.Text == "RegisterDecorator")
            {
                if (invocation.ArgumentList.Arguments.Count == 2)
                {
                    var arg0 = invocation.ArgumentList.Arguments[0].Expression;
                    var arg1 = invocation.ArgumentList.Arguments[1].Expression;
                    if (arg0 is TypeOfExpressionSyntax typeOf0 && arg1 is TypeOfExpressionSyntax typeOf1)
                    {
                        var decoratorType = context.SemanticModel.GetTypeInfo(typeOf0.Type).Type as INamedTypeSymbol;
                        var serviceType = context.SemanticModel.GetTypeInfo(typeOf1.Type).Type as INamedTypeSymbol;
                        if (decoratorType != null)
                        {
                            return new DecoratorRegistrationInfo(decoratorType, serviceType);
                        }
                    }
                }
            }
            return null;
        }

        private static string ToDisplayStringNoGlobal(INamedTypeSymbol symbol)
        {
            return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);
        }
    }
}
