namespace EasyFramework.DevTools.Cheats
{
    /// <summary>标注静态方法为作弊命令。仅在 UNITY_EDITOR || DEVELOPMENT_BUILD 下由 CheatRegistry 注册到调试控制台。</summary>
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class CheatAttribute : System.Attribute
    {
        public string Command { get; }
        public string Description { get; }

        public CheatAttribute(string command, string description = "")
        {
            Command = command;
            Description = description;
        }
    }
}
