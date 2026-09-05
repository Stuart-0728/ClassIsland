using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.Management;
using ClassIsland.Core.Abstractions.Services.SpeechService;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Core.Enums.Notification;
using ClassIsland.Services.SpeechService;
using ClassIsland.Shared.Abstraction.Services;
using ClassIsland.Shared.ComponentModels;
using ClassIsland.Shared.Enums;
using ClassIsland.Shared.Models.Profile;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ClassIsland.Views;

namespace ClassIsland.Services.Management;

/// <summary>
/// 两江巴蜀智慧教研平台后台同步托管服务
/// 负责定时拉取并自动转换课表到 ClassIsland Profile（实时刷新、消除“今天变明天”）、
/// 弹窗播报班级广播通知（必播语音与 1.wav 提示音）、
/// 实时接收平台对讲语音并播放音频片段、
/// 并向平台反馈确认回执
/// </summary>
public class BashuPlatformService : IHostedService
{
    private ILogger<BashuPlatformService> Logger { get; }
    private IProfileService ProfileService { get; }
    private ILessonsService LessonsService { get; }
    private IExactTimeService ExactTimeService { get; }
    private ISpeechService SpeechService { get; }
    private IAudioService AudioService { get; }
    private SettingsService SettingsService { get; }
    private INotificationHostService NotificationHostService { get; }
    private IManagementService ManagementService { get; }

    private DispatcherTimer? PollTimer { get; set; }
    private string LastScheduleSignature { get; set; } = "";
    private Profile? LastSyncedProfile;
    private readonly HashSet<long> ProcessedNotificationIds = new();
    private readonly Queue<long> ProcessedNotificationOrder = new();
    private readonly HashSet<long> ProcessedIntercomSegmentIds = new();
    private readonly Queue<long> ProcessedIntercomSegmentOrder = new();
    private bool IsPolling { get; set; } = false;
    private readonly HashSet<long> PresentedSessions = new();
    private readonly Queue<long> PresentedSessionsOrder = new();

    private static bool TrackBoundedId(HashSet<long> set, Queue<long> order, long id, int maxCapacity = 500)
    {
        if (set.Add(id))
        {
            order.Enqueue(id);
            while (order.Count > maxCapacity)
            {
                var oldest = order.Dequeue();
                set.Remove(oldest);
            }
            return true;
        }
        return false;
    }
    private readonly HashSet<long> PendingNotificationAcks = new();
    private readonly HashSet<long> PendingIntercomAcks = new();
    private readonly CancellationTokenSource Shutdown = new();
    private BashuPlatformConnection? LastConnection;
    public string Status { get; private set; } = "等待连接";
    public string LastSync { get; private set; } = "尚未同步";
    private NotificationRequest? IntercomNotification;
    private DateTime LastAudioAt;
    private readonly Queue<(BashuPlatformConnection Connection, JsonElement Segment, long Id)> NormalAudioQueue = new();
    private readonly Queue<(BashuPlatformConnection Connection, JsonElement Segment, long Id)> EmergencyAudioQueue = new();
    private bool IsAudioQueueRunning;
    private bool IsEmergencyAudioPlaying;
    private CancellationTokenSource? CurrentAudioCancellation;
    private long DisplayedIntercomSession;
    private (BashuPlatformConnection Connection, JsonElement Segment, long Id)? InterruptedAudio;
    private readonly HashSet<long> QueuedSegments = new();
    private readonly BashuRtcReceiver RtcReceiver;

    public BashuPlatformConnection? Connection => ManagementService.Connection as BashuPlatformConnection;

    public BashuPlatformService(
        ILogger<BashuPlatformService> logger,
        IProfileService profileService,
        ILessonsService lessonsService,
        IExactTimeService exactTimeService,
        ISpeechService speechService,
        IAudioService audioService,
        SettingsService settingsService,
        INotificationHostService notificationHostService,
        IManagementService managementService)
    {
        Logger = logger;
        ProfileService = profileService;
        LessonsService = lessonsService;
        ExactTimeService = exactTimeService;
        SpeechService = speechService;
        AudioService = audioService;
        SettingsService = settingsService;
        NotificationHostService = notificationHostService;
        ManagementService = managementService;
        RtcReceiver = new BashuRtcReceiver(audioService, notificationHostService, settingsService, logger);
        RtcReceiver.AudioStarted = sessionId =>
        {
            var previouslyPresented = !TrackBoundedId(PresentedSessions, PresentedSessionsOrder, sessionId);
            if (DisplayedIntercomSession == sessionId)
            {
                CurrentAudioCancellation?.Cancel();
                IntercomNotification?.Cancel();
                IntercomNotification = null;
                InterruptedAudio = null;
            }
            return previouslyPresented;
        };
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("启动两江巴蜀智慧教研平台同步托管服务");
        PollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        PollTimer.Tick += async (sender, args) => await PollOnceAsync();
        PollTimer.Start();
        _ = PollOnceAsync();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("停止两江巴蜀智慧教研平台同步托管服务");
        PollTimer?.Stop();
        Shutdown.Cancel();
        RtcReceiver.Stop();
        CurrentAudioCancellation?.Cancel();
        IntercomNotification?.Cancel();
        return Task.CompletedTask;
    }

    public async Task PollOnceAsync(bool forceSchedule = false)
    {
        if (IsPolling) { Status = "正在同步，请稍候"; return; }
        if (forceSchedule) LastScheduleSignature = "";
        var conn = Connection;
        if (conn == null || string.IsNullOrWhiteSpace(conn.Settings.BashuDeviceToken))
        {
            RtcReceiver.Stop();
            return;
        }

        IsPolling = true;
        try
        {
            if (LastConnection != conn)
            {
                LastConnection = conn;
                RtcReceiver.Stop();
                CurrentAudioCancellation?.Cancel();
                NormalAudioQueue.Clear(); EmergencyAudioQueue.Clear();
                InterruptedAudio = null;
                IntercomNotification?.Cancel(); IntercomNotification = null; DisplayedIntercomSession = 0;
                LastScheduleSignature = "";
                ProcessedNotificationIds.Clear(); ProcessedNotificationOrder.Clear();
                ProcessedIntercomSegmentIds.Clear(); ProcessedIntercomSegmentOrder.Clear();
                PendingNotificationAcks.Clear(); PendingIntercomAcks.Clear();
                PresentedSessions.Clear(); PresentedSessionsOrder.Clear();
                QueuedSegments.Clear();
            }
            foreach (var pending in PendingNotificationAcks.ToArray())
                if (await conn.AcknowledgeNotificationAsync(pending)) PendingNotificationAcks.Remove(pending);
            foreach (var pending in PendingIntercomAcks.ToArray())
                if (await conn.AcknowledgeIntercomSegmentAsync(pending)) PendingIntercomAcks.Remove(pending);
            if (IntercomNotification != null && QueuedSegments.Count == 0 && DateTime.UtcNow - LastAudioAt > TimeSpan.FromSeconds(6))
            {
                IntercomNotification.Cancel(); IntercomNotification = null;
            }
            await RtcReceiver.PollAsync(conn);
            var json = await conn.PollAsync(Shutdown.Token);
            if (string.IsNullOrWhiteSpace(json))
            {
                RtcReceiver.Stop();
                Status = conn.LastError;
                // 网络异常或断开时，自适应降频至 5 秒一次，减少 CPU 资源空耗
                if (PollTimer != null && PollTimer.Interval != TimeSpan.FromSeconds(5))
                {
                    PollTimer.Interval = TimeSpan.FromSeconds(5);
                }
                return;
            }

            // 成功通信，立即切回 1 秒快速响应
            if (PollTimer != null && PollTimer.Interval != TimeSpan.FromSeconds(1))
            {
                PollTimer.Interval = TimeSpan.FromSeconds(1);
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Status = "平台已连接";

            // 1. 同步课表（实时更新，精确对齐当天）
            if (root.TryGetProperty("dashboard", out var dashboard))
            {
                if (dashboard.TryGetProperty("scheduleWeek", out var scheduleEl) && scheduleEl.ValueKind == JsonValueKind.Array)
                {
                    var sig = scheduleEl.GetRawText();
                    if (sig != LastScheduleSignature || !ReferenceEquals(LastSyncedProfile, ProfileService.Profile))
                    {
                        await ApplyScheduleAsync(scheduleEl);
                        LastScheduleSignature = sig;
                        LastSyncedProfile = ProfileService.Profile;
                        LastSync = DateTime.Now.ToString("MM-dd HH:mm:ss");
                    }
                }
                else
                {
                    Status = "服务器尚未提供整周课表，请先更新平台服务";
                }
            }

            // 2. 接收广播通知（必播语音与 1.wav 提示音）
            if (root.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsEl.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idEl) ? BashuPlatformConnection.GetInt64Flexible(idEl) : 0;
                    if (id <= 0 || ProcessedNotificationIds.Contains(id))
                    {
                        continue;
                    }

                    TrackBoundedId(ProcessedNotificationIds, ProcessedNotificationOrder, id);
                    var content = item.TryGetProperty("content", out var cEl) ? BashuPlatformConnection.GetStringFlexible(cEl) : "";
                    var author = Author(item);
                    var priority = item.TryGetProperty("priority", out var pEl) ? BashuPlatformConnection.GetStringFlexible(pEl) : "normal";
                    var isEmergency = priority == "emergency";
                    var repeat = item.TryGetProperty("repeat_count", out var rEl) ? Math.Clamp(BashuPlatformConnection.GetInt32Flexible(rEl), 1, 10) : 1;
                    var isFullscreen = item.TryGetProperty("is_fullscreen", out var fsEl) && (fsEl.ValueKind == JsonValueKind.True || (fsEl.ValueKind == JsonValueKind.String && fsEl.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true));

                    if (isFullscreen)
                    {
                        Logger.LogInformation("收到平台全屏强提醒：[{}] {} (来自 {})", priority, content, author);

                        // 播放语音朗读
                        for (var i = 0; i < repeat; i++)
                        {
                            SpeechService.EnqueueSpeechQueue($"{author}通知：{content}");
                        }

                        // 弹出全屏强提醒窗口（必须人工点击“确认收到并关闭”后方可关闭）
                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                var win = new BashuFullscreenNotificationWindow(author, content, isEmergency);
                                win.Confirmed += (_, _) =>
                                {
                                    SpeechService.ClearSpeechQueue();
                                    PendingNotificationAcks.Add(id);
                                    if (Connection != null)
                                    {
                                        _ = Connection.AcknowledgeNotificationAsync(id);
                                    }
                                };
                                win.Show();
                                win.Activate();
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError(ex, "显示全屏通知窗口失败");
                            }
                        });
                    }
                    else
                    {
                        Logger.LogInformation("收到平台广播通知：[{}] {} (来自 {})", priority, content, author);

                        // 弹出 ClassIsland 原生通知卡片
                        var notification = new NotificationRequest
                        {
                            MaskContent = NotificationContent.CreateTwoIconsMask(
                                isEmergency ? $"【紧急广播】来自 {author}" : $"班级通知 · 来自 {author}",
                                rightIcon: "\uE7E7", factory: mask => mask.IsSpeechEnabled = false
                            ),
                            OverlayContent = NotificationContent.CreateRollingTextContent($"{author}：{content}", BashuNotificationTiming.Duration(content, author, repeat), repeat,
                                overlay => overlay.SpeechContent = string.Join("。", Enumerable.Repeat($"{author}通知：{content}", repeat))),
                            IsPriorityOverride = isEmergency,
                            PriorityOverride = isEmergency ? 100 : 0,
                            RequestNotificationSettings =
                            {
                                IsSettingsEnabled = true,
                                IsSpeechEnabled = true,
                                IsNotificationSoundEnabled = true,
                                IsNotificationTopmostEnabled = true
                            }
                        };
                        notification.Completed += (_, _) =>
                        {
                            if (Connection != conn) return;
                            if (notification.State == NotificationState.Completed)
                                PendingNotificationAcks.Add(id);
                            else
                                ProcessedNotificationIds.Remove(id);
                        };
                        NotificationHostService.ShowNotification(notification, Guid.Empty, Guid.Empty, true, false);
                    }
                }
            }

            // 3. 接收实时对讲音频片段
            if (root.TryGetProperty("intercom", out var intercomEl) && intercomEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var segment in intercomEl.EnumerateArray())
                {
                    if (segment.TryGetProperty("session_id", out var rtcSession) && RtcReceiver.Receiving(BashuPlatformConnection.GetInt64Flexible(rtcSession))) continue;
                    var segId = segment.TryGetProperty("id", out var sidEl) ? BashuPlatformConnection.GetInt64Flexible(sidEl) : 0;
                    if (segId <= 0 || ProcessedIntercomSegmentIds.Contains(segId))
                    {
                        continue;
                    }

                    if (!QueuedSegments.Add(segId)) continue;
                    var queuedSegment = segment.Clone();
                    var emergency = segment.TryGetProperty("priority", out var priorityEl) && priorityEl.GetString() == "emergency";
                    (emergency ? EmergencyAudioQueue : NormalAudioQueue).Enqueue((conn, queuedSegment, segId));
                }
                if (EmergencyAudioQueue.Count > 0 && !IsEmergencyAudioPlaying)
                    CurrentAudioCancellation?.Cancel();
                _ = DrainAudioQueueAsync();
            }
        }
        catch (Exception ex)
        {
            Status = "同步失败：" + ex.Message;
            Logger.LogWarning(ex, "执行平台轮询发生错误");
            if (PollTimer != null && PollTimer.Interval != TimeSpan.FromSeconds(5))
            {
                PollTimer.Interval = TimeSpan.FromSeconds(5);
            }
        }
        finally
        {
            IsPolling = false;
        }
    }

    private async Task DrainAudioQueueAsync()
    {
        if (IsAudioQueueRunning) return;
        IsAudioQueueRunning = true;
        try
        {
            while (!Shutdown.IsCancellationRequested && (EmergencyAudioQueue.Count > 0 || InterruptedAudio != null || NormalAudioQueue.Count > 0))
            {
                // Keep the emergency floor across normal network gaps between consecutive packets.
                if (EmergencyAudioQueue.Count == 0 && IntercomNotification?.PriorityOverride == 200 &&
                    DateTime.UtcNow - LastAudioAt < TimeSpan.FromSeconds(2))
                {
                    await Task.Delay(50, Shutdown.Token);
                    continue;
                }
                IsEmergencyAudioPlaying = EmergencyAudioQueue.Count > 0;
                var next = IsEmergencyAudioPlaying ? EmergencyAudioQueue.Dequeue() :
                    InterruptedAudio ?? NormalAudioQueue.Dequeue();
                if (!IsEmergencyAudioPlaying) InterruptedAudio = null;
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Shutdown.Token);
                CurrentAudioCancellation = cancellation;
                await PlayQueuedSegmentAsync(next.Connection, next.Segment, next.Id, cancellation.Token);
                CurrentAudioCancellation = null;
            }
        }
        catch (OperationCanceledException) when (Shutdown.IsCancellationRequested) { }
        finally { IsAudioQueueRunning = false; IsEmergencyAudioPlaying = false; CurrentAudioCancellation = null; }
    }

    private async Task PlayQueuedSegmentAsync(BashuPlatformConnection conn, JsonElement segment, long segId, CancellationToken token)
    {
        try
        {
            if (Shutdown.IsCancellationRequested || Connection != conn) return;
                    var author = Author(segment);
                    var sessionId = segment.TryGetProperty("session_id", out var session) ? BashuPlatformConnection.GetInt64Flexible(session) : segId;
                    if (RtcReceiver.Receiving(sessionId)) return;
                    var mime = segment.TryGetProperty("mime_type", out var mimeEl) ? mimeEl.GetString() ?? "" : "";
                    var emergency = segment.TryGetProperty("priority", out var priorityEl) && priorityEl.GetString() == "emergency";
                    try
                    {
                        var bytes = await conn.GetIntercomSegmentAudioAsync(segId, token);
                        if (bytes == null || bytes.Length == 0) return;
                        if (DisplayedIntercomSession != sessionId || IntercomNotification == null ||
                            IntercomNotification.CancellationToken.IsCancellationRequested)
                        {
                            var firstPresentation = TrackBoundedId(PresentedSessions, PresentedSessionsOrder, sessionId);
                            DisplayedIntercomSession = sessionId;
                            IntercomNotification?.Cancel();
                            IntercomNotification = new NotificationRequest
                            {
                                MaskContent = NotificationContent.CreateTwoIconsMask($"{(emergency ? "紧急广播" : "实时对讲")} · {author}", rightIcon: "lucide(\ue17c)"),
                                OverlayContent = NotificationContent.CreateSimpleTextContent($"{author} 正在讲话", overlay => overlay.Duration = TimeSpan.FromMinutes(20)),
                                IsPriorityOverride = true,
                                PriorityOverride = emergency ? 200 : 50,
                                RequestNotificationSettings = { IsSettingsEnabled = true, IsSpeechEnabled = false, IsNotificationSoundEnabled = false, IsNotificationTopmostEnabled = true }
                            };
                            if (!firstPresentation)
                                IntercomNotification.MaskContent.Duration = TimeSpan.FromMilliseconds(1);
                            NotificationHostService.ShowNotification(IntercomNotification, Guid.Empty, Guid.Empty, true, false);
                        }
                        // Audio must not race ahead of its island while another notification is speaking.
                        while (IntercomNotification is { State: not NotificationState.Playing } &&
                               !IntercomNotification.CancellationToken.IsCancellationRequested &&
                               !IntercomNotification.CompletedToken.IsCancellationRequested)
                            await Task.Delay(25, token);
                        if (IntercomNotification?.CancellationToken.IsCancellationRequested == true ||
                            IntercomNotification?.CompletedToken.IsCancellationRequested == true) return;
                        var playbackNotification = IntercomNotification;
                        if (playbackNotification == null || RtcReceiver.Receiving(sessionId)) return;
                        await PlayIntercomAudioAsync(bytes, mime, playbackNotification, token);
                        LastAudioAt = DateTime.UtcNow;
                        TrackBoundedId(ProcessedIntercomSegmentIds, ProcessedIntercomSegmentOrder, segId);
                        PendingIntercomAcks.Add(segId);
                        // The poll loop retries acknowledgements without adding an HTTP round trip between audio clips.
                    }
                    catch (OperationCanceledException)
                    {
                        // No success acknowledgement: the interrupted segment may be retried after the emergency.
                        if (!Shutdown.IsCancellationRequested && Connection == conn && !RtcReceiver.Receiving(sessionId))
                            InterruptedAudio = (conn, segment, segId);
                    }
                    catch (Exception ex)
                    {
                        Status = "对讲播放失败，请检查音量、音频设备及网页是否已更新";
                        Logger.LogWarning(ex, "对讲片段 {SegmentId} 未播放成功，不发送成功回执", segId);

                    }
        }
        finally { if (InterruptedAudio?.Id != segId) QueuedSegments.Remove(segId); }
    }

    private static string Author(JsonElement item)
    {
        var name = item.TryGetProperty("created_by_name", out var value) ? value.GetString() : null;
        return string.IsNullOrWhiteSpace(name) ? "平台教师" : name.Trim();
    }

    private async Task ApplyScheduleAsync(JsonElement scheduleEl)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var profile = ProfileService.Profile;
            BashuScheduleMapper.Apply(profile, scheduleEl);
            profile.RefreshTimeLayouts();
            ProfileService.SaveProfile(ProfileService.CurrentProfilePath);
            if (LessonsService is ClassIsland.Services.LessonsService lessons)
                lessons.RefreshAfterPlatformSync();
            LessonsService.StartMainTimer();
        });
    }

    private async Task PlayIntercomAudioAsync(byte[] bytes, string mimeType, NotificationRequest notification, CancellationToken token)
    {
        // The platform records independent PCM WAV segments, avoiding browser-only Opus/WebM decoders.
        if (!mimeType.StartsWith("audio/wav", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("请刷新平台网页后重新发起对讲（需要 PCM WAV 音频）");
        using var lease = await AudioService.TryInitializeDefaultPlaybackDeviceSafeAsync();
        if (lease == null) throw new InvalidOperationException("没有可用的音频输出设备");
        using var audio = new MemoryStream(bytes, false);
        using var playbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        // A higher-priority island may arrive while a legacy clip is already playing.
        // Stop its sound as well as its visual; retry only after the island resumes.
        async Task WatchPriorityAsync()
        {
            try
            {
                while (!playbackCancellation.IsCancellationRequested)
                {
                    if (notification.State != NotificationState.Playing || notification.CancellationToken.IsCancellationRequested)
                    { playbackCancellation.Cancel(); return; }
                    await Task.Delay(20, playbackCancellation.Token);
                }
            }
            catch (OperationCanceledException) { }
        }
        var priorityWatch = WatchPriorityAsync();
        try
        {
            await AudioService.PlayAudioAsync(audio, (float)SettingsService.Settings.SpeechVolume, playbackCancellation.Token);
            playbackCancellation.Token.ThrowIfCancellationRequested();
        }
        finally { playbackCancellation.Cancel(); await priorityWatch; }
    }
}
