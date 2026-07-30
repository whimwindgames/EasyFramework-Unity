using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

namespace EasyFramework.Core.Timing
{
    public sealed class TimerService : ITimerService, ITickable
    {
        public const int MaxCatchUpCallbacksPerAdvance = 100;
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
            if (float.IsNaN(delay) || float.IsInfinity(delay) || delay < 0f)
                throw new ArgumentOutOfRangeException(nameof(delay),
                    "Timer delay must be finite and non-negative.");
            if (repeat && delay <= 0f)
                throw new ArgumentOutOfRangeException(nameof(delay),
                    "Repeating timer interval must be greater than zero.");
            var entry = new Entry
            {
                Id = NextId(), Remaining = delay, Interval = delay,
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
            ValidateDelta(scaledDelta, nameof(scaledDelta));
            ValidateDelta(unscaledDelta, nameof(unscaledDelta));
            _iterating = true;
            try
            {
                for (var i = 0; i < _entries.Count; i++)
                {
                    var e = _entries[i];
                    if (e.Cancelled) continue;
                    e.Remaining -= e.Unscaled ? unscaledDelta : scaledDelta;
                    var callbacks = 0;
                    while (e.Remaining <= 0f && !e.Cancelled)
                    {
                        try
                        {
                            e.Callback();
                        }
                        catch (Exception exception)
                        {
                            e.Cancelled = true;
                            Debug.LogException(exception);
                            break;
                        }

                        callbacks++;
                        if (!e.Repeat)
                        {
                            e.Cancelled = true;
                            break;
                        }

                        e.Remaining += e.Interval;
                        if (callbacks >= MaxCatchUpCallbacksPerAdvance)
                        {
                            // 丢弃过量历史积压,防止卡顿帧导致回调风暴。
                            e.Remaining = e.Interval;
                            break;
                        }
                    }
                }
            }
            finally
            {
                _iterating = false;
                _entries.RemoveAll(e => e.Cancelled);
                if (_pendingAdd.Count > 0)
                {
                    _entries.AddRange(_pendingAdd);
                    _pendingAdd.Clear();
                }
            }
        }

        long NextId()
        {
            if (_nextId <= 0)
                _nextId = 1;
            var id = _nextId++;
            if (_nextId == long.MinValue)
                _nextId = 1;
            return id;
        }

        static void ValidateDelta(float value, string parameter)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(parameter,
                    "Timer delta must be finite and non-negative.");
        }
    }
}
