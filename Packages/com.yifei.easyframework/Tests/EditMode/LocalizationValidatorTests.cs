using System.Collections.Generic;
using EasyFramework.Services.Localization;
using EasyFramework.Services.Localization.Editor;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class LocalizationValidatorTests
    {
        static LocalizationTable MakeTable(string tableName,
            params (string key, IReadOnlyDictionary<string, string> localeValues)[] rows)
        {
            var table = ScriptableObject.CreateInstance<LocalizationTable>();
            table.name = tableName;
            foreach (var (key, localeValues) in rows)
                table.AddEntryForTest(key, localeValues);
            return table;
        }

        [Test]
        public void Validate_AllKeysHaveAllLocales_ReturnsNoIssues()
        {
            var table = MakeTable("Common",
                ("hello", new Dictionary<string, string> { { "zh-CN", "你好" }, { "en", "Hello" } }),
                ("bye", new Dictionary<string, string> { { "zh-CN", "再见" }, { "en", "Bye" } }));

            var issues = LocalizationValidator.Validate(new[] { table });

            Assert.IsEmpty(issues);
        }

        [Test]
        public void Validate_MissingLocaleForKey_ReportsIssue()
        {
            var table = MakeTable("Common",
                ("hello", new Dictionary<string, string> { { "zh-CN", "你好" }, { "en", "Hello" } }),
                ("bye", new Dictionary<string, string> { { "zh-CN", "再见" } })); // 缺 en

            var issues = LocalizationValidator.Validate(new[] { table });

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("Common", issues[0].TableName);
            Assert.AreEqual("bye", issues[0].Key);
            Assert.AreEqual("en", issues[0].MissingLocale);
        }

        [Test]
        public void Validate_MultipleTables_UnionsLocaleSetAcrossAllTables()
        {
            // 表 A 只有 zh-CN/en,表 B 引入了 ja——校验应把 ja 也纳入"应该存在的 locale 集合",
            // 从而发现表 A 里所有 key 都缺 ja。
            var tableA = MakeTable("A",
                ("k1", new Dictionary<string, string> { { "zh-CN", "1" }, { "en", "1" } }));
            var tableB = MakeTable("B",
                ("k2", new Dictionary<string, string> { { "zh-CN", "2" }, { "en", "2" }, { "ja", "2" } }));

            var issues = LocalizationValidator.Validate(new[] { tableA, tableB });

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("A", issues[0].TableName);
            Assert.AreEqual("k1", issues[0].Key);
            Assert.AreEqual("ja", issues[0].MissingLocale);
        }

        [Test]
        public void Validate_EmptyKey_IsSkipped()
        {
            var table = MakeTable("Common",
                ("", new Dictionary<string, string> { { "zh-CN", "空key" } }),
                ("valid", new Dictionary<string, string> { { "zh-CN", "有效" } }));

            var issues = LocalizationValidator.Validate(new[] { table });

            Assert.IsEmpty(issues); // 只有一个 locale(zh-CN)出现过,valid 的 zh-CN 已填,空 key 被跳过
        }

        [Test]
        public void Validate_NullOrEmptyTableList_ReturnsNoIssues()
        {
            Assert.IsEmpty(LocalizationValidator.Validate(null));
            Assert.IsEmpty(LocalizationValidator.Validate(new LocalizationTable[0]));
        }
    }
}
