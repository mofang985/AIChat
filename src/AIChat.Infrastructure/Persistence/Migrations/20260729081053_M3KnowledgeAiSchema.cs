using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M3KnowledgeAiSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "after_sale_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Scenario = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Keywords = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_after_sale_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ai_request_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ModelName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PromptTemplateCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    InputSummary = table.Column<string>(type: "text", nullable: true),
                    OutputSummary = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    PromptTokens = table.Column<int>(type: "integer", nullable: true),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_request_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "embedding_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EmbeddingModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    VectorRef = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    VectorVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_embedding_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "faq_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Question = table.Column<string>(type: "text", nullable: false),
                    Answer = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Keywords = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faq_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    SourceName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastIndexedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_search_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Query = table.Column<string>(type: "text", nullable: false),
                    SearchMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    HitCount = table.Column<int>(type: "integer", nullable: false),
                    ResultRefsJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_search_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "llm_provider_configs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ApiKeyEnvironmentVariable = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_llm_provider_configs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Brand = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PriceText = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Keywords = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TemplateType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SystemPrompt = table.Column<string>(type: "text", nullable: false),
                    UserPromptTemplate = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reply_suggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RpaTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerQuestion = table.Column<string>(type: "text", nullable: false),
                    Intent = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    RiskLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReplyText = table.Column<string>(type: "text", nullable: false),
                    KnowledgeRefsJson = table.Column<string>(type: "text", nullable: false),
                    ShouldAutoSend = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    ProviderCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ModelName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RawAiResponse = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reply_suggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reply_suggestions_rpa_tasks_RpaTaskId",
                        column: x => x.RpaTaskId,
                        principalTable: "rpa_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "risk_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Keywords = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RiskLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_chunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Keywords = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    VectorRef = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    EmbeddingModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_chunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_knowledge_chunks_knowledge_documents_KnowledgeDocumentId",
                        column: x => x.KnowledgeDocumentId,
                        principalTable: "knowledge_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_after_sale_rules_TenantId_Priority",
                table: "after_sale_rules",
                columns: new[] { "TenantId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_after_sale_rules_TenantId_RuleCode",
                table: "after_sale_rules",
                columns: new[] { "TenantId", "RuleCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ai_request_logs_TenantId_CreatedAt",
                table: "ai_request_logs",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_request_logs_TenantId_ProviderCode",
                table: "ai_request_logs",
                columns: new[] { "TenantId", "ProviderCode" });

            migrationBuilder.CreateIndex(
                name: "IX_embedding_records_TenantId_SourceType_SourceEntityId_Vector~",
                table: "embedding_records",
                columns: new[] { "TenantId", "SourceType", "SourceEntityId", "VectorVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_faq_items_TenantId_Category",
                table: "faq_items",
                columns: new[] { "TenantId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_faq_items_TenantId_Priority",
                table: "faq_items",
                columns: new[] { "TenantId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_chunks_KnowledgeDocumentId",
                table: "knowledge_chunks",
                column: "KnowledgeDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_chunks_TenantId_KnowledgeDocumentId_ChunkIndex",
                table: "knowledge_chunks",
                columns: new[] { "TenantId", "KnowledgeDocumentId", "ChunkIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_chunks_TenantId_SourceType_SourceEntityId",
                table: "knowledge_chunks",
                columns: new[] { "TenantId", "SourceType", "SourceEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_documents_TenantId_Title",
                table: "knowledge_documents",
                columns: new[] { "TenantId", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_search_logs_TenantId_CreatedAt",
                table: "knowledge_search_logs",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_llm_provider_configs_TenantId_ProviderCode",
                table: "llm_provider_configs",
                columns: new[] { "TenantId", "ProviderCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_TenantId_Name",
                table: "products",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_products_TenantId_ProductCode",
                table: "products",
                columns: new[] { "TenantId", "ProductCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_prompt_templates_TenantId_TemplateCode_Version",
                table: "prompt_templates",
                columns: new[] { "TenantId", "TemplateCode", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reply_suggestions_RpaTaskId",
                table: "reply_suggestions",
                column: "RpaTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_reply_suggestions_TenantId_CreatedAt",
                table: "reply_suggestions",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_risk_rules_TenantId_RuleName",
                table: "risk_rules",
                columns: new[] { "TenantId", "RuleName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "after_sale_rules");

            migrationBuilder.DropTable(
                name: "ai_request_logs");

            migrationBuilder.DropTable(
                name: "embedding_records");

            migrationBuilder.DropTable(
                name: "faq_items");

            migrationBuilder.DropTable(
                name: "knowledge_chunks");

            migrationBuilder.DropTable(
                name: "knowledge_search_logs");

            migrationBuilder.DropTable(
                name: "llm_provider_configs");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "prompt_templates");

            migrationBuilder.DropTable(
                name: "reply_suggestions");

            migrationBuilder.DropTable(
                name: "risk_rules");

            migrationBuilder.DropTable(
                name: "knowledge_documents");
        }
    }
}
