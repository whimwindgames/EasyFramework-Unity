using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using UnityEngine;
using VContainer.Unity;
using Debug = UnityEngine.Debug;

namespace EasyFramework.Core.Boot
{
    public sealed class BootFailedException : Exception
    {
        public BootFailedException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class GameBootstrap : IAsyncStartable
    {
        /// <summary>耗时超过该毫秒数的 BootTask 才打印计时日志;仅开发模式(Editor / Debug 包)生效。</summary>
        public const long SlowTaskThresholdMs = 50;

        readonly IReadOnlyList<IBootTask> _tasks;
        readonly IEventBus _events;

        public GameBootstrap(IEnumerable<IBootTask> tasks, IEventBus events)
        {
            _tasks = tasks.ToList();
            _events = events;
        }

        public async UniTask StartAsync(CancellationToken ct)
        {
            foreach (var group in _tasks.GroupBy(t => t.Priority).OrderBy(g => g.Key))
            {
                await UniTask.WhenAll(group.Select(t => RunSafe(t, ct)));
            }
            _events.Publish(new BootCompletedEvent());
        }

        static async UniTask RunSafe(IBootTask task, CancellationToken ct)
        {
            var isDevMode = Debug.isDebugBuild || Application.isEditor;
            var sw = isDevMode ? Stopwatch.StartNew() : null;
            try
            {
                await task.InitializeAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e) when (!task.IsCritical)
            {
                Debug.LogWarning($"[EasyFramework] Non-critical boot task {task.GetType().Name} failed: {e.Message}");
            }
            catch (Exception e)
            {
                throw new BootFailedException($"Critical boot task {task.GetType().Name} failed.", e);
            }
            finally
            {
                if (sw != null)
                {
                    sw.Stop();
                    if (sw.ElapsedMilliseconds > SlowTaskThresholdMs)
                        Debug.Log($"[EasyFramework] Boot task {task.GetType().Name} took {sw.ElapsedMilliseconds}ms.");
                }
            }
        }
    }
}
