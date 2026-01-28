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
        TextWriter? output = null,
        string[]? diagnosticIds = null,
        string? preferredFixTitle = null)
    {
        output ??= Console.Out;
        var diagnosticIdFilter = diagnosticIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        
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

        // Create analyzer options with the project's analyzer config
        var analyzerOptions = new AnalyzerOptions(
            additionalFiles: ImmutableArray<AdditionalText>.Empty,
            optionsProvider: new ProjectAnalyzerConfigOptionsProvider(project));

        // Run analyzers
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            analyzers,
            new CompilationWithAnalyzersOptions(analyzerOptions, null, true, false));
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        
        // Filter to only diagnostics with fixes and in source
        var fixableDiagnostics = diagnostics
            .Where(d => d.Location.IsInSource && 
                        d.Severity != DiagnosticSeverity.Hidden)
            .ToList();

        // Apply diagnostic ID filter if specified
        if (diagnosticIdFilter != null && diagnosticIdFilter.Count > 0)
        {
            fixableDiagnostics = fixableDiagnostics
                .Where(d => diagnosticIdFilter.Contains(d.Id))
                .ToList();
        }

        totalDiagnostics = fixableDiagnostics.Count;
        await output.WriteLineAsync($"  Found {totalDiagnostics} diagnostic(s)");

        if (totalDiagnostics == 0)
        {
            return new FormatResult(0, 0, modifiedFiles, unfixable);
        }

        // Group diagnostics by diagnostic ID and find appropriate fixer
        var diagnosticsByIdAndFixer = new Dictionary<string, (CodeFixProvider Fixer, string EquivalenceKey, List<Diagnostic> Diagnostics)>();
        
        foreach (var diagnostic in fixableDiagnostics)
        {
            // Skip if we already have a fixer for this diagnostic ID
            if (diagnosticsByIdAndFixer.ContainsKey(diagnostic.Id))
            {
                var entry = diagnosticsByIdAndFixer[diagnostic.Id];
                entry.Diagnostics.Add(diagnostic);
                diagnosticsByIdAndFixer[diagnostic.Id] = entry;
                continue;
            }

            // Find a fixer and determine equivalence key from first diagnostic
            foreach (var fixer in fixers)
            {
                if (!fixer.FixableDiagnosticIds.Contains(diagnostic.Id))
                    continue;

                var document = project.Solution.GetDocument(diagnostic.Location.SourceTree);
                if (document == null)
                    continue;

                // Get the code action to find its equivalence key
                var codeActions = new List<CodeAction>();
                var context = new CodeFixContext(
                    document,
                    diagnostic,
                    (action, _) => codeActions.Add(action),
                    CancellationToken.None);

                try
                {
                    await fixer.RegisterCodeFixesAsync(context);
                }
                catch
                {
                    continue;
                }

                // Prefer the specified fix title if provided
                var codeAction = preferredFixTitle != null
                    ? codeActions.FirstOrDefault(a => a.Title.StartsWith(preferredFixTitle, StringComparison.OrdinalIgnoreCase))
                      ?? codeActions.FirstOrDefault()
                    : codeActions.FirstOrDefault();

                if (codeAction == null)
                    continue;

                diagnosticsByIdAndFixer[diagnostic.Id] = (fixer, codeAction.EquivalenceKey ?? codeAction.Title, [diagnostic]);
                break;
            }

            // Track unfixable diagnostics
            if (!diagnosticsByIdAndFixer.ContainsKey(diagnostic.Id))
            {
                unfixable.Add($"{diagnostic.Id}: {diagnostic.GetMessage()}");
            }
        }

        // Apply fixes using FixAllProvider (bulk fix)
        var solution = project.Solution;

        foreach (var (diagnosticId, (fixer, equivalenceKey, diagnosticsToFix)) in diagnosticsByIdAndFixer)
        {
            var fixAllProvider = fixer.GetFixAllProvider();
            
            if (fixAllProvider == null)
            {
                // Fall back to applying fixes individually
                foreach (var diagnostic in diagnosticsToFix)
                {
                    var doc = solution.GetDocument(diagnostic.Location.SourceTree);
                    if (doc == null)
                        continue;

                    var codeActions = new List<CodeAction>();
                    var context = new CodeFixContext(
                        doc,
                        diagnostic,
                        (action, _) => codeActions.Add(action),
                        CancellationToken.None);

                    try
                    {
                        await fixer.RegisterCodeFixesAsync(context);
                    }
                    catch
                    {
                        continue;
                    }

                    var codeAction = codeActions.FirstOrDefault(a => a.EquivalenceKey == equivalenceKey)
                                  ?? codeActions.FirstOrDefault();

                    if (codeAction == null)
                        continue;

                    var operations = await codeAction.GetOperationsAsync(CancellationToken.None);
                    foreach (var operation in operations)
                    {
                        if (operation is ApplyChangesOperation applyChanges)
                        {
                            var changedSolution = applyChanges.ChangedSolution;
                            foreach (var projectChanges in changedSolution.GetChanges(solution).GetProjectChanges())
                            {
                                foreach (var docId in projectChanges.GetChangedDocuments())
                                {
                                    var changedDoc = changedSolution.GetDocument(docId);
                                    if (changedDoc?.FilePath != null && !modifiedFiles.Contains(changedDoc.FilePath))
                                    {
                                        modifiedFiles.Add(changedDoc.FilePath);
                                    }
                                }
                            }
                            
                            solution = changedSolution;
                            fixesApplied++;
                        }
                    }
                }
                continue;
            }

            // Get a document from the first diagnostic to create context
            var firstDiagnostic = diagnosticsToFix.First();
            var document = solution.GetDocument(firstDiagnostic.Location.SourceTree);
            if (document == null)
                continue;

            // Create a FixAllContext for project scope
            var diagnosticProvider = new DiagnosticProvider(diagnosticsToFix.ToImmutableArray());
            var fixAllContext = new FixAllContext(
                document,
                fixer,
                FixAllScope.Project,
                equivalenceKey,
                [diagnosticId],
                diagnosticProvider,
                CancellationToken.None);

            try
            {
                var fixAllAction = await fixAllProvider.GetFixAsync(fixAllContext);
                if (fixAllAction != null)
                {
                    var operations = await fixAllAction.GetOperationsAsync(CancellationToken.None);
                    foreach (var operation in operations)
                    {
                        if (operation is ApplyChangesOperation applyChanges)
                        {
                            // Track changed files
                            var changedSolution = applyChanges.ChangedSolution;
                            foreach (var projectChanges in changedSolution.GetChanges(solution).GetProjectChanges())
                            {
                                foreach (var docId in projectChanges.GetChangedDocuments())
                                {
                                    var changedDoc = changedSolution.GetDocument(docId);
                                    if (changedDoc?.FilePath != null && !modifiedFiles.Contains(changedDoc.FilePath))
                                    {
                                        modifiedFiles.Add(changedDoc.FilePath);
                                    }
                                }
                            }
                            
                            solution = changedSolution;
                            fixesApplied += diagnosticsToFix.Count;
                        }
                    }
                }
                else
                {
                    await output.WriteLineAsync($"  Warning: FixAllProvider returned null for {diagnosticId}");
                }
            }
            catch (Exception ex)
            {
                await output.WriteLineAsync($"  Warning: FixAll failed for {diagnosticId}: {ex.Message}");
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

/// <summary>
/// Provides diagnostics to a FixAllContext.
/// </summary>
internal sealed class DiagnosticProvider : FixAllContext.DiagnosticProvider
{
    private readonly ImmutableArray<Diagnostic> _diagnostics;

    public DiagnosticProvider(ImmutableArray<Diagnostic> diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
    {
        return Task.FromResult<IEnumerable<Diagnostic>>(_diagnostics);
    }

    public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
    {
        var result = _diagnostics.Where(d => 
            d.Location.SourceTree != null && 
            d.Location.SourceTree.FilePath == document.FilePath);
        return Task.FromResult<IEnumerable<Diagnostic>>(result);
    }

    public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
    {
        return Task.FromResult<IEnumerable<Diagnostic>>([]);
    }
}

/// <summary>
/// Provides analyzer config options from a Roslyn Project's analyzer config documents.
/// </summary>
internal sealed class ProjectAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly Project _project;
    private readonly Dictionary<string, AnalyzerConfigOptions> _treeOptions = new();
    private readonly AnalyzerConfigOptions _globalOptions;

    public ProjectAnalyzerConfigOptionsProvider(Project project)
    {
        _project = project;
        _globalOptions = new DictionaryAnalyzerConfigOptions(LoadGlobalOptions());
    }

    private Dictionary<string, string> LoadGlobalOptions()
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var configDoc in _project.AnalyzerConfigDocuments)
        {
            var text = configDoc.GetTextAsync().GetAwaiter().GetResult();
            if (text == null) continue;
            
            var content = text.ToString();
            var lines = content.Split('\n');
            var isGlobal = false;
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("is_global", StringComparison.OrdinalIgnoreCase) && 
                    trimmed.Contains("true", StringComparison.OrdinalIgnoreCase))
                {
                    isGlobal = true;
                }
                
                if (isGlobal && trimmed.StartsWith("build_property.", StringComparison.OrdinalIgnoreCase))
                {
                    var eqIdx = trimmed.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        var key = trimmed.Substring(0, eqIdx).Trim();
                        var value = trimmed.Substring(eqIdx + 1).Trim();
                        options[key] = value;
                    }
                }
            }
        }
        
        return options;
    }

    public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        // Return global options for all trees since we're mainly interested in build_property settings
        return _globalOptions;
    }

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        return _globalOptions;
    }
}

internal sealed class DictionaryAnalyzerConfigOptions : AnalyzerConfigOptions
{
    private readonly Dictionary<string, string> _options;

    public DictionaryAnalyzerConfigOptions(Dictionary<string, string> options)
    {
        _options = options;
    }

    public override bool TryGetValue(string key, out string value)
    {
        return _options.TryGetValue(key, out value!);
    }
}
