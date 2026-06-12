using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Localization
{
    public readonly struct LocaleChangedEvent
    {
        public readonly string LocaleCode;
        public LocaleChangedEvent(string localeCode) => LocaleCode = localeCode;
    }

    public interface ILocalizationService
    {
        string CurrentLocale { get; }
        string Get(string key);                       // 未找到返回 key 本身并 LogWarning
        UniTask SetLocaleAsync(string localeCode);    // 持久化 PlayerPrefs "ef.locale", 发 LocaleChangedEvent
    }
}
