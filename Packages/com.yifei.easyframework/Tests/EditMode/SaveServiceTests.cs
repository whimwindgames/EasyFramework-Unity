using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using EasyFramework.Services.Saves;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class SaveServiceTests
    {
        string _dir;

        sealed class TestSave : SaveData
        {
            public int Coins;
            public string PlayerName = "";
        }

        // v1: 无 Coins;v2: 加 Coins=0;v3: 加 PlayerName="anon"
        sealed class MigrateV1ToV2 : ISaveMigration
        {
            public int FromVersion => 1;
            public void Migrate(JObject raw) { raw["Coins"] = 0; raw["Version"] = 2; }
        }
        sealed class MigrateV2ToV3 : ISaveMigration
        {
            public int FromVersion => 2;
            public void Migrate(JObject raw) { raw["PlayerName"] = "anon"; raw["Version"] = 3; }
        }

        SaveProfile MakeProfile(int currentVersion = 3, IReadOnlyList<ISaveMigration> migrations = null)
            => new SaveProfile
            {
                DataType = typeof(TestSave),
                CurrentVersion = currentVersion,
                CreateNew = () => new TestSave { Version = currentVersion, Coins = 0, PlayerName = "" },
                Migrations = migrations ?? new ISaveMigration[] { new MigrateV1ToV2(), new MigrateV2ToV3() },
                FileName = "save.json",
                HmacSalt = "test-salt",
            };

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ef_save_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void LoadAsync_NoFile_CreatesNew()
        {
            var svc = new JsonSaveService(MakeProfile(), _dir);
            svc.LoadAsync().GetAwaiter().GetResult();
            Assert.AreEqual(3, svc.Data<TestSave>().Version);
            Assert.AreEqual(0, svc.Data<TestSave>().Coins);
        }

        [Test]
        public void SaveThenReload_RoundTrips()
        {
            var svc = new JsonSaveService(MakeProfile(), _dir);
            svc.LoadAsync().GetAwaiter().GetResult();
            svc.Data<TestSave>().Coins = 42;
            svc.Data<TestSave>().PlayerName = "hero";
            svc.Save();

            var svc2 = new JsonSaveService(MakeProfile(), _dir);
            svc2.LoadAsync().GetAwaiter().GetResult();
            Assert.AreEqual(42, svc2.Data<TestSave>().Coins);
            Assert.AreEqual("hero", svc2.Data<TestSave>().PlayerName);
        }

        [Test]
        public void TamperedFile_DetectedAndRebuilt()
        {
            var svc = new JsonSaveService(MakeProfile(), _dir);
            svc.LoadAsync().GetAwaiter().GetResult();
            svc.Data<TestSave>().Coins = 99;
            svc.Save();

            // 篡改 payload(不更新 HMAC)
            var path = Path.Combine(_dir, "save.json");
            var json = File.ReadAllText(path);
            File.WriteAllText(path, json.Replace("99", "999999"));

            var svc2 = new JsonSaveService(MakeProfile(), _dir);
            svc2.LoadAsync().GetAwaiter().GetResult();
            // 损坏 → 备份 + 重建新档
            Assert.IsTrue(File.Exists(path + ".corrupt"), "应备份损坏文件");
            Assert.AreEqual(0, svc2.Data<TestSave>().Coins, "应回退为新档");
        }

        [Test]
        public void MigrationChain_V1ToV3()
        {
            // 手写一个 v1 档(只有 Version=1),用框架写盘格式(payload + hmac)
            var profile = MakeProfile();
            var raw = new JObject { ["Version"] = 1 };
            var svcForWrite = new JsonSaveService(profile, _dir);
            // 暴露的 internal 测试钩子:把任意 JObject 以合法签名写入存档文件
            svcForWrite.WriteRawForTest(raw);

            var svc = new JsonSaveService(profile, _dir);
            svc.LoadAsync().GetAwaiter().GetResult();
            var data = svc.Data<TestSave>();
            Assert.AreEqual(3, data.Version);
            Assert.AreEqual(0, data.Coins);       // v1→v2 注入
            Assert.AreEqual("anon", data.PlayerName); // v2→v3 注入
        }

        [Test]
        public void Save_OverwritesExistingFileAtomically()
        {
            var svc = new JsonSaveService(MakeProfile(), _dir);
            svc.LoadAsync().GetAwaiter().GetResult();
            svc.Data<TestSave>().Coins = 1;
            svc.Save();
            svc.Data<TestSave>().Coins = 2;
            Assert.DoesNotThrow(() => svc.Save()); // File.Replace 目标已存在仍成功

            var svc2 = new JsonSaveService(MakeProfile(), _dir);
            svc2.LoadAsync().GetAwaiter().GetResult();
            Assert.AreEqual(2, svc2.Data<TestSave>().Coins);
        }
    }
}
