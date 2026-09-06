using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Enums.Notification;
using ClassIsland.Core.Models.Notification;
using Concentus;
using Microsoft.Extensions.Logging;
using SoundFlow.Components;

namespace ClassIsland.Services.Management;

public sealed class BashuRtcReceiver(IAudioService audio, INotificationHostService notifications, SettingsService settings, ILogger logger)
{
    private readonly ConcurrentDictionary<long, Reception> Sessions = new();
    private readonly object PollingGate = new();
    private BashuPlatformConnection? LastConnection;
    private CancellationTokenSource? PollingStop;
    private Task? PollingTask;
    public Func<long, bool>? AudioStarted;
    public static string HelperPath => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "BashuRtc.exe" : "BashuRtc");
    public bool Available => File.Exists(HelperPath);
    public bool Receiving(long id) => Sessions.TryGetValue(id, out var session) && session.HasAudio && !session.Stopped.IsCancellationRequested;

    public Task PollAsync(BashuPlatformConnection connection)
    {
        if (!Available) return Task.CompletedTask;
        lock (PollingGate)
        {
            if (LastConnection == connection && PollingTask is { IsCompleted: false }) return Task.CompletedTask;
            StopLocked(); LastConnection = connection; PollingStop = new CancellationTokenSource();
            PollingTask = Task.Run(() => PollingLoopAsync(connection, PollingStop.Token));
        }
        return Task.CompletedTask;
    }

    private async Task PollingLoopAsync(BashuPlatformConnection connection, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await PollCoreAsync(connection, token);
                var delay = Sessions.Values.Any(session => !session.HasAudio) ? 120 : Sessions.Count > 0 ? 2000 : 0;
                if (delay > 0) await Task.Delay(delay, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                logger.LogDebug("实时对讲信令暂时不可用：{Type}", error.GetType().Name);
                foreach (var entry in Sessions.ToArray()) if (Sessions.TryRemove(entry.Key, out var reception)) reception.Dispose();
                await Task.Delay(1000, token);
            }
        }
    }

    private async Task PollCoreAsync(BashuPlatformConnection connection, CancellationToken token)
    {
        var receiving = string.Join(",", Sessions.Keys.Where(Receiving));
        var waitMs = Sessions.Count == 0 ? 15000 : 0;
        using var data = JsonDocument.Parse(await connection.GetRtcAsync(receiving, waitMs, token));
        var active = data.RootElement.GetProperty("sessions").EnumerateArray().ToArray();
        foreach (var id in Sessions.Keys.Where(id => active.All(item => item.GetProperty("id").GetInt64() != id)).ToArray())
            if (Sessions.TryRemove(id, out var ended)) ended.Dispose();
        foreach (var item in active)
        {
            var id = item.GetProperty("id").GetInt64();
            if (Sessions.TryGetValue(id, out var current))
            {
                if (item.TryGetProperty("offerCandidates", out var candidates)) await current.AddCandidatesAsync(candidates, token);
                continue;
            }
            if (item.GetProperty("state").GetString() is "failed" or "closed") continue;
            var reception = new Reception(id, item.GetProperty("author").GetString() ?? "平台教师",
                item.GetProperty("priority").GetString() == "emergency");
            if (!Sessions.TryAdd(id, reception)) { reception.Dispose(); continue; }
            var offerCandidates = item.TryGetProperty("offerCandidates", out var initialCandidates) && initialCandidates.ValueKind == JsonValueKind.Array
                ? initialCandidates.Clone()
                : JsonSerializer.SerializeToElement(Array.Empty<object>());
            reception.RememberCandidates(offerCandidates);
            var input = JsonSerializer.Serialize(new { offer = item.GetProperty("offer").GetString(), offerCandidates, iceServers = data.RootElement.GetProperty("iceServers").Clone() });
            _ = RunAsync(connection, reception, input);
        }
    }
    public void Stop() { lock (PollingGate) StopLocked(); }
    private void StopLocked()
    {
        PollingStop?.Cancel(); PollingStop?.Dispose(); PollingStop = null; PollingTask = null; LastConnection = null;
        foreach (var entry in Sessions.ToArray()) if (Sessions.TryRemove(entry.Key, out var session)) session.Dispose();
    }

    private async Task RunAsync(BashuPlatformConnection connection, Reception session, string input)
    {
        Task? playback = null;
        try
        {
            var process = new Process { StartInfo = new ProcessStartInfo(HelperPath) {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true,
            } };
            session.Process = process;
            process.ErrorDataReceived += (_, _) => { }; // Never log SDP/ICE credentials.
            if (!process.Start()) throw new InvalidOperationException("无法启动实时对讲组件");
            session.ProcessStarted = true;
            process.BeginErrorReadLine();
            await process.StandardInput.WriteLineAsync(input);
            await process.StandardInput.FlushAsync();
            using var decoder = OpusCodecFactory.CreateDecoder(48000, 1);
            var decoded = new float[5760];
            while (!session.Stopped.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(session.Stopped.Token);
                if (line == null) break;
                if (line.Length > 65536) throw new InvalidDataException("实时音频消息过长");
                using var message = JsonDocument.Parse(line);
                var root = message.RootElement;
                switch (root.GetProperty("type").GetString())
                {
                    case "answer":
                        await connection.SendRtcAsync(session.Id, new { answer = root.GetProperty("sdp").GetString() });
                        break;
                    case "candidate":
                        await connection.SendRtcAsync(session.Id, new { candidate = root.GetProperty("candidate").Clone() });
                        break;
                    case "state":
                        var state = root.GetProperty("state").GetString();
                        if (state is "failed" or "closed") throw new IOException("实时连接中断");
                        if (state == "disconnected") logger.LogInformation("实时对讲连接短暂波动自愈中：{SessionId}", session.Id);
                        break;
                    case "audio":
                        var packet = Convert.FromBase64String(root.GetProperty("data").GetString()!);
                        if (packet.Length > 4096) continue;
                        var count = decoder.Decode(packet.AsSpan(), decoded.AsSpan(), 5760, false);
                        if (!session.HasAudio)
                        {
                            playback = StartPlaybackAsync(session);
                            await session.AudioReady.Task;
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                session.Stopped.Token.ThrowIfCancellationRequested();
                                session.HasAudio = true;
                                var previouslyPresented = AudioStarted?.Invoke(session.Id) == true;
                                session.Notification = new NotificationRequest {
                                    MaskContent = NotificationContent.CreateTwoIconsMask($"{(session.Emergency ? "紧急广播" : "实时对讲")} · {session.Author}"),
                                    OverlayContent = NotificationContent.CreateSimpleTextContent($"{session.Author} 正在讲话", content => content.Duration = TimeSpan.FromMinutes(20)),
                                    IsPriorityOverride = true, PriorityOverride = session.Emergency ? 200 : 50,
                                    RequestNotificationSettings = { IsSettingsEnabled = true, IsSpeechEnabled = false, IsNotificationSoundEnabled = false, IsNotificationTopmostEnabled = true }
                                };
                                if (previouslyPresented) session.Notification.MaskContent.Duration = TimeSpan.FromMilliseconds(1);
                                notifications.ShowNotification(session.Notification, Guid.Empty, Guid.Empty, true, false);
                            });
                            await connection.SendRtcAsync(session.Id, new { state = "connected" });
                        }
                        session.Buffer.Enabled = session.Notification?.State == NotificationState.Playing;
                        session.Buffer.Push(decoded.AsSpan(0, count));
                        break;
                    case "error": throw new IOException("实时音频组件连接失败");
                }
            }
        }
        catch (OperationCanceledException) when (session.Stopped.IsCancellationRequested) { }
        catch (Exception error) { logger.LogWarning("实时对讲回退至兼容通道：{Type}", error.GetType().Name); }
        finally
        {
            session.HasAudio = false;
            session.Dispose();
            if (playback != null) try { await playback; } catch { }
            session.Process?.Dispose();
            try { await connection.SendRtcAsync(session.Id, new { state = "failed" }); } catch { }
        }
    }
    private Task StartPlaybackAsync(Reception session) => Task.Run(async () =>
    {
        try
        {
            using var lease = await audio.TryInitializeDefaultPlaybackDeviceSafeAsync();
            if (lease == null) throw new InvalidOperationException("没有可用的音频输出设备");
            using var volumeLease = BashuSystemVolumeGuard.Acquire(LastConnection?.Settings.BashuAutoMaximizeVolume == true, logger);
            using var player = new SoundPlayer(audio.AudioEngine, IAudioService.DefaultAudioFormat, session.Buffer);
            player.Volume = (float)settings.Settings.SpeechVolume;
            lease.Value.MasterMixer.AddComponent(player);
            try {
                player.Play();
                session.AudioReady.TrySetResult();
                session.Stopped.Token.WaitHandle.WaitOne();
            } finally { lease.Value.MasterMixer.RemoveComponent(player); }
        }
        catch (Exception error) { session.AudioReady.TrySetException(error); throw; }
    });
    private sealed class Reception(long id, string author, bool emergency) : IDisposable
    {
        public long Id { get; } = id;
        public string Author { get; } = author;
        public bool Emergency { get; } = emergency;
        public bool HasAudio;
        public readonly CancellationTokenSource Stopped = new();
        public readonly TaskCompletionSource AudioReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly BashuRtcAudioBuffer Buffer = new();
        public NotificationRequest? Notification;
        public Process? Process;
        public volatile bool ProcessStarted;
        private readonly HashSet<string> SentCandidates = new();
        private readonly SemaphoreSlim InputGate = new(1, 1);
        public void RememberCandidates(JsonElement candidates)
        {
            if (candidates.ValueKind != JsonValueKind.Array) return;
            foreach (var candidate in candidates.EnumerateArray()) SentCandidates.Add(candidate.GetRawText());
        }
        public async Task AddCandidatesAsync(JsonElement candidates, CancellationToken token)
        {
            if (candidates.ValueKind != JsonValueKind.Array || !ProcessStarted || Process == null) return;
            try { if (Process.HasExited) return; } catch { return; }
            foreach (var candidate in candidates.EnumerateArray())
            {
                var raw = candidate.GetRawText();
                if (!SentCandidates.Add(raw)) continue;
                await InputGate.WaitAsync(token);
                try
                {
                    await Process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { candidate = candidate.Clone() }));
                    await Process.StandardInput.FlushAsync(token);
                }
                finally { InputGate.Release(); }
            }
        }
        public void Dispose() {
            if (Stopped.IsCancellationRequested) return;
            Stopped.Cancel();
            Dispatcher.UIThread.Post(() => Notification?.Cancel());
            try { if (Process is { HasExited: false }) Process.Kill(true); } catch { }
            Buffer.Dispose();
            InputGate.Dispose();
        }
    }
}
