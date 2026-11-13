using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;

namespace ClassIsland.Services.Management;

internal sealed class BashuPollingPolicy(TimeSpan normalInterval, Func<DateTimeOffset>? clock = null, Func<double>? jitter = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Func<double> _jitter = jitter ?? (() => Random.Shared.NextDouble());
    private int _failures;
    private TimeSpan _serverMinimum;
    private DateTimeOffset _nextAttempt;
    public TimeSpan Interval { get; private set; } = normalInterval;
    public bool IsDue => _clock() >= _nextAttempt;

    public void Reset()
    {
        _failures = 0;
        _serverMinimum = default;
        _nextAttempt = default;
        Interval = normalInterval;
    }

    public void Succeeded(HttpResponseMessage response)
    {
        _failures = 0;
        ReadMinimum(response);
        Schedule(Max(normalInterval, _serverMinimum));
    }

    public void Failed(HttpResponseMessage? response = null)
    {
        _failures = Math.Min(_failures + 1, 6);
        ReadMinimum(response);
        var retry = response?.Headers.RetryAfter;
        var requested = retry?.Delta ?? (retry?.Date - _clock()) ?? TimeSpan.Zero;
        var backoff = TimeSpan.FromSeconds(Math.Min(60, normalInterval.TotalSeconds * Math.Pow(2, _failures - 1)));
        Schedule(Max(Max(backoff, _serverMinimum), requested));
    }

    private void ReadMinimum(HttpResponseMessage? response)
    {
        if (response?.Headers.TryGetValues("X-Poll-Interval-Seconds", out var values) == true &&
            double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
            double.IsFinite(seconds) && seconds is > 0 and <= 3600)
            _serverMinimum = TimeSpan.FromSeconds(seconds);
    }

    private void Schedule(TimeSpan delay)
    {
        // A small positive margin avoids repeatedly hitting the server's boundary;
        // jitter prevents classrooms from all retrying at the same instant.
        Interval = delay + TimeSpan.FromMilliseconds(250 + Math.Clamp(_jitter(), 0, 1) * 250);
        _nextAttempt = _clock() + Interval;
    }

    private static TimeSpan Max(TimeSpan first, TimeSpan second) => first > second ? first : second;
}
