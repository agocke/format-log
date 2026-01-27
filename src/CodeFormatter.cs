using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace FormatLog;

public record FormatResult(
    int DiagnosticsFound,
    int FixesApplied, 
    List<string> ModifiedFiles,
    List<string> UnfixableDiagnostics);

public class CodeFormatter
{
    public static async Task<FormatResult> FormatAsync(
        string projectName,
        string[] commandLineArgs,
        string baseDirectory,
        bool verify,
        bool verbose,
        TextWriter? output = null)
    {
        output ??= Console.Out;
        var parsedArgs = CSharpCommandLineParser.Default.Parse(
            commandLineArgs,
            baseDirectory,
            sdkDirectory: null);

        if (parsedArgs.Errors.Any())
        {
            await output.WriteLineAsync($"Warning: Command line parse errors for {projectName}:");
            foreach (var error in parsedArgs.Errors)
            {
                await output.WriteLineAsync($"  {error.GetMessage()}");
            }
        }

        // Extract analyzer paths from command line
        var analyzerPaths = AnalyzerLoader.ExtractAnalyzerPaths(commandLineArgs).ToList();
        if (verbose)
        {
            await output.WriteLineAsync($"  Found {analyzerPaths.Count} analyzer reference(s)");
        }

        // Load analyzers and fixers
        var (analyzers, fixers) = AnalyzerLoader.LoadAnalyzers(analyzerPaths);
        if (verbose)
        {
            await output.WriteLineAsync($"  Loaded {analyzers.Length} analyzer(s), {fixers.Length} fixer(s)");
        }

        // Create workspace
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId(projectName);

        // Add metadata references
        var metadataReferences = parsedArgs.MetadataReferences
            .Select(r => MetadataReference.CreateFromFile(r.Reference))
            .Cast<MetadataReference>()
            .ToList();

        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Default,
            projectName,
            projectName,
            LanguageNames.CSharp,
            compilationOptions: parsedArgs.CompilationOptions,
            parseOptions: parsedArgs.ParseOptions,
            metadataReferences: metadataReferences);

        var solution = workspace.CurrentSolution.AddProject(projectInfo);

        // Add source files
        var sourceFiles = parsedArgs.SourceFiles;
        if (verbose)
        {
            await output.WriteLineAsync($"  Adding {sourceFiles.Length} source files");
        }

        foreach (var sourceFile in sourceFiles)
        {
            var filePath = sourceFile.Path;
            if (!File.Exists(filePath))
            {
                if (verbose)
                {
                    await output.WriteLineAsync($"  Warning: Source file not found: {filePath}");
                }
                continue;
            }

            var text = await File.ReadAllTextAsync(filePath);
            var documentId = DocumentId.CreateNewId(projectId, filePath);
            solution = solution.AddDocument(documentId, Path.GetFileName(filePath),
                SourceText.From(text), filePath: filePath);
        }

        workspace.TryApplyChanges(solution);

        var project = workspace.CurrentSolution.GetProject(projectId)!;

        // Run code fixers
        var result = await CodeFixRunner.ApplyFixesAsync(
            project,
            analyzers,
            fixers,
            verify,
            output);

        return result;
    }
}
