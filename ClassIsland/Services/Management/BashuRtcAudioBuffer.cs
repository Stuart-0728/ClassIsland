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
            // At most 120 ms stereo audio; discard old samples instead of turning
            // network jitter into a growing, audible delay.
            var excess = Samples.Count + mono.Length * 2 - 11520;
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
                if (Samples.Count >= 3840) // 40 ms initial cushion
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
                // Re-prime quickly after a real gap without replaying old speech.
                if (ConsecutiveUnderflowFrames >= 4)
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
