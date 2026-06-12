using System.Collections.Generic;
using EasyFramework.Core.Boot;
using EasyFramework.DevTools;
using EasyFramework.Services.Saves;
using VContainer;
using VContainer.Unity;

namespace EasyFramework.Template
{
    /// <summary>
    /// 新游戏入口作用域模板。复制 _Template 到你的 Assets/&lt;YourGame&gt;/ 后:
    ///   1. 改类名 TemplateGameLifetimeScope → &lt;YourGame&gt;LifetimeScope
    ///   2. 改命名空间 EasyFramework.Template → &lt;YourGame&gt;
    ///   3. 在 Boot 场景把本组件挂为 RootLifetimeScope 的子作用域(嵌套 LifetimeScope)
    /// 它注册游戏特有的:存档 profile、DevTools、状态机/玩法服务。
    /// </summary>
    public sealed class TemplateGameLifetimeScope : GameLifetimeScope
    {
        protected override void ConfigureGame(IContainerBuilder builder)
        {
            // ---- 1) DevTools 启动任务(控制台 + 角标 + 作弊命令)----
            // IsCritical=false,失败不阻塞;发布版(非 DEVELOPMENT_BUILD)内部 no-op。
            builder.RegisterEntryPoint<DevToolsBootTask>();

            // ---- 2) 注册游戏存档 profile ----
            // RootLifetimeScope 通过 GetSaveProfile() seam 决定生产存档载荷。
            // 若要替换存档载荷:继承 RootLifetimeScope 并 override GetSaveProfile() 返回下方
            // CreateSaveProfile() 的值(参见 README §三 步骤 3)。
            // 子作用域这里额外 RegisterInstance 方便游戏侧注入引用/测试。
            builder.RegisterInstance(CreateSaveProfile());

            // ---- 3) 注册你的玩法服务 / 状态机 ----
            // 例:builder.Register<TemplateGameFlow>(Lifetime.Singleton);
            //     builder.RegisterEntryPoint<TemplateFlowBootTask>();  // BootCompleted 后进 Menu
        }

        /// <summary>
        /// 示例 SaveProfile:把框架存档载荷换成 TemplateSaveData。
        /// 改名清单:把 HmacSalt 的值改成你游戏专属盐,避免与其它游戏共用密钥。
        /// </summary>
        static SaveProfile CreateSaveProfile()
            => new SaveProfile
            {
                DataType = typeof(TemplateSaveData),
                CurrentVersion = 1,
                CreateNew = () => new TemplateSaveData { Version = 1 },
                Migrations = new List<ISaveMigration>(),   // 版本升级时在此加 v1→v2 等迁移
                FileName = "save.json",
                HmacSalt = "your-game-salt",               // 改成你游戏专属盐
            };
    }
}
