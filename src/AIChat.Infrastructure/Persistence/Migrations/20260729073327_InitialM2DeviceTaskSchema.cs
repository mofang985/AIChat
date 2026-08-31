using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialM2DeviceTaskSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_hosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AssetCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CpuCores = table.Column<int>(type: "integer", nullable: true),
                    MemoryGb = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_hosts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Department = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "employee_client_access_policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ValidToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MaxDailyUsageMinutes = table.Column<int>(type: "integer", nullable: true),
                    MaxSessionMinutes = table.Column<int>(type: "integer", nullable: true),
                    PauseReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_client_access_policies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employee_client_access_policies_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wechat_work_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    WeChatId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PhoneNumberMasked = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wechat_work_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wechat_work_accounts_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "virtual_devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceHostId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    WeChatWorkAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    VmName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MachineCode = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_virtual_devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_virtual_devices_device_hosts_DeviceHostId",
                        column: x => x.DeviceHostId,
                        principalTable: "device_hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_virtual_devices_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_virtual_devices_wechat_work_accounts_WeChatWorkAccountId",
                        column: x => x.WeChatWorkAccountId,
                        principalTable: "wechat_work_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "rpa_client_instances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VirtualDeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    WeChatWorkAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientInstanceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ClientVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MachineName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RegisteredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastHeartbeatAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CurrentSessionStartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastCanContinueRun = table.Column<bool>(type: "boolean", nullable: false),
                    LastAccessStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastAccessReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rpa_client_instances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rpa_client_instances_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_rpa_client_instances_virtual_devices_VirtualDeviceId",
                        column: x => x.VirtualDeviceId,
                        principalTable: "virtual_devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_rpa_client_instances_wechat_work_accounts_WeChatWorkAccount~",
                        column: x => x.WeChatWorkAccountId,
                        principalTable: "wechat_work_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "rpa_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RpaClientInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    WeChatWorkAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    ConversationKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CustomerDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IncomingMessageText = table.Column<string>(type: "text", nullable: true),
                    AiReplyText = table.Column<string>(type: "text", nullable: true),
                    RiskResult = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ScheduledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rpa_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rpa_tasks_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_rpa_tasks_rpa_client_instances_RpaClientInstanceId",
                        column: x => x.RpaClientInstanceId,
                        principalTable: "rpa_client_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rpa_tasks_wechat_work_accounts_WeChatWorkAccountId",
                        column: x => x.WeChatWorkAccountId,
                        principalTable: "wechat_work_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "rpa_action_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RpaTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    RpaClientInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActionName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    OcrText = table.Column<string>(type: "text", nullable: true),
                    AiReplyText = table.Column<string>(type: "text", nullable: true),
                    RiskResult = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SanitizedScreenshotPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LoggedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rpa_action_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rpa_action_logs_rpa_client_instances_RpaClientInstanceId",
                        column: x => x.RpaClientInstanceId,
                        principalTable: "rpa_client_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rpa_action_logs_rpa_tasks_RpaTaskId",
                        column: x => x.RpaTaskId,
                        principalTable: "rpa_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_hosts_TenantId_HostName",
                table: "device_hosts",
                columns: new[] { "TenantId", "HostName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_client_access_policies_EmployeeId",
                table: "employee_client_access_policies",
                column: "EmployeeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employees_TenantId_EmployeeNo",
                table: "employees",
                columns: new[] { "TenantId", "EmployeeNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rpa_action_logs_RpaClientInstanceId_LoggedAtUtc",
                table: "rpa_action_logs",
                columns: new[] { "RpaClientInstanceId", "LoggedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_rpa_action_logs_RpaTaskId",
                table: "rpa_action_logs",
                column: "RpaTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_rpa_client_instances_ClientInstanceKey",
                table: "rpa_client_instances",
                column: "ClientInstanceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rpa_client_instances_EmployeeId",
                table: "rpa_client_instances",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_rpa_client_instances_VirtualDeviceId",
                table: "rpa_client_instances",
                column: "VirtualDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_rpa_client_instances_WeChatWorkAccountId",
                table: "rpa_client_instances",
                column: "WeChatWorkAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_rpa_tasks_EmployeeId",
                table: "rpa_tasks",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_rpa_tasks_RpaClientInstanceId_Status",
                table: "rpa_tasks",
                columns: new[] { "RpaClientInstanceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_rpa_tasks_WeChatWorkAccountId",
                table: "rpa_tasks",
                column: "WeChatWorkAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_virtual_devices_DeviceHostId",
                table: "virtual_devices",
                column: "DeviceHostId");

            migrationBuilder.CreateIndex(
                name: "IX_virtual_devices_EmployeeId",
                table: "virtual_devices",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_virtual_devices_TenantId_MachineCode",
                table: "virtual_devices",
                columns: new[] { "TenantId", "MachineCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_virtual_devices_TenantId_VmName",
                table: "virtual_devices",
                columns: new[] { "TenantId", "VmName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_virtual_devices_WeChatWorkAccountId",
                table: "virtual_devices",
                column: "WeChatWorkAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_wechat_work_accounts_EmployeeId",
                table: "wechat_work_accounts",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_wechat_work_accounts_TenantId_WeChatId",
                table: "wechat_work_accounts",
                columns: new[] { "TenantId", "WeChatId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_client_access_policies");

            migrationBuilder.DropTable(
                name: "rpa_action_logs");

            migrationBuilder.DropTable(
                name: "rpa_tasks");

            migrationBuilder.DropTable(
                name: "rpa_client_instances");

            migrationBuilder.DropTable(
                name: "virtual_devices");

            migrationBuilder.DropTable(
                name: "device_hosts");

            migrationBuilder.DropTable(
                name: "wechat_work_accounts");

            migrationBuilder.DropTable(
                name: "employees");
        }
    }
}
