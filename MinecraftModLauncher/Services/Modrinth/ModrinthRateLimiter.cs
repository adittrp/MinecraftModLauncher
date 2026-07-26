using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace MinecraftModLauncher.Services;

// rate limit: MAX 300 requests per minute, comply with modrinth's rate limit policy. space them out instead of hard stop
internal class ModrinthRateLimiter
{
    private readonly object _lock = new();
    private TimeSpan _minInterval = TimeSpan.FromMinutes(1) / 300;
    private DateTimeOffset _nextAllowedTime = DateTimeOffset.UtcNow;

    public async Task waitForSlot()
    {
        TimeSpan delay;

        lock (_lock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset scheduled = now > _nextAllowedTime ? now : _nextAllowedTime;
            delay = scheduled - now;
            _nextAllowedTime = scheduled + _minInterval;
        }
        
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay);
    }

    public void updateFromHeaders(HttpResponseHeaders headers)
    {
        lock (_lock)
        {
            if (headers.TryGetValues("X-Ratelimit-Limit", out var limitValues)
                && int.TryParse(limitValues.FirstOrDefault(), out int limit) && limit > 0) {
                _minInterval = TimeSpan.FromMinutes(1) / limit;
            }

            if (headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues)
                && int.TryParse(remainingValues.FirstOrDefault(), out int remaining)
                && headers.TryGetValues("X-RateLimit-Reset", out var resetValues)
                && int.TryParse(resetValues.FirstOrDefault(), out int resetSeconds))
            {
                if (remaining <= 5)
                {
                    DateTimeOffset resetAt = DateTimeOffset.UtcNow.AddSeconds(resetSeconds);
                    if (resetAt > _nextAllowedTime)
                        _nextAllowedTime = resetAt;
                }
            }
        }
    }
}