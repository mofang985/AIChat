using AIChat.Application.AI;
using AIChat.Infrastructure.AI;
using AIChat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AIChat.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Database connection string is not configured. Set the ConnectionStrings__DefaultConnection environment variable.");
        }

        services.AddDbContext<AIChatDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });
        services.AddHttpClient<ILlmProvider, OpenAICompatibleLlmProvider>();

        return services;
    }
}
