using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.Services.Configs
{
    public sealed class ConfigService : IConfigService
    {
        readonly Dictionary<string, string> _local = new();
        readonly SemaphoreSlim _refreshGate = new(1, 1);
        IReadOnlyDictionary<string, string> _remote =
            new Dictionary<string, string>();
        readonly IRemoteConfigProvider _provider;

        public ConfigService(IReadOnlyList<ConfigTable> tables, IRemoteConfigProvider provider)
        {
            _provider = provider;
            if (tables != null)
                foreach (var table in tables)
                    if (table != null)
                        foreach (var e in table.Entries)
                            _local[e.Key] = e.Value;
        }

        public async UniTask RefreshRemoteAsync(CancellationToken ct = default)
        {
            await _refreshGate.WaitAsync(ct);
            try
            {
                var fetched = await _provider.FetchAsync(ct);
                var snapshot = new Dictionary<string, string>();
                if (fetched != null)
                {
                    foreach (var pair in fetched)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key))
                        {
                            Debug.LogWarning("[EasyFramework] Remote config ignored an empty key.");
                            continue;
                        }
                        snapshot[pair.Key] = pair.Value;
                    }
                }
                // 整份快照原子替换,服务端删除的键不会继续残留。
                _remote = snapshot;
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        public bool Has(string key) => _remote.ContainsKey(key) || _local.ContainsKey(key);

        public T Get<T>(string key, T defaultValue)
        {
            if (!_remote.TryGetValue(key, out var raw) && !_local.TryGetValue(key, out raw))
                return defaultValue;

            try
            {
                var t = typeof(T);
                object parsed;
                if (t == typeof(int)) parsed = int.Parse(raw, CultureInfo.InvariantCulture);
                else if (t == typeof(float)) parsed = float.Parse(raw, CultureInfo.InvariantCulture);
                else if (t == typeof(bool)) parsed = bool.Parse(raw);
                else if (t == typeof(string)) parsed = raw;
                else
                {
                    Debug.LogWarning($"[EasyFramework] Config '{key}': unsupported type {t.Name}, using default.");
                    return defaultValue;
                }
                return (T)parsed;
            }
            catch (Exception)
            {
                Debug.LogWarning($"[EasyFramework] Config '{key}': failed to parse '{raw}' as {typeof(T).Name}, using default.");
                return defaultValue;
            }
        }
    }
}
