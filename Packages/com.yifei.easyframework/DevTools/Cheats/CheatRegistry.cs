using System;
using System.Collections.Generic;
using System.Reflection;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
#endif

namespace EasyFramework.DevTools.Cheats
{
    /// <summary>
    /// 扫描程序集内静态 [Cheat] 方法并桥接到 IngameDebugConsole。
    /// 全部逻辑包在 UNITY_EDITOR || DEVELOPMENT_BUILD 内(发布版零开销);公共签名恒在以保证引用方编译。
    /// </summary>
    public static class CheatRegistry
    {
        static readonly List<string> _registered = new List<string>();

        public static IReadOnlyList<string> RegisteredCommands => _registered;

        /// <summary>
        /// 桥接收集器:default 把命令转发到 DebugLogConsole.AddCommand;测试可替换为收集器以脱离真实 console。
        /// 参数:command / description / 目标静态方法。
        /// </summary>
        internal static Action<string, string, MethodInfo> AddCommandHandler =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            BridgeToConsole;
#else
            null;
#endif

        /// <summary>扫描 assembly 内所有静态方法上的 [Cheat],注册受支持的命令。重复命令名/不支持签名跳过。</summary>
        public static void RegisterAssembly(Assembly assembly)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (assembly == null) return;

            foreach (var type in assembly.GetTypes())
            {
                var methods = type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attr = method.GetCustomAttribute<CheatAttribute>();
                    if (attr == null) continue;

                    if (!IsSupportedSignature(method))
                    {
                        Debug.LogWarning(
                            $"[EasyFramework] Cheat '{attr.Command}' on {type.Name}.{method.Name} " +
                            "has unsupported parameters (only static methods with 0..N of int/float/string/bool are allowed); skipped.");
                        continue;
                    }

                    if (_registered.Contains(attr.Command))
                    {
                        Debug.LogWarning(
                            $"[EasyFramework] Duplicate cheat command '{attr.Command}' " +
                            $"({type.Name}.{method.Name}); keeping first registration, skipping this one.");
                        continue;
                    }

                    AddCommandHandler?.Invoke(attr.Command, attr.Description, method);
                    _registered.Add(attr.Command);
                }
            }
#else
            // 发布版:no-op(零开销)。
#endif
        }

        /// <summary>测试专用:重置已注册命令列表,使各测试用例独立。</summary>
        internal static void ResetForTesting()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _registered.Clear();
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static bool IsSupportedSignature(MethodInfo method)
        {
            if (!method.IsStatic) return false;          // 仅静态
            foreach (var p in method.GetParameters())
            {
                var t = p.ParameterType;
                if (t != typeof(int) && t != typeof(float) &&
                    t != typeof(string) && t != typeof(bool))
                    return false;
            }
            return true;
        }

        /// <summary>默认桥:把 Cheat 注册为 IngameDebugConsole 命令。</summary>
        static void BridgeToConsole(string command, string description, MethodInfo method)
        {
            // IngameDebugConsole 主路径:DebugLogConsole.AddCommand(command, description, delegate)
            // 支持把任意兼容签名的静态方法注册为命令;console 解析输入字符串转参后调用。
            try
            {
                var ps = method.GetParameters();
                Delegate del;

                // 显式映射 0/1/2 参以避免 IL2CPP AOT 对开放委托构造的限制,同时保持简洁。
                // 覆盖实际作弊方法的常见签名(无参/单基元参/双基元参)。
                switch (ps.Length)
                {
                    case 0:
                        del = (Action)Delegate.CreateDelegate(typeof(Action), method);
                        break;
                    case 1:
                        del = CreateDelegate1(method, ps[0].ParameterType);
                        break;
                    case 2:
                        del = CreateDelegate2(method, ps[0].ParameterType, ps[1].ParameterType);
                        break;
                    default:
                        // 3+ 参数:回退 Expression 路径(IL2CPP 风险极低,实际作弊方法很少超过 2 参)
                        del = CreateDelegateExpression(method);
                        break;
                }

                IngameDebugConsole.DebugLogConsole.AddCommand(command, description, del);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] Failed to bridge cheat '{command}' to console: {e.Message}");
            }
        }

        static Delegate CreateDelegate1(MethodInfo method, Type p0)
        {
            if (p0 == typeof(int))    return (Action<int>)   Delegate.CreateDelegate(typeof(Action<int>),    method);
            if (p0 == typeof(float))  return (Action<float>) Delegate.CreateDelegate(typeof(Action<float>),  method);
            if (p0 == typeof(string)) return (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), method);
            if (p0 == typeof(bool))   return (Action<bool>)  Delegate.CreateDelegate(typeof(Action<bool>),   method);
            throw new NotSupportedException($"Unsupported parameter type: {p0}");
        }

        static Delegate CreateDelegate2(MethodInfo method, Type p0, Type p1)
        {
            // 枚举常见双参组合;不支持的组合回退 Expression
            if (p0 == typeof(string) && p1 == typeof(bool))
                return (Action<string, bool>)Delegate.CreateDelegate(typeof(Action<string, bool>), method);
            if (p0 == typeof(int) && p1 == typeof(int))
                return (Action<int, int>)Delegate.CreateDelegate(typeof(Action<int, int>), method);
            if (p0 == typeof(int) && p1 == typeof(float))
                return (Action<int, float>)Delegate.CreateDelegate(typeof(Action<int, float>), method);
            if (p0 == typeof(float) && p1 == typeof(float))
                return (Action<float, float>)Delegate.CreateDelegate(typeof(Action<float, float>), method);
            if (p0 == typeof(string) && p1 == typeof(string))
                return (Action<string, string>)Delegate.CreateDelegate(typeof(Action<string, string>), method);
            if (p0 == typeof(bool) && p1 == typeof(bool))
                return (Action<bool, bool>)Delegate.CreateDelegate(typeof(Action<bool, bool>), method);
            if (p0 == typeof(int) && p1 == typeof(string))
                return (Action<int, string>)Delegate.CreateDelegate(typeof(Action<int, string>), method);
            if (p0 == typeof(string) && p1 == typeof(int))
                return (Action<string, int>)Delegate.CreateDelegate(typeof(Action<string, int>), method);
            // 其余组合回退 Expression
            return CreateDelegateExpression(method);
        }

        static Delegate CreateDelegateExpression(MethodInfo method)
        {
            var ps = method.GetParameters();
            var typeArgs = new Type[ps.Length + 1];
            for (var i = 0; i < ps.Length; i++) typeArgs[i] = ps[i].ParameterType;
            typeArgs[ps.Length] = method.ReturnType; // void 对应 typeof(void),Expression.GetDelegateType 自动选 Action 变体
            var delegateType = System.Linq.Expressions.Expression.GetDelegateType(typeArgs);
            return Delegate.CreateDelegate(delegateType, method);
        }
#endif
    }
}
