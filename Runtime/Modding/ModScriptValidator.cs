using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ColorlessOdyssey.Modding
{
    /// <summary>
    /// Rejects mod scripts using APIs that give access to the system (files, network, processes, reflection, native code...).
    /// It must run in the game on the compiled sources: a check made only by the mod project can be bypassed.
    /// This is a filter, not a sandbox: mod code runs with the same rights as the game.
    /// </summary>
    public static class ModScriptValidator
    {
        /// <summary>Assemblies a mod can use, on top of its own code. Everything else is forbidden.</summary>
        private static readonly HashSet<string> AllowedAssemblies = new(StringComparer.Ordinal)
        {
            "mscorlib", "netstandard", "System", "System.Core", "System.Runtime", "System.Collections",
            "UnityEngine", "UnityEngine.CoreModule", "UnityEngine.AnimationModule", "UnityEngine.AudioModule",
            "UnityEngine.ParticleSystemModule", "UnityEngine.Physics2DModule", "UnityEngine.PhysicsModule",
            "UnityEngine.TextRenderingModule", "UnityEngine.UI", "UnityEngine.UIModule", "UnityEngine.TilemapModule",
            "UnityEngine.SpriteShapeModule", "UnityEngine.InputLegacyModule", "Unity.TextMeshPro",
            "Unity.RenderPipelines.Core.Runtime", "Unity.RenderPipelines.Universal.Runtime",
            "Unity.RenderPipelines.Universal.2D.Runtime",
            "Assembly-CSharp", "ColorlessCore", "MyInspector"
        };

        private static readonly HashSet<string> AllowedNamespaces = new(StringComparer.Ordinal)
        {
            "System.Collections", "System.Collections.Generic", "System.Linq", "System.Text",
            "System.Text.RegularExpressions",
            "UnityEngine", "UnityEngine.Events", "UnityEngine.Serialization", "UnityEngine.Rendering",
            "UnityEngine.Rendering.Universal", "UnityEngine.UI", "UnityEngine.Tilemaps", "TMPro"
        };

        /// <summary>The System namespace is too wide to be allowed as a whole. Exceptions are allowed on top of this list.</summary>
        private static readonly HashSet<string> AllowedSystemTypes = new(StringComparer.Ordinal)
        {
            "Object", "ValueType", "Enum", "Void", "Boolean", "Byte", "SByte", "Char", "Int16", "UInt16", "Int32",
            "UInt32", "Int64", "UInt64", "Single", "Double", "Decimal", "String", "Array", "Math", "MathF", "Random",
            "Action", "Func", "Predicate", "Comparison", "Converter", "EventHandler", "EventArgs", "Attribute",
            "AttributeUsageAttribute", "AttributeTargets", "SerializableAttribute", "NonSerializedAttribute",
            "FlagsAttribute", "ObsoleteAttribute", "ParamArrayAttribute", "Nullable", "Tuple", "ValueTuple",
            "DateTime", "DateTimeKind", "DayOfWeek", "TimeSpan", "Guid", "Convert", "BitConverter", "IDisposable",
            "IComparable", "IEquatable", "IFormattable", "IFormatProvider", "ICloneable", "Span", "ReadOnlySpan",
            "Index", "Range", "StringComparison", "StringComparer", "StringSplitOptions", "MidpointRounding",
            "HashCode", "TypeCode", "Type"
        };

        /// <summary>Reading a name is the only thing a mod can do with a <see cref="Type"/>, to prevent reflection.</summary>
        private static readonly HashSet<string> AllowedTypeMembers = new(StringComparer.Ordinal)
        {
            "Name", "FullName", "Equals", "GetHashCode", "ToString", "op_Equality", "op_Inequality"
        };

        private static readonly HashSet<string> ForbiddenTypes = new(StringComparer.Ordinal)
        {
            // Unity: system, files, network, persistent data, scene loading
            "UnityEngine.Application", "UnityEngine.Resources", "UnityEngine.ResourcesAPI", "UnityEngine.ResourceRequest",
            "UnityEngine.AssetBundle", "UnityEngine.AssetBundleCreateRequest", "UnityEngine.AssetBundleRequest",
            "UnityEngine.PlayerPrefs", "UnityEngine.Caching", "UnityEngine.Cache", "UnityEngine.WWW",
            "UnityEngine.AndroidJavaObject", "UnityEngine.AndroidJavaClass", "UnityEngine.AndroidJavaProxy",
            "UnityEngine.AndroidJNI", "UnityEngine.AndroidJNIHelper", "UnityEngine.ScreenCapture",
            "UnityEngine.Microphone", "UnityEngine.WebCamTexture", "UnityEngine.LocationService",
            "UnityEngine.GUIUtility", "UnityEngine.Security",
            // Game: saves, serialization by type name, Steam, links, mod loading
            "TryliomUtility.ScriptableObjectUtility", "TryliomUtility.SerializableObject",
            "TryliomUtility.SerializableSystemType", "SteamAchievements", "SteamManager", "RunState",
            "LinkButtonController", "MainMenu", "PauseMenu", "SaveSelection", "ModLoader"
        };

        private static readonly HashSet<string> ForbiddenMembers = new(StringComparer.Ordinal)
        {
            // These call any method of any component by its name, private ones included
            "UnityEngine.GameObject.SendMessage", "UnityEngine.GameObject.SendMessageUpwards",
            "UnityEngine.GameObject.BroadcastMessage", "UnityEngine.Component.SendMessage",
            "UnityEngine.Component.SendMessageUpwards", "UnityEngine.Component.BroadcastMessage",
            "UnityEngine.MonoBehaviour.Invoke", "UnityEngine.MonoBehaviour.InvokeRepeating",
            // Player saves
            "RunData.SavePermanentData", "RunData.PermanentData", "RunData.DefaultPermanentData", "RunData.GeneralData",
            "RunData.SteamAchievements"
        };

        private static readonly string[] ReservedNamespaces = { "System", "UnityEngine", "UnityEditor", "Unity", "Microsoft", "Mono" };

        /// <summary>
        /// Adds to <paramref name="errors"/> every forbidden usage found. The compilation must have no compile error,
        /// otherwise some symbols cannot be resolved and checked.
        /// </summary>
        public static void Validate(CSharpCompilation compilation, List<string> errors)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                var reported = new HashSet<string>();

                foreach (var node in tree.GetRoot().DescendantNodesAndSelf(descendIntoTrivia: false))
                {
                    var error = CheckNode(node, model);

                    if (error == null)
                    {
                        continue;
                    }

                    var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var message = $"{tree.FilePath}({line}): {error}";

                    if (reported.Add(message))
                    {
                        errors.Add(message);
                    }
                }
            }
        }

        private static string CheckNode(SyntaxNode node, SemanticModel model)
        {
            var syntaxError = CheckSyntax(node);

            if (syntaxError != null)
            {
                return syntaxError;
            }

            var assembly = model.Compilation.Assembly;
            var symbolInfo = model.GetSymbolInfo(node);
            var error = CheckSymbol(symbolInfo.Symbol, assembly);

            // Dynamic or unresolved calls: every candidate must be allowed
            foreach (var candidate in symbolInfo.CandidateSymbols)
            {
                error ??= CheckSymbol(candidate, assembly);
            }

            if (error != null)
            {
                return error;
            }

            if (node is ExpressionSyntax expression)
            {
                // Catches forbidden objects returned by allowed members, even when stored in a "var"
                var typeInfo = model.GetTypeInfo(expression);

                error = CheckType(typeInfo.Type, assembly) ?? CheckType(typeInfo.ConvertedType, assembly)
                    ?? CheckSymbol(model.GetConversion(expression).MethodSymbol, assembly);
            }

            // Members called implicitly by the language
            if (error == null && node is CommonForEachStatementSyntax forEach)
            {
                var info = model.GetForEachStatementInfo(forEach);

                error = CheckSymbol(info.GetEnumeratorMethod, assembly) ?? CheckType(info.ElementType, assembly);
            }

            if (error == null && node.Parent is InitializerExpressionSyntax && node is ExpressionSyntax element)
            {
                error = CheckSymbol(model.GetCollectionInitializerSymbolInfo(element).Symbol, assembly);
            }

            return error;
        }

        private static string CheckSyntax(SyntaxNode node)
        {
            switch (node.Kind())
            {
                case SyntaxKind.UnsafeStatement:
                case SyntaxKind.PointerType:
                case SyntaxKind.FunctionPointerType:
                case SyntaxKind.FixedStatement:
                    return "unsafe code is forbidden";
                case SyntaxKind.MakeRefExpression:
                case SyntaxKind.RefTypeExpression:
                case SyntaxKind.RefValueExpression:
                case SyntaxKind.ArgListExpression:
                    return "undocumented reference keywords are forbidden";
                case SyntaxKind.DestructorDeclaration:
                    return "finalizers are forbidden";
                case SyntaxKind.AttributeList:
                    var target = ((AttributeListSyntax) node).Target?.Identifier.ValueText;

                    return target is "assembly" or "module" ? "assembly and module attributes are forbidden" : null;
                case SyntaxKind.NamespaceDeclaration:
                case SyntaxKind.FileScopedNamespaceDeclaration:
                    var name = ((BaseNamespaceDeclarationSyntax) node).Name.ToString();

                    // Declaring a type in these namespaces could trick the compiler or the runtime
                    foreach (var reserved in ReservedNamespaces)
                    {
                        if (name == reserved || name.StartsWith(reserved + ".", StringComparison.Ordinal))
                        {
                            return $"the namespace {name} is reserved";
                        }
                    }

                    return null;
            }

            if (node is MemberDeclarationSyntax member)
            {
                foreach (var modifier in member.Modifiers)
                {
                    if (modifier.IsKind(SyntaxKind.UnsafeKeyword) || modifier.IsKind(SyntaxKind.ExternKeyword))
                    {
                        return $"the {modifier.ValueText} modifier is forbidden";
                    }
                }
            }

            return null;
        }

        private static string CheckSymbol(ISymbol symbol, IAssemblySymbol modAssembly)
        {
            switch (symbol)
            {
                case null:
                    return null;
                case IAliasSymbol alias:
                    return CheckSymbol(alias.Target, modAssembly);
                case ITypeSymbol type:
                    return CheckType(type, modAssembly);
                case IMethodSymbol method:
                    foreach (var argument in method.TypeArguments)
                    {
                        var argumentError = CheckType(argument, modAssembly);

                        if (argumentError != null)
                        {
                            return argumentError;
                        }
                    }

                    if (method.MethodKind is MethodKind.LambdaMethod or MethodKind.LocalFunction)
                    {
                        return null;
                    }

                    return CheckMember(method.ReducedFrom ?? method, modAssembly);
                case IPropertySymbol:
                case IFieldSymbol:
                case IEventSymbol:
                    return CheckMember(symbol, modAssembly);
                default:
                    return null;
            }
        }

        private static string CheckMember(ISymbol member, IAssemblySymbol modAssembly)
        {
            var type = member.ContainingType;

            if (type == null || SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, modAssembly))
            {
                return null;
            }

            // Members of arrays and tuples have no assembly of their own
            if (type.IsTupleType || member.ContainingAssembly == null)
            {
                return null;
            }

            // "GetType().Name" is declared on MemberInfo
            if (member.Name == "Name" && GetFullName(type) == "System.Reflection.MemberInfo")
            {
                return null;
            }

            var error = CheckType(type, modAssembly);

            if (error != null)
            {
                return error;
            }

            var typeName = GetFullName(type);

            if (typeName == "System.Type" && !AllowedTypeMembers.Contains(member.Name))
            {
                return $"System.Type.{member.Name} is forbidden (reflection)";
            }

            return ForbiddenMembers.Contains($"{typeName}.{member.Name}") ? $"{typeName}.{member.Name} is forbidden" : null;
        }

        private static string CheckType(ITypeSymbol type, IAssemblySymbol modAssembly)
        {
            switch (type)
            {
                case null:
                    return null;
                case IDynamicTypeSymbol:
                    return "dynamic is forbidden";
                case IPointerTypeSymbol:
                case IFunctionPointerTypeSymbol:
                    return "pointers are forbidden";
                case IArrayTypeSymbol array:
                    return CheckType(array.ElementType, modAssembly);
                case ITypeParameterSymbol:
                    return null;
            }

            if (type is INamedTypeSymbol named)
            {
                foreach (var argument in named.TypeArguments)
                {
                    // A type argument referring to the type itself would recurse forever
                    if (SymbolEqualityComparer.Default.Equals(argument, type))
                    {
                        continue;
                    }

                    var argumentError = CheckType(argument, modAssembly);

                    if (argumentError != null)
                    {
                        return argumentError;
                    }
                }

                if (named.IsTupleType || named.IsAnonymousType)
                {
                    return null;
                }
            }

            if (type.TypeKind == TypeKind.Error || type.ContainingAssembly == null
                || SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, modAssembly))
            {
                return null;
            }

            // A nested type follows the rules of its outermost type
            var outer = type;

            while (outer.ContainingType != null)
            {
                outer = outer.ContainingType;
            }

            var fullName = GetFullName(outer);

            if (!AllowedAssemblies.Contains(type.ContainingAssembly.Name))
            {
                return $"{fullName} is forbidden (assembly {type.ContainingAssembly.Name})";
            }

            if (ForbiddenTypes.Contains(fullName))
            {
                return $"{fullName} is forbidden";
            }

            var space = outer.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : string.Empty;

            if (space == "System")
            {
                return AllowedSystemTypes.Contains(outer.Name) || outer.Name.EndsWith("Exception", StringComparison.Ordinal)
                    ? null
                    : $"{fullName} is forbidden";
            }

            // The game and its packages are allowed except the types listed above
            var assemblyName = type.ContainingAssembly.Name;

            if (assemblyName is "Assembly-CSharp" or "ColorlessCore" or "MyInspector")
            {
                return space.StartsWith("ColorlessOdyssey.Modding", StringComparison.Ordinal) ? $"{fullName} is forbidden" : null;
            }

            return AllowedNamespaces.Contains(space) ? null : $"{fullName} is forbidden (namespace {space})";
        }

        private static string GetFullName(ITypeSymbol type)
        {
            return type.ContainingNamespace is { IsGlobalNamespace: false } ns ? $"{ns.ToDisplayString()}.{type.Name}" : type.Name;
        }
    }
}
