using System.CommandLine;
using FormatLog;

var binlogArg = new Argument<FileInfo>("binlog") { Description = "Path to the MSBuild binary log file (.binlog)" };
var verifyOption = new Option<bool>("--verify-no-changes") { Description = "Check if files are formatted without making changes" };
var verboseOption = new Option<bool>("--verbose") { Description = "Enable verbose output" };
var diagnosticsOption = new Option<string[]>("--diagnostics", "-d") { Description = "Filter to specific diagnostic IDs (e.g., -d IDE0001 -d IDE0002)" };
var projectOption = new Option<string>("--project", "-p") { Description = "Filter to a specific project name (substring match)" };

var rootCommand = new RootCommand("Apply Roslyn code fixes using compilation info from an MSBuild binlog")
{
    binlogArg,
    verifyOption,
    verboseOption,
    diagnosticsOption,
    projectOption
};

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var binlog = parseResult.GetValue(binlogArg)!;
    var verify = parseResult.GetValue(verifyOption);
    var verbose = parseResult.GetValue(verboseOption);
    var diagnosticIds = parseResult.GetValue(diagnosticsOption) ?? [];
    var projectFilter = parseResult.GetValue(projectOption);

    if (!binlog.Exists)
    {
        Console.Error.WriteLine($"Error: Binlog file not found: {binlog.FullName}");
        return 1;
    }

    var cscInvocations = BinlogParser.ExtractCscInvocations(binlog.FullName);

    if (cscInvocations.Count == 0)
    {
        Console.Error.WriteLine("Error: No C# compiler invocations found in binlog");
        return 1;
    }

    // Filter by project name if specified
    if (!string.IsNullOrEmpty(projectFilter))
    {
        cscInvocations = cscInvocations
            .Where(i => i.ProjectName.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (cscInvocations.Count == 0)
        {
            Console.Error.WriteLine($"Error: No projects matching '{projectFilter}' found");
            return 1;
        }
    }

    if (verbose)
    {
        Console.WriteLine($"Found {cscInvocations.Count} C# compiler invocation(s)");
        if (diagnosticIds.Length > 0)
        {
            Console.WriteLine($"Filtering to diagnostics: {string.Join(", ", diagnosticIds)}");
        }
    }

    var hasChanges = false;

    foreach (var invocation in cscInvocations)
    {
        Console.WriteLine($"Processing: {invocation.ProjectName}");
        var result = await CodeFormatter.FormatAsync(
            invocation.ProjectName,
            invocation.CommandLineArgs,
            invocation.ProjectDirectory,
            verify,
            verbose,
            diagnosticIds: diagnosticIds);
        hasChanges |= result.FixesApplied > 0;
    }

    return verify && hasChanges ? 1 : 0;
});

return await rootCommand.Parse(args).InvokeAsync();
