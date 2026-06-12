namespace EasyFramework.Services.Haptics
{
    public enum HapticStrength { Light, Medium, Heavy }

    public interface IHapticsService
    {
        bool Enabled { get; set; }   // 持久化 PlayerPrefs "ef.haptics"
        void Vibrate(HapticStrength strength);
    }
}
