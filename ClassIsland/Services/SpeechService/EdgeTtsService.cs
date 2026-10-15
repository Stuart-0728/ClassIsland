using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.SpeechService;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared.Abstraction.Services;

using Edge_tts_sharp;
using Edge_tts_sharp.Model;

using Microsoft.Extensions.Logging;

using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace ClassIsland.Services.SpeechService;

[SpeechProviderInfo("classisland.speech.edgeTts", "EdgeTTS")]
public class EdgeTtsService : ISpeechService
{
    public static readonly string EdgeTtsCacheFolderPath = Path.Combine(CommonDirectories.AppCacheFolderPath, "EdgeTTS");

    public IAudioService AudioService { get; }
    private ILogger<EdgeTtsService> Logger { get; }

    private SettingsService SettingsService { get; }

    private List<eVoice> Voices { get; } = EdgeTts.GetVoice();

    private Queue<EdgeTtsPlayInfo> PlayingQueue { get; } = new();

    //private AudioPlayer? CurrentPlayer { get; set; }

    private bool IsPlaying { get; set; } = false;

    private EdgeTtsPlayInfo? _currentPlayInfo;
    private ISpeechService? _fallbackSpeechService;
    private readonly object _fallbackLock = new();
    
    public EdgeTtsService(IAudioService audioService, ILogger<EdgeTtsService> logger, SettingsService settingsService)
    {
        AudioService = audioService;
        Logger = logger;
        SettingsService = settingsService;
        
        Logger.LogInformation("初始化了EdgeTTS服务。");
    }

    private string GetCachePath(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        var md5 = MD5.HashData(data);
        var md5String = md5.Aggregate("", (current, t) => current + t.ToString("x2"));
        var path = Path.Combine(EdgeTtsCacheFolderPath, SettingsService.Settings.EdgeTtsVoiceName, $"{md5String}");
        var directory = Path.GetDirectoryName(path);
        if (!Directory.Exists(directory) && directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        return path;
    }

    public void EnqueueSpeechQueue(string text)
    {
        Logger.LogInformation("以{}朗读文本：{}", SettingsService.Settings.EdgeTtsVoiceName, text);
        var cancellationTokenSource = new CancellationTokenSource();

        var cache = GetCachePath(text);
        Logger.LogDebug("语音缓存：{}", cache);

        Task<bool>? task = null;
        if (!File.Exists(cache))
        {
            task = GenerateSpeechAsync(text, cache, cancellationTokenSource.Token);
        }
        
        if (cancellationTokenSource.IsCancellationRequested)
        {
            cancellationTokenSource.Dispose();
            return;
        }

        PlayingQueue.Enqueue(new EdgeTtsPlayInfo(cache, cancellationTokenSource, task, text));
        _ = ProcessPlayerList();
    }

    public void ClearSpeechQueue()
    {
        lock (_fallbackLock)
        {
            _currentPlayInfo?.CancellationTokenSource.Cancel();
            _fallbackSpeechService?.ClearSpeechQueue();
        }
        while (PlayingQueue.Count > 0)
        {
            var playInfo = PlayingQueue.Dequeue();
            playInfo.CancellationTokenSource.Cancel();
            playInfo.CancellationTokenSource.Dispose();
        }
    }

    private async Task ProcessPlayerList()
    {
        if (IsPlaying)
            return;
        IsPlaying = true;

        try
        {
            while (PlayingQueue.Count > 0)
            {
                var playInfo = _currentPlayInfo = PlayingQueue.Dequeue();
                try
                {
                    if (playInfo.CancellationTokenSource.IsCancellationRequested)
                        continue;
                    if (playInfo.DownloadTask != null)
                    {
                        Logger.LogDebug("等待下载完成");
                        var result = await playInfo.DownloadTask;
                        if (!result)
                        {
                            Logger.LogWarning("在线语音生成失败，尝试系统语音。");
                            TryFallbackSpeech(playInfo);
                            continue;
                        }
                        Logger.LogDebug("等待下载完成结束");
                    }

                    if (!File.Exists(playInfo.FilePath))
                    {
                        Logger.LogWarning("找不到语音缓存文件：{}", playInfo.FilePath);
                        TryFallbackSpeech(playInfo);
                        continue;
                    }

                    Logger.LogDebug("开始播放 {}", playInfo.FilePath);
                    await AudioService.PlayAudioAsync(playInfo.FilePath,
                        (float)SettingsService.Settings.SpeechVolume, playInfo.CancellationTokenSource.Token);
                    Logger.LogDebug("结束播放 {}", playInfo.FilePath);
                }
                catch (OperationCanceledException)
                {
                    // Cancelling a notification must not start a fallback reading.
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "无法播放在线语音，尝试系统语音。");
                    TryFallbackSpeech(playInfo);
                }
                finally
                {
                    _currentPlayInfo = null;
                    playInfo.CancellationTokenSource.Dispose();
                }
            }
        }
        finally
        {
            _currentPlayInfo = null;
            IsPlaying = false;
        }
    }

    private void TryFallbackSpeech(EdgeTtsPlayInfo playInfo)
    {
        lock (_fallbackLock)
        {
            if (playInfo.CancellationTokenSource.IsCancellationRequested || string.IsNullOrWhiteSpace(playInfo.Text))
                return;
            try
            {
                // Keep this instance separate so notification cancellation can also stop the fallback.
                if (_fallbackSpeechService == null)
                {
                    if (OperatingSystem.IsWindows()) _fallbackSpeechService = new SystemSpeechService();
                    else if (OperatingSystem.IsMacOS()) _fallbackSpeechService = new MacSpeechService();
                    else
                    {
                        Logger.LogWarning("当前平台没有系统语音备用提供方。");
                        return;
                    }
                }
                _fallbackSpeechService.EnqueueSpeechQueue(playInfo.Text);
                Logger.LogInformation("已切换系统语音朗读本次通知。");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "系统备用语音播放失败，请检查本机语音包和音频输出设备。");
            }
        }
    }

    private async Task<bool> GenerateSpeechAsync(string text, string cache, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        var voice = Voices.Find(voice => voice.ShortName == SettingsService.Settings.EdgeTtsVoiceName);
        if (voice == null)
        {
            Logger.LogWarning("找不到 EdgeTTS 语音配置：{}", SettingsService.Settings.EdgeTtsVoiceName);
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var audio = await EdgeSpeechClient.SynthesizeAsync(text, voice.ShortName, timeout.Token);
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllBytesAsync(cache, audio, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation(cancellationToken.IsCancellationRequested ? "已取消获取在线语音。" : "获取在线语音超时。");
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "获取EdgeTTS语音失败。");
            return false;
        }
    }
}
