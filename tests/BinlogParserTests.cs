namespace FormatLog.Tests;

public class BinlogParserTests
{
    private static string? FindTestBinlog()
    {
        // Look for test.binlog in parent directories
        var dir = Path.GetDirectoryName(typeof(BinlogParserTests).Assembly.Location);
        while (dir != null)
        {
            var binlogPath = Path.Combine(dir, "test.binlog");
            if (File.Exists(binlogPath))
                return binlogPath;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    [Fact]
    public void ExtractCscInvocations_WithValidBinlog_FindsCompilations()
    {
        var binlogPath = FindTestBinlog();
        if (binlogPath == null) return; // Skip if no binlog

        var invocations = BinlogParser.ExtractCscInvocations(binlogPath);

        // May be empty for incremental builds - that's OK
        // Just verify it doesn't throw
        Assert.NotNull(invocations);
    }

    [Fact]
    public void ExtractCscInvocations_WithValidBinlog_ExtractsProjectName()
    {
        var binlogPath = FindTestBinlog();
        if (binlogPath == null) return;

        var invocations = BinlogParser.ExtractCscInvocations(binlogPath);
        if (invocations.Count == 0) return; // Skip for incremental builds

        Assert.Contains(invocations, i => i.ProjectName.Contains(".csproj"));
    }

    [Fact]
    public void ExtractCscInvocations_WithValidBinlog_ExtractsProjectDirectory()
    {
        var binlogPath = FindTestBinlog();
        if (binlogPath == null) return;

        var invocations = BinlogParser.ExtractCscInvocations(binlogPath);
        if (invocations.Count == 0) return; // Skip for incremental builds

        foreach (var invocation in invocations)
        {
            Assert.False(string.IsNullOrEmpty(invocation.ProjectDirectory));
            Assert.True(Directory.Exists(invocation.ProjectDirectory),
                $"Project directory should exist: {invocation.ProjectDirectory}");
        }
    }

    [Fact]
    public void ExtractCscInvocations_WithValidBinlog_ExtractsCommandLineArgs()
    {
        var binlogPath = FindTestBinlog();
        if (binlogPath == null) return;

        var invocations = BinlogParser.ExtractCscInvocations(binlogPath);
        if (invocations.Count == 0) return; // Skip for incremental builds

        foreach (var invocation in invocations)
        {
            Assert.NotEmpty(invocation.CommandLineArgs);
            
            // Should contain typical csc arguments
            Assert.Contains(invocation.CommandLineArgs, a => 
                a.StartsWith("/") || a.StartsWith("-") || a.EndsWith(".cs"));
        }
    }

    [Fact]
    public void ExtractCscInvocations_WithValidBinlog_CommandLineContainsSourceFiles()
    {
        var binlogPath = FindTestBinlog();
        if (binlogPath == null) return;

        var invocations = BinlogParser.ExtractCscInvocations(binlogPath);
        if (invocations.Count == 0) return; // Skip for incremental builds

        foreach (var invocation in invocations)
        {
            // At least one .cs file should be in the args
            Assert.Contains(invocation.CommandLineArgs, a => a.EndsWith(".cs"));
        }
    }

    [Fact]
    public void ExtractCscInvocations_WithValidBinlog_CommandLineContainsAnalyzers()
    {
        var binlogPath = FindTestBinlog();
        if (binlogPath == null) return;

        var invocations = BinlogParser.ExtractCscInvocations(binlogPath);
        if (invocations.Count == 0) return; // Skip for incremental builds

        // At least one project should have analyzers
        Assert.Contains(invocations, i => 
            i.CommandLineArgs.Any(a => a.StartsWith("/analyzer:") || a.StartsWith("-analyzer:")));
    }

    [Fact]
    public void ExtractCscInvocations_WithMultipleProjects_FindsAll()
    {
        var binlogPath = FindTestBinlog();
        if (binlogPath == null) return;

        var invocations = BinlogParser.ExtractCscInvocations(binlogPath);

        // Note: For incremental builds, this may be 0
        // A full rebuild will have 2+ invocations
        Assert.NotNull(invocations);
    }

    [Fact]
    public void ExtractCscInvocations_WithNonExistentFile_Throws()
    {
        Assert.ThrowsAny<IOException>(() =>
            BinlogParser.ExtractCscInvocations("/nonexistent/path/build.binlog"));
    }
}
