using System;
using System.Collections.Generic;
using System.Reflection;
using EasyFramework.DevTools.Cheats;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class CheatRegistryTests
    {
        // ---- 被扫描的测试用作弊方法(本测试程序集内的静态方法)----
        static readonly List<string> Invoked = new();

        [Cheat("noarg_cmd", "no-arg cheat")]
        public static void NoArg() => Invoked.Add("noarg");

        [Cheat("int_cmd")]
        public static void IntArg(int amount) => Invoked.Add($"int:{amount}");

        [Cheat("mixed_cmd")]
        public static void Mixed(string name, bool flag) => Invoked.Add($"mixed:{name}:{flag}");

        // 重复命令名:后注册应被跳过(保留 NoArg 的 "noarg_cmd")
        [Cheat("noarg_cmd")]
        public static void DuplicateName() => Invoked.Add("dup");

        // 不支持的参数类型(Vector3 非基元)→ 跳过
        [Cheat("unsupported_cmd")]
        public static void Unsupported(UnityEngine.Vector3 v) => Invoked.Add("unsupported");

        // 非静态方法即使带 [Cheat] 也不应被注册(扫描只取静态)
        [Cheat("instance_cmd")]
        public void InstanceCheat() => Invoked.Add("instance");

        // ---- 收集器:替换桥接钩子 ----
        sealed class Collected
        {
            public string Command;
            public string Description;
            public MethodInfo Method;
        }

        List<Collected> _collected;
        Action<string, string, MethodInfo> _original;

        [SetUp]
        public void SetUp()
        {
            Invoked.Clear();
            _original = CheatRegistry.AddCommandHandler;
            _collected = new List<Collected>();
            CheatRegistry.AddCommandHandler =
                (cmd, desc, m) => _collected.Add(new Collected { Command = cmd, Description = desc, Method = m });
            // Reset registered commands between tests
            CheatRegistry.ResetForTesting();
        }

        [TearDown]
        public void TearDown() => CheatRegistry.AddCommandHandler = _original;

        [Test]
        public void RegisterAssembly_RegistersSupportedStaticCheats()
        {
            CheatRegistry.RegisterAssembly(Assembly.GetExecutingAssembly());

            var names = new List<string>();
            foreach (var c in _collected) names.Add(c.Command);

            CollectionAssert.Contains(names, "noarg_cmd");
            CollectionAssert.Contains(names, "int_cmd");
            CollectionAssert.Contains(names, "mixed_cmd");
        }

        [Test]
        public void RegisterAssembly_SkipsDuplicateCommandName()
        {
            CheatRegistry.RegisterAssembly(Assembly.GetExecutingAssembly());

            var count = 0;
            foreach (var c in _collected) if (c.Command == "noarg_cmd") count++;
            Assert.AreEqual(1, count, "重复命令名只注册一次(后注册被跳过)");
        }

        [Test]
        public void RegisterAssembly_SkipsUnsupportedSignature()
        {
            CheatRegistry.RegisterAssembly(Assembly.GetExecutingAssembly());
            var names = new List<string>();
            foreach (var c in _collected) names.Add(c.Command);
            CollectionAssert.DoesNotContain(names, "unsupported_cmd");
        }

        [Test]
        public void RegisterAssembly_SkipsInstanceMethod()
        {
            CheatRegistry.RegisterAssembly(Assembly.GetExecutingAssembly());
            var names = new List<string>();
            foreach (var c in _collected) names.Add(c.Command);
            CollectionAssert.DoesNotContain(names, "instance_cmd");
        }

        [Test]
        public void RegisteredCommands_ReflectsRegistered()
        {
            CheatRegistry.RegisterAssembly(Assembly.GetExecutingAssembly());
            CollectionAssert.Contains(CheatRegistry.RegisteredCommands, "noarg_cmd");
            CollectionAssert.Contains(CheatRegistry.RegisteredCommands, "int_cmd");
            // 不支持/重复/实例方法不在已注册列表
            CollectionAssert.DoesNotContain(CheatRegistry.RegisteredCommands, "unsupported_cmd");
            CollectionAssert.DoesNotContain(CheatRegistry.RegisteredCommands, "instance_cmd");
        }
    }
}
