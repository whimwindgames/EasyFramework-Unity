using System.Collections.Generic;
using System.Linq;
using EasyFramework.Services.Localization;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Services.Localization.Editor
{
    /// <summary>单条漏翻译记录:表名 + key + 缺失的 locale。</summary>
    public readonly struct MissingTranslation
    {
        public readonly string TableName;
        public readonly string Key;
        public readonly string MissingLocale;

        public MissingTranslation(string tableName, string key, string missingLocale)
        {
            TableName = tableName;
            Key = key;
            MissingLocale = missingLocale;
        }
    }

    /// <summary>
    /// 编辑器专用批量漏翻译校验。遍历传入的 LocalizationTable 集合,
    /// 汇总所有表里出现过的 locale 全集,对每个非空 key 检查是否覆盖全部 locale。
    /// 不改动运行时 ILocalizationService 契约,纯静态分析工具。
    /// </summary>
    public static class LocalizationValidator
    {
        public static IReadOnlyList<MissingTranslation> Validate(IReadOnlyList<LocalizationTable> tables)
        {
            var issues = new List<MissingTranslation>();
            if (tables == null || tables.Count == 0) return issues;

            // 第一遍:收集所有表里出现过的 locale 全集(某个 locale 只要在任意一个 key 上出现过,就视为"应该覆盖"的目标)。
            var allLocales = new HashSet<string>();
            foreach (var table in tables)
            {
                if (table == null) continue;
                foreach (var row in table.Rows)
                {
                    if (string.IsNullOrEmpty(row.Key) || row.Values == null) continue;
                    foreach (var lv in row.Values)
                        if (!string.IsNullOrEmpty(lv.Locale))
                            allLocales.Add(lv.Locale);
                }
            }

            // 第二遍:每个表的每个非空 key,检查是否覆盖 allLocales 全集。
            foreach (var table in tables)
            {
                if (table == null) continue;
                var tableName = table.name;
                foreach (var row in table.Rows)
                {
                    if (string.IsNullOrEmpty(row.Key)) continue;
                    var present = new HashSet<string>();
                    if (row.Values != null)
                        foreach (var lv in row.Values)
                            if (!string.IsNullOrEmpty(lv.Locale))
                                present.Add(lv.Locale);

                    foreach (var locale in allLocales)
                        if (!present.Contains(locale))
                            issues.Add(new MissingTranslation(tableName, row.Key, locale));
                }
            }

            return issues;
        }

        [MenuItem("EasyFramework/校验本地化表")]
        static void ValidateAllTablesMenuItem()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(LocalizationTable)}");
            var tables = guids
                .Select(guid => AssetDatabase.LoadAssetAtPath<LocalizationTable>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(t => t != null)
                .ToList();

            var issues = Validate(tables);
            if (issues.Count == 0)
            {
                Debug.Log($"[EasyFramework] 本地化校验通过:{tables.Count} 个表,未发现缺失翻译。");
                return;
            }

            Debug.LogWarning($"[EasyFramework] 本地化校验发现 {issues.Count} 处缺失翻译:");
            foreach (var issue in issues)
                Debug.LogWarning($"  表 [{issue.TableName}] key '{issue.Key}' 缺少 locale '{issue.MissingLocale}'");
        }
    }
}
