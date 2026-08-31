using AIChat.Application.AccessControl;
using AIChat.Application.RpaTasks;
using AIChat.Domain.Entities;
using AIChat.Domain.Enums;
using AIChat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIChat.Api.Endpoints;

public static class M2Endpoints
{
    public static IEndpointRouteBuilder MapM2Endpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/health/db", async (AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? Results.Ok(new { status = "Healthy", database = "PostgreSQL", utcTime = DateTimeOffset.UtcNow })
                : Results.Problem("Cannot connect to PostgreSQL.");
        });

        MapEmployeeEndpoints(api);
        MapWeChatAccountEndpoints(api);
        MapDeviceHostEndpoints(api);
        MapVirtualDeviceEndpoints(api);
        MapAgentEndpoints(api);
        MapReadOnlyConsoleEndpoints(api);

        return app;
    }

    private static void MapEmployeeEndpoints(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/employees");

        group.MapGet("/", async (AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var employees = await db.Employees
                .AsNoTracking()
                .OrderBy(x => x.EmployeeNo)
                .Select(x => new EmployeeDto(
                    x.Id,
                    x.TenantId,
                    x.EmployeeNo,
                    x.Name,
                    x.Department,
                    x.PhoneNumber,
                    x.IsActive,
                    x.Notes,
                    x.CreatedAt,
                    x.UpdatedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(employees);
        });

        group.MapPost("/", async (UpsertEmployeeRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.EmployeeNo) || string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { message = "EmployeeNo and Name are required." });
            }

            var employee = new Employee
            {
                TenantId = request.TenantId ?? Domain.Common.TenantDefaults.DefaultTenantId,
                EmployeeNo = request.EmployeeNo.Trim(),
                Name = request.Name.Trim(),
                Department = request.Department?.Trim(),
                PhoneNumber = request.PhoneNumber?.Trim(),
                IsActive = request.IsActive ?? true,
                Notes = request.Notes?.Trim()
            };

            db.Employees.Add(employee);
            db.EmployeeClientAccessPolicies.Add(new EmployeeClientAccessPolicy
            {
                TenantId = employee.TenantId,
                EmployeeId = employee.Id,
                Status = ClientAccessStatus.Disabled
            });

            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/employees/{employee.Id}", ToEmployeeDto(employee));
        });

        group.MapPut("/{id:guid}", async (Guid id, UpsertEmployeeRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var employee = await db.Employees.FindAsync([id], cancellationToken);
            if (employee is null)
            {
                return Results.NotFound();
            }

            if (!string.IsNullOrWhiteSpace(request.EmployeeNo))
            {
                employee.EmployeeNo = request.EmployeeNo.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                employee.Name = request.Name.Trim();
            }

            employee.Department = request.Department?.Trim();
            employee.PhoneNumber = request.PhoneNumber?.Trim();
            employee.IsActive = request.IsActive ?? employee.IsActive;
            employee.Notes = request.Notes?.Trim();

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToEmployeeDto(employee));
        });

        group.MapGet("/{id:guid}/client-access", async (Guid id, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var employeeExists = await db.Employees.AnyAsync(x => x.Id == id, cancellationToken);
            if (!employeeExists)
            {
                return Results.NotFound();
            }

            var policy = await db.EmployeeClientAccessPolicies
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.EmployeeId == id, cancellationToken);

            return Results.Ok(ToAccessPolicyDto(id, policy));
        });

        group.MapPut("/{id:guid}/client-access", async (Guid id, UpdateAccessPolicyRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var employee = await db.Employees.FindAsync([id], cancellationToken);
            if (employee is null)
            {
                return Results.NotFound();
            }

            if (request.ValidFromUtc is not null &&
                request.ValidToUtc is not null &&
                request.ValidFromUtc.Value > request.ValidToUtc.Value)
            {
                return Results.BadRequest(new { message = "ValidFromUtc cannot be later than ValidToUtc." });
            }

            var policy = await db.EmployeeClientAccessPolicies
                .FirstOrDefaultAsync(x => x.EmployeeId == id, cancellationToken);

            if (policy is null)
            {
                policy = new EmployeeClientAccessPolicy
                {
                    TenantId = employee.TenantId,
                    EmployeeId = id
                };
                db.EmployeeClientAccessPolicies.Add(policy);
            }

            policy.Status = request.Status;
            policy.ValidFromUtc = request.ValidFromUtc;
            policy.ValidToUtc = request.ValidToUtc;
            policy.MaxDailyUsageMinutes = request.MaxDailyUsageMinutes;
            policy.MaxSessionMinutes = request.MaxSessionMinutes;
            policy.PauseReason = request.PauseReason?.Trim();

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToAccessPolicyDto(id, policy));
        });
    }

    private static void MapWeChatAccountEndpoints(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/wechat-accounts");

        group.MapGet("/", async (AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var accounts = await db.WeChatWorkAccounts
                .AsNoTracking()
                .OrderBy(x => x.DisplayName)
                .Select(x => new WeChatWorkAccountDto(
                    x.Id,
                    x.TenantId,
                    x.EmployeeId,
                    x.DisplayName,
                    x.WeChatId,
                    x.PhoneNumberMasked,
                    x.Status,
                    x.Notes,
                    x.CreatedAt,
                    x.UpdatedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(accounts);
        });

        group.MapPost("/", async (UpsertWeChatWorkAccountRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.WeChatId))
            {
                return Results.BadRequest(new { message = "DisplayName and WeChatId are required." });
            }

            var employee = await db.Employees.FindAsync([request.EmployeeId], cancellationToken);
            if (employee is null)
            {
                return Results.BadRequest(new { message = "Employee does not exist." });
            }

            var account = new WeChatWorkAccount
            {
                TenantId = request.TenantId ?? employee.TenantId,
                EmployeeId = request.EmployeeId,
                DisplayName = request.DisplayName.Trim(),
                WeChatId = request.WeChatId.Trim(),
                PhoneNumberMasked = request.PhoneNumberMasked?.Trim(),
                Status = request.Status ?? WeChatWorkAccountStatus.Active,
                Notes = request.Notes?.Trim()
            };

            db.WeChatWorkAccounts.Add(account);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/wechat-accounts/{account.Id}", ToWeChatWorkAccountDto(account));
        });

        group.MapPut("/{id:guid}", async (Guid id, UpsertWeChatWorkAccountRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var account = await db.WeChatWorkAccounts.FindAsync([id], cancellationToken);
            if (account is null)
            {
                return Results.NotFound();
            }

            var employeeExists = await db.Employees.AnyAsync(x => x.Id == request.EmployeeId, cancellationToken);
            if (!employeeExists)
            {
                return Results.BadRequest(new { message = "Employee does not exist." });
            }

            account.EmployeeId = request.EmployeeId;
            account.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? account.DisplayName : request.DisplayName.Trim();
            account.WeChatId = string.IsNullOrWhiteSpace(request.WeChatId) ? account.WeChatId : request.WeChatId.Trim();
            account.PhoneNumberMasked = request.PhoneNumberMasked?.Trim();
            account.Status = request.Status ?? account.Status;
            account.Notes = request.Notes?.Trim();

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToWeChatWorkAccountDto(account));
        });
    }

    private static void MapDeviceHostEndpoints(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/device-hosts");

        group.MapGet("/", async (AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var hosts = await db.DeviceHosts
                .AsNoTracking()
                .OrderBy(x => x.HostName)
                .Select(x => new DeviceHostDto(
                    x.Id,
                    x.TenantId,
                    x.HostName,
                    x.AssetCode,
                    x.IpAddress,
                    x.CpuCores,
                    x.MemoryGb,
                    x.Status,
                    x.Notes,
                    x.CreatedAt,
                    x.UpdatedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(hosts);
        });

        group.MapPost("/", async (UpsertDeviceHostRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.HostName))
            {
                return Results.BadRequest(new { message = "HostName is required." });
            }

            var host = new DeviceHost
            {
                TenantId = request.TenantId ?? Domain.Common.TenantDefaults.DefaultTenantId,
                HostName = request.HostName.Trim(),
                AssetCode = request.AssetCode?.Trim(),
                IpAddress = request.IpAddress?.Trim(),
                CpuCores = request.CpuCores,
                MemoryGb = request.MemoryGb,
                Status = request.Status ?? DeviceHostStatus.Active,
                Notes = request.Notes?.Trim()
            };

            db.DeviceHosts.Add(host);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/device-hosts/{host.Id}", ToDeviceHostDto(host));
        });

        group.MapPut("/{id:guid}", async (Guid id, UpsertDeviceHostRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var host = await db.DeviceHosts.FindAsync([id], cancellationToken);
            if (host is null)
            {
                return Results.NotFound();
            }

            host.HostName = string.IsNullOrWhiteSpace(request.HostName) ? host.HostName : request.HostName.Trim();
            host.AssetCode = request.AssetCode?.Trim();
            host.IpAddress = request.IpAddress?.Trim();
            host.CpuCores = request.CpuCores;
            host.MemoryGb = request.MemoryGb;
            host.Status = request.Status ?? host.Status;
            host.Notes = request.Notes?.Trim();

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToDeviceHostDto(host));
        });
    }

    private static void MapVirtualDeviceEndpoints(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/virtual-devices");

        group.MapGet("/", async (AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var devices = await db.VirtualDevices
                .AsNoTracking()
                .OrderBy(x => x.VmName)
                .Select(x => new VirtualDeviceDto(
                    x.Id,
                    x.TenantId,
                    x.DeviceHostId,
                    x.EmployeeId,
                    x.WeChatWorkAccountId,
                    x.VmName,
                    x.MachineCode,
                    x.IpAddress,
                    x.Status,
                    x.LastSeenAtUtc,
                    x.Notes,
                    x.CreatedAt,
                    x.UpdatedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(devices);
        });

        group.MapPost("/", async (UpsertVirtualDeviceRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.VmName))
            {
                return Results.BadRequest(new { message = "VmName is required." });
            }

            var host = await db.DeviceHosts.FindAsync([request.DeviceHostId], cancellationToken);
            if (host is null)
            {
                return Results.BadRequest(new { message = "DeviceHost does not exist." });
            }

            if (!await OptionalEmployeeExistsAsync(request.EmployeeId, db, cancellationToken) ||
                !await OptionalWeChatAccountExistsAsync(request.WeChatWorkAccountId, db, cancellationToken))
            {
                return Results.BadRequest(new { message = "Employee or WeChatWorkAccount does not exist." });
            }

            var device = new VirtualDevice
            {
                TenantId = request.TenantId ?? host.TenantId,
                DeviceHostId = request.DeviceHostId,
                EmployeeId = request.EmployeeId,
                WeChatWorkAccountId = request.WeChatWorkAccountId,
                VmName = request.VmName.Trim(),
                MachineCode = request.MachineCode?.Trim(),
                IpAddress = request.IpAddress?.Trim(),
                Status = request.Status ?? VirtualDeviceStatus.Active,
                Notes = request.Notes?.Trim()
            };

            db.VirtualDevices.Add(device);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/virtual-devices/{device.Id}", ToVirtualDeviceDto(device));
        });

        group.MapPut("/{id:guid}", async (Guid id, UpsertVirtualDeviceRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var device = await db.VirtualDevices.FindAsync([id], cancellationToken);
            if (device is null)
            {
                return Results.NotFound();
            }

            var hostExists = await db.DeviceHosts.AnyAsync(x => x.Id == request.DeviceHostId, cancellationToken);
            if (!hostExists)
            {
                return Results.BadRequest(new { message = "DeviceHost does not exist." });
            }

            if (!await OptionalEmployeeExistsAsync(request.EmployeeId, db, cancellationToken) ||
                !await OptionalWeChatAccountExistsAsync(request.WeChatWorkAccountId, db, cancellationToken))
            {
                return Results.BadRequest(new { message = "Employee or WeChatWorkAccount does not exist." });
            }

            device.DeviceHostId = request.DeviceHostId;
            device.EmployeeId = request.EmployeeId;
            device.WeChatWorkAccountId = request.WeChatWorkAccountId;
            device.VmName = string.IsNullOrWhiteSpace(request.VmName) ? device.VmName : request.VmName.Trim();
            device.MachineCode = request.MachineCode?.Trim();
            device.IpAddress = request.IpAddress?.Trim();
            device.Status = request.Status ?? device.Status;
            device.Notes = request.Notes?.Trim();

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToVirtualDeviceDto(device));
        });
    }

    private static void MapAgentEndpoints(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/agent");

        group.MapPost("/register", async (RegisterAgentRequest request, AIChatDbContext db, ClientAccessEvaluator evaluator, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.ClientInstanceKey))
            {
                return Results.BadRequest(new { message = "ClientInstanceKey is required." });
            }

            if (!await OptionalVirtualDeviceExistsAsync(request.VirtualDeviceId, db, cancellationToken) ||
                !await OptionalEmployeeExistsAsync(request.EmployeeId, db, cancellationToken) ||
                !await OptionalWeChatAccountExistsAsync(request.WeChatWorkAccountId, db, cancellationToken))
            {
                return Results.BadRequest(new { message = "VirtualDevice, Employee or WeChatWorkAccount does not exist." });
            }

            var now = DateTimeOffset.UtcNow;
            var client = await db.RpaClientInstances
                .Include(x => x.Employee)
                .ThenInclude(x => x!.ClientAccessPolicy)
                .Include(x => x.VirtualDevice)
                .FirstOrDefaultAsync(x => x.ClientInstanceKey == request.ClientInstanceKey.Trim(), cancellationToken);

            if (client is null)
            {
                client = new RpaClientInstance
                {
                    ClientInstanceKey = request.ClientInstanceKey.Trim(),
                    RegisteredAtUtc = now
                };
                db.RpaClientInstances.Add(client);
            }

            client.MachineName = request.MachineName?.Trim();
            client.ClientVersion = request.ClientVersion?.Trim();
            client.Status = RpaClientStatus.Online;
            client.LastHeartbeatAtUtc = now;

            await BindClientAsync(client, request.VirtualDeviceId, request.EmployeeId, request.WeChatWorkAccountId, db, cancellationToken);

            if (client.VirtualDeviceId is not null)
            {
                var device = await db.VirtualDevices.FindAsync([client.VirtualDeviceId.Value], cancellationToken);
                if (device is not null)
                {
                    device.LastSeenAtUtc = now;
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            client = await LoadClientAsync(client.Id, null, db, cancellationToken);
            var decision = evaluator.Evaluate(client?.Employee, client?.Employee?.ClientAccessPolicy, now, 0, client?.CurrentSessionStartedAtUtc);

            if (client is not null)
            {
                await UpdateClientAccessStateAsync(client, decision, db, cancellationToken);
            }

            return Results.Ok(ToAgentRegistrationResponse(client!, decision));
        });

        group.MapPost("/heartbeat", async (AgentHeartbeatRequest request, AIChatDbContext db, ClientAccessEvaluator evaluator, CancellationToken cancellationToken) =>
        {
            var client = await LoadClientAsync(request.ClientInstanceId, request.ClientInstanceKey, db, cancellationToken);
            if (client is null)
            {
                return Results.NotFound(new { message = "RPA client instance does not exist." });
            }

            var now = DateTimeOffset.UtcNow;
            client.LastHeartbeatAtUtc = now;
            client.MachineName = string.IsNullOrWhiteSpace(request.MachineName) ? client.MachineName : request.MachineName.Trim();
            client.ClientVersion = string.IsNullOrWhiteSpace(request.ClientVersion) ? client.ClientVersion : request.ClientVersion.Trim();
            client.Status = RpaClientStatus.Online;

            if (request.IsTaskRunning && client.CurrentSessionStartedAtUtc is null)
            {
                client.CurrentSessionStartedAtUtc = request.SessionStartedAtUtc ?? now;
            }
            else if (!request.IsTaskRunning)
            {
                client.CurrentSessionStartedAtUtc = null;
            }

            if (client.VirtualDevice is not null)
            {
                client.VirtualDevice.LastSeenAtUtc = now;
            }

            var decision = evaluator.Evaluate(client.Employee, client.Employee?.ClientAccessPolicy, now, 0, client.CurrentSessionStartedAtUtc);
            if (!decision.CanContinueRun)
            {
                client.CurrentSessionStartedAtUtc = null;
            }

            await UpdateClientAccessStateAsync(client, decision, db, cancellationToken);
            return Results.Ok(ToAgentAccessPolicyResponse(client, decision));
        });

        group.MapGet("/access-policy", async (Guid? clientInstanceId, string? clientInstanceKey, AIChatDbContext db, ClientAccessEvaluator evaluator, CancellationToken cancellationToken) =>
        {
            var client = await LoadClientAsync(clientInstanceId, clientInstanceKey, db, cancellationToken);
            if (client is null)
            {
                return Results.NotFound(new { message = "RPA client instance does not exist." });
            }

            var decision = evaluator.Evaluate(client.Employee, client.Employee?.ClientAccessPolicy, DateTimeOffset.UtcNow, 0, client.CurrentSessionStartedAtUtc);
            return Results.Ok(ToAgentAccessPolicyResponse(client, decision));
        });

        group.MapPost("/tasks", async (CreateRpaTaskRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var client = await db.RpaClientInstances.FindAsync([request.RpaClientInstanceId], cancellationToken);
            if (client is null)
            {
                return Results.BadRequest(new { message = "RPA client instance does not exist." });
            }

            var task = new RpaTask
            {
                TenantId = client.TenantId,
                RpaClientInstanceId = client.Id,
                EmployeeId = request.EmployeeId ?? client.EmployeeId,
                WeChatWorkAccountId = request.WeChatWorkAccountId ?? client.WeChatWorkAccountId,
                TaskType = request.TaskType,
                Status = RpaTaskStatus.Pending,
                Priority = request.Priority ?? 100,
                ConversationKey = request.ConversationKey?.Trim(),
                CustomerDisplayName = request.CustomerDisplayName?.Trim(),
                IncomingMessageText = request.IncomingMessageText,
                AiReplyText = request.AiReplyText,
                RiskResult = request.RiskResult?.Trim(),
                ScheduledAtUtc = request.ScheduledAtUtc
            };

            db.RpaTasks.Add(task);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/agent/tasks/{task.Id}", ToRpaTaskDto(task));
        });

        group.MapPut("/tasks/{id:guid}/status", async (Guid id, UpdateRpaTaskStatusRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var task = await db.RpaTasks.FindAsync([id], cancellationToken);
            if (task is null)
            {
                return Results.NotFound();
            }

            task.Status = request.Status;
            task.ErrorMessage = request.ErrorMessage;
            if (request.Status == RpaTaskStatus.Running && task.StartedAtUtc is null)
            {
                task.StartedAtUtc = DateTimeOffset.UtcNow;
            }

            if (IsTerminalStatus(request.Status) && task.FinishedAtUtc is null)
            {
                task.FinishedAtUtc = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToRpaTaskDto(task));
        });

        group.MapPut("/tasks/{id:guid}/result", async (Guid id, UpdateRpaTaskResultRequest request, AIChatDbContext db, RpaTaskResultUpdater updater, CancellationToken cancellationToken) =>
        {
            var task = await db.RpaTasks.FindAsync([id], cancellationToken);
            if (task is null)
            {
                return Results.NotFound();
            }

            updater.Apply(
                task,
                new RpaTaskResultUpdate(
                    request.ConversationKey,
                    request.CustomerDisplayName,
                    request.IncomingMessageText,
                    request.AiReplyText,
                    request.RiskResult));

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToRpaTaskDto(task));
        });

        group.MapPost("/tasks/{id:guid}/action-logs", async (Guid id, CreateRpaActionLogRequest request, AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var task = await db.RpaTasks.FindAsync([id], cancellationToken);
            if (task is null)
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(request.ActionName))
            {
                return Results.BadRequest(new { message = "ActionName is required." });
            }

            var log = new RpaActionLog
            {
                TenantId = task.TenantId,
                RpaTaskId = task.Id,
                RpaClientInstanceId = task.RpaClientInstanceId,
                Level = request.Level ?? RpaActionLogLevel.Info,
                ActionName = request.ActionName.Trim(),
                Message = request.Message,
                OcrText = request.OcrText,
                AiReplyText = request.AiReplyText,
                RiskResult = request.RiskResult,
                SanitizedScreenshotPath = request.SanitizedScreenshotPath,
                LoggedAtUtc = DateTimeOffset.UtcNow
            };

            db.RpaActionLogs.Add(log);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/agent/tasks/{task.Id}/action-logs/{log.Id}", ToRpaActionLogDto(log));
        });
    }

    private static void MapReadOnlyConsoleEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/rpa-client-instances", async (AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var clients = await db.RpaClientInstances
                .AsNoTracking()
                .OrderByDescending(x => x.LastHeartbeatAtUtc ?? x.RegisteredAtUtc)
                .Select(x => new RpaClientInstanceDto(
                    x.Id,
                    x.TenantId,
                    x.VirtualDeviceId,
                    x.EmployeeId,
                    x.WeChatWorkAccountId,
                    x.ClientInstanceKey,
                    x.ClientVersion,
                    x.MachineName,
                    x.Status,
                    x.RegisteredAtUtc,
                    x.LastHeartbeatAtUtc,
                    x.CurrentSessionStartedAtUtc,
                    x.LastCanContinueRun,
                    x.LastAccessStatus,
                    x.LastAccessReason))
                .ToListAsync(cancellationToken);

            return Results.Ok(clients);
        });

        api.MapGet("/rpa-tasks", async (AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var tasks = await db.RpaTasks
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Take(100)
                .Select(x => new RpaTaskDto(
                    x.Id,
                    x.TenantId,
                    x.RpaClientInstanceId,
                    x.EmployeeId,
                    x.WeChatWorkAccountId,
                    x.TaskType,
                    x.Status,
                    x.Priority,
                    x.ConversationKey,
                    x.CustomerDisplayName,
                    x.IncomingMessageText,
                    x.AiReplyText,
                    x.RiskResult,
                    x.ScheduledAtUtc,
                    x.StartedAtUtc,
                    x.FinishedAtUtc,
                    x.ErrorMessage,
                    x.CreatedAt,
                    x.UpdatedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(tasks);
        });

        api.MapGet("/rpa-action-logs", async (AIChatDbContext db, CancellationToken cancellationToken) =>
        {
            var logs = await db.RpaActionLogs
                .AsNoTracking()
                .OrderByDescending(x => x.LoggedAtUtc)
                .Take(100)
                .Select(x => new RpaActionLogDto(
                    x.Id,
                    x.TenantId,
                    x.RpaTaskId,
                    x.RpaClientInstanceId,
                    x.Level,
                    x.ActionName,
                    x.Message,
                    x.OcrText,
                    x.AiReplyText,
                    x.RiskResult,
                    x.SanitizedScreenshotPath,
                    x.LoggedAtUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(logs);
        });
    }

    private static async Task BindClientAsync(
        RpaClientInstance client,
        Guid? virtualDeviceId,
        Guid? employeeId,
        Guid? weChatWorkAccountId,
        AIChatDbContext db,
        CancellationToken cancellationToken)
    {
        if (virtualDeviceId is not null)
        {
            var device = await db.VirtualDevices
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == virtualDeviceId.Value, cancellationToken);

            if (device is not null)
            {
                client.VirtualDeviceId = device.Id;
                client.TenantId = device.TenantId;
                client.EmployeeId = employeeId ?? device.EmployeeId ?? client.EmployeeId;
                client.WeChatWorkAccountId = weChatWorkAccountId ?? device.WeChatWorkAccountId ?? client.WeChatWorkAccountId;
            }
        }

        if (employeeId is not null)
        {
            client.EmployeeId = employeeId;
        }

        if (weChatWorkAccountId is not null)
        {
            client.WeChatWorkAccountId = weChatWorkAccountId;
        }
    }

    private static async Task<RpaClientInstance?> LoadClientAsync(Guid? id, string? clientInstanceKey, AIChatDbContext db, CancellationToken cancellationToken)
    {
        if (id is null && string.IsNullOrWhiteSpace(clientInstanceKey))
        {
            return null;
        }

        var query = db.RpaClientInstances
            .Include(x => x.Employee)
            .ThenInclude(x => x!.ClientAccessPolicy)
            .Include(x => x.VirtualDevice)
            .AsQueryable();

        if (id is not null)
        {
            return await query.FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken);
        }

        var key = clientInstanceKey!.Trim();
        return await query.FirstOrDefaultAsync(x => x.ClientInstanceKey == key, cancellationToken);
    }

    private static async Task UpdateClientAccessStateAsync(
        RpaClientInstance client,
        ClientAccessDecision decision,
        AIChatDbContext db,
        CancellationToken cancellationToken)
    {
        client.LastCanContinueRun = decision.CanContinueRun;
        client.LastAccessStatus = decision.Status;
        client.LastAccessReason = decision.Reason;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<bool> OptionalEmployeeExistsAsync(Guid? employeeId, AIChatDbContext db, CancellationToken cancellationToken)
    {
        return employeeId is null || await db.Employees.AnyAsync(x => x.Id == employeeId.Value, cancellationToken);
    }

    private static async Task<bool> OptionalVirtualDeviceExistsAsync(Guid? virtualDeviceId, AIChatDbContext db, CancellationToken cancellationToken)
    {
        return virtualDeviceId is null || await db.VirtualDevices.AnyAsync(x => x.Id == virtualDeviceId.Value, cancellationToken);
    }

    private static async Task<bool> OptionalWeChatAccountExistsAsync(Guid? accountId, AIChatDbContext db, CancellationToken cancellationToken)
    {
        return accountId is null || await db.WeChatWorkAccounts.AnyAsync(x => x.Id == accountId.Value, cancellationToken);
    }

    private static bool IsTerminalStatus(RpaTaskStatus status)
    {
        return status is RpaTaskStatus.Succeeded or RpaTaskStatus.Failed or RpaTaskStatus.Cancelled or RpaTaskStatus.Skipped;
    }

    private static EmployeeDto ToEmployeeDto(Employee employee)
    {
        return new EmployeeDto(
            employee.Id,
            employee.TenantId,
            employee.EmployeeNo,
            employee.Name,
            employee.Department,
            employee.PhoneNumber,
            employee.IsActive,
            employee.Notes,
            employee.CreatedAt,
            employee.UpdatedAt);
    }

    private static EmployeeClientAccessPolicyDto ToAccessPolicyDto(Guid employeeId, EmployeeClientAccessPolicy? policy)
    {
        return new EmployeeClientAccessPolicyDto(
            policy?.Id,
            policy?.TenantId ?? Domain.Common.TenantDefaults.DefaultTenantId,
            employeeId,
            policy?.Status ?? ClientAccessStatus.Disabled,
            policy?.ValidFromUtc,
            policy?.ValidToUtc,
            policy?.MaxDailyUsageMinutes,
            policy?.MaxSessionMinutes,
            policy?.PauseReason,
            policy?.CreatedAt,
            policy?.UpdatedAt);
    }

    private static WeChatWorkAccountDto ToWeChatWorkAccountDto(WeChatWorkAccount account)
    {
        return new WeChatWorkAccountDto(
            account.Id,
            account.TenantId,
            account.EmployeeId,
            account.DisplayName,
            account.WeChatId,
            account.PhoneNumberMasked,
            account.Status,
            account.Notes,
            account.CreatedAt,
            account.UpdatedAt);
    }

    private static DeviceHostDto ToDeviceHostDto(DeviceHost host)
    {
        return new DeviceHostDto(
            host.Id,
            host.TenantId,
            host.HostName,
            host.AssetCode,
            host.IpAddress,
            host.CpuCores,
            host.MemoryGb,
            host.Status,
            host.Notes,
            host.CreatedAt,
            host.UpdatedAt);
    }

    private static VirtualDeviceDto ToVirtualDeviceDto(VirtualDevice device)
    {
        return new VirtualDeviceDto(
            device.Id,
            device.TenantId,
            device.DeviceHostId,
            device.EmployeeId,
            device.WeChatWorkAccountId,
            device.VmName,
            device.MachineCode,
            device.IpAddress,
            device.Status,
            device.LastSeenAtUtc,
            device.Notes,
            device.CreatedAt,
            device.UpdatedAt);
    }

    private static RpaTaskDto ToRpaTaskDto(RpaTask task)
    {
        return new RpaTaskDto(
            task.Id,
            task.TenantId,
            task.RpaClientInstanceId,
            task.EmployeeId,
            task.WeChatWorkAccountId,
            task.TaskType,
            task.Status,
            task.Priority,
            task.ConversationKey,
            task.CustomerDisplayName,
            task.IncomingMessageText,
            task.AiReplyText,
            task.RiskResult,
            task.ScheduledAtUtc,
            task.StartedAtUtc,
            task.FinishedAtUtc,
            task.ErrorMessage,
            task.CreatedAt,
            task.UpdatedAt);
    }

    private static RpaActionLogDto ToRpaActionLogDto(RpaActionLog log)
    {
        return new RpaActionLogDto(
            log.Id,
            log.TenantId,
            log.RpaTaskId,
            log.RpaClientInstanceId,
            log.Level,
            log.ActionName,
            log.Message,
            log.OcrText,
            log.AiReplyText,
            log.RiskResult,
            log.SanitizedScreenshotPath,
            log.LoggedAtUtc);
    }

    private static AgentRegistrationResponse ToAgentRegistrationResponse(RpaClientInstance client, ClientAccessDecision decision)
    {
        return new AgentRegistrationResponse(
            client.Id,
            client.ClientInstanceKey,
            client.VirtualDeviceId,
            client.VirtualDevice?.VmName,
            client.EmployeeId,
            client.Employee?.Name,
            client.WeChatWorkAccountId,
            ToAgentAccessPolicyResponse(client, decision));
    }

    private static AgentAccessPolicyResponse ToAgentAccessPolicyResponse(RpaClientInstance client, ClientAccessDecision decision)
    {
        return new AgentAccessPolicyResponse(
            client.Id,
            client.ClientInstanceKey,
            decision.CanStartTask,
            decision.CanContinueRun,
            decision.Status,
            decision.Reason,
            decision.ValidFromUtc,
            decision.ValidToUtc,
            decision.MaxDailyUsageMinutes,
            decision.MaxSessionMinutes,
            decision.UsedDailyMinutes,
            decision.CurrentSessionMinutes,
            client.LastHeartbeatAtUtc);
    }
}

public sealed record UpsertEmployeeRequest(
    Guid? TenantId,
    string? EmployeeNo,
    string? Name,
    string? Department,
    string? PhoneNumber,
    bool? IsActive,
    string? Notes);

public sealed record EmployeeDto(
    Guid Id,
    Guid TenantId,
    string EmployeeNo,
    string Name,
    string? Department,
    string? PhoneNumber,
    bool IsActive,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record UpdateAccessPolicyRequest(
    ClientAccessStatus Status,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    int? MaxDailyUsageMinutes,
    int? MaxSessionMinutes,
    string? PauseReason);

public sealed record EmployeeClientAccessPolicyDto(
    Guid? Id,
    Guid TenantId,
    Guid EmployeeId,
    ClientAccessStatus Status,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    int? MaxDailyUsageMinutes,
    int? MaxSessionMinutes,
    string? PauseReason,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record UpsertWeChatWorkAccountRequest(
    Guid? TenantId,
    Guid EmployeeId,
    string? DisplayName,
    string? WeChatId,
    string? PhoneNumberMasked,
    WeChatWorkAccountStatus? Status,
    string? Notes);

public sealed record WeChatWorkAccountDto(
    Guid Id,
    Guid TenantId,
    Guid EmployeeId,
    string DisplayName,
    string WeChatId,
    string? PhoneNumberMasked,
    WeChatWorkAccountStatus Status,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record UpsertDeviceHostRequest(
    Guid? TenantId,
    string? HostName,
    string? AssetCode,
    string? IpAddress,
    int? CpuCores,
    int? MemoryGb,
    DeviceHostStatus? Status,
    string? Notes);

public sealed record DeviceHostDto(
    Guid Id,
    Guid TenantId,
    string HostName,
    string? AssetCode,
    string? IpAddress,
    int? CpuCores,
    int? MemoryGb,
    DeviceHostStatus Status,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record UpsertVirtualDeviceRequest(
    Guid? TenantId,
    Guid DeviceHostId,
    Guid? EmployeeId,
    Guid? WeChatWorkAccountId,
    string? VmName,
    string? MachineCode,
    string? IpAddress,
    VirtualDeviceStatus? Status,
    string? Notes);

public sealed record VirtualDeviceDto(
    Guid Id,
    Guid TenantId,
    Guid DeviceHostId,
    Guid? EmployeeId,
    Guid? WeChatWorkAccountId,
    string VmName,
    string? MachineCode,
    string? IpAddress,
    VirtualDeviceStatus Status,
    DateTimeOffset? LastSeenAtUtc,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record RegisterAgentRequest(
    string? ClientInstanceKey,
    Guid? VirtualDeviceId,
    Guid? EmployeeId,
    Guid? WeChatWorkAccountId,
    string? ClientVersion,
    string? MachineName);

public sealed record AgentHeartbeatRequest(
    Guid? ClientInstanceId,
    string? ClientInstanceKey,
    bool IsTaskRunning,
    DateTimeOffset? SessionStartedAtUtc,
    string? ClientVersion,
    string? MachineName);

public sealed record AgentRegistrationResponse(
    Guid ClientInstanceId,
    string ClientInstanceKey,
    Guid? VirtualDeviceId,
    string? VirtualDeviceName,
    Guid? EmployeeId,
    string? EmployeeName,
    Guid? WeChatWorkAccountId,
    AgentAccessPolicyResponse AccessPolicy);

public sealed record AgentAccessPolicyResponse(
    Guid ClientInstanceId,
    string ClientInstanceKey,
    bool CanStartTask,
    bool CanContinueRun,
    string Status,
    string Reason,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    int? MaxDailyUsageMinutes,
    int? MaxSessionMinutes,
    int UsedDailyMinutes,
    int? CurrentSessionMinutes,
    DateTimeOffset? LastHeartbeatAtUtc);

public sealed record CreateRpaTaskRequest(
    Guid RpaClientInstanceId,
    Guid? EmployeeId,
    Guid? WeChatWorkAccountId,
    RpaTaskType TaskType,
    int? Priority,
    string? ConversationKey,
    string? CustomerDisplayName,
    string? IncomingMessageText,
    string? AiReplyText,
    string? RiskResult,
    DateTimeOffset? ScheduledAtUtc);

public sealed record UpdateRpaTaskStatusRequest(
    RpaTaskStatus Status,
    string? ErrorMessage);

public sealed record UpdateRpaTaskResultRequest(
    string? ConversationKey,
    string? CustomerDisplayName,
    string? IncomingMessageText,
    string? AiReplyText,
    string? RiskResult);

public sealed record CreateRpaActionLogRequest(
    RpaActionLogLevel? Level,
    string? ActionName,
    string? Message,
    string? OcrText,
    string? AiReplyText,
    string? RiskResult,
    string? SanitizedScreenshotPath);

public sealed record RpaClientInstanceDto(
    Guid Id,
    Guid TenantId,
    Guid? VirtualDeviceId,
    Guid? EmployeeId,
    Guid? WeChatWorkAccountId,
    string ClientInstanceKey,
    string? ClientVersion,
    string? MachineName,
    RpaClientStatus Status,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? CurrentSessionStartedAtUtc,
    bool LastCanContinueRun,
    string? LastAccessStatus,
    string? LastAccessReason);

public sealed record RpaTaskDto(
    Guid Id,
    Guid TenantId,
    Guid RpaClientInstanceId,
    Guid? EmployeeId,
    Guid? WeChatWorkAccountId,
    RpaTaskType TaskType,
    RpaTaskStatus Status,
    int Priority,
    string? ConversationKey,
    string? CustomerDisplayName,
    string? IncomingMessageText,
    string? AiReplyText,
    string? RiskResult,
    DateTimeOffset? ScheduledAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record RpaActionLogDto(
    Guid Id,
    Guid TenantId,
    Guid? RpaTaskId,
    Guid RpaClientInstanceId,
    RpaActionLogLevel Level,
    string ActionName,
    string? Message,
    string? OcrText,
    string? AiReplyText,
    string? RiskResult,
    string? SanitizedScreenshotPath,
    DateTimeOffset LoggedAtUtc);
