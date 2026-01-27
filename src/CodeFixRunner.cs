using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FormatLog;

public class CodeFixRunner
{
    public static async Task<FormatResult> ApplyFixesAsync(
        Project project,
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        ImmutableArray<CodeFixProvider> fixers,
        bool verify,
        TextWriter? output = null)
    {
        output ??= Console.Out;
        
        var modifiedFiles = new List<string>();
        var unfixable = new List<string>();
        var fixesApplied = 0;
        var totalDiagnostics = 0;

        if (analyzers.IsEmpty)
        {
            await output.WriteLineAsync("  No analyzers loaded");
            return new FormatResult(0, 0, modifiedFiles, unfixable);
        }

        var compilation = await project.GetCompilationAsync();
        if (compilation == null)
        {
            await output.WriteLineAsync("  Failed to get compilation");
            return new FormatResult(0, 0, modifiedFiles, unfixable);
        }

        // Run analyzers
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        
        // Filter to only diagnostics with fixes and in source
        var fixableDiagnostics = diagnostics
            .Where(d => d.Location.IsInSource && 
                        d.Severity != DiagnosticSeverity.Hidden)
            .ToList();

        totalDiagnostics = fixableDiagnostics.Count;
        await output.WriteLineAsync($"  Found {totalDiagnostics} diagnostic(s)");

        if (totalDiagnostics == 0)
        {
            return new FormatResult(0, 0, modifiedFiles, unfixable);
        }

        // Build a map of diagnostic ID to fixers
        var fixerMap = new Dictionary<string, List<CodeFixProvider>>();
        foreach (var fixer in fixers)
        {
            foreach (var id in fixer.FixableDiagnosticIds)
            {
                if (!fixerMap.TryGetValue(id, out var list))
                {
                    list = new List<CodeFixProvider>();
                    fixerMap[id] = list;
                }
                list.Add(fixer);
            }
        }

        // Apply fixes
        var solution = project.Solution;
        var workspace = solution.Workspace;

        foreach (var diagnostic in fixableDiagnostics)
        {
            if (!fixerMap.TryGetValue(diagnostic.Id, out var applicableFixers))
            {
                unfixable.Add($"{diagnostic.Id}: {diagnostic.GetMessage()}");
                continue;
            }

            var document = solution.GetDocument(diagnostic.Location.SourceTree);
            if (document == null)
                continue;

            CodeAction? codeAction = null;

            foreach (var fixer in applicableFixers)
            {
                var context = new CodeFixContext(
                    document,
                    diagnostic,
                    (action, _) => codeAction ??= action,
                    CancellationToken.None);

                try
                {
                    await fixer.RegisterCodeFixesAsync(context);
                    if (codeAction != null)
                        break;
                }
                catch
                {
                    // Skip fixers that fail
                }
            }

            if (codeAction == null)
            {
                unfixable.Add($"{diagnostic.Id}: {diagnostic.GetMessage()}");
                continue;
            }

            // Apply the fix
            var operations = await codeAction.GetOperationsAsync(CancellationToken.None);
            foreach (var operation in operations)
            {
                if (operation is ApplyChangesOperation applyChanges)
                {
                    solution = applyChanges.ChangedSolution;
                    
                    // Track which files changed
                    var changedDoc = solution.GetDocument(document.Id);
                    if (changedDoc?.FilePath != null && !modifiedFiles.Contains(changedDoc.FilePath))
                    {
                        modifiedFiles.Add(changedDoc.FilePath);
                    }
                    
                    fixesApplied++;
                }
            }
        }

        // Write changes to disk
        if (!verify && modifiedFiles.Count > 0)
        {
            foreach (var docId in solution.Projects.SelectMany(p => p.DocumentIds))
            {
                var doc = solution.GetDocument(docId);
                if (doc?.FilePath == null || !modifiedFiles.Contains(doc.FilePath))
                    continue;

                var text = await doc.GetTextAsync();
                await File.WriteAllTextAsync(doc.FilePath, text.ToString());
                await output.WriteLineAsync($"  Fixed: {doc.FilePath}");
            }
        }
        else if (verify && modifiedFiles.Count > 0)
        {
            foreach (var file in modifiedFiles)
            {
                await output.WriteLineAsync($"  Would fix: {file}");
            }
        }

        await output.WriteLineAsync($"  {fixesApplied} fix(es) applied, {unfixable.Count} unfixable");

        return new FormatResult(totalDiagnostics, fixesApplied, modifiedFiles, unfixable);
    }
}
