using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace FormatLog.Tests;

/// <summary>
/// A simple test analyzer that reports a diagnostic for any class named "BadClassName"
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TestAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "TEST001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Bad class name",
        "Class '{0}' has a bad name",
        "Naming",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.NamedType);
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context)
    {
        var namedTypeSymbol = (INamedTypeSymbol)context.Symbol;

        if (namedTypeSymbol.Name == "BadClassName")
        {
            var diagnostic = Diagnostic.Create(Rule, namedTypeSymbol.Locations[0], namedTypeSymbol.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }
}

public class DiagnosticRunnerTests
{
    [Fact]
    public async Task RunAnalyzers_WithMatchingCode_FindsDiagnostics()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"format-log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var file = Path.Combine(testDir, "Test.cs");
            File.WriteAllText(file, "class BadClassName { }");

            // Create workspace with the code
            using var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId("TestProject");

            var projectInfo = ProjectInfo.Create(
                projectId,
                VersionStamp.Default,
                "TestProject",
                "TestProject",
                LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var solution = workspace.CurrentSolution.AddProject(projectInfo);

            var text = await File.ReadAllTextAsync(file);
            var docId = DocumentId.CreateNewId(projectId, file);
            solution = solution.AddDocument(docId, "Test.cs", SourceText.From(text), filePath: file);

            workspace.TryApplyChanges(solution);

            var project = workspace.CurrentSolution.GetProject(projectId)!;
            var compilation = await project.GetCompilationAsync();

            // Run our test analyzer
            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new TestAnalyzer());
            var compilationWithAnalyzers = compilation!.WithAnalyzers(analyzers);
            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

            Assert.Single(diagnostics);
            Assert.Equal(TestAnalyzer.DiagnosticId, diagnostics[0].Id);
            Assert.Contains("BadClassName", diagnostics[0].GetMessage());
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAnalyzers_WithNonMatchingCode_NoDiagnostics()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"format-log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var file = Path.Combine(testDir, "Test.cs");
            File.WriteAllText(file, "class GoodClassName { }");

            using var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId("TestProject");

            var projectInfo = ProjectInfo.Create(
                projectId,
                VersionStamp.Default,
                "TestProject",
                "TestProject",
                LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var solution = workspace.CurrentSolution.AddProject(projectInfo);

            var text = await File.ReadAllTextAsync(file);
            var docId = DocumentId.CreateNewId(projectId, file);
            solution = solution.AddDocument(docId, "Test.cs", SourceText.From(text), filePath: file);

            workspace.TryApplyChanges(solution);

            var project = workspace.CurrentSolution.GetProject(projectId)!;
            var compilation = await project.GetCompilationAsync();

            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new TestAnalyzer());
            var compilationWithAnalyzers = compilation!.WithAnalyzers(analyzers);
            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

            Assert.Empty(diagnostics);
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAnalyzers_WithMultipleIssues_FindsAllDiagnostics()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"format-log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var file = Path.Combine(testDir, "Test.cs");
            File.WriteAllText(file, @"
                class BadClassName { }
                class AnotherClass { }
                namespace Nested { class BadClassName { } }
            ");

            using var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId("TestProject");

            var projectInfo = ProjectInfo.Create(
                projectId,
                VersionStamp.Default,
                "TestProject",
                "TestProject",
                LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var solution = workspace.CurrentSolution.AddProject(projectInfo);

            var text = await File.ReadAllTextAsync(file);
            var docId = DocumentId.CreateNewId(projectId, file);
            solution = solution.AddDocument(docId, "Test.cs", SourceText.From(text), filePath: file);

            workspace.TryApplyChanges(solution);

            var project = workspace.CurrentSolution.GetProject(projectId)!;
            var compilation = await project.GetCompilationAsync();

            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new TestAnalyzer());
            var compilationWithAnalyzers = compilation!.WithAnalyzers(analyzers);
            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

            // Should find 2 instances of BadClassName
            Assert.Equal(2, diagnostics.Length);
            Assert.All(diagnostics, d => Assert.Equal(TestAnalyzer.DiagnosticId, d.Id));
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }
}
