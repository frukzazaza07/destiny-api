using System.Collections.Concurrent;
using StackExchange.Redis;

namespace TarotDestiny.Api.Services;

public interface ICacheLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(string cacheHash, CancellationToken cancellationToken);
}

public sealed class InMemoryCacheLock : ICacheLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IAsyncDisposable?> TryAcquireAsync(string cacheHash, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(cacheHash, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}

public sealed class RedisCacheLock(
    IConnectionMultiplexer connection,
    ILogger<RedisCacheLock> logger) : ICacheLock
{
    private static readonly TimeSpan LeaseTime = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan WaitTime = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan PollTime = TimeSpan.FromMilliseconds(100);
    private readonly InMemoryCacheLock _fallback = new();

    public async Task<IAsyncDisposable?> TryAcquireAsync(string cacheHash, CancellationToken cancellationToken)
    {
        var key = $"tarot:lock:{cacheHash}";
        var token = Guid.NewGuid().ToString("N");
        var deadline = DateTimeOffset.UtcNow.Add(WaitTime);
        try
        {
            var database = connection.GetDatabase();
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (await database.StringSetAsync(key, token, LeaseTime, When.NotExists))
                {
                    return new RedisLease(database, key, token, logger);
                }

                await Task.Delay(PollTime, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis lock failed for {CacheHash}; using an in-process single-flight lock", cacheHash);
            return await _fallback.TryAcquireAsync(cacheHash, cancellationToken);
        }

        return null;
    }

    private sealed class RedisLease : IAsyncDisposable
    {
        private const string ReleaseScript =
            "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";
        private const string RenewScript =
            "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end";
        private int _disposed;
        private readonly CancellationTokenSource _renewalCancellation = new();
        private readonly IDatabase _database;
        private readonly RedisKey _key;
        private readonly RedisValue _token;
        private readonly ILogger _logger;
        private readonly Task _renewalTask;

        public RedisLease(IDatabase database, RedisKey key, RedisValue token, ILogger logger)
        {
            _database = database;
            _key = key;
            _token = token;
            _logger = logger;
            _renewalTask = RenewAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _renewalCancellation.Cancel();
            try
            {
                await _renewalTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when the owner releases the lease.
            }

            try
            {
                await _database.ScriptEvaluateAsync(ReleaseScript, [_key], [_token]);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Redis lock release failed for {LockKey}", _key);
            }

            _renewalCancellation.Dispose();
        }

        private async Task RenewAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(40), _renewalCancellation.Token);
                    var renewed = (long)await _database.ScriptEvaluateAsync(
                        RenewScript,
                        [_key],
                        [_token, (long)LeaseTime.TotalMilliseconds]);
                    if (renewed == 0)
                    {
                        _logger.LogWarning("Redis lock ownership was lost for {LockKey}", _key);
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (_renewalCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Redis lock renewal failed for {LockKey}", _key);
            }
        }
    }
}
