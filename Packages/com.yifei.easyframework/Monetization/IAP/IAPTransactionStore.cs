using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EasyFramework.Monetization.IAP
{
    [Serializable]
    public sealed class IAPTransactionState
    {
        public List<PurchaseTransaction> Pending = new();
        public List<string> CompletedTransactionIds = new();
        public List<string> OwnedProductIds = new();
        public bool LegacyPlayerPrefsMigrated;
    }

    public interface IIAPTransactionStore
    {
        IAPTransactionState Load();
        void Save(IAPTransactionState state);
    }

    /// <summary>
    /// 独立于 PlayerPrefs 的交易日志。先写临时文件再替换正式文件,避免应用退出时留下半份 JSON。
    /// 此日志负责可靠补单而非防篡改;如需抵御已控制客户端的攻击者,必须替换为服务端验签。
    /// </summary>
    public sealed class JsonIAPTransactionStore : IIAPTransactionStore
    {
        readonly string _path;
        readonly object _sync = new();

        public JsonIAPTransactionStore(string directory, string fileName = "iap-transactions.json")
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("IAP transaction directory is required.", nameof(directory));
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("IAP transaction file name is required.", nameof(fileName));
            _path = Path.Combine(directory, fileName);
        }

        public IAPTransactionState Load()
        {
            lock (_sync)
            {
                if (!File.Exists(_path))
                    return NewState();

                try
                {
                    var state = JsonUtility.FromJson<IAPTransactionState>(File.ReadAllText(_path));
                    return Normalize(state);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[EasyFramework] IAP transaction journal could not be read: {e.Message}");
                    return NewState();
                }
            }
        }

        public void Save(IAPTransactionState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            lock (_sync)
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var temporaryPath = _path + ".tmp";
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(Normalize(state)));
                if (File.Exists(_path))
                {
                    var backupPath = _path + ".bak";
                    try
                    {
                        File.Replace(temporaryPath, _path, backupPath);
                        if (File.Exists(backupPath))
                            File.Delete(backupPath);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(temporaryPath, _path, true);
                        File.Delete(temporaryPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, _path);
                }
            }
        }

        static IAPTransactionState NewState() => Normalize(new IAPTransactionState());

        static IAPTransactionState Normalize(IAPTransactionState state)
        {
            state ??= new IAPTransactionState();
            state.Pending ??= new List<PurchaseTransaction>();
            state.CompletedTransactionIds ??= new List<string>();
            state.OwnedProductIds ??= new List<string>();
            return state;
        }
    }
}
