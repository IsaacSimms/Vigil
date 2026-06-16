// == Program (composition root for Vigil.Cli per §3, AGENTS, grill-me) == //

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using Vigil.Application.Coordinators;
using Vigil.Application.UseCases;
using Vigil.Cli.Commands;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Entities;
using Vigil.Domain.Models;
using Vigil.Infrastructure;
using Vigil.Infrastructure.Interpreters;
using Vigil.Infrastructure.Redactors;
using Vigil.Infrastructure.Repositories;

namespace Vigil.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        var registrations = new ServiceCollection();

        // Register interpreters (text-only for this iteration)
        registrations.AddSingleton<IArtifactInterpreter, PlainTextInterpreter>();
        registrations.AddSingleton<IArtifactInterpreter, ChangeRecordInterpreter>();
        registrations.AddSingleton<IArtifactInterpreter, JsonLogInterpreter>();

        // Selector (selects from the registered interpreters)
        registrations.AddSingleton<IArtifactInterpreterSelector>(sp =>
        {
            var interpreters = sp.GetServices<IArtifactInterpreter>();
            return new ArtifactInterpreterSelector(interpreters);
        });

        // Coordinators from App
        registrations.AddSingleton<EvidenceAssembler>();
        registrations.AddSingleton<DiagnosisValidator>();
        registrations.AddSingleton<ICitationResolver, SimpleCitationResolver>();

        // Redactor (real text masking)
        registrations.AddSingleton<IRedactor, TextRedactor>();

        // Analyzers: choose based on key presence (Grok primary, heuristic fallback/offline)
        registrations.AddSingleton<IDiagnosisAnalyzer>(sp =>
        {
            var options = new GrokOptions
            {
                ApiKey = Environment.GetEnvironmentVariable("XAI_API_KEY") ?? string.Empty,
                Model = "grok-3",
                MaxTokens = 2000,
                TimeoutSeconds = 60,
                Temperature = 0.1,
                BaseUrl = "https://api.x.ai/v1"
            };

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                return new GrokDiagnosisAnalyzer(options);
            }
            return new HeuristicDiagnosisAnalyzer();
        });

        // For UseCase we need explicit heuristic too for offline/dry-run
        registrations.AddSingleton<HeuristicDiagnosisAnalyzer>();
        registrations.AddSingleton<GrokDiagnosisAnalyzer>(sp =>
        {
            var options = new GrokOptions
            {
                ApiKey = Environment.GetEnvironmentVariable("XAI_API_KEY") ?? string.Empty,
                Model = "grok-3",
                MaxTokens = 2000,
                TimeoutSeconds = 60,
                Temperature = 0.1,
                BaseUrl = "https://api.x.ai/v1"
            };
            return new GrokDiagnosisAnalyzer(options);
        });

        // In-memory repo for v1
        registrations.AddSingleton<IDiagnosisRepository, InMemoryDiagnosisRepository>();

        // UseCase (note ctor now expects model + heuristic analyzers)
        registrations.AddSingleton<DiagnoseUseCase>(sp =>
        {
            var selector = sp.GetRequiredService<IArtifactInterpreterSelector>();
            var assembler = sp.GetRequiredService<EvidenceAssembler>();
            var redactor = sp.GetRequiredService<IRedactor>();
            var modelAnalyzer = sp.GetRequiredService<GrokDiagnosisAnalyzer>();
            var heuristicAnalyzer = sp.GetRequiredService<HeuristicDiagnosisAnalyzer>();
            var validator = sp.GetRequiredService<DiagnosisValidator>();
            var repo = sp.GetRequiredService<IDiagnosisRepository>();
            return new DiagnoseUseCase(selector, assembler, redactor, modelAnalyzer, heuristicAnalyzer, validator, repo);
        });

        // Grill advisor for conversational NL in TUI sessions (Strategy seam)
        registrations.AddSingleton<IGrillAdvisor>(sp =>
        {
            var options = new GrokOptions
            {
                ApiKey = Environment.GetEnvironmentVariable("XAI_API_KEY") ?? string.Empty,
                Model = "grok-3",
                MaxTokens = 1500,
                TimeoutSeconds = 45,
                Temperature = 0.3,
                BaseUrl = "https://api.x.ai/v1"
            };
            return new GrokGrillAdvisor(options);
        });

        // In-process client (default transport)
        registrations.AddSingleton<IVigilClient>(sp =>
        {
            var useCase = sp.GetRequiredService<DiagnoseUseCase>();
            var grillAdvisor = sp.GetRequiredService<IGrillAdvisor>();
            return new Vigil.Application.Clients.InProcessVigilClient(useCase, grillAdvisor);
        });

        // == Interactive Grill-me launch guard (primary UX per approved plan) == //
        // Bare invocation (no subcommand) launches the TUI session with natural language to the backend.
        if (Vigil.Application.GrillInteractive.ShouldRunInteractive(args, isTty: !Console.IsInputRedirected))
        {
            var sp = registrations.BuildServiceProvider();
            var client = sp.GetRequiredService<IVigilClient>();
            return RunGrillMeSession(client, Environment.CurrentDirectory);
        }

        var registrar = new TypeRegistrar(registrations);
        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.AddCommand<DiagnoseCommand>("diagnose");
            // History command stub for v1 completeness
            config.AddCommand<HistoryCommand>("history");
        });

        return app.Run(args);
    }

    // == Minimal working Grill-me TUI (activated on bare launch) == //
    // Demonstrates the approved experience: persistent SessionState with running tokens + context,
    // NL → Consult (context-aware), /load for evidence, /diagnose to interleave the real pipeline.
    static int RunGrillMeSession(IVigilClient client, string launchDir)
    {
        var state = new Vigil.Application.GrillSessionState(launchDir);

        AnsiConsole.Write(new Rule("[bold green]Vigil — Grill-me session[/]"));
        AnsiConsole.MarkupLine($"[grey]In[/] [cyan]{launchDir}[/]");
        AnsiConsole.MarkupLine("[grey]Type naturally. /help, /load, /paste, /diagnose, /status, exit.[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            var input = AnsiConsole.Ask<string>("[bold]> [/]");
            if (string.IsNullOrWhiteSpace(input)) continue;

            var intent = Vigil.Application.GrillInteractive.ParseIntent(input);

            if (intent.IsExplicitCommand)
            {
                var c = (intent.CommandName ?? "").ToLower();
                if (c is "exit" or "quit" or "q") { AnsiConsole.MarkupLine("[grey]Session ended.[/]"); break; }
                if (c is "help" or "?" )
                {
                    AnsiConsole.MarkupLine("[yellow]/load <rel-path>  /paste [name]  (paste text, then END)[/]");
                    AnsiConsole.MarkupLine("[yellow]/diagnose [--symptom \"...\"] [--offline]  diagnose me — <symptom>[/]");
                    AnsiConsole.MarkupLine("[yellow]/status  exit[/]");
                    continue;
                }
                if (c == "load" && intent.Arguments.Count > 0)
                {
                    var outcome = state.TryLoadEvidenceFromPath(intent.Arguments[0], hint: "loaded");
                    if (outcome.Loaded)
                        AnsiConsole.MarkupLine($"[green]Loaded[/] — evidence now {state.CurrentEvidenceCount}");
                    else if (outcome.SkipReason == "too-large")
                        AnsiConsole.MarkupLine($"[red]too large[/] — exceeds {Vigil.Application.GrillSessionState.MaxAutoLoadBytes} bytes; trim or load a smaller excerpt");
                    else
                        AnsiConsole.MarkupLine("[red]not found[/]");
                    continue;
                }
                if (c == "paste")
                {
                    var requestedName = intent.Arguments.Count > 0 ? intent.Arguments[0] : null;
                    HandlePasteInSession(client, state, launchDir, requestedName);
                    continue;
                }
                if (c == "diagnose")
                {
                    var argTail = Vigil.Application.GrillInteractive.ExtractSlashDiagnoseArgTail(input);
                    var parsed = Vigil.Application.GrillInteractive.ParseDiagnoseFlagsWithRemainder(argTail);
                    HandleDiagnoseInSession(client, state, input, parsed.Args, parsed.Remainder);
                    continue;
                }
                if (c == "status")
                {
                    AnsiConsole.MarkupLine($"Evidence: {state.CurrentEvidenceCount} | Turns: {state.Turns.Count} | Tokens: {state.TotalTokensUsed}");
                    continue;
                }
                continue;
            }

            // NL diagnose-intent routes to the same governed handler as /diagnose
            var diagnoseIntent = Vigil.Application.GrillInteractive.TryParseDiagnoseIntent(input);
            if (diagnoseIntent != null)
            {
                HandleDiagnoseInSession(client, state, input, diagnoseIntent.Args, diagnoseIntent.UtteranceRemainder);
                continue;
            }

            // Natural language — intent-gated auto-load, then Consult with bounded excerpts
            var loadResult = Vigil.Application.GrillInteractive.TryExtractAndLoadPaths(input, state);
            if (loadResult.LoadedFileNames.Count > 0)
                AnsiConsole.MarkupLine($"[green]Auto-loaded[/] {string.Join(", ", loadResult.LoadedFileNames)} — evidence now {state.CurrentEvidenceCount}");
            if (loadResult.NotFoundPaths.Count > 0)
                AnsiConsole.MarkupLine($"[yellow]Not found[/] {string.Join(", ", loadResult.NotFoundPaths)}");
            if (loadResult.SkippedTooLarge.Count > 0)
                AnsiConsole.MarkupLine($"[yellow]Too large[/] {string.Join(", ", loadResult.SkippedTooLarge)} — use /load after trimming or split the file");

            var reply = client.ConsultAsync(input, cwd: launchDir, lastDiagnosisId: state.LastDiagnosis?.Id, compactContext: state.GetCompactContextForChat().FormatForAdvisor()).GetAwaiter().GetResult();
            state.AppendTurn(input, reply);
            AnsiConsole.MarkupLine(reply);
        }
        return 0;
    }

    // == Multi-line /paste (clipboard logs → evidence + bounded consult preview) == //
    static void HandlePasteInSession(
        IVigilClient client,
        Vigil.Application.GrillSessionState state,
        string launchDir,
        string? requestedName)
    {
        var text = ReadMultilinePasteFromConsole();
        if (text == null)
            return;

        var lineCount = text.Split('\n').Length;
        var resolvedName = Vigil.Application.GrillInteractive.ResolvePasteEvidenceName(requestedName, state.CurrentEvidenceCount);
        var outcome = state.TryAddPastedEvidence(requestedName, text);

        if (outcome.Loaded)
        {
            var bytes = System.Text.Encoding.UTF8.GetByteCount(text);
            AnsiConsole.MarkupLine($"[green]Added[/] {outcome.FileName} as evidence ({bytes} bytes) — evidence now {state.CurrentEvidenceCount}");
        }
        else if (outcome.SkipReason == "already-loaded")
            AnsiConsole.MarkupLine($"[yellow]Already loaded[/] — {outcome.FileName}; sending preview to advisor anyway");
        else if (outcome.SkipReason == "too-large")
            AnsiConsole.MarkupLine($"[yellow]Too large for evidence[/] — exceeds {Vigil.Application.GrillSessionState.MaxAutoLoadBytes} bytes; sending preview to advisor only");

        var evidenceName = outcome.FileName ?? resolvedName;
        var consultMessage = Vigil.Application.GrillInteractive.BuildPasteConsultMessage(evidenceName, text, lineCount);
        var reply = client.ConsultAsync(
            consultMessage,
            cwd: launchDir,
            lastDiagnosisId: state.LastDiagnosis?.Id,
            compactContext: state.GetCompactContextForChat().FormatForAdvisor()).GetAwaiter().GetResult();

        state.AppendTurn($"[paste {evidenceName}]", reply);
        AnsiConsole.MarkupLine(reply);
    }

    static string? ReadMultilinePasteFromConsole()
    {
        AnsiConsole.MarkupLine("[grey]Paste mode: paste your text, then type END on its own line.[/]");
        AnsiConsole.MarkupLine("[grey](Avoid a lone END line inside pasted logs — it will close paste mode.)[/]");

        var lines = new List<string>();
        while (true)
        {
            var line = Console.ReadLine();
            if (line == null)
                break;

            if (Vigil.Application.GrillInteractive.IsPasteEndMarker(line))
                break;

            lines.Add(line);
        }

        var text = Vigil.Application.GrillInteractive.FinalizePastedLines(lines);
        if (string.IsNullOrWhiteSpace(text))
        {
            AnsiConsole.MarkupLine("[yellow]Empty paste — nothing added.[/]");
            return null;
        }

        return text;
    }

    // == In-session governed diagnose (slash + NL intent share this path) == //
    static void HandleDiagnoseInSession(
        IVigilClient client,
        Vigil.Application.GrillSessionState state,
        string rawInput,
        Vigil.Application.DiagnoseCommandArgs args,
        string? utteranceRemainder)
    {
        var loadResult = Vigil.Application.GrillInteractive.TryExtractAndLoadPaths(rawInput, state);
        if (loadResult.LoadedFileNames.Count > 0)
            AnsiConsole.MarkupLine($"[green]Auto-loaded[/] {string.Join(", ", loadResult.LoadedFileNames)} — evidence now {state.CurrentEvidenceCount}");
        if (loadResult.NotFoundPaths.Count > 0)
            AnsiConsole.MarkupLine($"[yellow]Not found[/] {string.Join(", ", loadResult.NotFoundPaths)}");
        if (loadResult.SkippedTooLarge.Count > 0)
            AnsiConsole.MarkupLine($"[yellow]Too large[/] {string.Join(", ", loadResult.SkippedTooLarge)} — use /load after trimming or split the file");

        if (state.CurrentEvidenceCount == 0)
            AnsiConsole.MarkupLine("[yellow]No evidence loaded — diagnosis will have weak/no citations. Use /load or mention files.[/]");

        var resolved = Vigil.Application.GrillInteractive.ResolveSymptom(args, utteranceRemainder, state);
        if (resolved.UsedGenericSymptomFallback)
            AnsiConsole.MarkupLine("[yellow]No symptom found — using generic fallback. Add --symptom or describe the issue.[/]");

        var request = Vigil.Application.GrillInteractive.BuildDiagnoseRequest(state, args, resolved.Symptom);
        var d = client.DiagnoseAsync(request).GetAwaiter().GetResult();
        state.SetLastDiagnosis(d);
        if (d.Provenance.Usage != null)
            state.RecordTokens(d.Provenance.Usage.InputTokens, d.Provenance.Usage.OutputTokens);
        RenderFullDiagnosisInSession(d);
    }

    // == Full render of governed Diagnosis inside TUI session == //
    // Shows the complete cited output (summary, causes with chain/conf/severity, citations by artifact ID + snippet, provenance, tokens).
    // Mirrors the trust contract from the original design while staying inside the conversational flow.
    static void RenderFullDiagnosisInSession(Diagnosis diagnosis)
    {
        AnsiConsole.Write(new Rule("[green]Diagnosis[/]"));

        var tree = new Tree($"[bold]{diagnosis.Summary}[/]");

        foreach (var cause in diagnosis.Causes)
        {
            var causeNode = tree.AddNode($"[cyan]{cause.Description}[/] (conf: {cause.Confidence.Value:P0}, {cause.Severity})");
            if (!string.IsNullOrWhiteSpace(cause.CausalChain))
                causeNode.AddNode($"Chain: {cause.CausalChain}");

            var citesNode = causeNode.AddNode("Citations");
            foreach (var cit in cause.Citations)
            {
                var snip = string.IsNullOrWhiteSpace(cit.Snippet) ? "" : $" - {cit.Snippet}";
                citesNode.AddNode($"Artifact {cit.EvidenceArtifactId}{snip}");
            }
        }

        AnsiConsole.Write(tree);

        var prov = diagnosis.Provenance;
        AnsiConsole.MarkupLine($"[grey]Provenance: {prov.AnalyzedBy} (tier){(prov.Reason.HasValue ? " " + prov.Reason : "")}[/]");
        if (prov.Usage != null)
            AnsiConsole.MarkupLine($"[grey]Tokens: in={prov.Usage.InputTokens} out={prov.Usage.OutputTokens}[/]");
    }
}

// Simple TypeRegistrar for Spectre + Microsoft DI (per grill-me)
public sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection _services;

    public TypeRegistrar(IServiceCollection services) => _services = services;

    public ITypeResolver Build() => new TypeResolver(_services.BuildServiceProvider());

    public void Register(Type service, Type implementation) => _services.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) => _services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) => _services.AddSingleton(service, _ => factory());
}

public sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly IServiceProvider _provider;

    public TypeResolver(IServiceProvider provider) => _provider = provider;

    public object? Resolve(Type? type) => type == null ? null : _provider.GetService(type);

    public void Dispose()
    {
        if (_provider is IDisposable disposable)
            disposable.Dispose();
    }
}