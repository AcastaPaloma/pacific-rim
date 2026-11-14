using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.Unity
{
    /// <summary>Logs Core errors to the Unity console: once per (kind, event type, subscriber), every one counted.</summary>
    public sealed class UnityLogErrorSink : ICoreErrorSink
    {
        private readonly HashSet<string> _logged = new HashSet<string>();
        private readonly Dictionary<CoreErrorKind, int> _counts = new Dictionary<CoreErrorKind, int>();

        public int Count { get; private set; }
        public int CountOf(CoreErrorKind kind) => _counts.TryGetValue(kind, out int n) ? n : 0;

        public void Report(CoreError error)
        {
            Count++;
            _counts[error.Kind] = CountOf(error.Kind) + 1;
            string key = error.Kind + "|" + (error.EventType != null ? error.EventType.FullName : "") + "|" + error.Subscriber
                         + (error.EventType == null ? "|" + error.Message : "");
            if (!_logged.Add(key)) return;
            string text = "[Core] " + error;
            if (error.IsWarning) Debug.LogWarning(text);
            else Debug.LogError(error.Exception != null ? text + "\n" + error.Exception : text);
        }
    }
}
