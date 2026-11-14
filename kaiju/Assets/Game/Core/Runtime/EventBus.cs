using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Core
{
    /// <summary>
    /// Synchronous typed event bus. See IEventBus for the rules. Owned by the thread that created it.
    /// Steady-state Publish allocates nothing: events stay in per-type struct ring buffers, handlers are
    /// interface calls, lookup is a Dictionary keyed by typeof(T). Subscribe/Unsubscribe and the first
    /// publish of a new type (or buffer growth) may allocate.
    /// </summary>
    public sealed class EventBus : IEventBus
    {
        private readonly Dictionary<Type, Channel> _channels = new Dictionary<Type, Channel>();
        private readonly ICoreErrorSink _errors;
        private readonly int _cascadeCap;
        private readonly int _ownerThread;

        private Channel[] _queue = new Channel[16];     // FIFO of channels, one entry per queued event
        private int _queueHead, _queueCount;
        private Channel[] _touched = new Channel[16];   // channels with events in the current cascade
        private int _touchedCount;
        private Channel[] _dirty = new Channel[8];      // channels with removals deferred until the cascade ends
        private int _dirtyCount;
        private bool _dispatching, _aborted;
        private int _cascadeEvents;
        private long _publishSeq;
        private string _cascadeRoot, _abortMessage;

        public EventBus(ICoreErrorSink errors, int maxEventsPerCascade)
        {
            _errors = errors ?? throw new ArgumentNullException(nameof(errors));
            if (maxEventsPerCascade < 1) throw new ArgumentOutOfRangeException(nameof(maxEventsPerCascade));
            _cascadeCap = maxEventsPerCascade;
            _ownerThread = Environment.CurrentManagedThreadId;
        }

        public int HandlerExceptionCount { get; private set; }

        public void Subscribe<T>(IEventHandler<T> handler) where T : unmanaged, IEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            CheckThread(typeof(T));
            GetChannel<T>().Add(handler, _publishSeq);
        }

        public bool Unsubscribe<T>(IEventHandler<T> handler) where T : unmanaged, IEvent
        {
            CheckThread(typeof(T));
            if (handler == null || !_channels.TryGetValue(typeof(T), out var c)) return false;
            var ch = (Channel<T>)c;
            if (!ch.Remove(handler, compactNow: !_dispatching)) return false;
            if (_dispatching && !ch.Dirty) { ch.Dirty = true; Push(ref _dirty, ref _dirtyCount, ch); }
            return true;
        }

        public int SubscriberCount<T>() where T : unmanaged, IEvent =>
            _channels.TryGetValue(typeof(T), out var c) ? ((Channel<T>)c).LiveCount : 0;

        public void Publish<T>(in T evt) where T : unmanaged, IEvent
        {
            CheckThread(typeof(T));
            var ch = GetChannel<T>();
            if (_aborted) return; // cascade already stopped: further nested publishes are dropped
            if (_cascadeEvents >= _cascadeCap) { Abort(typeof(T)); return; }
            if (!_dispatching) _cascadeRoot = typeof(T).Name;
            _cascadeEvents++;
            if (ch.CascadeCount++ == 0) Push(ref _touched, ref _touchedCount, ch);
            ch.Enqueue(in evt, ++_publishSeq);
            EnqueueChannel(ch);
            if (_dispatching) return;

            _dispatching = true;
            while (_queueCount > 0)
            {
                var next = _queue[_queueHead];
                _queue[_queueHead] = null;
                _queueHead = (_queueHead + 1) & (_queue.Length - 1);
                _queueCount--;
                next.DispatchNext(this);
            }
            _dispatching = false;
            EndCascade();
        }

        internal void ReportHandlerException(Type eventType, object handler, int index, Exception ex)
        {
            HandlerExceptionCount++;
            string who = handler.GetType().FullName + "#" + index;
            _errors.Report(new CoreError(CoreErrorKind.HandlerException,
                "Handler " + who + " threw while handling " + eventType.FullName + ": " + ex.GetType().Name + ": " + ex.Message,
                eventType, who, ex));
        }

        private Channel<T> GetChannel<T>() where T : unmanaged, IEvent
        {
            if (_channels.TryGetValue(typeof(T), out var c)) return (Channel<T>)c;
            var ch = new Channel<T>();
            _channels.Add(typeof(T), ch);
            return ch;
        }

        private void EnqueueChannel(Channel ch)
        {
            if (_queueCount == _queue.Length)
            {
                var bigger = new Channel[_queue.Length * 2];
                for (int i = 0; i < _queueCount; i++) bigger[i] = _queue[(_queueHead + i) & (_queue.Length - 1)];
                _queue = bigger; _queueHead = 0;
            }
            _queue[(_queueHead + _queueCount) & (_queue.Length - 1)] = ch;
            _queueCount++;
        }

        private static void Push(ref Channel[] array, ref int count, Channel ch)
        {
            if (count == array.Length) Array.Resize(ref array, array.Length * 2);
            array[count++] = ch;
        }

        private void Abort(Type refused)
        {
            _aborted = true;
            var sb = new StringBuilder();
            sb.Append("Event cascade started by ").Append(_cascadeRoot).Append(" exceeded maxEventsPerCascade=")
              .Append(_cascadeCap).Append(" (refused ").Append(refused.Name).Append("). Events in cascade: ");
            for (int i = 0; i < _touchedCount; i++)
                sb.Append(i == 0 ? "" : ", ").Append(_touched[i].EventType.Name).Append(" x").Append(_touched[i].CascadeCount);
            _abortMessage = sb.ToString();
            _errors.Report(new CoreError(CoreErrorKind.CascadeLimit, _abortMessage, refused));
            for (int i = 0; i < _touchedCount; i++) _touched[i].ClearPending();
            Array.Clear(_queue, 0, _queue.Length);
            _queueHead = 0; _queueCount = 0;
        }

        private void EndCascade()
        {
            for (int i = 0; i < _dirtyCount; i++) { _dirty[i].Compact(); _dirty[i].Dirty = false; _dirty[i] = null; }
            _dirtyCount = 0;
            for (int i = 0; i < _touchedCount; i++) { _touched[i].CascadeCount = 0; _touched[i] = null; }
            _touchedCount = 0;
            _cascadeEvents = 0;
            _cascadeRoot = null;
            if (!_aborted) return;
            _aborted = false;
            var msg = _abortMessage; _abortMessage = null;
            throw new EventCascadeLimitException(msg);
        }

        private void CheckThread(Type eventType)
        {
            if (Environment.CurrentManagedThreadId == _ownerThread) return;
            var msg = "EventBus used from thread " + Environment.CurrentManagedThreadId + " but it belongs to the simulation thread " + _ownerThread + " (" + eventType.Name + ").";
            _errors.Report(new CoreError(CoreErrorKind.WrongThread, msg, eventType));
            throw new InvalidOperationException(msg);
        }

        private abstract class Channel
        {
            internal int CascadeCount;
            internal bool Dirty;
            internal abstract Type EventType { get; }
            internal abstract void DispatchNext(EventBus bus);
            internal abstract void ClearPending();
            internal abstract void Compact();
        }

        private sealed class Channel<T> : Channel where T : unmanaged, IEvent
        {
            private IEventHandler<T>[] _handlers = new IEventHandler<T>[4];
            private long[] _since = new long[4];   // publish sequence at subscription time
            private int _count;                   // slots in use (removed slots are null until compaction)
            private T[] _pending = new T[4];
            private long[] _pendingSeq = new long[4];
            private int _head, _pendingCount;

            internal override Type EventType => typeof(T);

            internal int LiveCount
            {
                get { int n = 0; for (int i = 0; i < _count; i++) if (_handlers[i] != null) n++; return n; }
            }

            internal void Add(IEventHandler<T> h, long since)
            {
                for (int i = 0; i < _count; i++)
                    if (ReferenceEquals(_handlers[i], h))
                        throw new InvalidOperationException(h.GetType().FullName + " is already subscribed to " + typeof(T).FullName);
                if (_count == _handlers.Length) { Array.Resize(ref _handlers, _count * 2); Array.Resize(ref _since, _count * 2); }
                _handlers[_count] = h; _since[_count] = since; _count++;
            }

            internal bool Remove(IEventHandler<T> h, bool compactNow)
            {
                for (int i = 0; i < _count; i++)
                {
                    if (!ReferenceEquals(_handlers[i], h)) continue;
                    _handlers[i] = null;
                    if (compactNow) Compact();
                    return true;
                }
                return false;
            }

            internal override void Compact()
            {
                int w = 0;
                for (int r = 0; r < _count; r++)
                {
                    if (_handlers[r] == null) continue;
                    _handlers[w] = _handlers[r]; _since[w] = _since[r]; w++;
                }
                for (int i = w; i < _count; i++) _handlers[i] = null;
                _count = w;
            }

            internal void Enqueue(in T evt, long seq)
            {
                if (_pendingCount == _pending.Length)
                {
                    var p = new T[_pending.Length * 2]; var s = new long[_pending.Length * 2];
                    for (int i = 0; i < _pendingCount; i++)
                    {
                        int j = (_head + i) & (_pending.Length - 1);
                        p[i] = _pending[j]; s[i] = _pendingSeq[j];
                    }
                    _pending = p; _pendingSeq = s; _head = 0;
                }
                int tail = (_head + _pendingCount) & (_pending.Length - 1);
                _pending[tail] = evt; _pendingSeq[tail] = seq; _pendingCount++;
            }

            internal override void ClearPending() { _head = 0; _pendingCount = 0; }

            internal override void DispatchNext(EventBus bus)
            {
                T evt = _pending[_head];
                long seq = _pendingSeq[_head];
                _head = (_head + 1) & (_pending.Length - 1);
                _pendingCount--;
                int n = _count; // handlers added during this dispatch sit at index >= n
                for (int i = 0; i < n; i++)
                {
                    var h = _handlers[i];
                    if (h == null || _since[i] >= seq) continue;
                    try { h.Handle(in evt); }
                    catch (Exception ex) { bus.ReportHandlerException(typeof(T), h, i, ex); }
                }
            }
        }
    }
}
