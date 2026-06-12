using EasyFramework.Services.Saves;

namespace EasyFramework.Template
{
    /// <summary>模板存档载荷示例。复制后改成你游戏的字段(并相应改 SaveProfile 的 CurrentVersion/Migrations)。</summary>
    public sealed class TemplateSaveData : SaveData
    {
        // 示例字段:替换为你的游戏数据。
        public int ExampleCoins;
        public string ExamplePlayerName = "Player";
    }
}
