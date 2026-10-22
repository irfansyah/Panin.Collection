using Microsoft.Extensions.Options;
using Panin.Application.Sessions;

namespace Panin.Infrastructure.Sessions;

public sealed class UserSessionRegistry(IOptions<SessionOptions> options, TimeProvider time) : IUserSessionRegistry
{
    private sealed class Entry(string groupId, string sessionId, string host, DateTimeOffset lastSeen)
    {
        public string GroupId { get; } = groupId;

        public string SessionId { get; } = sessionId;

        public string Host { get; } = host;

        public DateTimeOffset LastSeen { get; set; } = lastSeen;
    }

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _activeWindow = TimeSpan.FromSeconds(options.Value.AbandonedAfterSeconds);

    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public bool HasActiveSession(string userId, string? exceptSessionId = null)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(userId, out var e)
                && e.SessionId != exceptSessionId
                && time.GetUtcNow() - e.LastSeen < _activeWindow;
        }
    }

    public SessionStartResult TryStart(string userId, string groupId, string sessionId, string host)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            if (_entries.TryGetValue(userId, out var e) && e.SessionId != sessionId && now - e.LastSeen < _activeWindow)
            {
                return SessionStartResult.BlockedByActiveSession;
            }

            _entries[userId] = new Entry(groupId, sessionId, host, now);
            return SessionStartResult.Started;
        }
    }

    public bool Validate(string userId, string groupId, string sessionId, string host)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(userId, out var e))
            {
                // Server direstart / sesi sempat dilepas: pulihkan sesi dari cookie yang sah.
                _entries[userId] = new Entry(groupId, sessionId, host, time.GetUtcNow());
                return true;
            }

            return e.SessionId == sessionId;
        }
    }

    public void Touch(string userId, string sessionId)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(userId, out var e) && e.SessionId == sessionId)
            {
                e.LastSeen = time.GetUtcNow();
            }
        }
    }

    public bool Release(string userId, string sessionId)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(userId, out var e) && e.SessionId == sessionId)
            {
                _entries.Remove(userId);
                return true;
            }

            return false;
        }
    }

    public IReadOnlyList<ExpiredSession> ReapExpired(TimeSpan maxAge)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            List<ExpiredSession>? expired = null;
            foreach (var (user, e) in _entries)
            {
                if (now - e.LastSeen > maxAge)
                {
                    (expired ??= []).Add(new ExpiredSession(user, e.GroupId, e.SessionId, e.Host));
                }
            }

            if (expired is null)
            {
                return [];
            }

            foreach (var x in expired)
            {
                _entries.Remove(x.UserId);
            }

            return expired;
        }
    }
}
