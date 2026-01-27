using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FormatLog;

public class AnalyzerLoader
{
    public static (ImmutableArray<DiagnosticAnalyzer> Analyzers, ImmutableArray<CodeFixProvider> Fixers) LoadAnalyzers(
        IEnumerable<string> analyzerPaths,
        HashSet<string>? diagnosticIdFilter = null)
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
                            
                            // If filtering by diagnostic ID, only include analyzers that produce those IDs
                            if (diagnosticIdFilter != null && diagnosticIdFilter.Count > 0)
                            {
                                var supportedIds = analyzer.SupportedDiagnostics.Select(d => d.Id);
                                if (!supportedIds.Any(id => diagnosticIdFilter.Contains(id)))
                                {
                                    continue; // Skip this analyzer
                                }
                            }
                            
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
                            
                            // If filtering by diagnostic ID, only include fixers for those IDs
                            if (diagnosticIdFilter != null && diagnosticIdFilter.Count > 0)
                            {
                                if (!fixer.FixableDiagnosticIds.Any(id => diagnosticIdFilter.Contains(id)))
                                {
                                    continue; // Skip this fixer
                                }
                            }
                            
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
