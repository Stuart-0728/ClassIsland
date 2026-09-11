#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using SoundFlow.Enums;
using SoundFlow.Interfaces;

namespace ClassIsland.Services.Management;

/// <summary>
/// WebSocket 实时对讲 PCM 专用流式缓冲区：
/// 负责 16kHz 16-bit 单声道向 48kHz 32-bit 立体声的硬件对齐转换、小抖动平滑与防积压。
/// </summary>
public sealed class BashuWsAudioBuffer : ISoundDataProvider
{
    private readonly Queue<float> Samples = new();
    private readonly object Gate = new();
    private bool Primed;
    private int ConsecutiveUnderflowFrames;

    public bool Enabled { get; set; } = true;
    public int Position { get; private set; }
    public int Length => 0;
    public bool CanSeek => false;
    public SampleFormat SampleFormat => SampleFormat.F32;
    public int SampleRate => 48000;
    public bool IsDisposed { get; private set; }
#pragma warning disable CS0067
    public event EventHandler<EventArgs>? EndOfStreamReached;
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;
#pragma warning restore CS0067

    public int BufferedSampleCount
    {
        get
        {
            lock (Gate) return Samples.Count;
        }
    }

    public void PushPcm16Mono16k(ReadOnlySpan<byte> pcm16Bytes)
    {
        var sampleCount = pcm16Bytes.Length / 2;
        if (sampleCount <= 0) return;

        lock (Gate)
        {
            if (IsDisposed || !Enabled) return;

            for (var i = 0; i < sampleCount; i++)
            {
                var s = BinaryPrimitives.ReadInt16LittleEndian(pcm16Bytes.Slice(i * 2, 2));
                var f = s / 32768.0f;
                // 3 倍上采样：16kHz -> 48kHz；立体声复制：L 和 R
                Samples.Enqueue(f); Samples.Enqueue(f);
                Samples.Enqueue(f); Samples.Enqueue(f);
                Samples.Enqueue(f); Samples.Enqueue(f);
            }

            // 保持最大 500ms 立体声缓冲区上限（48000 个采样点），避免弱网抖动产生持续累积延迟
            while (Samples.Count > 48000)
            {
                Samples.Dequeue();
            }
        }
    }

    public int ReadBytes(Span<float> buffer)
    {
        buffer.Clear();
        lock (Gate)
        {
            if (IsDisposed) return 0;
            if (!Enabled)
            {
                Samples.Clear();
                Primed = false;
                ConsecutiveUnderflowFrames = 0;
                return buffer.Length;
            }

            if (!Primed)
            {
                // 起播前等待 80ms 抖动缓冲（7680 个采样点），确保首秒与后续网络抖动时平滑衔接
                if (Samples.Count >= 7680)
                {
                    Primed = true;
                    ConsecutiveUnderflowFrames = 0;
                }
                else
                {
                    return buffer.Length;
                }
            }

            if (Samples.Count > 0)
            {
                var count = Math.Min(buffer.Length, Samples.Count);
                for (var i = 0; i < count; i++)
                {
                    buffer[i] = Samples.Dequeue();
                }
                ConsecutiveUnderflowFrames = 0;
            }
            else
            {
                ConsecutiveUnderflowFrames++;
                // 连续多次欠载说明讲话已真正停顿或结束，快速重置起播门控，防止陈旧数据回放
                if (ConsecutiveUnderflowFrames >= 8)
                {
                    Primed = false;
                }
            }

            Position = (Position + buffer.Length) % int.MaxValue;
            return buffer.Length; // 遇到空档输出平滑静音，保持音频设备持续运行不重置
        }
    }

    public void Seek(int offset) => throw new NotSupportedException();

    public void Dispose()
    {
        lock (Gate)
        {
            IsDisposed = true;
            Samples.Clear();
        }
    }
}
