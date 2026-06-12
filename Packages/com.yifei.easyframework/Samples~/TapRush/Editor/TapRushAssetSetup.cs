using System.IO;
using EasyFramework.Services.UI;
using Game.UI;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 一次性生成 TapRush 运行期资产并标记 Addressable:
    ///   - 四个 UI 面板 prefab(挂面板脚本,外观运行时由面板 OnSetup 代码构建)→ key "ui/<TypeName>"
    ///   - 圆圈 prefab(Sprite 圆 + TapRushCircle)→ key "circle"
    ///   - tap 音效 AudioClip(0.1s 正弦波)→ key "audio/tap"
    /// 由验证代理执行一次:菜单 EasyFramework/TapRush/Setup Assets。
    /// 同时补上 Phase 2 推迟的 Addressables 真实加载冒烟(运行时 G.Asset/G.UI/G.Audio 经 key 真实加载)。
    /// </summary>
    public static class TapRushAssetSetup
    {
        const string Dir = "Assets/Game/GeneratedAssets";

        [MenuItem("EasyFramework/TapRush/Setup Assets")]
        public static void SetupAssets()
        {
            Directory.CreateDirectory(Dir);

            var settings = EnsureAddressableSettings();

            // ---- UI 面板 prefab(仅脚本 + RectTransform;外观运行时建)----
            CreatePanelPrefab<MenuWindow>(settings);
            CreatePanelPrefab<GameHud>(settings);
            CreatePanelPrefab<ResultWindow>(settings);
            CreatePanelPrefab<ConfirmPopup>(settings);

            // ---- 圆 sprite + 圆圈 prefab ----
            var circleSprite = CreateCircleSprite();
            CreateCirclePrefab(settings, circleSprite);

            // ---- tap 音效 clip ----
            CreateTapClip(settings);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TapRush] Asset setup complete (prefabs/sprite/clip generated and marked addressable).");
        }

        static AddressableAssetSettings EnsureAddressableSettings()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            }
            return settings;
        }

        static void MarkAddressable(AddressableAssetSettings settings, string assetPath, string key)
        {
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            entry.address = key;
        }

        static void CreatePanelPrefab<T>(AddressableAssetSettings settings) where T : UIPanel
        {
            var typeName = typeof(T).Name;
            var go = new GameObject(typeName, typeof(RectTransform));
            go.AddComponent<T>();

            var path = $"{Dir}/{typeName}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);

            MarkAddressable(settings, path, "ui/" + typeName);
        }

        static Sprite CreateCircleSprite()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var center = new Vector2(size / 2f, size / 2f);
            var radius = size / 2f - 2f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Vector2.Distance(new Vector2(x, y), center);
                tex.SetPixel(x, y, d <= radius ? new Color(0.95f, 0.4f, 0.3f, 1f) : Color.clear);
            }
            tex.Apply();

            var texPath = $"{Dir}/CircleTex.png";
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);

            // 设为 Sprite 导入
            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(texPath);
        }

        static void CreateCirclePrefab(AddressableAssetSettings settings, Sprite sprite)
        {
            var go = new GameObject("Circle");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            go.AddComponent<TapRushCircle>();

            var path = $"{Dir}/Circle.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);

            MarkAddressable(settings, path, "circle");
        }

        static void CreateTapClip(AddressableAssetSettings settings)
        {
            const int sampleRate = 44100;
            const float duration = 0.1f;
            const float freq = 880f;
            var count = (int)(sampleRate * duration);
            var samples = new float[count];
            for (var i = 0; i < count; i++)
            {
                // 正弦波 + 线性衰减包络,避免爆音。
                var env = 1f - (float)i / count;
                samples[i] = Mathf.Sin(2f * Mathf.PI * freq * i / sampleRate) * 0.5f * env;
            }

            var clip = AudioClip.Create("TapSfx", count, 1, sampleRate, false);
            clip.SetData(samples, 0);

            var path = $"{Dir}/TapSfx.asset";
            // 幂等:已有 asset 先删除,再创建,避免 CreateAsset 在路径已存在时报错。
            if (AssetDatabase.LoadAssetAtPath<AudioClip>(path) != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);

            MarkAddressable(settings, path, "audio/tap");
        }
    }
}
