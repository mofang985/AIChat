using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using AIChat.RpaClient.Automation;
using AIChat.RpaClient.Backend;
using AIChat.RpaClient.Configuration;
using Microsoft.Extensions.Configuration;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace AIChat.RpaClient;

public partial class MainWindow : Window
{
    private readonly HttpClient _httpClient = new();
    private readonly HttpClient _visionHttpClient = new();
    private readonly RpaBackendClient _backendClient;
    private readonly DispatcherTimer _heartbeatTimer;
    private readonly WeChatWindowLocator _windowLocator = new();
    private readonly ScreenCaptureService _screenCaptureService = new();
    private readonly WeChatLayoutDetector _layoutDetector;
    private readonly YoloOnnxVisionDetector _yoloVisionDetector;
    private readonly PaddleOcrEngine _ocrEngine = new();
    private readonly VisionOcrReviewer _visionOcrReviewer;
    private readonly MouseKeyboardExecutor _inputExecutor = new();
    private readonly UnreadConversationQueueScanner _unreadQueueScanner;
    private readonly UnreadConversationControlledSwitcher _unreadConversationSwitcher;
    private Guid? _clientInstanceId;
    private DateTimeOffset? _sessionStartedAtUtc;
    private CancellationTokenSource? _taskCancellation;
    private RpaClientOptions _options = new();
    private bool _canStartTask;
    private bool _isTaskRunning;
    private bool _isHeartbeatInProgress;

    public MainWindow()
    {
        InitializeComponent();

        _layoutDetector = new WeChatLayoutDetector(_screenCaptureService);
        _yoloVisionDetector = new YoloOnnxVisionDetector(_screenCaptureService);
        _visionOcrReviewer = new VisionOcrReviewer(_visionHttpClient);
        _unreadQueueScanner = new UnreadConversationQueueScanner(_screenCaptureService, _ocrEngine);
        _unreadConversationSwitcher = new UnreadConversationControlledSwitcher(_inputExecutor, _screenCaptureService, _ocrEngine, _windowLocator);
        _backendClient = new RpaBackendClient(_httpClient);
        _heartbeatTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _heartbeatTimer.Tick += async (_, _) => await SendHeartbeatAsync();

        LoadInitialConfiguration();
        UpdateStartTaskState(false, "等待注册和授权。");
        AppendLog("客户端已启动，等待注册。");
    }

    private async void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        RegisterButton.IsEnabled = false;

        try
        {
            if (!TryConfigureHttpClient())
            {
                return;
            }

            var request = new RegisterAgentRequest(
                ClientInstanceKey: ClientKeyTextBox.Text.Trim(),
                VirtualDeviceId: ParseGuidOrNull(VirtualDeviceIdTextBox.Text),
                EmployeeId: ParseGuidOrNull(EmployeeIdTextBox.Text),
                WeChatWorkAccountId: ParseGuidOrNull(WeChatAccountIdTextBox.Text),
                ClientVersion: _options.ClientVersion,
                MachineName: Environment.MachineName);

            var result = await _backendClient.RegisterAsync(request, CancellationToken.None);

            _clientInstanceId = result.ClientInstanceId;
            ClientInstanceIdTextBlock.Text = result.ClientInstanceId.ToString();
            EmployeeTextBlock.Text = string.IsNullOrWhiteSpace(result.EmployeeName)
                ? result.EmployeeId?.ToString() ?? "未绑定"
                : $"{result.EmployeeName} ({result.EmployeeId})";
            VmTextBlock.Text = string.IsNullOrWhiteSpace(result.VirtualDeviceName)
                ? result.VirtualDeviceId?.ToString() ?? "未绑定"
                : $"{result.VirtualDeviceName} ({result.VirtualDeviceId})";

            ApplyAccessPolicy(result.AccessPolicy);
            AppendLog("客户端注册成功。");
        }
        catch (Exception ex)
        {
            AppendLog($"注册异常：{ex.Message}");
        }
        finally
        {
            RegisterButton.IsEnabled = true;
        }
    }

    private async void HeartbeatButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryConfigureHttpClient())
        {
            return;
        }

        if (!_heartbeatTimer.IsEnabled)
        {
            _heartbeatTimer.Start();
            HeartbeatButton.Content = "停止心跳";
            AppendLog("心跳已开启。");
            await SendHeartbeatAsync();
            return;
        }

        _heartbeatTimer.Stop();
        HeartbeatButton.Content = "开启心跳";
        AppendLog("心跳已停止。");
    }

    private async void StartTaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_canStartTask)
        {
            AppendLog("授权不可用，不能开始任务。");
            return;
        }

        if (_clientInstanceId is null)
        {
            AppendLog("请先注册客户端。");
            return;
        }

        if (_isTaskRunning)
        {
            AppendLog("已有任务正在运行。");
            return;
        }

        if (!TryConfigureHttpClient())
        {
            return;
        }

        _isTaskRunning = true;
        _sessionStartedAtUtc = DateTimeOffset.UtcNow;
        _taskCancellation = new CancellationTokenSource();
        UpdateStartTaskState(false, "任务运行中。");
        ResetM4Status();

        try
        {
            await SendHeartbeatAsync();
            var runner = new SingleConversationTaskRunner(
                _backendClient,
                _windowLocator,
                _screenCaptureService,
                _layoutDetector,
                _yoloVisionDetector,
                _ocrEngine,
                _visionOcrReviewer,
                _inputExecutor,
                _options.Automation,
                CreateRunnerCallbacks());

            await runner.RunAsync(
                _clientInstanceId.Value,
                ParseGuidOrNull(EmployeeIdTextBox.Text),
                ParseGuidOrNull(WeChatAccountIdTextBox.Text),
                _taskCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            CurrentTaskStatusTextBlock.Text = "任务已取消";
            LastErrorTextBlock.Text = "员工已暂停或紧急停止。";
            AppendLog("任务已取消。");
        }
        catch (Exception ex)
        {
            CurrentTaskStatusTextBlock.Text = "任务异常停止";
            LastErrorTextBlock.Text = ex.Message;
            AppendLog($"任务执行异常：{ex.Message}");
        }
        finally
        {
            _isTaskRunning = false;
            _sessionStartedAtUtc = null;
            _taskCancellation?.Dispose();
            _taskCancellation = null;
            UpdateStartTaskState(_canStartTask, _canStartTask ? "可以开始任务。" : "授权不可用。");
            await SendHeartbeatAsync();
        }
    }

    private async void StartContinuousButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_options.Automation.EnableContinuousReply)
        {
            AppendLog("连续监听未启用，请先在 appsettings.json 中设置 EnableContinuousReply=true。");
            return;
        }

        if (!_canStartTask)
        {
            AppendLog("授权不可用，不能开始连续监听。");
            return;
        }

        if (_clientInstanceId is null)
        {
            AppendLog("请先注册客户端。");
            return;
        }

        if (_isTaskRunning)
        {
            AppendLog("已有任务正在运行。");
            return;
        }

        if (!TryConfigureHttpClient())
        {
            return;
        }

        _isTaskRunning = true;
        _sessionStartedAtUtc = DateTimeOffset.UtcNow;
        _taskCancellation = new CancellationTokenSource();
        UpdateStartTaskState(false, "连续监听运行中。");
        ResetM4Status();
        ContinuousStatusTextBlock.Text = "准备启动";

        try
        {
            await SendHeartbeatAsync();
            var runner = new ContinuousConversationTaskRunner(
                _backendClient,
                _windowLocator,
                _screenCaptureService,
                _layoutDetector,
                _yoloVisionDetector,
                _ocrEngine,
                _visionOcrReviewer,
                _inputExecutor,
                _options.Automation,
                CreateRunnerCallbacks());

            await runner.RunAsync(
                _clientInstanceId.Value,
                ParseGuidOrNull(EmployeeIdTextBox.Text),
                ParseGuidOrNull(WeChatAccountIdTextBox.Text),
                _taskCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            CurrentTaskStatusTextBlock.Text = "连续监听已停止";
            ContinuousStatusTextBlock.Text = "员工已暂停或紧急停止。";
            LastErrorTextBlock.Text = "员工已暂停或紧急停止。";
            AppendLog("连续监听已取消。");
        }
        catch (Exception ex)
        {
            CurrentTaskStatusTextBlock.Text = "连续监听异常停止";
            ContinuousStatusTextBlock.Text = ex.Message;
            LastErrorTextBlock.Text = ex.Message;
            AppendLog($"连续监听异常：{ex.Message}");
        }
        finally
        {
            _isTaskRunning = false;
            _sessionStartedAtUtc = null;
            _taskCancellation?.Dispose();
            _taskCancellation = null;
            UpdateStartTaskState(_canStartTask, _canStartTask ? "可以开始任务。" : "授权不可用。");
            await SendHeartbeatAsync();
        }
    }

    private async void SwitchFirstUnreadConversationButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_options.Automation.EnableUnreadQueueControlledSwitch)
        {
            AppendLog("未读队列受控切换未启用，请先在 appsettings.json 中设置 EnableUnreadQueueControlledSwitch=true。");
            return;
        }

        if (!_options.Automation.EnableUnreadQueueReadOnlyScan)
        {
            AppendLog("未读队列扫描未启用，不能执行受控切换。");
            return;
        }

        if (!_canStartTask)
        {
            AppendLog("授权不可用，不能执行受控切换。");
            return;
        }

        if (_isTaskRunning)
        {
            AppendLog("已有任务正在运行，不能同时执行受控切换。");
            return;
        }

        _isTaskRunning = true;
        _sessionStartedAtUtc = DateTimeOffset.UtcNow;
        _taskCancellation = new CancellationTokenSource();
        UpdateStartTaskState(false, "未读队列受控切换中。");
        CurrentTaskStatusTextBlock.Text = "未读队列受控切换中";
        LastErrorTextBlock.Text = "无";

        try
        {
            await SendHeartbeatAsync();
            var switchStopwatch = Stopwatch.StartNew();
            var (window, layout, snapshot) = await CaptureUnreadQueueSnapshotAsync(_taskCancellation.Token);
            ApplyUnreadQueueSnapshot(snapshot);
            AppendLog($"受控切换前复核完成：{snapshot.Summary}");
            if (!string.IsNullOrWhiteSpace(snapshot.DebugCapturePath))
            {
                AppendLog($"未读队列调试截图：{snapshot.DebugCapturePath}");
            }

            var target = UnreadConversationSwitchPlanner.FindFirstSwitchableCandidate(snapshot);
            if (target is null)
            {
                CurrentTaskStatusTextBlock.Text = "未找到可切换候选";
                AppendLog("受控切换已停止：没有通过 M5.3 稳定性预演的可切换候选。");
                return;
            }

            CurrentTaskStatusTextBlock.Text = "正在受控切换未读会话";
            var result = await _unreadConversationSwitcher.SwitchAsync(
                window,
                layout,
                target,
                _options.Automation,
                _taskCancellation.Token);
            CurrentTaskStatusTextBlock.Text = result.IsSuccess ? "受控切换完成" : result.Status;
            LastErrorTextBlock.Text = result.IsSuccess ? "无" : result.Reason;
            AppendLog($"受控切换结果：{result.ToLogMessage()} 耗时 {switchStopwatch.ElapsedMilliseconds} ms。");
        }
        catch (OperationCanceledException)
        {
            CurrentTaskStatusTextBlock.Text = "受控切换已取消";
            LastErrorTextBlock.Text = "员工已暂停或紧急停止。";
            AppendLog("未读队列受控切换已取消。");
        }
        catch (Exception ex)
        {
            CurrentTaskStatusTextBlock.Text = "受控切换异常停止";
            LastErrorTextBlock.Text = ex.Message;
            AppendLog($"未读队列受控切换异常：{ex.Message}");
        }
        finally
        {
            _isTaskRunning = false;
            _sessionStartedAtUtc = null;
            _taskCancellation?.Dispose();
            _taskCancellation = null;
            UpdateStartTaskState(_canStartTask, _canStartTask ? "可以开始任务。" : "授权不可用。");
            await SendHeartbeatAsync();
        }
    }

    private async void ScanUnreadQueueButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_options.Automation.EnableUnreadQueueReadOnlyScan)
        {
            AppendLog("未读队列只读扫描未启用，请先在 appsettings.json 中设置 EnableUnreadQueueReadOnlyScan=true。");
            return;
        }

        if (!_canStartTask)
        {
            AppendLog("授权不可用，不能扫描未读队列。");
            return;
        }

        if (_isTaskRunning)
        {
            AppendLog("已有任务正在运行，不能同时扫描未读队列。");
            return;
        }

        _isTaskRunning = true;
        _sessionStartedAtUtc = DateTimeOffset.UtcNow;
        _taskCancellation = new CancellationTokenSource();
        UpdateStartTaskState(false, "未读队列只读扫描中。");
        SetUnreadQueueStatus("扫描中", "正在扫描", "0");
        CurrentTaskStatusTextBlock.Text = "未读队列只读扫描中";
        LastErrorTextBlock.Text = "无";

        try
        {
            await SendHeartbeatAsync();
            await ScanUnreadQueueOnceAsync(_taskCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            CurrentTaskStatusTextBlock.Text = "未读队列扫描已取消";
            LastErrorTextBlock.Text = "员工已暂停或紧急停止。";
            AppendLog("未读队列只读扫描已取消。");
        }
        catch (Exception ex)
        {
            CurrentTaskStatusTextBlock.Text = "未读队列扫描异常停止";
            LastErrorTextBlock.Text = ex.Message;
            ApplyUnreadQueueSnapshot(UnreadConversationQueueSnapshot.Empty(DateTimeOffset.UtcNow, System.Drawing.Rectangle.Empty, ex.Message));
            AppendLog($"未读队列只读扫描异常：{ex.Message}");
        }
        finally
        {
            _isTaskRunning = false;
            _sessionStartedAtUtc = null;
            _taskCancellation?.Dispose();
            _taskCancellation = null;
            UpdateStartTaskState(_canStartTask, _canStartTask ? "可以开始任务。" : "授权不可用。");
            await SendHeartbeatAsync();
        }
    }

    private async Task ScanUnreadQueueOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scanStopwatch = Stopwatch.StartNew();
        var (_, _, snapshot) = await CaptureUnreadQueueSnapshotAsync(cancellationToken);
        ApplyUnreadQueueSnapshot(snapshot);
        CurrentTaskStatusTextBlock.Text = "未读队列只读扫描完成";
        AppendLog($"未读队列只读扫描完成：{snapshot.Summary} 耗时 {scanStopwatch.ElapsedMilliseconds} ms。");
        if (!string.IsNullOrWhiteSpace(snapshot.DebugCapturePath))
        {
            AppendLog($"未读队列调试截图：{snapshot.DebugCapturePath}");
        }
    }

    private async Task<(WeChatWindow Window, WeChatLayoutResult Layout, UnreadConversationQueueSnapshot Snapshot)> CaptureUnreadQueueSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CurrentTaskStatusTextBlock.Text = "正在定位微信窗口";
        UnreadQueueStatusTextBlock.Text = "正在定位微信窗口";

        var window = _windowLocator.FindByTitleKeyword(_options.Automation.WeChatWindowTitleKeyword)
            ?? throw new InvalidOperationException($"未找到标题包含“{_options.Automation.WeChatWindowTitleKeyword}”的微信窗口。");
        WindowTargetTextBlock.Text = FormatWindowTarget(window);

        CurrentTaskStatusTextBlock.Text = "正在检测微信布局";
        UnreadQueueStatusTextBlock.Text = "正在检测微信布局";
        var layout = await _layoutDetector.DetectAsync(
            window,
            _options.Automation,
            Guid.NewGuid(),
            cancellationToken,
            saveDebugCapture: _options.Automation.EnableLayoutDebugCaptures);
        LayoutStatusTextBlock.Text = layout.ToLogMessage();
        if (!layout.IsUsable)
        {
            throw new InvalidOperationException($"微信布局定位失败：{layout.Reason}");
        }

        if (layout.ConversationListRegion.IsEmpty || layout.ConversationListRegion.Width <= 0 || layout.ConversationListRegion.Height <= 0)
        {
            var empty = UnreadConversationQueueSnapshot.Empty(
                DateTimeOffset.UtcNow,
                layout.ConversationListRegion,
                "当前布局没有可用会话列表区域，未读队列扫描已跳过。");
            return (window, layout, empty);
        }

        CurrentTaskStatusTextBlock.Text = "正在扫描可见未读会话";
        UnreadQueueStatusTextBlock.Text = "正在扫描可见未读会话";
        var snapshot = await _unreadQueueScanner.ScanAsync(
            layout,
            _options.Automation,
            Guid.NewGuid(),
            cancellationToken);
        return (window, layout, snapshot);
    }

    private async void PauseTaskButton_Click(object sender, RoutedEventArgs e)
    {
        _taskCancellation?.Cancel();
        _isTaskRunning = false;
        _sessionStartedAtUtc = null;
        AppendLog("任务已暂停。");
        await SendHeartbeatAsync();
    }

    private async void EmergencyStopButton_Click(object sender, RoutedEventArgs e)
    {
        _taskCancellation?.Cancel();
        _isTaskRunning = false;
        _sessionStartedAtUtc = null;
        _heartbeatTimer.Stop();
        HeartbeatButton.Content = "开启心跳";
        AppendLog("已执行紧急停止，心跳计时器已关闭。");
        await SendHeartbeatAsync();
    }

    private async Task SendHeartbeatAsync()
    {
        if (_isHeartbeatInProgress)
        {
            return;
        }

        if (!TryConfigureHttpClient())
        {
            return;
        }

        if (_clientInstanceId is null && string.IsNullOrWhiteSpace(ClientKeyTextBox.Text))
        {
            AppendLog("心跳失败：缺少客户端实例 ID 或 Key。");
            return;
        }

        _isHeartbeatInProgress = true;
        try
        {
            var request = new AgentHeartbeatRequest(
                ClientInstanceId: _clientInstanceId,
                ClientInstanceKey: ClientKeyTextBox.Text.Trim(),
                IsTaskRunning: _isTaskRunning,
                SessionStartedAtUtc: _sessionStartedAtUtc,
                ClientVersion: _options.ClientVersion,
                MachineName: Environment.MachineName);

            var result = await _backendClient.SendHeartbeatAsync(request, CancellationToken.None);

            _clientInstanceId = result.ClientInstanceId;
            ClientInstanceIdTextBlock.Text = result.ClientInstanceId.ToString();
            ApplyAccessPolicy(result);
            HeartbeatStatusTextBlock.Text = result.LastHeartbeatAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "已上报";
            AppendLog($"心跳成功：{result.Status}，{result.Reason}");

            if (!result.CanContinueRun)
            {
                _taskCancellation?.Cancel();
                _isTaskRunning = false;
                _sessionStartedAtUtc = null;
            }
        }
        catch (Exception ex)
        {
            HeartbeatStatusTextBlock.Text = "异常";
            AppendLog($"心跳异常：{ex.Message}");
        }
        finally
        {
            _isHeartbeatInProgress = false;
        }
    }

    private void LoadInitialConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables("AICHAT_RPA_")
            .Build();

        _options = configuration.Get<RpaClientOptions>() ?? new RpaClientOptions();
        if (string.IsNullOrWhiteSpace(_options.ClientInstanceKey))
        {
            _options.ClientInstanceKey = CreateDefaultClientKey();
        }

        ApiBaseUrlTextBox.Text = _options.ApiBaseUrl;
        ClientKeyTextBox.Text = _options.ClientInstanceKey;
        EmployeeIdTextBox.Text = _options.EmployeeId?.ToString() ?? string.Empty;
        VirtualDeviceIdTextBox.Text = _options.VirtualDeviceId?.ToString() ?? string.Empty;
        WeChatAccountIdTextBox.Text = _options.WeChatWorkAccountId?.ToString() ?? string.Empty;
        _backendClient.ConfigureBaseUrl(_options.ApiBaseUrl);
        ApplySendModeDisplay();
    }

    private void ApplySendModeDisplay()
    {
        var sendMode = _options.Automation.SendMode;
        var inputOnlyAction = _options.Automation.InputOnlyAfterVerifyAction;
        SendModeBadge.Text = sendMode switch
        {
            RpaSendMode.DryRun => "DryRun：不输入不发送",
            RpaSendMode.InputOnly => $"InputOnly：{inputOnlyAction.ToDisplayText()}，不点击发送",
            RpaSendMode.RealSendTest => "真实发送测试已开启",
            RpaSendMode.ProductionGuarded => "生产真实发送已开启",
            _ => sendMode.ToString()
        };
        RuntimeModeTextBlock.Text = $"有人值守，单线程顺序执行；{sendMode.ToDisplayText()}；InputOnly 后处理：{inputOnlyAction.ToDisplayText()}";

        var (background, foreground) = sendMode switch
        {
            RpaSendMode.DryRun => ("#ECFDF3", "#027A48"),
            RpaSendMode.InputOnly => ("#FFF4E5", "#B54708"),
            RpaSendMode.RealSendTest => ("#FEF3F2", "#B42318"),
            RpaSendMode.ProductionGuarded => ("#FDF2FA", "#C11574"),
            _ => ("#EFF8FF", "#175CD3")
        };
        SendModeBadgeBorder.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(background));
        SendModeBadge.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(foreground));
    }

    private bool TryConfigureHttpClient()
    {
        var apiBaseUrl = ApiBaseUrlTextBox.Text.Trim().TrimEnd('/');
        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri))
        {
            AppendLog("API 地址格式不正确。");
            return false;
        }

        _ = uri;
        _options.ApiBaseUrl = apiBaseUrl;
        _options.ClientInstanceKey = ClientKeyTextBox.Text.Trim();
        _options.EmployeeId = ParseGuidOrNull(EmployeeIdTextBox.Text);
        _options.VirtualDeviceId = ParseGuidOrNull(VirtualDeviceIdTextBox.Text);
        _options.WeChatWorkAccountId = ParseGuidOrNull(WeChatAccountIdTextBox.Text);
        _backendClient.ConfigureBaseUrl(apiBaseUrl);
        return true;
    }

    private void ApplyAccessPolicy(AgentAccessPolicyResponse policy)
    {
        _canStartTask = policy.CanStartTask;
        AccessStatusTextBlock.Text = $"{policy.Status}：{policy.Reason}";
        ValidToTextBlock.Text = policy.ValidToUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未设置结束时间";
        AuthorizationBadge.Text = policy.CanContinueRun ? "授权有效" : "授权不可用";
        AuthorizationBadge.Foreground = policy.CanContinueRun
            ? System.Windows.Media.Brushes.DarkGreen
            : System.Windows.Media.Brushes.Firebrick;

        UpdateStartTaskState(policy.CanStartTask && !_isTaskRunning, policy.CanStartTask ? "可以开始任务。" : policy.Reason);
    }

    private void ResetM4Status()
    {
        CurrentTaskStatusTextBlock.Text = "准备执行";
        OcrTextBox.Text = string.Empty;
        AiReplyTextBox.Text = string.Empty;
        RiskLevelTextBlock.Text = "未生成";
        LayoutStatusTextBlock.Text = "未定位";
        CountdownTextBlock.Text = "未开始";
        LastErrorTextBlock.Text = "无";
        ContinuousStatusTextBlock.Text = "未开启";
        ContinuousReplyCountTextBlock.Text = $"0/{(_options.Automation.MaxRepliesPerContinuousSession <= 0 ? "不限" : _options.Automation.MaxRepliesPerContinuousSession.ToString())}";
        ContinuousLastPollTextBlock.Text = "未轮询";
        ContinuousLatestSenderTextBlock.Text = "未知发送方";
        ContinuousLatestMessageTextBlock.Text = "未识别";
        ContinuousMergeCountdownTextBlock.Text = "未等待";
        ContinuousFailureCountTextBlock.Text = "0";
        WindowTargetTextBlock.Text = "未锁定";
        ResetUnreadQueueStatus();
    }

    private RpaTaskRunnerCallbacks CreateRunnerCallbacks()
    {
        return new RpaTaskRunnerCallbacks(
            AppendLog,
            value => RunOnUi(() => CurrentTaskStatusTextBlock.Text = value),
            value => RunOnUi(() => OcrTextBox.Text = value),
            value => RunOnUi(() => AiReplyTextBox.Text = value),
            value => RunOnUi(() => RiskLevelTextBlock.Text = value),
            value => RunOnUi(() => LayoutStatusTextBlock.Text = value),
            value => RunOnUi(() => CountdownTextBlock.Text = value),
            value => RunOnUi(() => LastErrorTextBlock.Text = string.IsNullOrWhiteSpace(value) ? "无" : value),
            value => RunOnUi(() => ContinuousStatusTextBlock.Text = value),
            value => RunOnUi(() => ContinuousReplyCountTextBlock.Text = value),
            value => RunOnUi(() => ContinuousLastPollTextBlock.Text = value),
            value => RunOnUi(() => ContinuousLatestMessageTextBlock.Text = value),
            value => RunOnUi(() => ContinuousLatestSenderTextBlock.Text = value),
            value => RunOnUi(() => ContinuousMergeCountdownTextBlock.Text = value),
            value => RunOnUi(() => ContinuousFailureCountTextBlock.Text = value),
            value => RunOnUi(() => WindowTargetTextBlock.Text = value),
            value => RunOnUi(() => ApplyUnreadQueueSnapshot(value)));
    }

    private void UpdateStartTaskState(bool enabled, string message)
    {
        StartTaskButton.IsEnabled = enabled;
        StartContinuousButton.IsEnabled = enabled && _options.Automation.EnableContinuousReply;
        ScanUnreadQueueButton.IsEnabled = enabled && _options.Automation.EnableUnreadQueueReadOnlyScan;
        SwitchUnreadConversationButton.IsEnabled = enabled && _options.Automation.EnableUnreadQueueControlledSwitch;
        CanStartTextBlock.Text = message;
    }

    private void ApplyUnreadQueueSnapshot(UnreadConversationQueueSnapshot snapshot)
    {
        UnreadQueueStatusTextBlock.Text = string.IsNullOrWhiteSpace(snapshot.Summary) ? "已扫描" : snapshot.Summary;
        UnreadQueueLastScanTextBlock.Text = snapshot.ScannedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        UnreadQueueCandidateCountTextBlock.Text = snapshot.Candidates.Count.ToString();
        UnreadQueueListBox.Items.Clear();
        if (snapshot.Candidates.Count == 0)
        {
            UnreadQueueListBox.Items.Add("无可见数字未读候选");
        }
        else
        {
            foreach (var candidate in snapshot.Candidates)
            {
                UnreadQueueListBox.Items.Add(candidate.ToDisplayText());
            }
        }

        if (!string.IsNullOrWhiteSpace(snapshot.DebugCapturePath))
        {
            UnreadQueueStatusTextBlock.Text = $"{UnreadQueueStatusTextBlock.Text} 调试截图：{snapshot.DebugCapturePath}";
        }
    }

    private void ResetUnreadQueueStatus()
    {
        SetUnreadQueueStatus("未扫描", "未扫描", "0");
        UnreadQueueListBox.Items.Clear();
    }

    private void SetUnreadQueueStatus(string status, string lastScan, string candidateCount)
    {
        UnreadQueueStatusTextBlock.Text = status;
        UnreadQueueLastScanTextBlock.Text = lastScan;
        UnreadQueueCandidateCountTextBlock.Text = candidateCount;
    }

    private static string FormatWindowTarget(WeChatWindow window)
    {
        return $"{window.Title} / 0x{window.Handle.ToInt64():X} / Monitor={FormatRectangle(window.MonitorBounds)} / Client={FormatRectangle(window.ClientBounds)} / DPI={(window.Dpi == 0 ? "未知" : window.Dpi.ToString())}";
    }

    private static string FormatRectangle(System.Drawing.Rectangle rectangle)
    {
        return $"X={rectangle.X},Y={rectangle.Y},W={rectangle.Width},H={rectangle.Height}";
    }

    private void AppendLog(string message)
    {
        RunOnUi(() =>
        {
            var displayText = $"{DateTime.Now:HH:mm:ss}  {message}";
            _ = RuntimeLogScreenshotPathExtractor.TryExtract(message, out var screenshotPath);
            RuntimeLogListBox.Items.Insert(0, new RuntimeLogItem(displayText, screenshotPath));
            while (RuntimeLogListBox.Items.Count > 200)
            {
                RuntimeLogListBox.Items.RemoveAt(RuntimeLogListBox.Items.Count - 1);
            }
        });
    }

    private void RuntimeLogListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (System.Windows.Controls.ItemsControl.ContainerFromElement(RuntimeLogListBox, source) is not System.Windows.Controls.ListBoxItem item ||
            item.DataContext is not RuntimeLogItem logItem ||
            string.IsNullOrWhiteSpace(logItem.ScreenshotPath))
        {
            return;
        }

        e.Handled = true;
        TryOpenRuntimeLogScreenshot(logItem.ScreenshotPath);
    }

    private void TryOpenRuntimeLogScreenshot(string screenshotPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(screenshotPath));
            if (!File.Exists(fullPath))
            {
                AppendLog($"截图文件不存在：{fullPath}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppendLog($"打开截图失败：{ex.Message}");
        }
    }

    private sealed record RuntimeLogItem(string DisplayText, string ScreenshotPath)
    {
        public string? ScreenshotToolTip => string.IsNullOrWhiteSpace(ScreenshotPath) ? null : "双击打开截图";
    }

    private void RunOnUi(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.Invoke(action);
    }

    protected override void OnClosed(EventArgs e)
    {
        _taskCancellation?.Cancel();
        _heartbeatTimer.Stop();
        _yoloVisionDetector.Dispose();
        _ocrEngine.Dispose();
        _visionHttpClient.Dispose();
        _httpClient.Dispose();
        base.OnClosed(e);
    }

    private static Guid? ParseGuidOrNull(string? value)
    {
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static string CreateDefaultClientKey()
    {
        var raw = $"{Environment.MachineName}|{Environment.UserName}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..12];
        return $"rpa-{Environment.MachineName}-{hash}".ToLowerInvariant();
    }
}
