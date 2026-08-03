using System;

namespace EasyFramework
{
    /// <summary>
    /// 可选框架模块。Core 始终安装；其余模块由项目按需组合，避免 2D/3D 项目被迫启用
    /// 不适用的 UI、输入、相机、对象池或商业化实现。
    /// </summary>
    [Flags]
    public enum FrameworkFeatures
    {
        None = 0,
        Core = 1 << 0,
        Assets = 1 << 1,
        Scenes = 1 << 2,
        Save = 1 << 3,
        Config = 1 << 4,
        Http = 1 << 5,
        Pooling = 1 << 6,
        UI = 1 << 7,
        Audio = 1 << 8,
        Input = 1 << 9,
        Camera = 1 << 10,
        Juice = 1 << 11,
        Localization = 1 << 12,
        Haptics = 1 << 13,
        Analytics = 1 << 14,
        Ads = 1 << 15,
        IAP = 1 << 16,
        ContentUpdate = 1 << 17,

        All = Core | Assets | Scenes | Save | Config | Http | Pooling | UI |
              Audio | Input | Camera | Juice | Localization | Haptics |
              Analytics | Ads | IAP | ContentUpdate,
    }

    public static class FrameworkFeatureSets
    {
        /// <summary>只安装生命周期、事件总线与计时器。</summary>
        public const FrameworkFeatures CoreOnly = FrameworkFeatures.Core;

        /// <summary>
        /// 成熟项目常用底座：保留 DI/事件、配置、存档、HTTP、音频、本地化和触感，
        /// 不接管项目现有 UI、输入、相机、对象池、场景与商业化。
        /// </summary>
        public const FrameworkFeatures ExistingProject =
            FrameworkFeatures.Core |
            FrameworkFeatures.Assets |
            FrameworkFeatures.Save |
            FrameworkFeatures.Config |
            FrameworkFeatures.Http |
            FrameworkFeatures.Audio |
            FrameworkFeatures.Localization |
            FrameworkFeatures.Haptics |
            FrameworkFeatures.Analytics;
    }
}
