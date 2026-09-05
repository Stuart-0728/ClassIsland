using System;
using System.Collections.Generic;
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
    private readonly Dictionary<long, Reception> Sessions = new();
    private BashuPlatformConnection? LastConnection;
    public Func<long, bool>? AudioStarted;
    public static string HelperPath => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "BashuRtc.exe" : "BashuRtc");
    public bool Available => File.Exists(HelperPath);
    public bool Receiving(long id) => Sessions.TryGetValue(id, out var session) && session.HasAudio && !session.Stopped.IsCancellationRequested;

    public async Task PollAsync(BashuPlatformConnection connection)
    {
        if (!Available) return;
        if (LastConnection != connection) { Stop(); LastConnection = connection; }
        JsonDocument data;
        try { data = JsonDocument.Parse(await connection.GetRtcAsync(string.Join(",", Sessions.Keys.Where(Receiving)))); }
        catch { Stop(); return; } // Revocation/network loss must not leave a background live receiver.
        using var dataScope = data;
        var active = data.RootElement.GetProperty("sessions").EnumerateArray().ToArray();
        foreach (var id in Sessions.Keys.Where(id => active.All(item => item.GetProperty("id").GetInt64() != id)).ToArray())
        { Sessions[id].Dispose(); Sessions.Remove(id); }
        foreach (var item in active)
        {
            var id = item.GetProperty("id").GetInt64();
            if (Sessions.ContainsKey(id) || item.GetProperty("state").GetString() is "failed" or "closed") continue;
            var reception = new Reception(id, item.GetProperty("author").GetString() ?? "平台教师",
                item.GetProperty("priority").GetString() == "emergency");
            Sessions[id] = reception;
            var input = JsonSerializer.Serialize(new { offer = item.GetProperty("offer").GetString(), iceServers = data.RootElement.GetProperty("iceServers").Clone() });
            _ = RunAsync(connection, reception, input);
        }
    }
    public void Stop() { foreach (var session in Sessions.Values) session.Dispose(); Sessions.Clear(); }

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
        public void Dispose() {
            if (Stopped.IsCancellationRequested) return;
            Stopped.Cancel();
            Dispatcher.UIThread.Post(() => Notification?.Cancel());
            try { if (Process is { HasExited: false }) Process.Kill(true); } catch { }
            Buffer.Dispose();
        }
    }
}
