using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;
using VContainer.Unity;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Reflection;
using EasyFramework.DevTools.Cheats;
using EasyFramework.DevTools.Overlay;
using UnityEngine;
#endif

namespace EasyFramework.DevTools
{
    /// <summary>
    /// DevTools 启动任务:DEV/EDITOR 下挂 PerfOverlay 角标、实例化 IngameDebugConsole、注册作弊命令。
    /// 非关键(IsCritical=false)、晚启(Priority=90),失败不阻塞游戏。发布版为 no-op。
    /// 由子作用域 GameLifetimeScope 经 RegisterEntryPoint 注册:作为 IInitializable
    /// (子作用域入口点 dispatcher 调用)兼 IBootTask(契约保留)。框架 GameBootstrap 只收集根
    /// 作用域 IBootTask,看不到子作用域的,故子作用域 DevTools 经 IInitializable 入口点自驱动。
    /// 不改框架 Boot 接线文件。
    /// </summary>
    public sealed class DevToolsBootTask : IBootTask, IInitializable
    {
        public int Priority => 90;
        public bool IsCritical => false;

        // VContainer 入口点:子作用域 build 后调用。
        public void Initialize() => Run();

        public UniTask InitializeAsync(CancellationToken ct)
        {
            Run();
            return UniTask.CompletedTask;
        }

        void Run()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            try
            {
                var host = ResolveHost();

                // 1) FPS/内存角标
                if (host.GetComponent<PerfOverlay>() == null)
                    host.AddComponent<PerfOverlay>();

                // 2) IngameDebugConsole 预制体实例化(三指下滑呼出)
                EnsureDebugConsole(host);

                // 3) 注册全部已加载程序集里的 [Cheat](Game/Template 的作弊方法)
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    // 只扫描业务/框架程序集,跳过系统程序集以省时(名字前缀过滤)
                    var name = asm.GetName().Name;
                    if (name.StartsWith("Game") || name.StartsWith("EasyFramework"))
                        CheatRegistry.RegisterAssembly(asm);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] DevToolsBootTask failed (non-critical): {e.Message}");
            }
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static GameObject ResolveHost()
        {
            var root = UnityEngine.Object.FindFirstObjectByType<EasyFramework.RootLifetimeScope>();
            if (root != null) return root.gameObject;

            var host = new GameObject("[DevTools]");
            UnityEngine.Object.DontDestroyOnLoad(host);
            return host;
        }

        static void EnsureDebugConsole(GameObject host)
        {
            // 已存在则不重复实例化(IngameDebugConsole 单例)。
            var existing = UnityEngine.Object.FindFirstObjectByType<IngameDebugConsole.DebugLogManager>();
            if (existing != null) return;

            // 主路径:从 Resources 加载包自带 prefab 并 Instantiate。
            // 注意:IngameDebugConsole 包的 prefab 位于包内,不在 Resources 文件夹——
            // 若 Resources.Load 返回 null,将 LogWarning 跳过,不影响游戏启动(IsCritical=false)。
            var prefab = Resources.Load<GameObject>("IngameDebugConsole");
            if (prefab == null)
            {
                Debug.LogWarning("[EasyFramework] IngameDebugConsole prefab not found in Resources; console not spawned. " +
                    "To enable: copy the IngameDebugConsole prefab from the package to Assets/Resources/IngameDebugConsole.prefab.");
                return;
            }
            var instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = "[IngameDebugConsole]";
            UnityEngine.Object.DontDestroyOnLoad(instance);
        }
#endif
    }
}
