using Den.RulesEngine;
using Microsoft.AspNetCore.Mvc;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Providers.Registry;
using QuillForge.Web.Contracts;

namespace QuillForge.Web.Endpoints;

public static class GameTemplateEndpoints
{
    public static void MapGameTemplateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/game-templates");

        group.MapGet("/", async ([FromServices] IGameTemplateService templateService, CancellationToken ct) =>
        {
            var templates = await templateService.ListAsync(ct);
            return Results.Ok(new GameTemplateListResponse
            {
                Templates = templates,
            });
        });

        group.MapGet("/catalog", ([FromServices] GameModuleRegistry registry, [FromServices] ProviderRegistry providerRegistry) =>
        {
            var modules = registry.Modules
                .Select(module => ToModuleOption(module.Descriptor))
                .OrderBy(module => module.DisplayName, StringComparer.Ordinal)
                .ThenBy(module => module.ModuleId, StringComparer.Ordinal)
                .ThenBy(module => module.ModuleVersion, StringComparer.Ordinal)
                .ToArray();
            var providers = providerRegistry.GetAllConfigs()
                .Select(config => new GameTemplateProviderOption
                {
                    Alias = config.Alias,
                    Type = config.Type.ToString(),
                    DefaultModel = config.DefaultModel,
                    ContextLimit = config.ContextLimit,
                })
                .OrderBy(provider => provider.Alias, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Results.Ok(new GameTemplateCatalogResponse
            {
                Modules = modules,
                Providers = providers,
            });
        });

        group.MapGet("/{templateId}", async (
            string templateId,
            [FromServices] IGameTemplateService templateService,
            CancellationToken ct) =>
        {
            try
            {
                var envelope = await templateService.LoadAsync(templateId, ct);
                return Results.Ok(ToResponse(envelope));
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { Error = $"Game template {templateId} not found" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        group.MapPut("/{templateId}", async (
            string templateId,
            SaveGameTemplateRequest request,
            [FromServices] IGameTemplateService templateService,
            CancellationToken ct) =>
        {
            try
            {
                var envelope = await templateService.SaveAsync(templateId, request.Template, ct);
                return envelope.Validation.IsValid
                    ? Results.Ok(ToResponse(envelope))
                    : Results.BadRequest(ToResponse(envelope));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        group.MapPost("/{templateId}/clone", async (
            string templateId,
            CloneGameTemplateRequest request,
            [FromServices] IGameTemplateService templateService,
            CancellationToken ct) =>
        {
            try
            {
                var envelope = await templateService.CloneAsync(templateId, request.TargetTemplateId, request.DisplayName, ct);
                return envelope.Validation.IsValid
                    ? Results.Ok(ToResponse(envelope))
                    : Results.BadRequest(ToResponse(envelope));
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { Error = $"Game template {templateId} not found" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { Error = ex.Message });
            }
        });

        group.MapPost("/validate", async (
            ValidateGameTemplateRequest request,
            [FromServices] IGameTemplateService templateService,
            CancellationToken ct) =>
        {
            var validation = await templateService.ValidateAsync(request.Template, ct);
            return Results.Ok(new ValidateGameTemplateResponse
            {
                Validation = validation,
            });
        });

        group.MapDelete("/{templateId}", async (
            string templateId,
            [FromServices] IGameTemplateService templateService,
            CancellationToken ct) =>
        {
            try
            {
                await templateService.DeleteAsync(templateId, ct);
                return Results.Ok(new DeleteGameTemplateResponse
                {
                    TemplateId = templateId,
                });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { Error = $"Game template {templateId} not found" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });
    }

    private static GameTemplateResponse ToResponse(GameTemplateValidationEnvelope envelope) =>
        new()
        {
            Template = envelope.Template,
            Validation = envelope.Validation,
        };

    private static GameTemplateModuleOption ToModuleOption(GameModuleDescriptor descriptor) =>
        new()
        {
            ModuleId = descriptor.ModuleId.Value,
            ModuleVersion = descriptor.ModuleVersion.Value,
            DisplayName = descriptor.DisplayName,
            MinimumTemplateVersion = descriptor.MinimumTemplateVersion.Value,
            MaximumTemplateVersion = descriptor.MaximumTemplateVersion.Value,
            MinimumPlayers = descriptor.PlayerCount.Minimum,
            MaximumPlayers = descriptor.PlayerCount.Maximum,
            SetupFields = descriptor.SetupFields
                .Select(field => new GameTemplateSetupFieldOption
                {
                    Name = field.Name,
                    ValueKind = field.ValueKind.ToString(),
                    IsRequired = field.IsRequired,
                    DisplayName = field.DisplayName,
                    Description = field.Description,
                })
                .ToArray(),
            CommunicationCapabilities = new GameTemplateCommunicationCapabilitiesOption
            {
                AllowsPublicChannelMessages = descriptor.CommunicationCapabilities.AllowsPublicChannelMessages,
                AllowsDirectMessages = descriptor.CommunicationCapabilities.AllowsDirectMessages,
            },
            MemoryExpectations = new GameTemplateMemoryExpectationsOption
            {
                UsesRoundSummaries = descriptor.MemoryExpectations.UsesRoundSummaries,
                SuggestedSummaryTokenBudget = descriptor.MemoryExpectations.SuggestedSummaryTokenBudget,
                MaximumRetainedRoundSummaries = descriptor.MemoryExpectations.MaximumRetainedRoundSummaries,
            },
            ParticipantRequirements = new GameTemplateParticipantRequirementsOption
            {
                AllowsHumanParticipants = descriptor.ParticipantRequirements.AllowsHumanParticipants,
                AllowsAgentParticipants = descriptor.ParticipantRequirements.AllowsAgentParticipants,
                AllowsSystemParticipants = descriptor.ParticipantRequirements.AllowsSystemParticipants,
                MinimumHumanParticipants = descriptor.ParticipantRequirements.MinimumHumanParticipants,
                MinimumAgentParticipants = descriptor.ParticipantRequirements.MinimumAgentParticipants,
            },
        };
}
