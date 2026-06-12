using TMPro;
using UnityEngine;

namespace EasyFramework.Services.Localization
{
    /// <summary>把本地化文本绑定到 TMP_Text,语言切换时自动刷新。</summary>
    [RequireComponent(typeof(TMP_Text))]
    public sealed class LocalizedText : MonoBehaviour
    {
        [SerializeField] string _key;

        TMP_Text _text;
        System.IDisposable _sub;

        void Awake() => _text = GetComponent<TMP_Text>();

        void OnEnable()
        {
            if (!LocalizationRuntime.IsInitialized) return;
            _sub = LocalizationRuntime.Events.Subscribe<LocaleChangedEvent>(_ => Refresh());
            Refresh();
        }

        void OnDisable()
        {
            _sub?.Dispose();
            _sub = null;
        }

        public void SetKey(string key)
        {
            _key = key;
            Refresh();
        }

        void Refresh()
        {
            if (_text == null || string.IsNullOrEmpty(_key) || !LocalizationRuntime.IsInitialized) return;
            _text.text = LocalizationRuntime.Loc.Get(_key);
        }
    }
}
