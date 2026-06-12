using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.Services.Localization
{
    [CreateAssetMenu(fileName = "LocalizationTable", menuName = "EasyFramework/Localization Table")]
    public sealed class LocalizationTable : ScriptableObject
    {
        [System.Serializable]
        public struct LocaleValue
        {
            public string Locale;   // 如 "zh-CN" / "en"
            public string Value;
        }

        [System.Serializable]
        public struct Row
        {
            public string Key;
            public List<LocaleValue> Values;
        }

        [SerializeField] List<Row> _rows = new();

        public IReadOnlyList<Row> Rows => _rows;

        /// <summary>测试钩子:运行时追加一行(key + 每 locale 值)。</summary>
        internal void AddEntryForTest(string key, IReadOnlyDictionary<string, string> localeValues)
        {
            var values = new List<LocaleValue>();
            foreach (var kv in localeValues)
                values.Add(new LocaleValue { Locale = kv.Key, Value = kv.Value });
            _rows.Add(new Row { Key = key, Values = values });
        }
    }
}
