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
        AnsiConsole.MarkupLine("[grey]Type naturally. /help, /load <path>, /diagnose, /status, exit.[/]");
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
                    AnsiConsole.MarkupLine("[yellow]/load <rel-path>  /diagnose  /status  exit[/]");
                    continue;
                }
                if (c == "load" && intent.Arguments.Count > 0)
                {
                    var p = intent.Arguments[0];
                    var full = System.IO.Path.IsPathRooted(p) ? p : System.IO.Path.Combine(launchDir, p);
                    if (System.IO.File.Exists(full))
                    {
                        var txt = System.IO.File.ReadAllText(full);
                        state.AddEvidence(new RawSource(System.IO.Path.GetFileName(full), txt, null, "loaded"));
                        AnsiConsole.MarkupLine($"[green]Loaded[/] — evidence now {state.CurrentEvidenceCount}");
                    }
                    else AnsiConsole.MarkupLine("[red]not found[/]");
                    continue;
                }
                if (c == "diagnose")
                {
                    var srcs = state.GetCurrentEvidenceSnapshot();
                    if (srcs.Count == 0) srcs = new List<RawSource> { new RawSource("sample", "error in session context", null, "sample") };
                    var symptom = intent.SuggestedSymptom ?? "issue from grill session";
                    var d = client.DiagnoseAsync(new DiagnoseRequest(srcs, new ScopeHints(Symptom: symptom))).GetAwaiter().GetResult();
                    state.SetLastDiagnosis(d);
                    // Record tokens from the governed path for the running list in session state
                    if (d.Provenance.Usage != null)
                        state.RecordTokens(d.Provenance.Usage.InputTokens, d.Provenance.Usage.OutputTokens);
                    // Full render of the governed cited Diagnosis inside the session (tree + citations + provenance)
                    RenderFullDiagnosisInSession(d, false);
                    continue;
                }
                if (c == "status")
                {
                    AnsiConsole.MarkupLine($"Evidence: {state.CurrentEvidenceCount} | Turns: {state.Turns.Count} | Tokens: {state.TotalTokensUsed}");
                    continue;
                }
                continue;
            }

            // Natural language — calls through the Consult seam (context from SessionState passed in)
            var reply = client.ConsultAsync(input, cwd: launchDir, lastDiagnosisId: state.LastDiagnosis?.Id, compactContext: state.GetCompactContextForChat().ToString()).GetAwaiter().GetResult();
            state.AppendTurn(input, reply);
            AnsiConsole.MarkupLine(reply);
        }
        return 0;
    }

    // == Full render of governed Diagnosis inside TUI session == //
    // Shows the complete cited output (summary, causes with chain/conf/severity, citations by artifact ID + snippet, provenance, tokens).
    // Mirrors the trust contract from the original design while staying inside the conversational flow.
    static void RenderFullDiagnosisInSession(Diagnosis diagnosis, bool dryRun)
    {
        var rule = new Rule(dryRun ? "[yellow]DRY-RUN PREVIEW (session)[/]" : "[green]Diagnosis[/]");
        AnsiConsole.Write(rule);

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

        if (dryRun)
            AnsiConsole.MarkupLine("[yellow]This was a dry-run inside the session. No model call was made for the formal analysis.[/]");
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