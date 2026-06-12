using System.Collections.Generic;
using EasyFramework;
using EasyFramework.Services.Saves;

namespace Game
{
    /// <summary>
    /// TapRush 专用组合根:仅覆盖 GetSaveProfile() 让生产存档载荷为 TapRushSaveData。
    /// 挂在 Boot 场景的 [EasyFramework] 节点上替换基类 RootLifetimeScope。
    /// 框架服务全部由基类 RootLifetimeScope.Configure 注册;游戏服务由子作用域
    /// TapRushGameLifetimeScope 注册。
    /// </summary>
    public sealed class TapRushRootLifetimeScope : RootLifetimeScope
    {
        protected override SaveProfile GetSaveProfile()
            => new SaveProfile
            {
                DataType = typeof(TapRushSaveData),
                CurrentVersion = 1,
                CreateNew = () => new TapRushSaveData { Version = 1, HighScore = 0 },
                Migrations = new List<ISaveMigration>(),
                FileName = "save.json",
                HmacSalt = "tap-rush-salt",
            };
    }
}
