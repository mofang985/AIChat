using System.Diagnostics;
using System.Text.Json;
using AIChat.Application.AI;
using AIChat.Application.Knowledge;
using AIChat.Application.Risk;
using AIChat.Domain.Common;
using AIChat.Domain.Entities;
using AIChat.Domain.Enums;
using AIChat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIChat.Api.Endpoints;

public static class M3Endpoints
{
    private const string BuiltInReplySystemPrompt = """
        你是个人微信工作号RPA AI客服系统中的电商客服助手。
        你只能基于提供的知识库内容回答，不得编造价格、库存、物流、活动、售后承诺。
        客户问题可能是一组连续消息，你必须逐项理解并用一条综合回复覆盖全部可回答问题。
        如果无法覆盖其中任意一个业务问题，请将 ShouldAutoSend 设为 false。
        必须输出 JSON，字段为 Intent、Confidence、RiskLevel、ReplyText、KnowledgeRefs、ShouldAutoSend。
        RiskLevel 只能是 Low、Medium、High。KnowledgeRefs 是字符串数组。
        """;

    private const string BuiltInReplyUserPrompt = """
        本轮待回复客户消息组：
        {Question}

        最近客户消息上下文：
        {ConversationContext}

        知识库引用：
        {KnowledgeContext}

        请生成简洁、亲切、专业的一条微信客服综合回复，并覆盖消息组中的全部可回答问题。
        如果知识库不足以回答，请将 ShouldAutoSend 设为 false，并在 ReplyText 中提示需要人工确认。
        """;

    private const string BuiltInNoKnowledgeFallbackSystemPrompt = """
        你是个人微信工作号RPA AI客服系统中的微信客服助手。
        当前没有命中知识库。你不能编造价格、库存、物流、活动、售后承诺、赔付承诺或商品参数。
        客户问题可能是一组连续消息，你必须判断整组消息是否都属于低风险轻量回复场景。
        如果客户只是寒暄、确认、认可、轻量闲聊或不需要业务事实的问题，可以生成一句非常简短、自然的回复，并将 ShouldAutoSend 设为 true。
        对实时天气、实时新闻、外部网页查询、你无法直接执行的工具类请求，如果不涉及店铺业务承诺，可以明确说明当前无法直接查询，并建议客户使用官方 App、网站或其他可靠渠道；这类能力边界回复可以将 ShouldAutoSend 设为 true。
        如果客户在问具体业务事实、商品信息、价格、优惠、库存、物流、售后、投诉、赔偿或需要人工判断的问题，必须将 ShouldAutoSend 设为 false。
        必须输出 JSON，字段为 Intent、Confidence、RiskLevel、ReplyText、KnowledgeRefs、ShouldAutoSend。
        RiskLevel 只能是 Low、Medium、High。KnowledgeRefs 输出空数组。
        """;

    private const string BuiltInNoKnowledgeFallbackUserPrompt = """
        本轮待回复客户消息组：
        {Question}

        最近客户消息上下文：
        {ConversationContext}

        知识库引用：
        {KnowledgeContext}

        请判断是否可以给出无知识库的低风险短回复。
        可以自动发送时，回复要像微信真人客服一样自然，优先使用 4 到 12 个字，例如“嗯嗯好的”“好的，我知道啦”“可以的”。
        如果客户要求查询实时天气、实时新闻、外部网页或你无法直接执行的工具类动作，且不涉及店铺业务承诺，可以给出简短能力边界回复，并将 ShouldAutoSend 设为 true，例如“我这边不能直接查询实时天气，建议您查看天气 App 哦。”。
        如果消息组里包含多个问题，必须确认每个问题都可以低风险自动回复；否则 ShouldAutoSend=false。
        不可以自动发送时，ReplyText 写给员工看的简短原因，ShouldAutoSend=false。
        """;

    private const string BuiltInLlmOnlySystemPrompt = """
        你是个人微信工作号RPA AI客服系统中的微信客服助手。
        当前回复模式为 LlmOnly：不要检索或引用知识库，只能根据本轮待回复客户消息组和双方聊天上下文生成回复。
        客户问题可能是一组连续消息，你必须逐项理解并用一条综合回复覆盖全部可回答问题。
        对寒暄、认可、介绍自己、轻量闲聊、简单确认，可以生成自然、简短、像真人客服的回复，例如“你好呀，很高兴认识你”“哈哈，谢谢认可”。
        对实时天气、实时新闻、外部网页查询、你无法直接执行的工具类请求，如果不涉及店铺业务承诺，可以明确说明当前无法直接查询，并建议客户使用官方 App、网站或其他可靠渠道；这类能力边界回复可以将 ShouldAutoSend 设为 true。
        对价格、库存、物流、售后、赔付、商品参数、活动优惠等业务事实，如果聊天上下文没有明确依据，必须将 ShouldAutoSend 设为 false，不能编造或承诺。
        必须输出 JSON，字段为 Intent、Confidence、RiskLevel、ReplyText、KnowledgeRefs、ShouldAutoSend。
        RiskLevel 只能是 Low、Medium、High。KnowledgeRefs 必须输出空数组。
        """;

    private const string BuiltInLlmOnlyUserPrompt = """
        本轮待回复客户消息组：
        {Question}

        双方聊天上下文：
        {ConversationContext}

        知识库引用：
        {KnowledgeContext}

        请结合本轮待回复客户消息组和双方上下文生成一条综合回复建议。
        如果只是寒暄、认可、介绍自己或轻量闲聊，可以给出自然低风险回复，并将 ShouldAutoSend 设为 true。
        如果客户要求查询实时天气、实时新闻、外部网页或你无法直接执行的工具类动作，且不涉及店铺业务承诺，可以给出简短能力边界回复，并将 ShouldAutoSend 设为 true，例如“我这边不能直接查询实时天气，建议您查看天气 App 哦。”。
        如果消息组里有任何一个问题无法安全回答，ReplyText 写给员工看的简短原因，ShouldAutoSend=false。
        如果涉及价格、库存、物流、售后、赔付、商品参数、活动优惠等业务事实且上下文没有明确依据，ReplyText 写给员工看的简短原因，ShouldAutoSend=false。
        """;

    public static IEndpointRouteBuilder MapM3Endpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        MapProducts(api);
        MapFaqs(api);
        MapAfterSaleRules(api);
        MapRiskRules(api);
        MapPromptTemplates(api);
        MapLlmProviderConfigs(api);
        MapKnowledge(api);
        MapAi(api);

        return app;
    }

    private static void MapProducts(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/products");

        group.MapGet("/", async (AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var items = await db.Products
                .AsNoTracking()
                .OrderBy(x => x.ProductCode)
                .Select(x => ToProductDto(x))
                .ToListAsync(cancellationToken);

            return Results.Ok(items);
        });

        group.MapPost("/", async (UpsertProductRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.ProductCode) || string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { message = "ProductCode and Name are required." });
            }

            var product = new Product
            {
                TenantId = request.TenantId ?? TenantDefaults.DefaultTenantId,
                ProductCode = request.ProductCode.Trim(),
                Name = request.Name.Trim(),
                Category = request.Category?.Trim(),
                Brand = request.Brand?.Trim(),
                PriceText = request.PriceText?.Trim(),
                Summary = request.Summary,
                Description = request.Description,
                Keywords = request.Keywords?.Trim(),
                IsActive = request.IsActive ?? true
            };

            db.Products.Add(product);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/products/{product.Id}", ToProductDto(product));
        });

        group.MapPut("/{id:guid}", async (Guid id, UpsertProductRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var product = await db.Products.FindAsync([id], cancellationToken);
            if (product is null)
            {
                return Results.NotFound();
            }

            product.ProductCode = string.IsNullOrWhiteSpace(request.ProductCode) ? product.ProductCode : request.ProductCode.Trim();
            product.Name = string.IsNullOrWhiteSpace(request.Name) ? product.Name : request.Name.Trim();
            product.Category = request.Category?.Trim();
            product.Brand = request.Brand?.Trim();
            product.PriceText = request.PriceText?.Trim();
            product.Summary = request.Summary;
            product.Description = request.Description;
            product.Keywords = request.Keywords?.Trim();
            product.IsActive = request.IsActive ?? product.IsActive;

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToProductDto(product));
        });
    }

    private static void MapFaqs(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/faqs");

        group.MapGet("/", async (AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var items = await db.FaqItems
                .AsNoTracking()
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.CreatedAt)
                .Select(x => ToFaqItemDto(x))
                .ToListAsync(cancellationToken);

            return Results.Ok(items);
        });

        group.MapPost("/", async (UpsertFaqItemRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Question) || string.IsNullOrWhiteSpace(request.Answer))
            {
                return Results.BadRequest(new { message = "Question and Answer are required." });
            }

            var item = new FaqItem
            {
                TenantId = request.TenantId ?? TenantDefaults.DefaultTenantId,
                Question = request.Question.Trim(),
                Answer = request.Answer.Trim(),
                Category = request.Category?.Trim(),
                Keywords = request.Keywords?.Trim(),
                Priority = request.Priority ?? 100,
                IsActive = request.IsActive ?? true
            };

            db.FaqItems.Add(item);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/faqs/{item.Id}", ToFaqItemDto(item));
        });

        group.MapPut("/{id:guid}", async (Guid id, UpsertFaqItemRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var item = await db.FaqItems.FindAsync([id], cancellationToken);
            if (item is null)
            {
                return Results.NotFound();
            }

            item.Question = string.IsNullOrWhiteSpace(request.Question) ? item.Question : request.Question.Trim();
            item.Answer = string.IsNullOrWhiteSpace(request.Answer) ? item.Answer : request.Answer.Trim();
            item.Category = request.Category?.Trim();
            item.Keywords = request.Keywords?.Trim();
            item.Priority = request.Priority ?? item.Priority;
            item.IsActive = request.IsActive ?? item.IsActive;

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToFaqItemDto(item));
        });
    }

    private static void MapAfterSaleRules(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/after-sale-rules");

        group.MapGet("/", async (AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var items = await db.AfterSaleRules
                .AsNoTracking()
                .OrderBy(x => x.Priority)
                .Select(x => ToAfterSaleRuleDto(x))
                .ToListAsync(cancellationToken);

            return Results.Ok(items);
        });

        group.MapPost("/", async (UpsertAfterSaleRuleRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.RuleCode) ||
                string.IsNullOrWhiteSpace(request.Title) ||
                string.IsNullOrWhiteSpace(request.Content))
            {
                return Results.BadRequest(new { message = "RuleCode, Title and Content are required." });
            }

            var rule = new AfterSaleRule
            {
                TenantId = request.TenantId ?? TenantDefaults.DefaultTenantId,
                RuleCode = request.RuleCode.Trim(),
                Title = request.Title.Trim(),
                Scenario = request.Scenario?.Trim(),
                Content = request.Content.Trim(),
                Keywords = request.Keywords?.Trim(),
                Priority = request.Priority ?? 100,
                IsActive = request.IsActive ?? true
            };

            db.AfterSaleRules.Add(rule);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/after-sale-rules/{rule.Id}", ToAfterSaleRuleDto(rule));
        });

        group.MapPut("/{id:guid}", async (Guid id, UpsertAfterSaleRuleRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var rule = await db.AfterSaleRules.FindAsync([id], cancellationToken);
            if (rule is null)
            {
                return Results.NotFound();
            }

            rule.RuleCode = string.IsNullOrWhiteSpace(request.RuleCode) ? rule.RuleCode : request.RuleCode.Trim();
            rule.Title = string.IsNullOrWhiteSpace(request.Title) ? rule.Title : request.Title.Trim();
            rule.Scenario = request.Scenario?.Trim();
            rule.Content = string.IsNullOrWhiteSpace(request.Content) ? rule.Content : request.Content.Trim();
            rule.Keywords = request.Keywords?.Trim();
            rule.Priority = request.Priority ?? rule.Priority;
            rule.IsActive = request.IsActive ?? rule.IsActive;

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToAfterSaleRuleDto(rule));
        });
    }

    private static void MapRiskRules(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/risk-rules");

        group.MapGet("/", async (AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var items = await db.RiskRules
                .AsNoTracking()
                .OrderBy(x => x.RuleName)
                .Select(x => ToRiskRuleDto(x))
                .ToListAsync(cancellationToken);

            return Results.Ok(items);
        });

        group.MapPost("/", async (UpsertRiskRuleRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.RuleName) || string.IsNullOrWhiteSpace(request.Keywords))
            {
                return Results.BadRequest(new { message = "RuleName and Keywords are required." });
            }

            var rule = new RiskRule
            {
                TenantId = request.TenantId ?? TenantDefaults.DefaultTenantId,
                RuleName = request.RuleName.Trim(),
                Keywords = request.Keywords.Trim(),
                RiskLevel = request.RiskLevel ?? RiskLevel.High,
                Action = request.Action ?? RiskRuleAction.ManualReview,
                Description = request.Description?.Trim(),
                IsEnabled = request.IsEnabled ?? true
            };

            db.RiskRules.Add(rule);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/risk-rules/{rule.Id}", ToRiskRuleDto(rule));
        });

        group.MapPut("/{id:guid}", async (Guid id, UpsertRiskRuleRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var rule = await db.RiskRules.FindAsync([id], cancellationToken);
            if (rule is null)
            {
                return Results.NotFound();
            }

            rule.RuleName = string.IsNullOrWhiteSpace(request.RuleName) ? rule.RuleName : request.RuleName.Trim();
            rule.Keywords = string.IsNullOrWhiteSpace(request.Keywords) ? rule.Keywords : request.Keywords.Trim();
            rule.RiskLevel = request.RiskLevel ?? rule.RiskLevel;
            rule.Action = request.Action ?? rule.Action;
            rule.Description = request.Description?.Trim();
            rule.IsEnabled = request.IsEnabled ?? rule.IsEnabled;

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToRiskRuleDto(rule));
        });
    }

    private static void MapPromptTemplates(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/prompt-templates");

        group.MapGet("/", async (AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var items = await db.PromptTemplates
                .AsNoTracking()
                .OrderBy(x => x.TemplateCode)
                .ThenBy(x => x.Version)
                .Select(x => ToPromptTemplateDto(x))
                .ToListAsync(cancellationToken);

            return Results.Ok(items);
        });

        group.MapPost("/", async (UpsertPromptTemplateRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.TemplateCode) ||
                string.IsNullOrWhiteSpace(request.Name) ||
                string.IsNullOrWhiteSpace(request.SystemPrompt) ||
                string.IsNullOrWhiteSpace(request.UserPromptTemplate))
            {
                return Results.BadRequest(new { message = "TemplateCode, Name, SystemPrompt and UserPromptTemplate are required." });
            }

            var template = new PromptTemplate
            {
                TenantId = request.TenantId ?? TenantDefaults.DefaultTenantId,
                TemplateCode = request.TemplateCode.Trim(),
                Name = request.Name.Trim(),
                TemplateType = request.TemplateType ?? PromptTemplateType.ReplySuggestion,
                SystemPrompt = request.SystemPrompt,
                UserPromptTemplate = request.UserPromptTemplate,
                Version = string.IsNullOrWhiteSpace(request.Version) ? "v1" : request.Version.Trim(),
                IsActive = request.IsActive ?? true
            };

            db.PromptTemplates.Add(template);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/prompt-templates/{template.Id}", ToPromptTemplateDto(template));
        });

        group.MapPut("/{id:guid}", async (Guid id, UpsertPromptTemplateRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var template = await db.PromptTemplates.FindAsync([id], cancellationToken);
            if (template is null)
            {
                return Results.NotFound();
            }

            template.TemplateCode = string.IsNullOrWhiteSpace(request.TemplateCode) ? template.TemplateCode : request.TemplateCode.Trim();
            template.Name = string.IsNullOrWhiteSpace(request.Name) ? template.Name : request.Name.Trim();
            template.TemplateType = request.TemplateType ?? template.TemplateType;
            template.SystemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt) ? template.SystemPrompt : request.SystemPrompt;
            template.UserPromptTemplate = string.IsNullOrWhiteSpace(request.UserPromptTemplate) ? template.UserPromptTemplate : request.UserPromptTemplate;
            template.Version = string.IsNullOrWhiteSpace(request.Version) ? template.Version : request.Version.Trim();
            template.IsActive = request.IsActive ?? template.IsActive;

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToPromptTemplateDto(template));
        });
    }

    private static void MapLlmProviderConfigs(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/llm-provider-configs");

        group.MapGet("/", async (AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var items = await db.LlmProviderConfigs
                .AsNoTracking()
                .OrderBy(x => x.ProviderCode)
                .Select(x => ToLlmProviderConfigDto(x))
                .ToListAsync(cancellationToken);

            return Results.Ok(items);
        });

        group.MapPost("/", async (UpsertLlmProviderConfigRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.ProviderCode) ||
                string.IsNullOrWhiteSpace(request.DisplayName) ||
                string.IsNullOrWhiteSpace(request.BaseUrl) ||
                string.IsNullOrWhiteSpace(request.ModelName) ||
                string.IsNullOrWhiteSpace(request.ApiKeyEnvironmentVariable))
            {
                return Results.BadRequest(new { message = "ProviderCode, DisplayName, BaseUrl, ModelName and ApiKeyEnvironmentVariable are required." });
            }

            var config = new LlmProviderConfig
            {
                TenantId = request.TenantId ?? TenantDefaults.DefaultTenantId,
                ProviderCode = request.ProviderCode.Trim(),
                ProviderType = request.ProviderType ?? LlmProviderType.OpenAICompatible,
                DisplayName = request.DisplayName.Trim(),
                BaseUrl = request.BaseUrl.Trim(),
                ModelName = request.ModelName.Trim(),
                ApiKeyEnvironmentVariable = request.ApiKeyEnvironmentVariable.Trim(),
                IsEnabled = request.IsEnabled ?? true,
                TimeoutSeconds = request.TimeoutSeconds ?? 60,
                Notes = request.Notes?.Trim()
            };

            db.LlmProviderConfigs.Add(config);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/llm-provider-configs/{config.Id}", ToLlmProviderConfigDto(config));
        });

        group.MapPut("/{id:guid}", async (Guid id, UpsertLlmProviderConfigRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var config = await db.LlmProviderConfigs.FindAsync([id], cancellationToken);
            if (config is null)
            {
                return Results.NotFound();
            }

            config.ProviderCode = string.IsNullOrWhiteSpace(request.ProviderCode) ? config.ProviderCode : request.ProviderCode.Trim();
            config.ProviderType = request.ProviderType ?? config.ProviderType;
            config.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? config.DisplayName : request.DisplayName.Trim();
            config.BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? config.BaseUrl : request.BaseUrl.Trim();
            config.ModelName = string.IsNullOrWhiteSpace(request.ModelName) ? config.ModelName : request.ModelName.Trim();
            config.ApiKeyEnvironmentVariable = string.IsNullOrWhiteSpace(request.ApiKeyEnvironmentVariable) ? config.ApiKeyEnvironmentVariable : request.ApiKeyEnvironmentVariable.Trim();
            config.IsEnabled = request.IsEnabled ?? config.IsEnabled;
            config.TimeoutSeconds = request.TimeoutSeconds ?? config.TimeoutSeconds;
            config.Notes = request.Notes?.Trim();

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToLlmProviderConfigDto(config));
        });
    }

    private static void MapKnowledge(RouteGroupBuilder api)
    {
        api.MapPost("/knowledge/search", async (
            KnowledgeSearchRequest request,
            AIChatDbContext db,
            KeywordKnowledgeSearchService searchService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return Results.BadRequest(new { message = "Query is required." });
            }

            var response = await SearchKnowledgeAsync(request, db, searchService, cancellationToken);
            return Results.Ok(response);
        });
    }

    private static void MapAi(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/ai");

        group.MapGet("/reply-suggestions", async (AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var items = await db.ReplySuggestions
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Take(100)
                .Select(x => ToReplySuggestionDto(x))
                .ToListAsync(cancellationToken);

            return Results.Ok(items);
        });

        group.MapGet("/request-logs", async (AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var items = await db.AiRequestLogs
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Take(100)
                .Select(x => ToAiRequestLogDto(x))
                .ToListAsync(cancellationToken);

            return Results.Ok(items);
        });

        group.MapPost("/reply-suggestions", async (
            CreateReplySuggestionRequest request,
            AIChatDbContext db,
            KeywordKnowledgeSearchService searchService,
            RiskRuleEvaluator riskEvaluator,
            StructuredReplyParser parser,
            ILlmProvider llmProvider,
            ILoggerFactory loggerFactory,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.CustomerQuestion))
            {
                return Results.BadRequest(new { message = "CustomerQuestion is required." });
            }

            var startedAt = Stopwatch.StartNew();
            var logger = loggerFactory.CreateLogger("AIChat.Api.Endpoints.M3Endpoints");
            var replyModeParse = AiReplyModePolicy.Parse(configuration["Ai:ReplyMode"]);
            var enableLlmOnlyBusinessFactGuard = configuration.GetValue("Ai:EnableLlmOnlyBusinessFactGuard", true);
            if (!replyModeParse.IsValid)
            {
                logger.LogWarning(
                    "Ai:ReplyMode 配置非法：{ReplyMode}，已按 KnowledgeFirst 处理。",
                    replyModeParse.RawValue);
            }

            var replyMode = replyModeParse.Mode;
            var searchResponse = replyMode == AiReplyMode.KnowledgeFirst
                ? await SearchKnowledgeAsync(
                    new KnowledgeSearchRequest(request.CustomerQuestion, KnowledgeSearchMode.Keyword, request.MaxKnowledgeResults),
                    db,
                    searchService,
                    cancellationToken)
                : new KnowledgeSearchResponse(request.CustomerQuestion.Trim(), KnowledgeSearchMode.Keyword, []);

            var riskRules = await db.RiskRules
                .AsNoTracking()
                .Where(x => x.IsEnabled)
                .Select(x => new RiskRuleCandidate(x.Id, x.RuleName, x.Keywords, x.RiskLevel, x.Action))
                .ToListAsync(cancellationToken);

            var questionRiskMatches = riskEvaluator.Evaluate(request.CustomerQuestion, riskRules);
            var questionRiskLevel = riskEvaluator.GetHighestRiskLevel(questionRiskMatches);

            var provider = await ResolveProviderAsync(request.ProviderCode, db, cancellationToken);
            if (provider is null)
            {
                var suggestion = await SaveReplySuggestionFailureAsync(
                    request,
                    db,
                    ReplySuggestionStatus.Failed,
                    questionRiskLevel,
                    "AI Provider 未配置或未启用。",
                    string.Empty,
                    searchResponse.Results,
                    null,
                    null,
                    cancellationToken);

                await SaveAiRequestLogAsync(db, "ReplySuggestion", null, null, null, request.CustomerQuestion, null, AiRequestStatus.Failed, "AI Provider 未配置或未启用。", (int)startedAt.ElapsedMilliseconds, null, null, cancellationToken);
                return Results.Ok(ToReplySuggestionDto(suggestion));
            }

            var apiKey = ResolveApiKey(provider.ApiKeyEnvironmentVariable, configuration);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                var suggestion = await SaveReplySuggestionFailureAsync(
                    request,
                    db,
                    ReplySuggestionStatus.Failed,
                    questionRiskLevel,
                    $"API Key 环境变量未配置：{provider.ApiKeyEnvironmentVariable}",
                    string.Empty,
                    searchResponse.Results,
                    provider.ProviderCode,
                    provider.ModelName,
                    cancellationToken);

                await SaveAiRequestLogAsync(db, "ReplySuggestion", provider.ProviderCode, provider.ModelName, null, request.CustomerQuestion, null, AiRequestStatus.Failed, "API Key 环境变量未配置。", (int)startedAt.ElapsedMilliseconds, null, null, cancellationToken);
                return Results.Ok(ToReplySuggestionDto(suggestion));
            }

            var hasKnowledgeHits = searchResponse.Results.Count > 0;
            ResolvedPromptTemplate prompt;
            string knowledgeContext;
            if (replyMode == AiReplyMode.LlmOnly)
            {
                prompt = new ResolvedPromptTemplate(
                    "built-in-llm-only",
                    BuiltInLlmOnlySystemPrompt,
                    BuiltInLlmOnlyUserPrompt);
                knowledgeContext = "当前模式不使用知识库。";
            }
            else
            {
                prompt = hasKnowledgeHits
                    ? await ResolvePromptTemplateAsync(request.PromptTemplateCode, db, cancellationToken)
                    : new ResolvedPromptTemplate(
                        "built-in-no-knowledge-fallback",
                        BuiltInNoKnowledgeFallbackSystemPrompt,
                        BuiltInNoKnowledgeFallbackUserPrompt);
                knowledgeContext = hasKnowledgeHits
                    ? BuildKnowledgeContext(searchResponse.Results)
                    : "未命中知识库。";
            }
            var userPrompt = ApplyPromptVariables(
                prompt.UserPromptTemplate,
                request.CustomerQuestion,
                request.ConversationContext,
                knowledgeContext);
            var llmResponse = await llmProvider.GenerateAsync(
                new LlmChatRequest(
                    provider.BaseUrl,
                    apiKey,
                    provider.ProviderCode,
                    provider.ModelName,
                    prompt.SystemPrompt,
                    userPrompt,
                    provider.TimeoutSeconds),
                cancellationToken);

            if (!llmResponse.Succeeded)
            {
                var suggestion = await SaveReplySuggestionFailureAsync(
                    request,
                    db,
                    ReplySuggestionStatus.Failed,
                    questionRiskLevel,
                    llmResponse.ErrorMessage ?? "AI 调用失败。",
                    string.Empty,
                    searchResponse.Results,
                    provider.ProviderCode,
                    provider.ModelName,
                    cancellationToken);

                await SaveAiRequestLogAsync(db, "ReplySuggestion", provider.ProviderCode, provider.ModelName, prompt.TemplateCode, request.CustomerQuestion, null, AiRequestStatus.Failed, llmResponse.ErrorMessage, llmResponse.DurationMs, llmResponse.PromptTokens, llmResponse.CompletionTokens, cancellationToken);
                return Results.Ok(ToReplySuggestionDto(suggestion));
            }

            if (!parser.TryParseReply(llmResponse.Content, out var parsed, out var parseError))
            {
                var suggestion = await SaveReplySuggestionFailureAsync(
                    request,
                    db,
                    ReplySuggestionStatus.Failed,
                    RiskLevel.High,
                    parseError,
                    string.Empty,
                    searchResponse.Results,
                    provider.ProviderCode,
                    provider.ModelName,
                    cancellationToken,
                    llmResponse.Content);

                await SaveAiRequestLogAsync(db, "ReplySuggestion", provider.ProviderCode, provider.ModelName, prompt.TemplateCode, request.CustomerQuestion, llmResponse.Content, AiRequestStatus.Failed, parseError, llmResponse.DurationMs, llmResponse.PromptTokens, llmResponse.CompletionTokens, cancellationToken);
                return Results.Ok(ToReplySuggestionDto(suggestion));
            }

            var replyRiskMatches = riskEvaluator.Evaluate($"{request.CustomerQuestion}\n{request.ConversationContext}\n{parsed.ReplyText}", riskRules);
            var finalRiskLevel = MaxRisk(questionRiskLevel, parsed.RiskLevel, riskEvaluator.GetHighestRiskLevel(replyRiskMatches));
            var modeAutoSendDecision = replyMode == AiReplyMode.LlmOnly
                ? AiReplyModePolicy.EvaluateLlmOnlyAutoSend(request.CustomerQuestion, parsed.ReplyText, enableLlmOnlyBusinessFactGuard)
                : new AiReplyAutoSendDecision(
                    hasKnowledgeHits || AiReplyModePolicy.IsSafeNoKnowledgeFallbackReply(parsed.ReplyText),
                    hasKnowledgeHits
                        ? "未满足自动发送条件，需要人工复核。"
                        : "未命中知识库且回复不符合低风险短回复规则，需要人工复核。");
            var effectiveModelAutoSend = parsed.ShouldAutoSend ||
                (replyMode == AiReplyMode.LlmOnly &&
                    finalRiskLevel == RiskLevel.Low &&
                    modeAutoSendDecision.IsAllowed &&
                    AiReplyModePolicy.CanOverrideModelAutoSendForSafeCapabilityBoundary(request.CustomerQuestion, parsed.ReplyText));
            var shouldAutoSend = effectiveModelAutoSend && finalRiskLevel == RiskLevel.Low && modeAutoSendDecision.IsAllowed;

            var finalSuggestion = new ReplySuggestion
            {
                TenantId = request.TenantId ?? TenantDefaults.DefaultTenantId,
                RpaTaskId = request.RpaTaskId,
                CustomerQuestion = request.CustomerQuestion.Trim(),
                Intent = parsed.Intent,
                Confidence = parsed.Confidence,
                RiskLevel = finalRiskLevel,
                ReplyText = parsed.ReplyText,
                KnowledgeRefsJson = JsonSerializer.Serialize(searchResponse.Results.Select(ToKnowledgeRefDto)),
                ShouldAutoSend = shouldAutoSend,
                Status = shouldAutoSend ? ReplySuggestionStatus.Generated : ReplySuggestionStatus.ManualReviewRequired,
                FailureReason = shouldAutoSend
                    ? null
                    : BuildReplySuggestionFailureReason(effectiveModelAutoSend, finalRiskLevel, modeAutoSendDecision.FailureReason, replyModeParse),
                ProviderCode = provider.ProviderCode,
                ModelName = provider.ModelName,
                RawAiResponse = llmResponse.Content
            };

            db.ReplySuggestions.Add(finalSuggestion);
            await SaveAiRequestLogAsync(db, "ReplySuggestion", provider.ProviderCode, provider.ModelName, prompt.TemplateCode, request.CustomerQuestion, llmResponse.Content, AiRequestStatus.Succeeded, null, llmResponse.DurationMs, llmResponse.PromptTokens, llmResponse.CompletionTokens, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(ToReplySuggestionDto(finalSuggestion));
        });
    }

    private static async Task<KnowledgeSearchResponse> SearchKnowledgeAsync(
        KnowledgeSearchRequest request,
        AIChatDbContext db,
        KeywordKnowledgeSearchService searchService,
        CancellationToken cancellationToken)
    {
        var maxResults = request.MaxResults is > 0 ? request.MaxResults.Value : 5;
        var candidates = new List<KnowledgeSearchCandidate>();

        candidates.AddRange(await db.Products
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new KnowledgeSearchCandidate(
                KnowledgeSourceType.Product,
                x.Id,
                x.Name,
                $"{x.ProductCode} {x.Category} {x.Brand} {x.PriceText} {x.Summary} {x.Description}",
                x.Keywords,
                100))
            .ToListAsync(cancellationToken));

        candidates.AddRange(await db.FaqItems
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new KnowledgeSearchCandidate(
                KnowledgeSourceType.Faq,
                x.Id,
                x.Question,
                x.Answer,
                x.Keywords,
                x.Priority))
            .ToListAsync(cancellationToken));

        candidates.AddRange(await db.AfterSaleRules
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new KnowledgeSearchCandidate(
                KnowledgeSourceType.AfterSaleRule,
                x.Id,
                x.Title,
                $"{x.Scenario} {x.Content}",
                x.Keywords,
                x.Priority))
            .ToListAsync(cancellationToken));

        candidates.AddRange(await db.KnowledgeChunks
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new KnowledgeSearchCandidate(
                x.SourceType,
                x.Id,
                x.Title,
                x.Content,
                x.Keywords,
                x.ChunkIndex))
            .ToListAsync(cancellationToken));

        var results = searchService.Search(request.Query, candidates, maxResults)
            .Select(x => new KnowledgeSearchResultDto(x.SourceType, x.SourceId, x.Title, x.Snippet, x.Score))
            .ToList();

        var log = new KnowledgeSearchLog
        {
            Query = request.Query.Trim(),
            SearchMode = request.SearchMode ?? KnowledgeSearchMode.Keyword,
            HitCount = results.Count,
            ResultRefsJson = JsonSerializer.Serialize(results.Select(ToKnowledgeRefDto))
        };
        db.KnowledgeSearchLogs.Add(log);
        await db.SaveChangesAsync(cancellationToken);

        return new KnowledgeSearchResponse(request.Query.Trim(), log.SearchMode, results);
    }

    private static async Task<LlmProviderConfig?> ResolveProviderAsync(string? providerCode, AIChatDbContext db, CancellationToken cancellationToken)
    {
        var query = db.LlmProviderConfigs.AsNoTracking().Where(x => x.IsEnabled);
        if (!string.IsNullOrWhiteSpace(providerCode))
        {
            var code = providerCode.Trim();
            return await query.FirstOrDefaultAsync(x => x.ProviderCode == code, cancellationToken);
        }

        return await query.OrderBy(x => x.ProviderCode).FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<ResolvedPromptTemplate> ResolvePromptTemplateAsync(string? promptTemplateCode, AIChatDbContext db, CancellationToken cancellationToken)
    {
        var query = db.PromptTemplates
            .AsNoTracking()
            .Where(x => x.IsActive && x.TemplateType == PromptTemplateType.ReplySuggestion);

        PromptTemplate? template;
        if (string.IsNullOrWhiteSpace(promptTemplateCode))
        {
            template = await query.OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            var code = promptTemplateCode.Trim();
            template = await query.Where(x => x.TemplateCode == code).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        }

        return template is null
            ? new ResolvedPromptTemplate("built-in-reply-suggestion", BuiltInReplySystemPrompt, BuiltInReplyUserPrompt)
            : new ResolvedPromptTemplate(template.TemplateCode, template.SystemPrompt, template.UserPromptTemplate);
    }

    private static string ResolveApiKey(string variableName, IConfiguration configuration)
    {
        var envValue = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue;
        }

        return configuration[variableName.Replace("__", ":")] ?? string.Empty;
    }

    private static string ApplyPromptVariables(string template, string question, string? conversationContext, string knowledgeContext)
    {
        return template
            .Replace("{Question}", question, StringComparison.OrdinalIgnoreCase)
            .Replace("{ConversationContext}", string.IsNullOrWhiteSpace(conversationContext) ? "无" : conversationContext.Trim(), StringComparison.OrdinalIgnoreCase)
            .Replace("{KnowledgeContext}", knowledgeContext, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildKnowledgeContext(IReadOnlyCollection<KnowledgeSearchResultDto> results)
    {
        return string.Join(
            "\n\n",
            results.Select((x, index) => $"[{index + 1}] {x.SourceType}:{x.SourceId}\n标题：{x.Title}\n内容：{x.Snippet}"));
    }

    private static RiskLevel MaxRisk(params RiskLevel[] levels)
    {
        return levels.Max();
    }

    private static string BuildReplySuggestionFailureReason(
        bool parsedShouldAutoSend,
        RiskLevel finalRiskLevel,
        string? modeFailureReason,
        AiReplyModeParseResult replyModeParse)
    {
        var reason = !parsedShouldAutoSend
            ? "AI 判断不应自动发送，需要人工复核。"
            : finalRiskLevel != RiskLevel.Low
                ? "最终风险等级不是 Low，需要人工复核。"
                : modeFailureReason ?? "未满足自动发送条件，需要人工复核。";

        return replyModeParse.IsValid
            ? reason
            : $"Ai:ReplyMode 配置非法（{replyModeParse.RawValue}），已按 KnowledgeFirst 处理；{reason}";
    }

    private static async Task<ReplySuggestion> SaveReplySuggestionFailureAsync(
        CreateReplySuggestionRequest request,
        AIChatDbContext db,
        ReplySuggestionStatus status,
        RiskLevel riskLevel,
        string failureReason,
        string replyText,
        IReadOnlyCollection<KnowledgeSearchResultDto> knowledgeResults,
        string? providerCode,
        string? modelName,
        CancellationToken cancellationToken,
        string? rawAiResponse = null)
    {
        var suggestion = new ReplySuggestion
        {
            TenantId = request.TenantId ?? TenantDefaults.DefaultTenantId,
            RpaTaskId = request.RpaTaskId,
            CustomerQuestion = request.CustomerQuestion.Trim(),
            Intent = null,
            Confidence = 0,
            RiskLevel = riskLevel,
            ReplyText = replyText,
            KnowledgeRefsJson = JsonSerializer.Serialize(knowledgeResults.Select(ToKnowledgeRefDto)),
            ShouldAutoSend = false,
            Status = status,
            FailureReason = failureReason,
            ProviderCode = providerCode,
            ModelName = modelName,
            RawAiResponse = rawAiResponse
        };

        db.ReplySuggestions.Add(suggestion);
        await db.SaveChangesAsync(cancellationToken);
        return suggestion;
    }

    private static async Task SaveAiRequestLogAsync(
        AIChatDbContext db,
        string requestType,
        string? providerCode,
        string? modelName,
        string? promptTemplateCode,
        string? inputSummary,
        string? outputSummary,
        AiRequestStatus status,
        string? errorMessage,
        int? durationMs,
        int? promptTokens,
        int? completionTokens,
        CancellationToken cancellationToken)
    {
        db.AiRequestLogs.Add(new AiRequestLog
        {
            RequestType = requestType,
            ProviderCode = providerCode,
            ModelName = modelName,
            PromptTemplateCode = promptTemplateCode,
            InputSummary = Truncate(inputSummary, 1000),
            OutputSummary = Truncate(outputSummary, 1000),
            Status = status,
            ErrorMessage = errorMessage,
            DurationMs = durationMs,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private static KnowledgeRefDto ToKnowledgeRefDto(KnowledgeSearchResultDto result)
    {
        return new KnowledgeRefDto(result.SourceType, result.SourceId, result.Title, result.Score);
    }

    private static ProductDto ToProductDto(Product item)
    {
        return new ProductDto(item.Id, item.TenantId, item.ProductCode, item.Name, item.Category, item.Brand, item.PriceText, item.Summary, item.Description, item.Keywords, item.IsActive, item.CreatedAt, item.UpdatedAt);
    }

    private static FaqItemDto ToFaqItemDto(FaqItem item)
    {
        return new FaqItemDto(item.Id, item.TenantId, item.Question, item.Answer, item.Category, item.Keywords, item.Priority, item.IsActive, item.CreatedAt, item.UpdatedAt);
    }

    private static AfterSaleRuleDto ToAfterSaleRuleDto(AfterSaleRule item)
    {
        return new AfterSaleRuleDto(item.Id, item.TenantId, item.RuleCode, item.Title, item.Scenario, item.Content, item.Keywords, item.Priority, item.IsActive, item.CreatedAt, item.UpdatedAt);
    }

    private static RiskRuleDto ToRiskRuleDto(RiskRule item)
    {
        return new RiskRuleDto(item.Id, item.TenantId, item.RuleName, item.Keywords, item.RiskLevel, item.Action, item.Description, item.IsEnabled, item.CreatedAt, item.UpdatedAt);
    }

    private static PromptTemplateDto ToPromptTemplateDto(PromptTemplate item)
    {
        return new PromptTemplateDto(item.Id, item.TenantId, item.TemplateCode, item.Name, item.TemplateType, item.SystemPrompt, item.UserPromptTemplate, item.Version, item.IsActive, item.CreatedAt, item.UpdatedAt);
    }

    private static LlmProviderConfigDto ToLlmProviderConfigDto(LlmProviderConfig item)
    {
        return new LlmProviderConfigDto(item.Id, item.TenantId, item.ProviderCode, item.ProviderType, item.DisplayName, item.BaseUrl, item.ModelName, item.ApiKeyEnvironmentVariable, item.IsEnabled, item.TimeoutSeconds, item.Notes, item.CreatedAt, item.UpdatedAt);
    }

    private static ReplySuggestionDto ToReplySuggestionDto(ReplySuggestion item)
    {
        return new ReplySuggestionDto(item.Id, item.TenantId, item.RpaTaskId, item.CustomerQuestion, item.Intent, item.Confidence, item.RiskLevel, item.ReplyText, item.KnowledgeRefsJson, item.ShouldAutoSend, item.Status, item.FailureReason, item.ProviderCode, item.ModelName, item.CreatedAt, item.UpdatedAt);
    }

    private static AiRequestLogDto ToAiRequestLogDto(AiRequestLog item)
    {
        return new AiRequestLogDto(item.Id, item.TenantId, item.RequestType, item.ProviderCode, item.ModelName, item.PromptTemplateCode, item.InputSummary, item.OutputSummary, item.Status, item.ErrorMessage, item.DurationMs, item.PromptTokens, item.CompletionTokens, item.CreatedAt, item.UpdatedAt);
    }

    private sealed record ResolvedPromptTemplate(string TemplateCode, string SystemPrompt, string UserPromptTemplate);
}

public sealed record UpsertProductRequest(Guid? TenantId, string? ProductCode, string? Name, string? Category, string? Brand, string? PriceText, string? Summary, string? Description, string? Keywords, bool? IsActive);
public sealed record ProductDto(Guid Id, Guid TenantId, string ProductCode, string Name, string? Category, string? Brand, string? PriceText, string? Summary, string? Description, string? Keywords, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record UpsertFaqItemRequest(Guid? TenantId, string? Question, string? Answer, string? Category, string? Keywords, int? Priority, bool? IsActive);
public sealed record FaqItemDto(Guid Id, Guid TenantId, string Question, string Answer, string? Category, string? Keywords, int Priority, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record UpsertAfterSaleRuleRequest(Guid? TenantId, string? RuleCode, string? Title, string? Scenario, string? Content, string? Keywords, int? Priority, bool? IsActive);
public sealed record AfterSaleRuleDto(Guid Id, Guid TenantId, string RuleCode, string Title, string? Scenario, string Content, string? Keywords, int Priority, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record UpsertRiskRuleRequest(Guid? TenantId, string? RuleName, string? Keywords, RiskLevel? RiskLevel, RiskRuleAction? Action, string? Description, bool? IsEnabled);
public sealed record RiskRuleDto(Guid Id, Guid TenantId, string RuleName, string Keywords, RiskLevel RiskLevel, RiskRuleAction Action, string? Description, bool IsEnabled, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record UpsertPromptTemplateRequest(Guid? TenantId, string? TemplateCode, string? Name, PromptTemplateType? TemplateType, string? SystemPrompt, string? UserPromptTemplate, string? Version, bool? IsActive);
public sealed record PromptTemplateDto(Guid Id, Guid TenantId, string TemplateCode, string Name, PromptTemplateType TemplateType, string SystemPrompt, string UserPromptTemplate, string Version, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record UpsertLlmProviderConfigRequest(Guid? TenantId, string? ProviderCode, LlmProviderType? ProviderType, string? DisplayName, string? BaseUrl, string? ModelName, string? ApiKeyEnvironmentVariable, bool? IsEnabled, int? TimeoutSeconds, string? Notes);
public sealed record LlmProviderConfigDto(Guid Id, Guid TenantId, string ProviderCode, LlmProviderType ProviderType, string DisplayName, string BaseUrl, string ModelName, string ApiKeyEnvironmentVariable, bool IsEnabled, int TimeoutSeconds, string? Notes, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record KnowledgeSearchRequest(string Query, KnowledgeSearchMode? SearchMode, int? MaxResults);
public sealed record KnowledgeSearchResponse(string Query, KnowledgeSearchMode SearchMode, IReadOnlyList<KnowledgeSearchResultDto> Results);
public sealed record KnowledgeSearchResultDto(KnowledgeSourceType SourceType, Guid SourceId, string Title, string Snippet, decimal Score);
public sealed record KnowledgeRefDto(KnowledgeSourceType SourceType, Guid SourceId, string Title, decimal Score);

public sealed record CreateReplySuggestionRequest(Guid? TenantId, Guid? RpaTaskId, string CustomerQuestion, string? ConversationContext, string? ProviderCode, string? PromptTemplateCode, int? MaxKnowledgeResults);
public sealed record ReplySuggestionDto(Guid Id, Guid TenantId, Guid? RpaTaskId, string CustomerQuestion, string? Intent, decimal Confidence, RiskLevel RiskLevel, string ReplyText, string KnowledgeRefsJson, bool ShouldAutoSend, ReplySuggestionStatus Status, string? FailureReason, string? ProviderCode, string? ModelName, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
public sealed record AiRequestLogDto(Guid Id, Guid TenantId, string RequestType, string? ProviderCode, string? ModelName, string? PromptTemplateCode, string? InputSummary, string? OutputSummary, AiRequestStatus Status, string? ErrorMessage, int? DurationMs, int? PromptTokens, int? CompletionTokens, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
