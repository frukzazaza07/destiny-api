using System.Collections.Concurrent;
using System.Security.Cryptography;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public interface IDeckService
{
    ShuffleDeckResponse CreateSession(string spread);
    ResolveDeckResponse? Resolve(string sessionId, IReadOnlyList<int> selectedIndexes);
}

public sealed class DeckService(ITarotCatalog catalog) : IDeckService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, DeckSession> _sessions = new();
    private long _nextCleanupUnixMilliseconds;

    public ShuffleDeckResponse CreateSession(string spread)
    {
        var normalizedSpread = SpreadIds.Normalize(spread);
        var now = DateTimeOffset.UtcNow;
        RemoveExpiredSessions(now);

        var deck = catalog.AllCards.Select(card => card.Id).ToList();

        Shuffle(deck);

        var sessionId = Guid.NewGuid().ToString("N");
        _sessions[sessionId] = new DeckSession(
            sessionId,
            normalizedSpread,
            deck,
            now.Add(SessionLifetime));

        return new ShuffleDeckResponse(
            sessionId,
            normalizedSpread,
            deck.Count,
            ReadingPositions.ForSpread(normalizedSpread).Count);
    }

    public ResolveDeckResponse? Resolve(string sessionId, IReadOnlyList<int> selectedIndexes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(selectedIndexes);

        var now = DateTimeOffset.UtcNow;
        RemoveExpiredSessions(now);

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return null;
        }

        lock (session.Gate)
        {
            if (session.ExpiresAt <= now)
            {
                _sessions.TryRemove(sessionId, out _);
                return null;
            }

            var positions = ReadingPositions.ForSpread(session.Spread);
            ValidateSelection(selectedIndexes, positions.Count, session.Deck.Count);

            if (session.ResolvedResponse is not null)
            {
                if (session.ResolvedIndexes!.SequenceEqual(selectedIndexes))
                {
                    return session.ResolvedResponse;
                }

                throw new ArgumentException(
                    "Deck session has already been resolved with a different selection.",
                    nameof(selectedIndexes));
            }

            var cards = selectedIndexes
                .Select((index, i) =>
                {
                    var cardId = session.Deck[index];
                    var orientation = RandomNumberGenerator.GetInt32(2) == 0
                        ? Orientation.UPRIGHT
                        : Orientation.REVERSED;
                    return new SelectedCard(positions[i], cardId, orientation);
                })
                .ToArray();

            session.ResolvedIndexes = selectedIndexes.ToArray();
            session.ResolvedResponse = new ResolveDeckResponse(sessionId, session.Spread, cards);
            return session.ResolvedResponse;
        }
    }

    private static void ValidateSelection(
        IReadOnlyList<int> selectedIndexes,
        int requiredCount,
        int deckCount)
    {
        if (selectedIndexes.Count != requiredCount)
        {
            throw new ArgumentException(
                $"This spread requires exactly {requiredCount} selected index(es).",
                nameof(selectedIndexes));
        }

        if (selectedIndexes.Distinct().Count() != selectedIndexes.Count)
        {
            throw new ArgumentException("Selected indexes must not repeat.", nameof(selectedIndexes));
        }

        if (selectedIndexes.Any(index => index < 0 || index >= deckCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectedIndexes),
                $"Selected indexes must be between 0 and {deckCount - 1}.");
        }
    }

    private void RemoveExpiredSessions(DateTimeOffset now)
    {
        var nowMilliseconds = now.ToUnixTimeMilliseconds();
        var nextCleanup = Volatile.Read(ref _nextCleanupUnixMilliseconds);
        if (nowMilliseconds < nextCleanup)
        {
            return;
        }

        var newNextCleanup = now.Add(CleanupInterval).ToUnixTimeMilliseconds();
        if (Interlocked.CompareExchange(
                ref _nextCleanupUnixMilliseconds,
                newNextCleanup,
                nextCleanup) != nextCleanup)
        {
            return;
        }

        foreach (var (sessionId, session) in _sessions)
        {
            if (session.ExpiresAt <= now)
            {
                _sessions.TryRemove(sessionId, out _);
            }
        }
    }

    private static void Shuffle<T>(IList<T> values)
    {
        for (var i = values.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }

    private sealed class DeckSession(
        string sessionId,
        string spread,
        IReadOnlyList<string> deck,
        DateTimeOffset expiresAt)
    {
        public string SessionId { get; } = sessionId;
        public string Spread { get; } = spread;
        public IReadOnlyList<string> Deck { get; } = deck;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public object Gate { get; } = new();
        public int[]? ResolvedIndexes { get; set; }
        public ResolveDeckResponse? ResolvedResponse { get; set; }
    }
}
