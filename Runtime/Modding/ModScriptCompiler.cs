using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ColorlessOdyssey.Modding
{
    /// <summary>
    /// Compiles the sources of a mod and validates them with <see cref="ModScriptValidator"/>.
    /// Mods ship sources, never DLLs, so everything that runs has been checked.
    /// </summary>
    public static class ModScriptCompiler
    {
        /// <summary>
        /// Returns the compiled assembly image, or null if the sources do not compile or use forbidden APIs,
        /// in which case <paramref name="errors"/> explains why. Nothing is loaded.
        /// </summary>
        /// <param name="assemblyName">Name of the assembly. Must be unique for each load, as Mono cannot unload one.</param>
        /// <param name="sourcePaths">C# files of the mod.</param>
        /// <param name="errors">Filled with compile and security errors.</param>
        public static byte[] Compile(string assemblyName, IReadOnlyList<string> sourcePaths, List<string> errors)
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
            var trees = new List<SyntaxTree>(sourcePaths.Count);

            foreach (var path in sourcePaths)
            {
                trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path));
            }

            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: false);
            var compilation = CSharpCompilation.Create(assemblyName, trees, GetReferences(), options);

            foreach (var diagnostic in compilation.GetDiagnostics())
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    errors.Add(diagnostic.ToString());
                }
            }

            if (errors.Count > 0)
            {
                return null;
            }

            ModScriptValidator.Validate(compilation, errors);

            if (errors.Count > 0)
            {
                return null;
            }

            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);

            if (result.Success)
            {
                return stream.ToArray();
            }

            foreach (var diagnostic in result.Diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    errors.Add(diagnostic.ToString());
                }
            }

            return null;
        }

        private static List<MetadataReference> GetReferences()
        {
            var references = new List<MetadataReference>();
            var names = new HashSet<string>(StringComparer.Ordinal);

            // Facade referenced by the game assemblies, not necessarily loaded yet
            try
            {
                Assembly.Load("netstandard");
            }
            catch (FileNotFoundException)
            {
            }

            // Previously loaded mods have no location, so they are not referenced
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location) || !names.Add(assembly.GetName().Name))
                {
                    continue;
                }

                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }

            return references;
        }
    }
}
