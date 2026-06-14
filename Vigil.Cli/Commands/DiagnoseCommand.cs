// == DiagnoseCommand (thin Spectre command per §3/§10/AGENTS; no business logic) == //

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Entities;
using Vigil.Domain.Models;

namespace Vigil.Cli.Commands;

/// <summary>
/// Thin command: gathers input (stdin + flags), builds DiagnoseRequest, calls IVigilClient, renders result.
/// All logic behind the IVigilClient seam.
/// </summary>
public sealed class DiagnoseCommand : AsyncCommand<DiagnoseCommand.Settings>
{
    private readonly IVigilClient _client;

    public DiagnoseCommand(IVigilClient client)
    {
        _client = client;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[SOURCES]")]
        public string[]? Sources { get; init; }

        [CommandOption("--symptom")]
        public string? Symptom { get; init; }

        [CommandOption("--offline")]
        public bool Offline { get; init; }

        [CommandOption("--dry-run")]
        public bool DryRun { get; init; }

        [CommandOption("--json")]
        public bool Json { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var sources = new List<RawSource>();

        // stdin as primary if available
        if (!Console.IsInputRedirected)
        {
            // for demo, if no pipe, use a sample or require flag
        }
        else
        {
            var stdin = await Console.In.ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(stdin))
            {
                sources.Add(new RawSource(Text: stdin.Trim(), Hint: "stdin"));
            }
        }

        // TODO: support --logs etc repeatable flags for multiple sources (skeleton uses stdin or sample)
        if (sources.Count == 0)
        {
            // sample for demo if no input
            sources.Add(new RawSource(Text: "error after change deploy-456 to payment-service", Hint: "sample"));
        }

        var hints = new ScopeHints(Symptom: settings.Symptom);
        var request = new DiagnoseRequest(sources, hints, settings.Offline, settings.DryRun);

        var diagnosis = await _client.DiagnoseAsync(request);

        if (settings.Json)
        {
            // simple json for piping
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(diagnosis, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            RenderHuman(diagnosis, settings.DryRun);
        }

        return 0;
    }

    private void RenderHuman(Diagnosis diagnosis, bool dryRun)
    {
        var rule = new Rule(dryRun ? "[yellow]DRY-RUN PREVIEW[/]" : "[green]Diagnosis[/]");
        AnsiConsole.Write(rule);

        var tree = new Tree($"[bold]{diagnosis.Summary}[/]");

        foreach (var cause in diagnosis.Causes)
        {
            var causeNode = tree.AddNode($"[cyan]{cause.Description}[/] (conf: {cause.Confidence.Value:P0}, {cause.Severity})");

            if (!string.IsNullOrEmpty(cause.CausalChain))
                causeNode.AddNode($"Chain: {cause.CausalChain}");

            var cites = causeNode.AddNode("Citations");
            foreach (var cit in cause.Citations)
            {
                cites.AddNode($"Artifact {cit.EvidenceArtifactId} - {cit.Snippet ?? "(no snippet)"}");
            }
        }

        AnsiConsole.Write(tree);

        // Provenance
        var prov = diagnosis.Provenance;
        AnsiConsole.MarkupLine($"[grey]Provenance: {prov.AnalyzedBy} (tier) {prov.Reason?.ToString() ?? ""}[/]");

        if (prov.Usage != null)
        {
            AnsiConsole.MarkupLine($"[grey]Tokens: in={prov.Usage.InputTokens} out={prov.Usage.OutputTokens}[/]");
        }

        if (dryRun)
        {
            AnsiConsole.MarkupLine("[yellow]This was a dry-run. No model call was made.[/]");
        }
    }
}
