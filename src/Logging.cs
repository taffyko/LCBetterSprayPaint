using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;

/// Wrapper around ManualLogSource that suppresses repeated logs within a short timeframe
/// to help reduce log spam when things unexpectedly break due to future updates/mod conflicts
internal class QuietLogSource : ILogSource, IDisposable  {
    ManualLogSource innerLog;
    public string SourceName { get; }
    public QuietLogSource(string sourceName) {
        innerLog = new ManualLogSource(sourceName);
        SourceName = sourceName;
    }

    static readonly TimeSpan baseRecentLogTimeout = TimeSpan.FromMilliseconds(5000);

    /// Timestamp when last logged, number of times encountered since
    Dictionary<string, (DateTime, int)> recentLogs = new Dictionary<string, (DateTime, int)>();
    int logsSuppressed = 0;
    int lastReportedLogsSuppressed = 0;

    #if DEBUG
    bool suppressLogs = false;
    #else
    bool suppressLogs = true;
    #endif

    /// Minimum number of repeat logs that must be encountered before suppressing
    const int minimumBeforeSuppressal = 5;

    public event EventHandler<LogEventArgs> LogEvent {
        add { innerLog.LogEvent += value; }
        remove { innerLog.LogEvent -= value; }
    }

    public void Dispose() {
        innerLog.Dispose();
    }

    object AddStackTrace(object data) {
        var str = data as string;
        if (str != null) {
            var stackTrace = new System.Diagnostics.StackTrace(3, true);
            return $"{str}\n{stackTrace}";
        }
        return data;
    }

    public void Log(LogLevel level, object data, bool addStackTrace = false)
    {
        // Purge recentLogs to prevent it from growing indefinitely
        if (recentLogs.Count > 30) {
            foreach (var kvp in recentLogs.Where((kvp) => (DateTime.UtcNow - kvp.Value.Item1) > baseRecentLogTimeout).ToArray()) {
                recentLogs.Remove(kvp.Key);
            }
        }
        var str = data as string;
        if (str != null) {
            if (recentLogs.TryGetValue(str, out var val)) {
                var recentLogTimeout = val.Item2 < 100 ? baseRecentLogTimeout : baseRecentLogTimeout * 2;
                if ((DateTime.UtcNow - val.Item1) > recentLogTimeout) {
                    // Timeout since last encounter expired, don't suppress. Reset timeout and counter.
                    recentLogs[str] = (DateTime.UtcNow, val.Item2 > minimumBeforeSuppressal ? minimumBeforeSuppressal : 0);
                } else {
                    // Update counter
                    recentLogs[str] = (val.Item1, val.Item2 + 1);
                    if (val.Item2 >= minimumBeforeSuppressal) {
                        ++logsSuppressed;
                        if (suppressLogs) {
                            return;
                        }
                    }
                }
            } else {
                // New log encountered, add to recentLogs
                recentLogs.Add(str, (DateTime.UtcNow, 0));
            }
        }
        innerLog.Log(level, addStackTrace ? AddStackTrace(data) : data);
        if (suppressLogs && logsSuppressed > lastReportedLogsSuppressed) {
            innerLog.LogInfo($"{logsSuppressed - lastReportedLogsSuppressed} duplicate logs suppressed");
            lastReportedLogsSuppressed = logsSuppressed;
        }
    }

    public void LogInfo(object data)
    {
        Log(LogLevel.Info, data);
    }

    public void LogError(object data, bool addStackTrace = true)
    {
        Log(LogLevel.Error, data, addStackTrace);
    }

    public void LogWarning(object data, bool addStackTrace = true)
    {
        Log(LogLevel.Warning, data, addStackTrace);
    }

    /// Helper for local testing only (making do until I can get proper debugging support working on this version of Unity)
    public void LogLoud(string data)
    {
        for (int i = 0; i < 25; ++i) {
            Log(LogLevel.Info, $"{data} ({i})");
        }
    }

}