using System;
using System.Collections.Generic;
using EasyFramework.Core.Events;
using EasyFramework.Services.Localization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasyFramework.Tests
{
    public class TableLocalizationServiceTests
    {
        const string LocaleKey = "ef.locale";

        sealed class FakeBus : IEventBus
        {
            public readonly List<object> Published = new();
            public void Publish<T>(T evt) => Published.Add(evt);
            public IDisposable Subscribe<T>(Action<T> handler) => null;
        }

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(LocaleKey);
            PlayerPrefs.Save();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(LocaleKey);
            PlayerPrefs.Save();
        }

        static LocalizationTable MakeTable(params (string key, string zh, string en)[] rows)
        {
            var t = ScriptableObject.CreateInstance<LocalizationTable>();
            foreach (var (k, zh, en) in rows)
                t.AddEntryForTest(k, new Dictionary<string, string> { { "zh-CN", zh }, { "en", en } });
            return t;
        }

        static TableLocalizationService Make(FakeBus bus, params LocalizationTable[] tables)
            => new TableLocalizationService(tables, bus, defaultLocale: "zh-CN");

        [Test]
        public void Get_ReturnsCurrentLocaleValue()
        {
            var svc = Make(new FakeBus(), MakeTable(("hello", "你好", "Hello")));
            Assert.AreEqual("你好", svc.Get("hello"));
        }

        [Test]
        public void DefaultLocale_IsUsedInitially()
        {
            var svc = Make(new FakeBus(), MakeTable(("hello", "你好", "Hello")));
            Assert.AreEqual("zh-CN", svc.CurrentLocale);
        }

        [Test]
        public void Get_MissingKey_ReturnsKeyAndWarns()
        {
            var svc = Make(new FakeBus(), MakeTable(("hello", "你好", "Hello")));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*missing.*"));
            Assert.AreEqual("nope", svc.Get("nope"));
        }

        [Test]
        public void SetLocaleAsync_SwitchesValue_PublishesEvent_Persists()
        {
            var bus = new FakeBus();
            var svc = Make(bus, MakeTable(("hello", "你好", "Hello")));
            svc.SetLocaleAsync("en").GetAwaiter().GetResult();
            Assert.AreEqual("Hello", svc.Get("hello"));
            Assert.AreEqual("en", svc.CurrentLocale);
            Assert.AreEqual("en", PlayerPrefs.GetString(LocaleKey, ""));
            Assert.AreEqual(1, bus.Published.Count);
            Assert.IsInstanceOf<LocaleChangedEvent>(bus.Published[0]);
            Assert.AreEqual("en", ((LocaleChangedEvent)bus.Published[0]).LocaleCode);
        }

        [Test]
        public void LaterTable_OverridesEarlierTable_SameKey()
        {
            var t1 = MakeTable(("hello", "你好A", "HelloA"));
            var t2 = MakeTable(("hello", "你好B", "HelloB"));
            var svc = Make(new FakeBus(), t1, t2); // t2 后注册,覆盖
            Assert.AreEqual("你好B", svc.Get("hello"));
        }

        [Test]
        public void PersistedLocale_RestoredOnConstruction()
        {
            PlayerPrefs.SetString(LocaleKey, "en");
            PlayerPrefs.Save();
            var svc = Make(new FakeBus(), MakeTable(("hello", "你好", "Hello")));
            Assert.AreEqual("en", svc.CurrentLocale);
            Assert.AreEqual("Hello", svc.Get("hello"));
        }
    }
}
