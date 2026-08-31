using Microsoft.Extensions.DependencyInjection;
using AIChat.Application.AccessControl;
using AIChat.Application.AI;
using AIChat.Application.Knowledge;
using AIChat.Application.Risk;
using AIChat.Application.RpaTasks;

namespace AIChat.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<ClientAccessEvaluator>();
        services.AddSingleton<KeywordKnowledgeSearchService>();
        services.AddSingleton<RiskRuleEvaluator>();
        services.AddSingleton<StructuredReplyParser>();
        services.AddSingleton<RpaTaskResultUpdater>();

        return services;
    }
}
