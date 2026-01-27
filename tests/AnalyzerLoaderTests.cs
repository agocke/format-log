namespace FormatLog.Tests;

public class AnalyzerLoaderTests
{
    [Fact]
    public void ExtractAnalyzerPaths_WithAnalyzerArgs_ExtractsPaths()
    {
        var loader = new AnalyzerLoader();
        var args = new[]
        {
            "/target:library",
            "/analyzer:/path/to/analyzer1.dll",
            "-analyzer:/path/to/analyzer2.dll",
            "/reference:/some/ref.dll",
            "/analyzer:/path/to/analyzer3.dll"
        };

        var paths = AnalyzerLoader.ExtractAnalyzerPaths(args).ToList();

        Assert.Equal(3, paths.Count);
        Assert.Contains("/path/to/analyzer1.dll", paths);
        Assert.Contains("/path/to/analyzer2.dll", paths);
        Assert.Contains("/path/to/analyzer3.dll", paths);
    }

    [Fact]
    public void ExtractAnalyzerPaths_WithNoAnalyzers_ReturnsEmpty()
    {
        var loader = new AnalyzerLoader();
        var args = new[]
        {
            "/target:library",
            "/reference:/some/ref.dll"
        };

        var paths = AnalyzerLoader.ExtractAnalyzerPaths(args).ToList();

        Assert.Empty(paths);
    }

    [Fact]
    public void LoadAnalyzers_WithNonExistentPath_SkipsGracefully()
    {
        var loader = new AnalyzerLoader();
        var paths = new[] { "/nonexistent/analyzer.dll" };

        var (analyzers, fixers) = AnalyzerLoader.LoadAnalyzers(paths);

        Assert.Empty(analyzers);
        Assert.Empty(fixers);
    }
}
