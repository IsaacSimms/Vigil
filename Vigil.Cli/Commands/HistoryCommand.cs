// == HistoryCommand (stub for v1 per plan; full query in Phase 5/6) == //

using Spectre.Console;
using Spectre.Console.Cli;

namespace Vigil.Cli.Commands;

public sealed class HistoryCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[yellow]History not fully implemented in this v1 skeleton.[/]");
        return 0;
    }
}
