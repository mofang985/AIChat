using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AIChat.Infrastructure.Persistence;

public sealed class AIChatDbContextFactory : IDesignTimeDbContextFactory<AIChatDbContext>
{
    public AIChatDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=localhost;Port=5432;Database=aichat;Username=postgres;Password=CHANGE_ME";
        }

        var options = new DbContextOptionsBuilder<AIChatDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AIChatDbContext(options);
    }
}
