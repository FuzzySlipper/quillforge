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
                .Select(ToModuleOption)
                .OrderBy(module => module.DisplayName, StringComparer.Ordinal)
                .ThenBy(module => module.ModuleId, StringComparer.Ordinal)
                .ThenBy(module => module.ModuleVersion, StringComparer.Ordinal)
                .ToArray();
            var providers = providerRegistry.GetAllConfigs()
                .Select(config => new GameTemplateProviderOption
                {
                    Alias = config.Alias,
                    Type = config.Type.ToString(),
                    Model = config.DefaultModel,
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

        group.MapGet("/persona-prompts", async (
            [FromServices] IGamePersonaPromptService personaPromptService,
            CancellationToken ct) =>
        {
            var userPrompts = await personaPromptService.ListUserPromptsAsync(ct);
            return Results.Ok(new GamePersonaPromptListResponse
            {
                Prompts = ToPersonaPromptOptions(userPrompts),
            });
        });

        group.MapPost("/persona-prompts/open", async (
            OpenGamePersonaPromptRequest request,
            [FromServices] IGamePersonaPromptService personaPromptService,
            CancellationToken ct) =>
        {
            try
            {
                if (request.Selection.Source == GamePersonaPromptSource.User
                    && !string.IsNullOrWhiteSpace(request.Selection.UserPromptName))
                {
                    var existing = await personaPromptService.TryOpenUserPromptAsync(request.Selection.UserPromptName, ct);
                    return existing is null
                        ? Results.NotFound(new { Error = $"Game persona prompt {request.Selection.UserPromptName} not found" })
                        : Results.Ok(ToPersonaPromptDocumentResponse(existing, createdCopy: false));
                }

                var created = await personaPromptService.CreateForEditAsync(request.BaseName, request.SeedContent, ct);
                return Results.Ok(ToPersonaPromptDocumentResponse(created, createdCopy: true));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        group.MapPut("/persona-prompts/{promptName}", async (
            string promptName,
            WriteGamePersonaPromptRequest request,
            [FromServices] IGamePersonaPromptService personaPromptService,
            CancellationToken ct) =>
        {
            try
            {
                await personaPromptService.SaveUserPromptAsync(promptName, request.Content, ct);
                return Results.Ok(new WriteGamePersonaPromptResponse
                {
                    Name = promptName,
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        group.MapGet("/{moduleId}/prompt-templates", async (
            string moduleId,
            [FromServices] IGamePromptTemplateService promptTemplateService,
            CancellationToken ct) =>
        {
            try
            {
                var userPrompts = await promptTemplateService.ListUserPromptsAsync(moduleId, ct);
                return Results.Ok(new GamePromptTemplateListResponse
                {
                    ModuleId = moduleId,
                    Prompts = ToPromptOptions(userPrompts),
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        group.MapPost("/{moduleId}/prompt-templates/open", async (
            string moduleId,
            OpenGamePromptTemplateRequest request,
            [FromServices] IGamePromptTemplateService promptTemplateService,
            CancellationToken ct) =>
        {
            try
            {
                if (request.Selection.Source == GamePromptTemplateSource.User
                    && !string.IsNullOrWhiteSpace(request.Selection.UserPromptName))
                {
                    var existing = await promptTemplateService.TryOpenUserPromptAsync(moduleId, request.Selection.UserPromptName, ct);
                    return existing is null
                        ? Results.NotFound(new { Error = $"Game prompt template {request.Selection.UserPromptName} not found" })
                        : Results.Ok(ToPromptDocumentResponse(existing, createdCopy: false));
                }

                var copy = await promptTemplateService.CopyDefaultForEditAsync(moduleId, ct);
                return Results.Ok(ToPromptDocumentResponse(copy, createdCopy: true));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        group.MapPut("/{moduleId}/prompt-templates/{promptName}", async (
            string moduleId,
            string promptName,
            WriteGamePromptTemplateRequest request,
            [FromServices] IGamePromptTemplateService promptTemplateService,
            CancellationToken ct) =>
        {
            try
            {
                await promptTemplateService.SaveUserPromptAsync(moduleId, promptName, request.Content, ct);
                return Results.Ok(new WriteGamePromptTemplateResponse
                {
                    ModuleId = moduleId,
                    Name = promptName,
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
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

    private static IReadOnlyList<GamePersonaPromptOption> ToPersonaPromptOptions(IReadOnlyList<GameUserPersonaPromptInfo> userPrompts)
    {
        var options = new List<GamePersonaPromptOption>
        {
            new()
            {
                Value = "none",
                DisplayName = "None",
                Source = GamePersonaPromptSource.None,
                IsNone = true,
                Tokens = 0,
            },
        };
        options.AddRange(userPrompts.Select(prompt => new GamePersonaPromptOption
        {
            Value = $"user:{prompt.Name}",
            DisplayName = prompt.Name,
            Source = GamePersonaPromptSource.User,
            UserPromptName = prompt.Name,
            IsNone = false,
            Tokens = prompt.Tokens,
            Size = prompt.Size,
            RelativePath = prompt.RelativePath,
        }));
        return options;
    }

    private static GamePersonaPromptDocumentResponse ToPersonaPromptDocumentResponse(
        GameUserPersonaPromptDocument document,
        bool createdCopy) =>
        new()
        {
            Name = document.Name,
            DisplayName = document.Name,
            RelativePath = document.RelativePath,
            Selection = GamePersonaPromptSelection.ForUserPrompt(document.Name),
            Content = document.Content,
            Tokens = document.Tokens,
            CreatedCopy = createdCopy,
        };

    private static IReadOnlyList<GamePromptTemplateOption> ToPromptOptions(IReadOnlyList<GameUserPromptTemplateInfo> userPrompts)
    {
        var options = new List<GamePromptTemplateOption>
        {
            new()
            {
                Value = "default",
                DisplayName = "Default",
                Source = GamePromptTemplateSource.Default,
                IsDefault = true,
                Tokens = 0,
            },
        };
        options.AddRange(userPrompts.Select(prompt => new GamePromptTemplateOption
        {
            Value = $"user:{prompt.Name}",
            DisplayName = prompt.Name,
            Source = GamePromptTemplateSource.User,
            UserPromptName = prompt.Name,
            IsDefault = false,
            Tokens = prompt.Tokens,
            Size = prompt.Size,
            RelativePath = prompt.RelativePath,
        }));
        return options;
    }

    private static GamePromptTemplateDocumentResponse ToPromptDocumentResponse(
        GameUserPromptTemplateDocument document,
        bool createdCopy) =>
        new()
        {
            ModuleId = document.ModuleId,
            Name = document.Name,
            DisplayName = document.Name,
            RelativePath = document.RelativePath,
            Selection = GamePromptTemplateSelection.ForUserPrompt(document.Name),
            Content = document.Content,
            Tokens = document.Tokens,
            CreatedCopy = createdCopy,
        };

    private static GameTemplateModuleOption ToModuleOption(IGameModule module)
    {
        var descriptor = module.Descriptor;
        var requiredAssets = descriptor.RequiredPromptAssets
            .Select(asset => $"{asset.AssetId}:{asset.Kind}")
            .ToHashSet(StringComparer.Ordinal);

        return new GameTemplateModuleOption
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
            Stages = descriptor.AuthoringHooks.Stages
                .Select(stage => new GameTemplateStageHookOption
                {
                    StageId = stage.StageId.Value,
                    DisplayName = stage.DisplayName,
                    Description = stage.Description,
                    Sequence = stage.Sequence,
                    AllowsPublicMessages = stage.AllowsPublicMessages,
                    AllowsDirectMessages = stage.AllowsDirectMessages,
                })
                .ToArray(),
            ActionForms = descriptor.AuthoringHooks.ActionForms
                .Select(form => new GameTemplateActionFormOption
                {
                    IntentName = form.IntentName,
                    StageId = form.StageId.Value,
                    DisplayName = form.DisplayName,
                    Description = form.Description,
                    Layout = form.Layout.ToString(),
                    Fields = form.Fields
                        .Select(field => new GameTemplateActionFieldOption
                        {
                            Name = field.Name,
                            ValueKind = field.ValueKind.ToString(),
                            IsRequired = field.IsRequired,
                            DisplayName = field.DisplayName,
                            Description = field.Description,
                        })
                        .ToArray(),
                })
                .ToArray(),
            PromptAssets = module.GetPromptAssets()
                .Select(asset => new GameTemplatePromptAssetOption
                {
                    AssetId = asset.AssetId,
                    Kind = asset.Kind.ToString(),
                    IsRequired = requiredAssets.Contains($"{asset.AssetId}:{asset.Kind}"),
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
            ProjectionCapabilities = new GameTemplateProjectionCapabilitiesOption
            {
                SupportsPublicEventProjection = descriptor.AuthoringHooks.ProjectionCapabilities.SupportsPublicEventProjection,
                SupportsParticipantPrivateProjection = descriptor.AuthoringHooks.ProjectionCapabilities.SupportsParticipantPrivateProjection,
                SupportsHostInspectorProjection = descriptor.AuthoringHooks.ProjectionCapabilities.SupportsHostInspectorProjection,
            },
        };
    }
}
