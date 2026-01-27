using System.CommandLine;
using FormatLog;

var binlogArg = new Argument<FileInfo>("binlog") { Description = "Path to the MSBuild binary log file (.binlog)" };
var verifyOption = new Option<bool>("--verify-no-changes") { Description = "Check if files are formatted without making changes" };
var verboseOption = new Option<bool>("--verbose") { Description = "Enable verbose output" };

var rootCommand = new RootCommand("Format C# source files using compilation info from an MSBuild binlog")
{
    binlogArg,
    verifyOption,
    verboseOption
};

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var binlog = parseResult.GetValue(binlogArg)!;
    var verify = parseResult.GetValue(verifyOption);
    var verbose = parseResult.GetValue(verboseOption);

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

    if (verbose)
    {
        Console.WriteLine($"Found {cscInvocations.Count} C# compiler invocation(s)");
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
            verbose);
        hasChanges |= result.FixesApplied > 0;
    }

    return verify && hasChanges ? 1 : 0;
});

return await rootCommand.Parse(args).InvokeAsync();
