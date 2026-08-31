using AIChat.Domain.Common;
using AIChat.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIChat.Infrastructure.Persistence;

public sealed class AIChatDbContext(DbContextOptions<AIChatDbContext> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeClientAccessPolicy> EmployeeClientAccessPolicies => Set<EmployeeClientAccessPolicy>();
    public DbSet<WeChatWorkAccount> WeChatWorkAccounts => Set<WeChatWorkAccount>();
    public DbSet<DeviceHost> DeviceHosts => Set<DeviceHost>();
    public DbSet<VirtualDevice> VirtualDevices => Set<VirtualDevice>();
    public DbSet<RpaClientInstance> RpaClientInstances => Set<RpaClientInstance>();
    public DbSet<RpaTask> RpaTasks => Set<RpaTask>();
    public DbSet<RpaActionLog> RpaActionLogs => Set<RpaActionLog>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<FaqItem> FaqItems => Set<FaqItem>();
    public DbSet<AfterSaleRule> AfterSaleRules => Set<AfterSaleRule>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<RiskRule> RiskRules => Set<RiskRule>();
    public DbSet<ReplySuggestion> ReplySuggestions => Set<ReplySuggestion>();
    public DbSet<AiRequestLog> AiRequestLogs => Set<AiRequestLog>();
    public DbSet<KnowledgeSearchLog> KnowledgeSearchLogs => Set<KnowledgeSearchLog>();
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();
    public DbSet<LlmProviderConfig> LlmProviderConfigs => Set<LlmProviderConfig>();
    public DbSet<EmbeddingRecord> EmbeddingRecords => Set<EmbeddingRecord>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditFields();
        return base.SaveChanges();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureEmployee(modelBuilder);
        ConfigureEmployeeClientAccessPolicy(modelBuilder);
        ConfigureWeChatWorkAccount(modelBuilder);
        ConfigureDeviceHost(modelBuilder);
        ConfigureVirtualDevice(modelBuilder);
        ConfigureRpaClientInstance(modelBuilder);
        ConfigureRpaTask(modelBuilder);
        ConfigureRpaActionLog(modelBuilder);
        ConfigureProduct(modelBuilder);
        ConfigureFaqItem(modelBuilder);
        ConfigureAfterSaleRule(modelBuilder);
        ConfigureKnowledgeDocument(modelBuilder);
        ConfigureKnowledgeChunk(modelBuilder);
        ConfigureRiskRule(modelBuilder);
        ConfigureReplySuggestion(modelBuilder);
        ConfigureAiRequestLog(modelBuilder);
        ConfigureKnowledgeSearchLog(modelBuilder);
        ConfigurePromptTemplate(modelBuilder);
        ConfigureLlmProviderConfig(modelBuilder);
        ConfigureEmbeddingRecord(modelBuilder);
    }

    private static void ConfigureEmployee(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Employee>();
        entity.ToTable("employees");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.EmployeeNo).HasMaxLength(64).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Department).HasMaxLength(128);
        entity.Property(x => x.PhoneNumber).HasMaxLength(32);
        entity.Property(x => x.Notes).HasMaxLength(512);
        entity.HasIndex(x => new { x.TenantId, x.EmployeeNo }).IsUnique();
    }

    private static void ConfigureEmployeeClientAccessPolicy(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<EmployeeClientAccessPolicy>();
        entity.ToTable("employee_client_access_policies");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.PauseReason).HasMaxLength(512);
        entity.HasIndex(x => x.EmployeeId).IsUnique();
        entity.HasOne(x => x.Employee)
            .WithOne(x => x.ClientAccessPolicy)
            .HasForeignKey<EmployeeClientAccessPolicy>(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureWeChatWorkAccount(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WeChatWorkAccount>();
        entity.ToTable("wechat_work_accounts");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
        entity.Property(x => x.WeChatId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.PhoneNumberMasked).HasMaxLength(32);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Notes).HasMaxLength(512);
        entity.HasIndex(x => new { x.TenantId, x.WeChatId }).IsUnique();
        entity.HasOne(x => x.Employee)
            .WithMany(x => x.WeChatWorkAccounts)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureDeviceHost(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DeviceHost>();
        entity.ToTable("device_hosts");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.HostName).HasMaxLength(128).IsRequired();
        entity.Property(x => x.AssetCode).HasMaxLength(128);
        entity.Property(x => x.IpAddress).HasMaxLength(64);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Notes).HasMaxLength(512);
        entity.HasIndex(x => new { x.TenantId, x.HostName }).IsUnique();
    }

    private static void ConfigureVirtualDevice(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<VirtualDevice>();
        entity.ToTable("virtual_devices");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.VmName).HasMaxLength(128).IsRequired();
        entity.Property(x => x.MachineCode).HasMaxLength(256);
        entity.Property(x => x.IpAddress).HasMaxLength(64);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Notes).HasMaxLength(512);
        entity.HasIndex(x => new { x.TenantId, x.VmName }).IsUnique();
        entity.HasIndex(x => new { x.TenantId, x.MachineCode }).IsUnique();
        entity.HasOne(x => x.DeviceHost)
            .WithMany(x => x.VirtualDevices)
            .HasForeignKey(x => x.DeviceHostId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Employee)
            .WithMany(x => x.VirtualDevices)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(x => x.WeChatWorkAccount)
            .WithMany()
            .HasForeignKey(x => x.WeChatWorkAccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureRpaClientInstance(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RpaClientInstance>();
        entity.ToTable("rpa_client_instances");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.ClientInstanceKey).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ClientVersion).HasMaxLength(64);
        entity.Property(x => x.MachineName).HasMaxLength(128);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.LastAccessStatus).HasMaxLength(64);
        entity.Property(x => x.LastAccessReason).HasMaxLength(512);
        entity.HasIndex(x => x.ClientInstanceKey).IsUnique();
        entity.HasOne(x => x.VirtualDevice)
            .WithMany(x => x.RpaClientInstances)
            .HasForeignKey(x => x.VirtualDeviceId)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(x => x.Employee)
            .WithMany(x => x.RpaClientInstances)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(x => x.WeChatWorkAccount)
            .WithMany(x => x.RpaClientInstances)
            .HasForeignKey(x => x.WeChatWorkAccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureRpaTask(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RpaTask>();
        entity.ToTable("rpa_tasks");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.TaskType).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.ConversationKey).HasMaxLength(128);
        entity.Property(x => x.CustomerDisplayName).HasMaxLength(128);
        entity.Property(x => x.IncomingMessageText).HasColumnType("text");
        entity.Property(x => x.AiReplyText).HasColumnType("text");
        entity.Property(x => x.RiskResult).HasMaxLength(512);
        entity.Property(x => x.ErrorMessage).HasColumnType("text");
        entity.HasIndex(x => new { x.RpaClientInstanceId, x.Status });
        entity.HasOne(x => x.RpaClientInstance)
            .WithMany(x => x.RpaTasks)
            .HasForeignKey(x => x.RpaClientInstanceId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(x => x.WeChatWorkAccount)
            .WithMany(x => x.RpaTasks)
            .HasForeignKey(x => x.WeChatWorkAccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureRpaActionLog(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RpaActionLog>();
        entity.ToTable("rpa_action_logs");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Level).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.ActionName).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Message).HasColumnType("text");
        entity.Property(x => x.OcrText).HasColumnType("text");
        entity.Property(x => x.AiReplyText).HasColumnType("text");
        entity.Property(x => x.RiskResult).HasMaxLength(512);
        entity.Property(x => x.SanitizedScreenshotPath).HasMaxLength(512);
        entity.HasIndex(x => new { x.RpaClientInstanceId, x.LoggedAtUtc });
        entity.HasOne(x => x.RpaTask)
            .WithMany(x => x.ActionLogs)
            .HasForeignKey(x => x.RpaTaskId)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(x => x.RpaClientInstance)
            .WithMany()
            .HasForeignKey(x => x.RpaClientInstanceId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureProduct(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Product>();
        entity.ToTable("products");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.ProductCode).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(256).IsRequired();
        entity.Property(x => x.Category).HasMaxLength(128);
        entity.Property(x => x.Brand).HasMaxLength(128);
        entity.Property(x => x.PriceText).HasMaxLength(128);
        entity.Property(x => x.Summary).HasColumnType("text");
        entity.Property(x => x.Description).HasColumnType("text");
        entity.Property(x => x.Keywords).HasMaxLength(512);
        entity.HasIndex(x => new { x.TenantId, x.ProductCode }).IsUnique();
        entity.HasIndex(x => new { x.TenantId, x.Name });
    }

    private static void ConfigureFaqItem(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<FaqItem>();
        entity.ToTable("faq_items");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Question).HasColumnType("text");
        entity.Property(x => x.Answer).HasColumnType("text");
        entity.Property(x => x.Category).HasMaxLength(128);
        entity.Property(x => x.Keywords).HasMaxLength(512);
        entity.HasIndex(x => new { x.TenantId, x.Category });
        entity.HasIndex(x => new { x.TenantId, x.Priority });
    }

    private static void ConfigureAfterSaleRule(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AfterSaleRule>();
        entity.ToTable("after_sale_rules");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.RuleCode).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Title).HasMaxLength(256).IsRequired();
        entity.Property(x => x.Scenario).HasMaxLength(256);
        entity.Property(x => x.Content).HasColumnType("text");
        entity.Property(x => x.Keywords).HasMaxLength(512);
        entity.HasIndex(x => new { x.TenantId, x.RuleCode }).IsUnique();
        entity.HasIndex(x => new { x.TenantId, x.Priority });
    }

    private static void ConfigureKnowledgeDocument(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<KnowledgeDocument>();
        entity.ToTable("knowledge_documents");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Title).HasMaxLength(256).IsRequired();
        entity.Property(x => x.Category).HasMaxLength(128);
        entity.Property(x => x.Content).HasColumnType("text");
        entity.Property(x => x.SourceName).HasMaxLength(256);
        entity.HasIndex(x => new { x.TenantId, x.Title });
    }

    private static void ConfigureKnowledgeChunk(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<KnowledgeChunk>();
        entity.ToTable("knowledge_chunks");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(x => x.Title).HasMaxLength(256).IsRequired();
        entity.Property(x => x.Content).HasColumnType("text");
        entity.Property(x => x.Keywords).HasMaxLength(512);
        entity.Property(x => x.VectorRef).HasMaxLength(512);
        entity.Property(x => x.EmbeddingModel).HasMaxLength(128);
        entity.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceEntityId });
        entity.HasIndex(x => new { x.TenantId, x.KnowledgeDocumentId, x.ChunkIndex });
        entity.HasOne(x => x.KnowledgeDocument)
            .WithMany(x => x.Chunks)
            .HasForeignKey(x => x.KnowledgeDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureRiskRule(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RiskRule>();
        entity.ToTable("risk_rules");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.RuleName).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Keywords).HasMaxLength(512).IsRequired();
        entity.Property(x => x.RiskLevel).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Action).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(512);
        entity.HasIndex(x => new { x.TenantId, x.RuleName }).IsUnique();
    }

    private static void ConfigureReplySuggestion(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReplySuggestion>();
        entity.ToTable("reply_suggestions");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.CustomerQuestion).HasColumnType("text");
        entity.Property(x => x.Intent).HasMaxLength(128);
        entity.Property(x => x.Confidence).HasPrecision(5, 4);
        entity.Property(x => x.RiskLevel).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.ReplyText).HasColumnType("text");
        entity.Property(x => x.KnowledgeRefsJson).HasColumnType("text");
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(x => x.FailureReason).HasColumnType("text");
        entity.Property(x => x.ProviderCode).HasMaxLength(128);
        entity.Property(x => x.ModelName).HasMaxLength(128);
        entity.Property(x => x.RawAiResponse).HasColumnType("text");
        entity.HasIndex(x => new { x.TenantId, x.CreatedAt });
        entity.HasIndex(x => x.RpaTaskId);
        entity.HasOne(x => x.RpaTask)
            .WithMany()
            .HasForeignKey(x => x.RpaTaskId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureAiRequestLog(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AiRequestLog>();
        entity.ToTable("ai_request_logs");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.RequestType).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ProviderCode).HasMaxLength(128);
        entity.Property(x => x.ModelName).HasMaxLength(128);
        entity.Property(x => x.PromptTemplateCode).HasMaxLength(128);
        entity.Property(x => x.InputSummary).HasColumnType("text");
        entity.Property(x => x.OutputSummary).HasColumnType("text");
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.ErrorMessage).HasColumnType("text");
        entity.HasIndex(x => new { x.TenantId, x.CreatedAt });
        entity.HasIndex(x => new { x.TenantId, x.ProviderCode });
    }

    private static void ConfigureKnowledgeSearchLog(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<KnowledgeSearchLog>();
        entity.ToTable("knowledge_search_logs");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Query).HasColumnType("text");
        entity.Property(x => x.SearchMode).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.ResultRefsJson).HasColumnType("text");
        entity.HasIndex(x => new { x.TenantId, x.CreatedAt });
    }

    private static void ConfigurePromptTemplate(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PromptTemplate>();
        entity.ToTable("prompt_templates");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.TemplateCode).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(128).IsRequired();
        entity.Property(x => x.TemplateType).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(x => x.SystemPrompt).HasColumnType("text");
        entity.Property(x => x.UserPromptTemplate).HasColumnType("text");
        entity.Property(x => x.Version).HasMaxLength(64).IsRequired();
        entity.HasIndex(x => new { x.TenantId, x.TemplateCode, x.Version }).IsUnique();
    }

    private static void ConfigureLlmProviderConfig(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LlmProviderConfig>();
        entity.ToTable("llm_provider_configs");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.ProviderCode).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ProviderType).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
        entity.Property(x => x.BaseUrl).HasMaxLength(512).IsRequired();
        entity.Property(x => x.ModelName).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ApiKeyEnvironmentVariable).HasMaxLength(256).IsRequired();
        entity.Property(x => x.Notes).HasMaxLength(512);
        entity.HasIndex(x => new { x.TenantId, x.ProviderCode }).IsUnique();
    }

    private static void ConfigureEmbeddingRecord(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<EmbeddingRecord>();
        entity.ToTable("embedding_records");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(x => x.ProviderCode).HasMaxLength(128);
        entity.Property(x => x.EmbeddingModel).HasMaxLength(128).IsRequired();
        entity.Property(x => x.VectorRef).HasMaxLength(512).IsRequired();
        entity.Property(x => x.VectorVersion).HasMaxLength(64).IsRequired();
        entity.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceEntityId, x.VectorVersion }).IsUnique();
    }

    private void ApplyAuditFields()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = entry.Entity.CreatedAt == default ? now : entry.Entity.CreatedAt;
                entry.Entity.TenantId = entry.Entity.TenantId == Guid.Empty ? TenantDefaults.DefaultTenantId : entry.Entity.TenantId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
