using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.Monetization.IAP
{
    [CreateAssetMenu(fileName = "ProductCatalog", menuName = "EasyFramework/Product Catalog")]
    public sealed class ProductCatalog : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string Id;
            public ProductType Type;
            public List<string> RewardDescriptions;
        }

        [SerializeField] List<Entry> _entries = new();

        public IReadOnlyList<Entry> Entries => _entries;

        public IReadOnlyList<string> ProductIds()
        {
            var ids = new List<string>(_entries.Count);
            foreach (var e in _entries) ids.Add(e.Id);
            return ids;
        }

        public bool TryGet(string id, out Entry entry)
        {
            foreach (var e in _entries)
                if (e.Id == id) { entry = e; return true; }
            entry = default;
            return false;
        }

        /// <summary>测试钩子:运行时追加条目。</summary>
        internal void AddEntryForTest(string id, ProductType type, List<string> rewards)
            => _entries.Add(new Entry { Id = id, Type = type, RewardDescriptions = rewards });
    }
}
