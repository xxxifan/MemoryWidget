using System.Globalization;
using System.Text.Json;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace MemoryWidgetProvider;

internal sealed class WidgetProvider : IWidgetProvider
{
    // 小、中两套布局放在同一个模板里，按 $host.widgetSize 选择。
    // 切换尺寸时宿主可以直接用已缓存的数据重新渲染，不必等 provider 回推新模板。
    // 宿主每次收到数据都会重建整张卡片，图片异步解码会闪一下，因此只有进度条用图片，按钮用原生强调色按钮。
    // 中尺寸：按钮以上的内容放进 height=stretch 的容器，吃掉剩余高度，让按钮沉到卡片底部；
    //         拉伸后宿主不再保留底部内边距，末尾用一个空容器补出与左右一致的边距。
    // 中尺寸的清理按钮用 accent 样式的 Container + selectAction 模拟全宽大按钮（原生 Action 无法调宽高，图片按钮刷新会闪）。
    // 小尺寸：百分比与清理按钮并排，下面是进度条和已用/总量，整体垂直居中。
    private const string MemoryWidgetTemplate = """
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.5",
          "body": [
            {
              "type": "Container",
              "$when": "${$host.widgetSize != 'small'}",
              "height": "stretch",
              "items": [
                {
                  "type": "Container",
                  "height": "stretch",
                  "items": [
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
                      "color": "${memoryColor}",
                      "wrap": true
                    },
                    {
                      "type": "Image",
                      "$when": "${$host.hostTheme == 'light'}",
                      "url": "${progressLight}",
                      "altText": "内存占用 ${memoryLoad}",
                      "size": "stretch",
                      "spacing": "small"
                    },
                    {
                      "type": "Image",
                      "$when": "${$host.hostTheme != 'light'}",
                      "url": "${progressDark}",
                      "altText": "内存占用 ${memoryLoad}",
                      "size": "stretch",
                      "spacing": "small"
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
                      "text": "${cleanStatus}",
                      "spacing": "small",
                      "wrap": true
                    }
                  ]
                },
                {
                  "type": "Container",
                  "style": "accent",
                  "roundedCorners": true,
                  "minHeight": "40px",
                  "verticalContentAlignment": "center",
                  "spacing": "medium",
                  "selectAction": {
                    "type": "Action.Execute",
                    "title": "${cleanButtonTitle}",
                    "verb": "clean_memory",
                    "associatedInputs": "none"
                  },
                  "items": [
                    {
                      "type": "TextBlock",
                      "text": "${cleanButtonTitle}",
                      "weight": "bolder",
                      "horizontalAlignment": "center"
                    }
                  ]
                },
                {
                  "type": "Container",
                  "minHeight": "12px",
                  "spacing": "none",
                  "items": []
                }
              ]
            },
            {
              "type": "Container",
              "$when": "${$host.widgetSize == 'small'}",
              "height": "stretch",
              "verticalContentAlignment": "center",
              "items": [
                {
                  "type": "ColumnSet",
                  "columns": [
                    {
                      "type": "Column",
                      "width": "stretch",
                      "verticalContentAlignment": "center",
                      "items": [
                        {
                          "type": "TextBlock",
                          "text": "${memoryLoad}",
                          "size": "extraLarge",
                          "weight": "bolder",
                          "color": "${memoryColor}"
                        }
                      ]
                    },
                    {
                      "type": "Column",
                      "width": "auto",
                      "verticalContentAlignment": "center",
                      "items": [
                        {
                          "type": "ActionSet",
                          "actions": [
                            {
                              "type": "Action.Execute",
                              "title": "${cleanButtonTitle}",
                              "verb": "clean_memory",
                              "style": "positive",
                              "associatedInputs": "none"
                            }
                          ]
                        }
                      ]
                    }
                  ]
                },
                {
                  "type": "Image",
                  "$when": "${$host.hostTheme == 'light'}",
                  "url": "${progressLight}",
                  "altText": "内存占用 ${memoryLoad}",
                  "size": "stretch",
                  "spacing": "default"
                },
                {
                  "type": "Image",
                  "$when": "${$host.hostTheme != 'light'}",
                  "url": "${progressDark}",
                  "altText": "内存占用 ${memoryLoad}",
                  "size": "stretch",
                  "spacing": "default"
                },
                {
                  "type": "TextBlock",
                  "text": "已用 ${usedGiB} / ${totalGiB}",
                  "spacing": "default",
                  "wrap": true
                }
              ]
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
            // 模板已按 $host.widgetSize 内置两套布局，切换尺寸由宿主用缓存数据直接重绘，
            // 这里不再立即推送，避免同一份数据再渲染一次造成闪烁；数据交给定时刷新即可。
            lock (_syncRoot)
            {
                var widgetState = EnsureWidgetStateLocked(widgetContext);
                widgetState.IsActive = widgetContext.IsActive;
                UpdateRefreshLoopLocked();
            }
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

                var force = state.ForceNextUpdate;
                state.ForceNextUpdate = false;
                snapshot = state.Clone();
                snapshot.ForceNextUpdate = force;
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
            var payload = CreatePayload(
                widgetInfo,
                snapshot.UsedPercentage,
                $"{snapshot.UsedPercentage}%",
                FormatGiB(snapshot.TotalBytes),
                FormatGiB(snapshot.UsedBytes),
                FormatGiB(snapshot.AvailableBytes),
                widgetInfo.CleanupStatusText);

            var data = JsonSerializer.Serialize(payload, JsonOptions);

            // 宿主每次收到数据都会重建卡片，进度条图片会闪一下；数据没变就不推送
            lock (_syncRoot)
            {
                if (!_runningWidgets.TryGetValue(widgetInfo.WidgetId, out var state)
                    || (!widgetInfo.ForceNextUpdate && string.Equals(state.LastSentData, data, StringComparison.Ordinal)))
                {
                    return;
                }
            }

            var request = new WidgetUpdateRequestOptions(widgetInfo.WidgetId)
            {
                Template = MemoryWidgetTemplate,
                Data = data,
                CustomState = snapshot.UsedPercentage.ToString(CultureInfo.InvariantCulture)
            };

            WidgetManager.GetDefault().UpdateWidget(request);
            SetLastSentData(widgetInfo.WidgetId, data);
        }
        catch (Exception ex)
        {
            ProviderLogger.Error($"UpdateWidget 失败: id={widgetInfo.WidgetId}", ex);
            SetLastSentData(widgetInfo.WidgetId, null);
            TrySendErrorWidget(widgetInfo);
        }
    }

    private void SetLastSentData(string widgetId, string? data)
    {
        lock (_syncRoot)
        {
            if (_runningWidgets.TryGetValue(widgetId, out var state))
            {
                state.LastSentData = data;
            }
        }
    }

    private void TrySendErrorWidget(WidgetRuntimeState widgetInfo)
    {
        var widgetId = widgetInfo.WidgetId;
        try
        {
            var payload = CreatePayload(widgetInfo, 0, "N/A", "N/A", "N/A", "N/A", "数据读取失败");

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

    private static WidgetPayload CreatePayload(
        WidgetRuntimeState widgetInfo,
        int usedPercentage,
        string memoryLoad,
        string totalGiB,
        string usedGiB,
        string availableGiB,
        string cleanStatus)
    {
        var level = WidgetVisuals.GetLevel(usedPercentage);
        return new WidgetPayload(
            memoryLoad,
            WidgetVisuals.GetTextColor(level),
            totalGiB,
            usedGiB,
            availableGiB,
            cleanStatus,
            widgetInfo.IsCleaning ? "清理中…" : "清理内存",
            WidgetVisuals.CreateProgressBar(usedPercentage, level, isLightTheme: true),
            WidgetVisuals.CreateProgressBar(usedPercentage, level, isLightTheme: false));
    }

    private static string FormatGiB(ulong bytes)
    {
        // 保留 1 位小数，减少数值抖动带来的推送次数
        var value = bytes / 1024d / 1024d / 1024d;
        return $"{value:0.0} GiB";
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
        string MemoryColor,
        string TotalGiB,
        string UsedGiB,
        string AvailableGiB,
        string CleanStatus,
        string CleanButtonTitle,
        string ProgressLight,
        string ProgressDark);

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
        public string? LastSentData { get; set; }

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
