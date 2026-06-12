using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

namespace EasyFramework.Core.Timing
{
    public sealed class TimerService : ITimerService, ITickable
    {
        sealed class Entry
        {
            public long Id;
            public float Remaining;
            public float Interval;
            public bool Repeat;
            public bool Unscaled;
            public Action Callback;
            public bool Cancelled;
        }

        readonly List<Entry> _entries = new();
        readonly List<Entry> _pendingAdd = new();
        long _nextId = 1;
        bool _iterating;

        public TimerHandle Schedule(float delay, Action callback, bool repeat = false, bool useUnscaledTime = false)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            var entry = new Entry
            {
                Id = _nextId++, Remaining = delay, Interval = delay,
                Repeat = repeat, Unscaled = useUnscaledTime, Callback = callback
            };
            if (_iterating) _pendingAdd.Add(entry); else _entries.Add(entry);
            return new TimerHandle(entry.Id);
        }

        public void Cancel(TimerHandle handle)
        {
            foreach (var e in _entries)
                if (e.Id == handle.Id) { e.Cancelled = true; return; }
            foreach (var e in _pendingAdd)
                if (e.Id == handle.Id) { e.Cancelled = true; return; }
        }

        public void Tick() => Advance(Time.deltaTime, Time.unscaledDeltaTime);

        // internal for tests via direct call(本类在 Core,方法设 public 供测试与高级用法)
        public void Advance(float scaledDelta, float unscaledDelta)
        {
            _iterating = true;
            for (var i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.Cancelled) continue;
                e.Remaining -= e.Unscaled ? unscaledDelta : scaledDelta;
                while (e.Remaining <= 0f && !e.Cancelled)
                {
                    e.Callback();
                    if (e.Repeat) e.Remaining += e.Interval;
                    else { e.Cancelled = true; break; }
                }
            }
            _iterating = false;
            _entries.RemoveAll(e => e.Cancelled);
            if (_pendingAdd.Count > 0) { _entries.AddRange(_pendingAdd); _pendingAdd.Clear(); }
        }
    }
}
