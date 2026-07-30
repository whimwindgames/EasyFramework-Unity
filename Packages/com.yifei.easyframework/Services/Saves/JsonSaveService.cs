using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EasyFramework.Services.Saves
{
    public sealed class JsonSaveService : ISaveService
    {
        readonly SaveProfile _profile;
        readonly string _dir;
        readonly string _path;
        SaveData _data;

        public JsonSaveService(SaveProfile profile, string saveDirectory)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            if (_profile.DataType == null || !typeof(SaveData).IsAssignableFrom(_profile.DataType))
                throw new ArgumentException("SaveProfile.DataType must derive from SaveData.", nameof(profile));
            if (_profile.CreateNew == null)
                throw new ArgumentException("SaveProfile.CreateNew is required.", nameof(profile));
            if (_profile.CurrentVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(profile), "CurrentVersion must be at least 1.");
            if (string.IsNullOrWhiteSpace(_profile.FileName) ||
                Path.GetFileName(_profile.FileName) != _profile.FileName)
                throw new ArgumentException("SaveProfile.FileName must be a file name, not a path.", nameof(profile));
            if (string.IsNullOrEmpty(_profile.HmacSalt))
                throw new ArgumentException("SaveProfile.HmacSalt is required.", nameof(profile));
            if (string.IsNullOrWhiteSpace(saveDirectory))
                throw new ArgumentException("Save directory is required.", nameof(saveDirectory));
            _dir = saveDirectory;
            _path = Path.Combine(_dir, _profile.FileName);
        }

        public UniTask LoadAsync()
        {
            if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir);

            if (!File.Exists(_path))
            {
                _data = CreateNew();
                return UniTask.CompletedTask;
            }

            string payload;
            try
            {
                var envelope = JObject.Parse(File.ReadAllText(_path));
                payload = (string)envelope["payload"];
                var storedHmac = (string)envelope["hmac"];
                if (payload == null || storedHmac == null || !ConstantTimeEquals(storedHmac, ComputeHmac(payload)))
                    throw new InvalidDataException("HMAC mismatch.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] Save corrupt ({e.Message}); backing up and recreating.");
                BackupCorrupt();
                _data = CreateNew();
                return UniTask.CompletedTask;
            }

            try
            {
                var raw = JObject.Parse(payload);
                if (!ApplyMigrations(raw))
                {
                    _data = CreateNew();
                    return UniTask.CompletedTask;
                }
                _data = (SaveData)raw.ToObject(_profile.DataType);
                if (_data == null)
                    throw new InvalidDataException("Save payload deserialized to null.");
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[EasyFramework] Save payload invalid ({e.Message}); backing up and recreating.");
                BackupCorrupt();
                _data = CreateNew();
                return UniTask.CompletedTask;
            }
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// Returns false when the save file has a version newer than the current binary
        /// (e.g. after a TestFlight rollback or a cloud save from a newer device).
        /// The caller should back up the file and create a fresh save instead of
        /// silently deserialising an incompatible structure.
        /// </summary>
        bool ApplyMigrations(JObject raw)
        {
            var version = raw["Version"]?.Value<int>() ?? 1;

            // Future-version guard: disk file is newer than this binary.
            if (version > _profile.CurrentVersion)
            {
                Debug.LogWarning(
                    $"[EasyFramework] Save version {version} is newer than current binary version " +
                    $"{_profile.CurrentVersion}. Backing up as .future and recreating.");
                BackupFuture();
                return false;
            }

            if (_profile.Migrations == null) return true;

            while (version < _profile.CurrentVersion)
            {
                var found = false;
                foreach (var m in _profile.Migrations)
                {
                    if (m.FromVersion == version)
                    {
                        m.Migrate(raw);
                        var nextVersion = raw["Version"]?.Value<int>() ?? version + 1;
                        if (nextVersion <= version)
                            throw new InvalidDataException(
                                $"Migration from version {version} did not advance the version.");
                        if (nextVersion > _profile.CurrentVersion)
                            throw new InvalidDataException(
                                $"Migration advanced past target version {_profile.CurrentVersion}.");
                        version = nextVersion;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    // No migration registered for this version; fields will use CLR defaults.
                    Debug.LogWarning(
                        $"[EasyFramework] No migration found for save version {version} " +
                        $"(target {_profile.CurrentVersion}). Missing fields will use default values.");
                    break;
                }
            }
            return true;
        }

        public void Save()
        {
            if (_data == null) throw new InvalidOperationException("Call LoadAsync before Save.");
            if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir);

            _data.Version = _profile.CurrentVersion;
            var payload = JsonConvert.SerializeObject(_data, Formatting.None);
            var envelope = new JObject
            {
                ["payload"] = payload,
                ["hmac"] = ComputeHmac(payload),
            };

            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, envelope.ToString(Formatting.None));
            if (!File.Exists(_path))
            {
                File.Move(tmp, _path);
                return;
            }
            try
            {
                File.Replace(tmp, _path, null);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(tmp, _path, true);
                File.Delete(tmp);
            }
        }

        public T Data<T>() where T : SaveData
        {
            if (_data == null)
                throw new InvalidOperationException("Call LoadAsync before Data.");
            if (_data is not T typed)
                throw new InvalidCastException(
                    $"Save profile contains {_data.GetType().Name}, not {typeof(T).Name}.");
            return typed;
        }

        SaveData CreateNew()
        {
            var data = _profile.CreateNew();
            if (data == null || !_profile.DataType.IsInstanceOfType(data))
                throw new InvalidOperationException(
                    "SaveProfile.CreateNew returned null or the wrong data type.");
            data.Version = _profile.CurrentVersion;
            return data;
        }

        void BackupCorrupt()
        {
            try
            {
                var corrupt = _path + ".corrupt";
                if (File.Exists(corrupt)) File.Delete(corrupt);
                File.Move(_path, corrupt);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] Failed to back up corrupt save: {e.Message}");
            }
        }

        void BackupFuture()
        {
            try
            {
                var future = _path + ".future";
                if (File.Exists(future)) File.Delete(future);
                File.Move(_path, future);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] Failed to back up future-version save: {e.Message}");
            }
        }

        string ComputeHmac(string payload)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_profile.HmacSalt));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        static bool ConstantTimeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>测试钩子:把任意 JObject 以合法签名写入存档文件(用于构造旧版本档)。</summary>
        internal void WriteRawForTest(JObject raw)
        {
            if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir);
            var payload = raw.ToString(Formatting.None);
            var envelope = new JObject
            {
                ["payload"] = payload,
                ["hmac"] = ComputeHmac(payload),
            };
            File.WriteAllText(_path, envelope.ToString(Formatting.None));
        }
    }
}
