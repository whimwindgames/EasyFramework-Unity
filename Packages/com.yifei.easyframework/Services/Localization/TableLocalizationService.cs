using System.Collections.Generic;
using System;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using UnityEngine;

namespace EasyFramework.Services.Localization
{
    public sealed class TableLocalizationService : ILocalizationService
    {
        const string LocaleKey = "ef.locale";

        readonly Dictionary<string, Dictionary<string, string>> _entries = new();
        readonly IEventBus _events;

        public string CurrentLocale { get; private set; }

        public TableLocalizationService(IReadOnlyList<LocalizationTable> tables,
            IEventBus events, string defaultLocale = "zh-CN")
        {
            _events = events;
            CurrentLocale = PlayerPrefs.GetString(LocaleKey, defaultLocale);

            if (tables != null)
            {
                foreach (var table in tables)
                {
                    if (table == null) continue;
                    foreach (var row in table.Rows)
                    {
                        if (string.IsNullOrEmpty(row.Key)) continue;
                        if (!_entries.TryGetValue(row.Key, out var map))
                            _entries[row.Key] = map = new Dictionary<string, string>();
                        if (row.Values != null)
                            foreach (var lv in row.Values)
                                map[lv.Locale] = lv.Value; // 后注册覆盖
                    }
                }
            }
        }

        public string Get(string key)
        {
            if (_entries.TryGetValue(key, out var map)
                && map.TryGetValue(CurrentLocale, out var value))
                return value;

            Debug.LogWarning($"[EasyFramework] Localization missing key '{key}' for locale '{CurrentLocale}'.");
            return key;
        }

        public UniTask SetLocaleAsync(string localeCode)
        {
            if (string.IsNullOrWhiteSpace(localeCode))
                throw new ArgumentException("Locale code is required.", nameof(localeCode));
            if (CurrentLocale == localeCode)
                return UniTask.CompletedTask;
            CurrentLocale = localeCode;
            PlayerPrefs.SetString(LocaleKey, localeCode);
            PlayerPrefs.Save();
            _events.Publish(new LocaleChangedEvent(localeCode));
            return UniTask.CompletedTask;
        }
    }
}
