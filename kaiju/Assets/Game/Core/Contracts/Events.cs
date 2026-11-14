using System;

namespace Game.Core
{
    /// <summary>Marker for bus events. Implement on an unmanaged struct (no strings, arrays or object references).</summary>
    public interface IEvent { }

    /// <summary>Receives events of one type. Implemented by a class; the bus holds it by reference.</summary>
    public interface IEventHandler<T> where T : unmanaged, IEvent
    {
        void Handle(in T evt);
    }

    /// <summary>
    /// Typed, synchronous event bus (contract v1). Rules:
    /// order = subscription order per event type; nested publishes are queued FIFO and drained before the
    /// outermost Publish returns; a handler receives exactly the events published after it subscribed and
    /// before it unsubscribed; a throwing handler is reported to the error sink and the others still run.
    /// </summary>
    public interface IEventBus
    {
        void Subscribe<T>(IEventHandler<T> handler) where T : unmanaged, IEvent;
        bool Unsubscribe<T>(IEventHandler<T> handler) where T : unmanaged, IEvent;
        void Publish<T>(in T evt) where T : unmanaged, IEvent;
        int SubscriberCount<T>() where T : unmanaged, IEvent;
        /// <summary>Number of handler exceptions caught since the bus was created.</summary>
        int HandlerExceptionCount { get; }
    }

    /// <summary>Thrown by the outermost Publish when one cascade exceeded the configured event cap.</summary>
    public sealed class EventCascadeLimitException : Exception
    {
        public EventCascadeLimitException(string message) : base(message) { }
    }

    public enum CoreErrorKind
    {
        HandlerException = 1,
        CascadeLimit = 2,
        WrongThread = 3,
        ConfigMissing = 4,   // warning: defaults (everything off) are used
        ConfigMalformed = 5,
        SaveError = 6,
        FixedStepMismatch = 7, // warning: the adapter overrode Unity's value
    }

    /// <summary>One reported problem. Allocated only on the error path.</summary>
    public sealed class CoreError
    {
        public CoreError(CoreErrorKind kind, string message, Type eventType = null, string subscriber = null, Exception exception = null)
        {
            Kind = kind; Message = message; EventType = eventType; Subscriber = subscriber; Exception = exception;
        }
        public CoreErrorKind Kind { get; }
        public string Message { get; }
        public Type EventType { get; }
        public string Subscriber { get; }
        public Exception Exception { get; }
        public bool IsWarning => Kind == CoreErrorKind.ConfigMissing || Kind == CoreErrorKind.FixedStepMismatch;
        public override string ToString() => Kind + ": " + Message;
    }

    /// <summary>Where Core reports problems it does not throw. Tests collect; the Unity adapter logs.</summary>
    public interface ICoreErrorSink
    {
        void Report(CoreError error);
    }
}
