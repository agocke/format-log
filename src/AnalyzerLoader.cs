using System.Collections.Immutable;
using System.Composition.Hosting;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FormatLog;

public class AnalyzerLoader
{
    public static (ImmutableArray<DiagnosticAnalyzer> Analyzers, ImmutableArray<CodeFixProvider> Fixers) LoadAnalyzers(
        IEnumerable<string> analyzerPaths)
    {
        var analyzers = new List<DiagnosticAnalyzer>();
        var fixers = new List<CodeFixProvider>();

        foreach (var path in analyzerPaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var assembly = Assembly.LoadFrom(path);
                
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract || !type.IsClass)
                        continue;

                    if (typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
                    {
                        try
                        {
                            var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(type)!;
                            analyzers.Add(analyzer);
                        }
                        catch
                        {
                            // Skip analyzers that can't be instantiated
                        }
                    }

                    if (typeof(CodeFixProvider).IsAssignableFrom(type))
                    {
                        try
                        {
                            var fixer = (CodeFixProvider)Activator.CreateInstance(type)!;
                            fixers.Add(fixer);
                        }
                        catch
                        {
                            // Skip fixers that can't be instantiated
                        }
                    }
                }
            }
            catch
            {
                // Skip assemblies that can't be loaded
            }
        }

        return (analyzers.ToImmutableArray(), fixers.ToImmutableArray());
    }

    public static IEnumerable<string> ExtractAnalyzerPaths(string[] commandLineArgs)
    {
        foreach (var arg in commandLineArgs)
        {
            // /analyzer:path or -analyzer:path
            if (arg.StartsWith("/analyzer:", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("-analyzer:", StringComparison.OrdinalIgnoreCase))
            {
                yield return arg.Substring(10);
            }
        }
    }
}
