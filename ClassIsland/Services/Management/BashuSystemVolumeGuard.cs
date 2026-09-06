using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace ClassIsland.Services.Management;

/// <summary>
/// Temporarily raises the Windows default render endpoint while one or more
/// platform broadcasts are active, then restores the exact previous state.
/// </summary>
public static class BashuSystemVolumeGuard
{
    private static readonly object Gate = new();
    private static int LeaseCount;
    private static float PreviousVolume;
    private static bool PreviousMute;
    private static string? DeviceId;

    public static IDisposable Acquire(bool enabled, ILogger logger)
    {
        if (!enabled || !OperatingSystem.IsWindows()) return EmptyLease.Instance;
        lock (Gate)
        {
            try
            {
                if (LeaseCount == 0)
                {
                    using var enumerator = new MMDeviceEnumerator();
                    using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    DeviceId = device.ID;
                    PreviousVolume = device.AudioEndpointVolume.MasterVolumeLevelScalar;
                    PreviousMute = device.AudioEndpointVolume.Mute;
                    device.AudioEndpointVolume.Mute = false;
                    device.AudioEndpointVolume.MasterVolumeLevelScalar = 1.0f;
                }
                LeaseCount++;
                return new VolumeLease(logger);
            }
            catch (Exception error)
            {
                logger.LogWarning("无法临时提升系统音量：{Type}", error.GetType().Name);
                DeviceId = null; LeaseCount = 0;
                return EmptyLease.Instance;
            }
        }
    }

    private static void Release(ILogger logger)
    {
        lock (Gate)
        {
            if (LeaseCount <= 0 || --LeaseCount > 0) return;
            try
            {
                if (string.IsNullOrWhiteSpace(DeviceId)) return;
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDevice(DeviceId);
                device.AudioEndpointVolume.MasterVolumeLevelScalar = PreviousVolume;
                device.AudioEndpointVolume.Mute = PreviousMute;
            }
            catch (Exception error)
            {
                logger.LogWarning("无法恢复播报前系统音量：{Type}", error.GetType().Name);
            }
            finally { DeviceId = null; }
        }
    }

    private sealed class VolumeLease(ILogger logger) : IDisposable
    {
        private int Disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref Disposed, 1) == 0) Release(logger);
        }
    }

    private sealed class EmptyLease : IDisposable
    {
        public static readonly EmptyLease Instance = new();
        public void Dispose() { }
    }
}
