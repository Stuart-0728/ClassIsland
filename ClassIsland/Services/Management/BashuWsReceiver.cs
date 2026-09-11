using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Enums.Notification;
using ClassIsland.Core.Models.Notification;
using Microsoft.Extensions.Logging;
using SoundFlow.Components;

namespace ClassIsland.Services.Management;

/// <summary>
/// 两江巴蜀智慧教研平台全双工 WebSocket 实时对讲接收器：
/// 直连平台的 /ws/intercom 通道，毫秒级即时接收流式语音，彻底消除 HTTP 轮询的延迟累积与分段卡顿。
/// </summary>
public sealed class BashuWsReceiver(
    IAudioService audio,
    INotificationHostService notifications,
    SettingsService settings,
    ILogger logger) : IDisposable
{
    private readonly object SessionGate = new();
    private readonly object ConnectionGate = new();
    private CancellationTokenSource? LifetimeCts;
    private Task? ConnectionLoopTask;
    private BashuPlatformConnection? CurrentConnection;
    private WsSession? ActiveSession;
    private readonly HashSet<long> ReceivedSessionIds = new();
    private readonly List<long> ReceivedSessionIdsOrder = new();
    private readonly object SessionIdGate = new();

    public Func<long, bool>? AudioStarted { get; set; }

    public bool HasReceivedSession(long sessionId)
    {
        lock (SessionIdGate)
        {
            return ReceivedSessionIds.Contains(sessionId);
        }
    }

    public void RecordReceivedSession(long sessionId)
    {
        if (sessionId <= 0) return;
        lock (SessionIdGate)
        {
            if (ReceivedSessionIds.Add(sessionId))
            {
                ReceivedSessionIdsOrder.Add(sessionId);
                while (ReceivedSessionIdsOrder.Count > 100)
                {
                    var oldest = ReceivedSessionIdsOrder[0];
                    ReceivedSessionIdsOrder.RemoveAt(0);
                    ReceivedSessionIds.Remove(oldest);
                }
            }
        }
    }

    public bool Receiving(long sessionId)
    {
        lock (SessionGate)
        {
            return ActiveSession != null &&
                   ActiveSession.Id == sessionId &&
                   ActiveSession.IsActive &&
                   !ActiveSession.Stopped.IsCancellationRequested;
        }
    }

    public void EnsureConnected(BashuPlatformConnection connection)
    {
        lock (ConnectionGate)
        {
            if (CurrentConnection == connection && ConnectionLoopTask is { IsCompleted: false })
            {
                return;
            }

            StopConnectionLocked();
            CurrentConnection = connection;
            LifetimeCts = new CancellationTokenSource();
            var token = LifetimeCts.Token;
            ConnectionLoopTask = Task.Run(() => ConnectionLoopAsync(connection, token), token);
        }
    }

    private void StopConnectionLocked()
    {
        LifetimeCts?.Cancel();
        LifetimeCts?.Dispose();
        LifetimeCts = null;
        CloseActiveSession();
    }

    private async Task ConnectionLoopAsync(BashuPlatformConnection conn, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (string.IsNullOrWhiteSpace(conn.Settings.BashuDeviceToken))
            {
                await Task.Delay(2000, token);
                continue;
            }

            ClientWebSocket? ws = null;
            CancellationTokenSource? socketCts = null;
            try
            {
                var baseUrl = string.IsNullOrWhiteSpace(conn.Settings.BashuServerUrl)
                    ? "https://bashu.cqaibase.cn"
                    : conn.Settings.BashuServerUrl.TrimEnd('/');
                var wsUrl = (baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    ? "wss://" + baseUrl.Substring(8)
                    : "ws://" + baseUrl.Substring(7)) +
                    "/ws/intercom?token=" + Uri.EscapeDataString(conn.Settings.BashuDeviceToken);

                ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

                socketCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                await ws.ConnectAsync(new Uri(wsUrl), socketCts.Token);
                logger.LogInformation("两江巴蜀平台实时 WebSocket 流式对讲通道连接成功");

                var receiveBuffer = new byte[65536];
                using var pingTimer = new Timer(_ =>
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        try
                        {
                            var pingBytes = Encoding.UTF8.GetBytes("{\"type\":\"ping\"}");
                            _ = ws.SendAsync(pingBytes, WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                        catch { }
                    }
                }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(20));

                using var silenceWatchdog = new Timer(_ =>
                {
                    WsSession? sessionToCheck;
                    lock (SessionGate)
                    {
                        sessionToCheck = ActiveSession;
                    }
                    if (sessionToCheck != null && sessionToCheck.IsActive &&
                        DateTime.UtcNow - sessionToCheck.LastAudioReceivedAt > TimeSpan.FromSeconds(3.0))
                    {
                        logger.LogInformation("WebSocket 对讲会话 {SessionId} 持续 3 秒无新音频帧，自动平滑结课并关闭提醒", sessionToCheck.Id);
                        EndSession(sessionToCheck.Id, immediate: false);
                    }
                }, null, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(500));

                while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(receiveBuffer, socketCts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var text = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
                        await HandleTextMessageAsync(text, conn);
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary && result.Count > 8)
                    {
                        HandleBinaryAudioChunk(receiveBuffer.AsSpan(0, result.Count), conn);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug("WebSocket 实时对讲通道重试中：{Message}", ex.Message);
            }
            finally
            {
                CloseActiveSession();
                if (ws != null)
                {
                    try { ws.Dispose(); } catch { }
                }
                socketCts?.Dispose();
            }

            if (!token.IsCancellationRequested)
            {
                await Task.Delay(2000, token);
            }
        }
    }

    private async Task HandleTextMessageAsync(string json, BashuPlatformConnection conn)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl)) return;
            var type = typeEl.GetString();

            switch (type)
            {
                case "intercom-start":
                {
                    var sessionId = root.TryGetProperty("sessionId", out var sEl)
                        ? BashuPlatformConnection.GetInt64Flexible(sEl)
                        : 0;
                    var author = root.TryGetProperty("author", out var aEl) ? aEl.GetString() ?? "平台教师" : "平台教师";
                    var emergency = root.TryGetProperty("priority", out var pEl) && pEl.GetString() == "emergency";
                    if (sessionId > 0)
                    {
                        await StartSessionAsync(sessionId, author, emergency, conn);
                    }
                    break;
                }
                case "intercom-end":
                case "intercom-cancel":
                {
                    var sessionId = root.TryGetProperty("sessionId", out var sEl)
                        ? BashuPlatformConnection.GetInt64Flexible(sEl)
                        : 0;
                    if (sessionId > 0)
                    {
                        EndSession(sessionId, type == "intercom-cancel");
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "解析 WebSocket 文本消息失败");
        }
    }

    private void HandleBinaryAudioChunk(ReadOnlySpan<byte> data, BashuPlatformConnection conn)
    {
        if (data.Length <= 8) return;
        var sessionId = (long)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(0, 4));
        RecordReceivedSession(sessionId);
        var rawAudio = data.Slice(8);

        ReadOnlySpan<byte> pcmPayload;
        if (BashuAudioCombiner.TryGetDataChunk(rawAudio, out var dataOffset, out var dataLength))
        {
            pcmPayload = rawAudio.Slice(dataOffset, dataLength);
        }
        else
        {
            pcmPayload = rawAudio.Length > 44 ? rawAudio.Slice(44) : rawAudio;
        }

        if (pcmPayload.Length <= 0) return;

        WsSession? session;
        lock (SessionGate)
        {
            session = ActiveSession;
        }

        if (session == null || session.Id != sessionId || !session.IsActive)
        {
            // 防竞态保底：如果音频帧先于 intercom-start 文本帧到达，自动即时启动会话
            _ = StartSessionAsync(sessionId, "平台教师", false, conn);
            lock (SessionGate)
            {
                session = ActiveSession;
            }
        }

        if (session != null)
        {
            session.LastAudioReceivedAt = DateTime.UtcNow;
            session.Buffer.PushPcm16Mono16k(pcmPayload);
        }
    }

    private async Task StartSessionAsync(long sessionId, string author, bool emergency, BashuPlatformConnection conn)
    {
        RecordReceivedSession(sessionId);
        WsSession newSession;
        lock (SessionGate)
        {
            if (ActiveSession != null)
            {
                if (ActiveSession.Id == sessionId && ActiveSession.IsActive) return;
                if (ActiveSession.Emergency && !emergency) return;
                ActiveSession.Dispose();
            }

            newSession = new WsSession(sessionId, author, emergency);
            ActiveSession = newSession;
        }

        AudioStarted?.Invoke(sessionId);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (newSession.Stopped.IsCancellationRequested) return;
            newSession.Notification = new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask($"{(emergency ? "紧急广播" : "实时对讲")} · {author}", rightIcon: "lucide(\ue17c)"),
                OverlayContent = NotificationContent.CreateSimpleTextContent($"{author} 正在讲话", content => content.Duration = TimeSpan.FromMinutes(20)),
                IsPriorityOverride = true,
                PriorityOverride = emergency ? 200 : 50,
                RequestNotificationSettings = { IsSettingsEnabled = true, IsSpeechEnabled = false, IsNotificationSoundEnabled = false, IsNotificationTopmostEnabled = true }
            };
            newSession.Notification.MaskContent.Duration = TimeSpan.FromSeconds(5);
            notifications.ShowNotification(newSession.Notification, Guid.Empty, Guid.Empty, true, false);
        });

        newSession.PlaybackTask = Task.Run(async () =>
        {
            try
            {
                using var lease = await audio.TryInitializeDefaultPlaybackDeviceSafeAsync();
                if (lease == null) throw new InvalidOperationException("没有可用的音频输出设备");
                using var volumeLease = BashuSystemVolumeGuard.Acquire(conn.Settings.BashuAutoMaximizeVolume, logger);
                using var player = new SoundPlayer(audio.AudioEngine, IAudioService.DefaultAudioFormat, newSession.Buffer);
                player.Volume = (float)settings.Settings.SpeechVolume;
                lease.Value.MasterMixer.AddComponent(player);
                try
                {
                    player.Play();
                    newSession.Stopped.Token.WaitHandle.WaitOne();
                }
                finally
                {
                    lease.Value.MasterMixer.RemoveComponent(player);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "实时 WebSocket 对讲播放异常：会话 {SessionId}", sessionId);
            }
            finally
            {
                newSession.Dispose();
            }
        });
    }

    private void EndSession(long sessionId, bool immediate)
    {
        WsSession? session;
        lock (SessionGate)
        {
            if (ActiveSession == null) return;
            if (sessionId > 0 && ActiveSession.Id != sessionId) return;
            session = ActiveSession;
        }

        if (session == null) return;
        RecordReceivedSession(session.Id);

        if (immediate)
        {
            session.Dispose();
            lock (SessionGate)
            {
                if (ActiveSession == session) ActiveSession = null;
            }
        }
        else
        {
            Task.Run(async () =>
            {
                // 等待尾部缓冲播放完毕后平滑关闭通知
                await Task.Delay(250);
                session.Dispose();
                lock (SessionGate)
                {
                    if (ActiveSession == session) ActiveSession = null;
                }
            });
        }
    }

    private void CloseActiveSession()
    {
        lock (SessionGate)
        {
            if (ActiveSession != null)
            {
                RecordReceivedSession(ActiveSession.Id);
                ActiveSession.Dispose();
                ActiveSession = null;
            }
        }
    }

    public void Dispose()
    {
        lock (ConnectionGate)
        {
            StopConnectionLocked();
        }
    }

    private sealed class WsSession(long id, string author, bool emergency) : IDisposable
    {
        public long Id { get; } = id;
        public string Author { get; } = author;
        public bool Emergency { get; } = emergency;
        public BashuWsAudioBuffer Buffer { get; } = new();
        public NotificationRequest? Notification { get; set; }
        public CancellationTokenSource Stopped { get; } = new();
        public Task? PlaybackTask { get; set; }
        public volatile bool IsActive = true;
        public DateTime LastAudioReceivedAt { get; set; } = DateTime.UtcNow;

        public void Dispose()
        {
            if (!IsActive) return;
            IsActive = false;
            try
            {
                Stopped.Cancel();
            }
            catch { }
            var notif = Notification;
            Notification = null;
            if (notif != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try { notif.Cancel(); } catch { }
                });
            }
            Buffer.Dispose();
        }
    }
}
