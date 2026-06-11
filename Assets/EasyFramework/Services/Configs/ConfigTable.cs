using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.Services.Configs
{
    [CreateAssetMenu(fileName = "ConfigTable", menuName = "EasyFramework/Config Table")]
    public sealed class ConfigTable : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string Key;
            public string Value;
        }

        [SerializeField] List<Entry> _entries = new();

        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>测试钩子:运行时向表内追加条目(编辑器/单测用)。</summary>
        internal void AddEntryForTest(string key, string value)
            => _entries.Add(new Entry { Key = key, Value = value });
    }
}
