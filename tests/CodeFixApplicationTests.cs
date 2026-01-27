using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace FormatLog.Tests;

/// <summary>
/// A test code fix provider that renames "BadClassName" to "GoodClassName"
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public class TestCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [TestAnalyzer.DiagnosticId];

    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root == null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var declaration = root.FindToken(diagnosticSpan.Start).Parent?
            .AncestorsAndSelf()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault();

        if (declaration == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Rename to GoodClassName",
                createChangedDocument: c => RenameClass(context.Document, declaration, c),
                equivalenceKey: "RenameToGoodClassName"),
            diagnostic);
    }

    private static async Task<Document> RenameClass(
        Document document,
        ClassDeclarationSyntax classDecl,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null) return document;

        var newClassDecl = classDecl.WithIdentifier(
            SyntaxFactory.Identifier("GoodClassName")
                .WithTriviaFrom(classDecl.Identifier));

        var newRoot = root.ReplaceNode(classDecl, newClassDecl);
        return document.WithSyntaxRoot(newRoot);
    }
}

public class CodeFixApplicationTests
{
    [Fact]
    public async Task ApplyFixes_WithMatchingFixer_AppliesFix()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"format-log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var file = Path.Combine(testDir, "Test.cs");
            File.WriteAllText(file, "class BadClassName { }");

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

            var analyzers = ImmutableArray.Create<Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer>(new TestAnalyzer());
            var fixers = ImmutableArray.Create<CodeFixProvider>(new TestCodeFixProvider());

            var output = new StringWriter();
            var result = await CodeFixRunner.ApplyFixesAsync(project, analyzers, fixers, verify: false, output);

            Assert.Equal(1, result.DiagnosticsFound);
            Assert.Equal(1, result.FixesApplied);
            Assert.Single(result.ModifiedFiles);
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyFixes_InVerifyMode_DoesNotWriteFiles()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"format-log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var file = Path.Combine(testDir, "Test.cs");
            var originalContent = "class BadClassName { }";
            File.WriteAllText(file, originalContent);

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

            var analyzers = ImmutableArray.Create<Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer>(new TestAnalyzer());
            var fixers = ImmutableArray.Create<CodeFixProvider>(new TestCodeFixProvider());

            var output = new StringWriter();
            var result = await CodeFixRunner.ApplyFixesAsync(project, analyzers, fixers, verify: true, output);

            Assert.Equal(1, result.DiagnosticsFound);
            Assert.Equal(1, result.FixesApplied);

            // File should NOT have been modified
            var actualContent = await File.ReadAllTextAsync(file);
            Assert.Equal(originalContent, actualContent);
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyFixes_WithNoMatchingFixer_ReportsUnfixable()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"format-log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var file = Path.Combine(testDir, "Test.cs");
            File.WriteAllText(file, "class BadClassName { }");

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

            // Analyzer but NO fixer
            var analyzers = ImmutableArray.Create<Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer>(new TestAnalyzer());
            var fixers = ImmutableArray<CodeFixProvider>.Empty;

            var output = new StringWriter();
            var result = await CodeFixRunner.ApplyFixesAsync(project, analyzers, fixers, verify: false, output);

            Assert.Equal(1, result.DiagnosticsFound);
            Assert.Equal(0, result.FixesApplied);
            Assert.Single(result.UnfixableDiagnostics);
            Assert.Contains("TEST001", result.UnfixableDiagnostics[0]);
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyFixes_WithNoDiagnostics_ReturnsZero()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"format-log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var file = Path.Combine(testDir, "Test.cs");
            File.WriteAllText(file, "class GoodClassName { }"); // No issue

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

            var analyzers = ImmutableArray.Create<Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer>(new TestAnalyzer());
            var fixers = ImmutableArray.Create<CodeFixProvider>(new TestCodeFixProvider());

            var output = new StringWriter();
            var result = await CodeFixRunner.ApplyFixesAsync(project, analyzers, fixers, verify: false, output);

            Assert.Equal(0, result.DiagnosticsFound);
            Assert.Equal(0, result.FixesApplied);
            Assert.Empty(result.ModifiedFiles);
            Assert.Empty(result.UnfixableDiagnostics);
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }
}
