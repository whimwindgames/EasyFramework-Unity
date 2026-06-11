using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Saves
{
    public abstract class SaveData { public int Version; }

    public interface ISaveMigration
    {
        int FromVersion { get; }
        void Migrate(Newtonsoft.Json.Linq.JObject raw);
    }

    public sealed class SaveProfile
    {
        public Type DataType;
        public int CurrentVersion;
        public Func<SaveData> CreateNew;
        public IReadOnlyList<ISaveMigration> Migrations;
        public string FileName = "save.json";
        public string HmacSalt = "easyframework";
    }

    public interface ISaveService
    {
        UniTask LoadAsync();
        void Save();
        T Data<T>() where T : SaveData;
    }
}
