using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
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
                .Where(x => x.symbol != null)
                .Collect()
                .Combine(registrationCalls);

            context.RegisterSourceOutput(interfaceDeclarations, (spc, data) =>
            {
                var interfaces = data.Left;
                var registrations = data.Right;
                var scribanInterfaces = new List<object>();
                foreach (var entry in interfaces)
                {
                    var symbol = (INamedTypeSymbol)entry.symbol!;
                    var decoratorTypes = GetAllDecoratorTypes(symbol);
                    var methodDecoratorTypes = GetAllInterfaceMethods(symbol)
                        .SelectMany(GetDecoratorTypes)
                        .ToList();
                    var registrationDecorators = GetRegistrationDecoratorTypes(symbol, registrations);
                    var allDecoratorTypes = decoratorTypes
                        .Concat(methodDecoratorTypes)
                        .Concat(registrationDecorators)
                        .Distinct()
                        .ToList();

                    if (allDecoratorTypes.Count == 0)
                    {
                        continue;
                    }
                    var interfaceType = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var interfaceTypeShort = symbol.Name;
                    var interfaceNamespace = symbol.ContainingNamespace.ToDisplayString();

                    var decorators = new List<object>();
                    foreach (var decoratorType in allDecoratorTypes)
                    {
                        // Remove generic markers for type name construction
                        var decoratorTypeSimple = decoratorType.Split('.').Last().Replace("Decorator", "");
                        if (decoratorTypeSimple.Contains("<"))
                        {
                            decoratorTypeSimple = decoratorTypeSimple.Substring(0, decoratorTypeSimple.IndexOf('<'));
                        }
                        var concreteDecoratorTypeName = $"{interfaceNamespace}.{interfaceTypeShort}{decoratorTypeSimple}Decorator";
                        decorators.Add(new
                        {
                            type_name = concreteDecoratorTypeName,
                            decorator_typeof = $"typeof({concreteDecoratorTypeName})"
                        });
                    }

                    scribanInterfaces.Add(new
                    {
                        interface_type = interfaceType,
                        interface_type_short = interfaceTypeShort,
                        interface_namespace = interfaceNamespace,
                        service_typeof = $"typeof({interfaceType})",
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

        private static List<string> GetAllDecoratorTypes(INamedTypeSymbol symbol)
        {
            var decoratorTypes = new List<string>();
            foreach (var iface in GetInterfaceHierarchy(symbol))
            {
                decoratorTypes.AddRange(GetDecoratorTypes(iface));
            }
            return decoratorTypes;
        }

        private static IEnumerable<IMethodSymbol> GetAllInterfaceMethods(INamedTypeSymbol symbol)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var iface in GetInterfaceHierarchy(symbol))
            {
                foreach (var method in iface.GetMembers().OfType<IMethodSymbol>()
                             .Where(m => m.MethodKind == MethodKind.Ordinary))
                {
                    var key = GetMethodKey(method);
                    if (seen.Add(key))
                    {
                        yield return method;
                    }
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> GetInterfaceHierarchy(INamedTypeSymbol symbol)
        {
            yield return symbol;
            foreach (var iface in symbol.AllInterfaces)
            {
                yield return iface;
            }
        }

        private static string GetMethodKey(IMethodSymbol method)
        {
            var parameters = string.Join(",",
                method.Parameters.Select(p =>
                    $"{p.RefKind}:{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}"));
            return $"{method.Name}`{method.Arity}({parameters})";
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
                IdentifierNameSyntax idName2 => idName2,
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
                IdentifierNameSyntax idNameRegisterDecorator5 => idNameRegisterDecorator5,
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
            if (nameSyntax is IdentifierNameSyntax idNameRegisterDecorator4 && idNameRegisterDecorator4.Identifier.Text == "RegisterDecorator")
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
