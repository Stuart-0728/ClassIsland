using System;
using System.Collections.Generic;
using SoundFlow.Enums;
using SoundFlow.Interfaces;

namespace ClassIsland.Services.Management;

/// <summary>Bounded live PCM buffer: small initial jitter cushion, never replay a growing backlog.</summary>
public sealed class BashuRtcAudioBuffer : ISoundDataProvider
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
    public event EventHandler<EventArgs>? EndOfStreamReached;
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;

    public void Push(ReadOnlySpan<float> mono)
    {
        lock (Gate)
        {
            if (IsDisposed) return;
            if (!Enabled) { Samples.Clear(); Primed = false; ConsecutiveUnderflowFrames = 0; return; }
            // At most 200 ms stereo audio; discard old samples when the network catches up in bursts.
            var excess = Samples.Count + mono.Length * 2 - 19200;
            while (excess-- > 0 && Samples.Count > 0) Samples.Dequeue();
            foreach (var sample in mono) { Samples.Enqueue(sample); Samples.Enqueue(sample); }
        }
    }
    public int ReadBytes(Span<float> buffer)
    {
        buffer.Clear();
        lock (Gate)
        {
            if (IsDisposed) return 0;
            if (!Enabled) { Samples.Clear(); Primed = false; ConsecutiveUnderflowFrames = 0; return buffer.Length; }
            if (!Primed)
            {
                if (Samples.Count >= 5760) // 60 ms initial cushion
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
                for (var i = 0; i < count; i++) buffer[i] = Samples.Dequeue();
                ConsecutiveUnderflowFrames = 0;
            }
            else
            {
                ConsecutiveUnderflowFrames++;
                // Only drop back to un-primed pre-buffer state if starved continuously for > 200 ms
                if (ConsecutiveUnderflowFrames >= 10)
                {
                    Primed = false;
                }
            }
            Position = (Position + buffer.Length) % int.MaxValue;
            return buffer.Length; // Silence during gaps keeps the audio device running continuously.
        }
    }
    public void Seek(int offset) => throw new NotSupportedException();
    public void Dispose() { lock (Gate) { IsDisposed = true; Samples.Clear(); } }
}
