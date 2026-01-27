namespace FormatLog.Tests;

public class CodeFormatterTests
{
    [Fact]
    public async Task FormatAsync_WithNoAnalyzers_ReturnsZeroDiagnostics()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"format-log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var sourceFile = Path.Combine(testDir, "Test.cs");
            File.WriteAllText(sourceFile, """
                class Test { void Method() { } }
                """);

            // No analyzers specified
            var args = new[]
            {
                "/target:library",
                "/langversion:latest",
                sourceFile
            };

            var output = new StringWriter();

            var result = await CodeFormatter.FormatAsync(
                "TestProject",
                args,
                testDir,
                verify: false,
                verbose: true,
                output: output);

            Assert.Equal(0, result.DiagnosticsFound);
            Assert.Equal(0, result.FixesApplied);
            Assert.Empty(result.ModifiedFiles);
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task FormatAsync_WithVerifyMode_DoesNotModifyFiles()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"format-log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var sourceFile = Path.Combine(testDir, "Test.cs");
            var originalContent = """
                class Test { void Method() { } }
                """;
            File.WriteAllText(sourceFile, originalContent);

            var args = new[]
            {
                "/target:library",
                "/langversion:latest",
                sourceFile
            };

            var output = new StringWriter();

            var result = await CodeFormatter.FormatAsync(
                "TestProject",
                args,
                testDir,
                verify: true,
                verbose: false,
                output: output);

            // File should NOT have been modified
            var actualContent = await File.ReadAllTextAsync(sourceFile);
            Assert.Equal(originalContent, actualContent);
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }
}
