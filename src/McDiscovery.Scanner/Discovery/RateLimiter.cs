using System.Diagnostics;

namespace McDiscovery.Scanner.Discovery;

/// <summary>
/// Paces work to an average of <c>rate</c> operations per second. Timer resolution differs per OS (about
/// 1 ms on Linux, 15 ms on Windows), so it sleeps in bursts and compensates using a running total.
/// Not thread safe: use one per sending loop.
/// </summary>
public sealed class RateLimiter
{
    private readonly double _rate;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _count;

    public RateLimiter(double ratePerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ratePerSecond);
        _rate = ratePerSecond;
    }

    public async ValueTask WaitAsync(CancellationToken ct)
    {
        _count++;
        var due = TimeSpan.FromSeconds(_count / _rate);
        var ahead = due - _clock.Elapsed;
        if (ahead > TimeSpan.FromMilliseconds(2))
        {
            await Task.Delay(ahead, ct);
        }
        else if (ahead < TimeSpan.FromSeconds(-1))
        {
            // We fell behind (e.g. the process was paused): don't burst to catch up.
            _count = (long)(_clock.Elapsed.TotalSeconds * _rate);
        }
    }
}
