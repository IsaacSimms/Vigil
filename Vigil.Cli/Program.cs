// == Program (composition root for Vigil.Cli per §3, AGENTS, grill-me) == //

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using Vigil.Application.Coordinators;
using Vigil.Application.UseCases;
using Vigil.Cli.Commands;
using Vigil.Domain.Abstractions;
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

        // In-process client (default transport)
        registrations.AddSingleton<IVigilClient>(sp =>
        {
            var useCase = sp.GetRequiredService<DiagnoseUseCase>();
            return new Vigil.Application.Clients.InProcessVigilClient(useCase);
        });

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

