using System.Globalization;
using System.Text.Json;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace MemoryWidgetProvider;

internal sealed class WidgetProvider : IWidgetProvider
{
    private const string MemoryWidgetTemplate = """
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.5",
          "body": [
            {
              "type": "TextBlock",
              "text": "内存占用率",
              "size": "large",
              "weight": "bolder",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "text": "${memoryLoad}",
              "size": "extraLarge",
              "weight": "bolder",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "text": "已用 ${usedGiB} / ${totalGiB}",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "text": "可用 ${availableGiB}",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "text": "更新时间 ${updatedAt}",
              "isSubtle": true,
              "spacing": "small",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "text": "${cleanStatus}",
              "spacing": "small",
              "wrap": true
            }
          ],
          "actions": [
            {
              "type": "Action.Execute",
              "title": "清理内存",
              "verb": "clean_memory",
              "associatedInputs": "none"
            }
          ]
        }
        """;

    private static readonly ManualResetEvent EmptyWidgetListEvent = new(initialState: false);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, WidgetRuntimeState> _runningWidgets = new(StringComparer.Ordinal);
    private readonly Timer _refreshTimer;
    private int _isRefreshRunning;
    private bool _isTimerRunning;

    public WidgetProvider()
    {
        ProviderLogger.Info("WidgetProvider 构造开始。");
        _refreshTimer = new Timer(_ => RefreshActiveWidgets(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        try
        {
            RestoreRunningWidgets();
            ProviderLogger.Info("WidgetProvider 构造完成。");
        }
        catch (Exception ex)
        {
            ProviderLogger.Error("WidgetProvider 构造阶段恢复运行中 widget 失败，继续以空状态运行。", ex);
        }
    }

    public static ManualResetEvent GetEmptyWidgetListEvent()
    {
        return EmptyWidgetListEvent;
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        try
        {
            ProviderLogger.Info($"CreateWidget: id={widgetContext.Id}, definition={widgetContext.DefinitionId}");
            WidgetRuntimeState widgetState;
            lock (_syncRoot)
            {
                widgetState = EnsureWidgetStateLocked(widgetContext);
                widgetState.IsActive = widgetContext.IsActive;
                widgetState.ForceNextUpdate = true;
                UpdateRefreshLoopLocked();
                EmptyWidgetListEvent.Reset();
            }

            QueueWidgetUpdate(widgetState);
        }
        catch (Exception ex)
        {
            ProviderLogger.Error("CreateWidget 回调异常。", ex);
        }
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        try
        {
            ProviderLogger.Info($"DeleteWidget: id={widgetId}");
            lock (_syncRoot)
            {
                _runningWidgets.Remove(widgetId);
                UpdateRefreshLoopLocked();
                if (_runningWidgets.Count == 0)
                {
                    EmptyWidgetListEvent.Set();
                }
            }
        }
        catch (Exception ex)
        {
            ProviderLogger.Error("DeleteWidget 回调异常。", ex);
        }
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        try
        {
            if (!string.Equals(actionInvokedArgs.Verb, "clean_memory", StringComparison.Ordinal))
            {
                return;
            }

            var widgetId = actionInvokedArgs.WidgetContext.Id;
            ProviderLogger.Info($"OnActionInvoked clean_memory: id={widgetId}");

            var shouldRunCleanup = false;
            WidgetRuntimeState? snapshot = null;

            lock (_syncRoot)
            {
                if (!_runningWidgets.TryGetValue(widgetId, out var state))
                {
                    return;
                }

                if (!state.IsCleaning)
                {
                    state.IsCleaning = true;
                    state.CleanupStatusText = "清理中...";
                    shouldRunCleanup = true;
                }

                state.ForceNextUpdate = true;
                snapshot = state.Clone();
            }

            if (snapshot is not null)
            {
                QueueWidgetUpdate(snapshot);
            }

            if (!shouldRunCleanup)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(
                static state =>
                {
                    var args = (CleanupWorkerArgs)state!;
                    args.Provider.RunMemoryCleanup(args.WidgetId);
                },
                new CleanupWorkerArgs(this, widgetId));
        }
        catch (Exception ex)
        {
            ProviderLogger.Error("OnActionInvoked 回调异常。", ex);
        }
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        try
        {
            var widgetContext = contextChangedArgs.WidgetContext;
            ProviderLogger.Info($"OnWidgetContextChanged: id={widgetContext.Id}, size={widgetContext.Size}");
            WidgetRuntimeState widgetState;
            lock (_syncRoot)
            {
                widgetState = EnsureWidgetStateLocked(widgetContext);
                widgetState.IsActive = widgetContext.IsActive;
                widgetState.ForceNextUpdate = true;
                UpdateRefreshLoopLocked();
            }

            QueueWidgetUpdate(widgetState);
        }
        catch (Exception ex)
        {
            ProviderLogger.Error("OnWidgetContextChanged 回调异常。", ex);
        }
    }

    public void Activate(WidgetContext widgetContext)
    {
        try
        {
            ProviderLogger.Info($"Activate: id={widgetContext.Id}, definition={widgetContext.DefinitionId}");
            WidgetRuntimeState widgetState;
            lock (_syncRoot)
            {
                widgetState = EnsureWidgetStateLocked(widgetContext);
                widgetState.IsActive = true;
                widgetState.ForceNextUpdate = true;
                UpdateRefreshLoopLocked();
            }

            QueueWidgetUpdate(widgetState);
        }
        catch (Exception ex)
        {
            ProviderLogger.Error("Activate 回调异常。", ex);
        }
    }

    public void Deactivate(string widgetId)
    {
        try
        {
            ProviderLogger.Info($"Deactivate: id={widgetId}");
            lock (_syncRoot)
            {
                if (_runningWidgets.TryGetValue(widgetId, out var state))
                {
                    state.IsActive = false;
                }

                UpdateRefreshLoopLocked();
            }
        }
        catch (Exception ex)
        {
            ProviderLogger.Error("Deactivate 回调异常。", ex);
        }
    }

    private void RestoreRunningWidgets()
    {
        ProviderLogger.Info("RestoreRunningWidgets: begin");
        var runningWidgets = WidgetManager.GetDefault().GetWidgetInfos();
        ProviderLogger.Info($"RestoreRunningWidgets: GetWidgetInfos 完成，count={runningWidgets.Length}");

        lock (_syncRoot)
        {
            foreach (var widgetInfo in runningWidgets)
            {
                var context = widgetInfo.WidgetContext;
                _runningWidgets[context.Id] = new WidgetRuntimeState(context.Id, context.DefinitionId)
                {
                    IsActive = context.IsActive
                };
            }

            UpdateRefreshLoopLocked();
            if (_runningWidgets.Count == 0)
            {
                EmptyWidgetListEvent.Set();
            }
        }
    }

    private void RefreshActiveWidgets()
    {
        if (Interlocked.CompareExchange(ref _isRefreshRunning, 1, 0) != 0)
        {
            return;
        }

        try
        {
            List<WidgetRuntimeState> activeWidgets;
            lock (_syncRoot)
            {
                activeWidgets = _runningWidgets.Values
                    .Where(widget => widget.IsActive)
                    .Select(widget => widget.Clone())
                    .ToList();
            }

            if (activeWidgets.Count == 0)
            {
                return;
            }

            foreach (var widget in activeWidgets)
            {
                QueueWidgetUpdate(widget);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isRefreshRunning, 0);
        }
    }

    private void UpdateRefreshLoopLocked()
    {
        if (_runningWidgets.Count == 0)
        {
            if (!_isTimerRunning)
            {
                return;
            }

            _isTimerRunning = false;
            _refreshTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        var hasActiveWidget = _runningWidgets.Values.Any(widget => widget.IsActive);
        if (hasActiveWidget)
        {
            if (_isTimerRunning)
            {
                return;
            }

            _isTimerRunning = true;
            _refreshTimer.Change(RefreshInterval, RefreshInterval);
            return;
        }

        if (!_isTimerRunning)
        {
            return;
        }

        _isTimerRunning = false;
        _refreshTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void RunMemoryCleanup(string widgetId)
    {
        string cleanupStatusText;

        try
        {
            var result = MemoryCleaner.TrimWorkingSets();
            cleanupStatusText = result.SucceededProcessCount == 0
                ? $"清理完成：未释放 ({result.DurationMs} ms)"
                : $"清理完成：约释放 {FormatMiB(result.ReleasedBytes)} ({result.SucceededProcessCount}/{result.AttemptedProcessCount} 进程, {result.DurationMs} ms)";
        }
        catch (Exception ex)
        {
            ProviderLogger.Error("执行内存清理失败。", ex);
            cleanupStatusText = $"清理失败：{ex.GetBaseException().Message}";
        }

        WidgetRuntimeState? snapshot = null;
        lock (_syncRoot)
        {
            if (!_runningWidgets.TryGetValue(widgetId, out var state))
            {
                return;
            }

            state.IsCleaning = false;
            state.CleanupStatusText = cleanupStatusText;
            state.ForceNextUpdate = true;
            snapshot = state.Clone();
        }

        if (snapshot is not null)
        {
            QueueWidgetUpdate(snapshot);
        }
    }

    private WidgetRuntimeState EnsureWidgetStateLocked(WidgetContext widgetContext)
    {
        if (_runningWidgets.TryGetValue(widgetContext.Id, out var existing))
        {
            return existing;
        }

        var created = new WidgetRuntimeState(widgetContext.Id, widgetContext.DefinitionId);
        created.ForceNextUpdate = true;
        _runningWidgets[widgetContext.Id] = created;
        return created;
    }

    private void QueueWidgetUpdate(WidgetRuntimeState widgetInfo)
    {
        var shouldStartWorker = false;
        lock (_syncRoot)
        {
            if (!_runningWidgets.TryGetValue(widgetInfo.WidgetId, out var state))
            {
                return;
            }

            state.HasPendingUpdate = true;
            if (!state.IsUpdateWorkerRunning)
            {
                state.IsUpdateWorkerRunning = true;
                shouldStartWorker = true;
            }
        }

        if (!shouldStartWorker)
        {
            return;
        }

        var args = new UpdateWorkerArgs(this, widgetInfo.WidgetId);
        ThreadPool.QueueUserWorkItem(static state =>
        {
            try
            {
                var workerArgs = (UpdateWorkerArgs)state!;
                workerArgs.Provider.ProcessWidgetUpdateQueue(workerArgs.WidgetId);
            }
            catch (Exception ex)
            {
                ProviderLogger.Error("更新队列线程异常。", ex);
            }
        }, args);
    }

    private void ProcessWidgetUpdateQueue(string widgetId)
    {
        while (true)
        {
            WidgetRuntimeState? snapshot;
            lock (_syncRoot)
            {
                if (!_runningWidgets.TryGetValue(widgetId, out var state))
                {
                    return;
                }

                if (!state.HasPendingUpdate)
                {
                    state.IsUpdateWorkerRunning = false;
                    return;
                }

                state.HasPendingUpdate = false;
                if (!state.IsActive && !state.ForceNextUpdate)
                {
                    continue;
                }

                state.ForceNextUpdate = false;
                snapshot = state.Clone();
            }

            UpdateWidget(snapshot);
        }
    }

    private void UpdateWidget(WidgetRuntimeState widgetInfo)
    {
        if (!string.Equals(widgetInfo.DefinitionId, ProviderContract.MemoryUsageDefinitionId, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var snapshot = SystemMemoryReader.Read();
            var payload = new WidgetPayload(
                $"{snapshot.UsedPercentage}%",
                FormatGiB(snapshot.TotalBytes),
                FormatGiB(snapshot.UsedBytes),
                FormatGiB(snapshot.AvailableBytes),
                DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                widgetInfo.CleanupStatusText);

            var request = new WidgetUpdateRequestOptions(widgetInfo.WidgetId)
            {
                Template = MemoryWidgetTemplate,
                Data = JsonSerializer.Serialize(payload, JsonOptions),
                CustomState = snapshot.UsedPercentage.ToString(CultureInfo.InvariantCulture)
            };

            WidgetManager.GetDefault().UpdateWidget(request);
        }
        catch (Exception ex)
        {
            ProviderLogger.Error($"UpdateWidget 失败: id={widgetInfo.WidgetId}", ex);
            TrySendErrorWidget(widgetInfo.WidgetId);
        }
    }

    private void TrySendErrorWidget(string widgetId)
    {
        try
        {
            var payload = new WidgetPayload(
                "N/A",
                "N/A",
                "N/A",
                "N/A",
                DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                "数据读取失败");

            var request = new WidgetUpdateRequestOptions(widgetId)
            {
                Template = MemoryWidgetTemplate,
                Data = JsonSerializer.Serialize(payload, JsonOptions),
                CustomState = "error"
            };

            WidgetManager.GetDefault().UpdateWidget(request);
        }
        catch (Exception ex)
        {
            ProviderLogger.Error($"UpdateWidget 错误回退失败: id={widgetId}", ex);
        }
    }

    private static string FormatGiB(ulong bytes)
    {
        var value = bytes / 1024d / 1024d / 1024d;
        return $"{value:0.00} GiB";
    }

    private static string FormatMiB(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 MiB";
        }

        var value = bytes / 1024d / 1024d;
        return $"{value:0.0} MiB";
    }

    private sealed record WidgetPayload(
        string MemoryLoad,
        string TotalGiB,
        string UsedGiB,
        string AvailableGiB,
        string UpdatedAt,
        string CleanStatus);

    private sealed class WidgetRuntimeState
    {
        public WidgetRuntimeState(string widgetId, string definitionId)
        {
            WidgetId = widgetId;
            DefinitionId = definitionId;
            CleanupStatusText = "清理状态：未执行";
        }

        public string WidgetId { get; }
        public string DefinitionId { get; }
        public bool IsActive { get; set; }
        public bool HasPendingUpdate { get; set; }
        public bool IsUpdateWorkerRunning { get; set; }
        public bool ForceNextUpdate { get; set; }
        public bool IsCleaning { get; set; }
        public string CleanupStatusText { get; set; }

        public WidgetRuntimeState Clone()
        {
            return new WidgetRuntimeState(WidgetId, DefinitionId)
            {
                IsActive = IsActive,
                HasPendingUpdate = HasPendingUpdate,
                IsUpdateWorkerRunning = IsUpdateWorkerRunning,
                ForceNextUpdate = ForceNextUpdate,
                IsCleaning = IsCleaning,
                CleanupStatusText = CleanupStatusText
            };
        }
    }

    private sealed record UpdateWorkerArgs(WidgetProvider Provider, string WidgetId);
    private sealed record CleanupWorkerArgs(WidgetProvider Provider, string WidgetId);
}
