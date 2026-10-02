using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RusK.EffectKit
{
    /// <summary>
    /// RusK の Effect Tuner で使う自作エフェクトを、AssetBundle に書き出す。
    /// - Assets/Effects の中のフォルダ 1 つ = バンドル 1 つ (フォルダ名.bundle)。Assets/Effects 直下のプレハブは effects.bundle
    /// - プレハブ 1 つ = エフェクト 1 つ。ゲームでは「差し替え」の一覧の「自作」に、プレハブの名前で出る
    /// - 使えるのは Unity の部品 (ParticleSystem・Trail・Mesh・Light など) だけ。自分で書いたスクリプトはゲームでは動かない
    /// </summary>
    public static class RuskEffectKit
    {
        public const string EffectsRoot = "Assets/Effects";
        public const string BuildDir = "Build";
        public const string Extension = ".bundle";
        private const string LooseBundle = "effects";
        private const string RuskFolderKey = "RuskEffectKit.RuskFolder";

        /// <summary>書き出したバンドルをコピーする先 (ゲームの RusK フォルダ)。空ならコピーしない</summary>
        public static string RuskFolder
        {
            get => EditorPrefs.GetString(RuskFolderKey, "");
            set => EditorPrefs.SetString(RuskFolderKey, value ?? "");
        }

        [MenuItem("RusK/エフェクトを書き出す (Build Effects)", priority = 0)]
        public static void BuildMenu() => Build(interactive: true);

        [MenuItem("RusK/ゲームの RusK フォルダを選ぶ (Set Game Folder)", priority = 20)]
        public static void ChooseRuskFolder()
        {
            var picked = EditorUtility.OpenFolderPanel("ゲームのフォルダ (ved.exe のあるところ) か、その中の RusK フォルダ", RuskFolder, "");
            if (string.IsNullOrEmpty(picked)) return;
            var rusk = ResolveRuskFolder(picked);
            if (rusk == null)
            {
                EditorUtility.DisplayDialog("RusK Effect Kit",
                    "RusK フォルダが見つかりません。ゲームのフォルダ (ved.exe のあるところ) を選んでください。\n" +
                    "RusK folder not found. Pick the game folder (where ved.exe is).", "OK");
                return;
            }
            RuskFolder = rusk;
            Debug.Log($"[RusK] 書き出し先: {Path.Combine(rusk, "effects")}");
        }

        [MenuItem("RusK/書き出したフォルダを開く (Open Build Folder)", priority = 21)]
        public static void OpenBuildFolder()
        {
            Directory.CreateDirectory(BuildDir);
            EditorUtility.RevealInFinder(Path.GetFullPath(BuildDir) + Path.DirectorySeparatorChar);
        }

        [MenuItem("RusK/見本のエフェクトを作る (Create Sample)", priority = 40)]
        public static void CreateSampleMenu()
        {
            EnsurePipeline();
            var path = SampleEffect.Create();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>コマンドラインから: Unity.exe -batchmode -projectPath EffectKit -executeMethod RusK.EffectKit.RuskEffectKit.BuildFromCommandLine [-ruskFolder 〈ゲームの RusK フォルダ〉] -quit</summary>
        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-ruskFolder");
            if (i >= 0 && i + 1 < args.Length) RuskFolder = ResolveRuskFolder(args[i + 1]) ?? "";
            EnsurePipeline();
            if (!Build(interactive: false)) EditorApplication.Exit(1);
        }

        /// <summary>見本を作ってから書き出す (初回の確認用)</summary>
        public static void SetupFromCommandLine()
        {
            EnsurePipeline();
            if (!FindPrefabs().Any()) SampleEffect.Create();
            BuildFromCommandLine();
        }

        // ---- 書き出し ----

        private static bool Build(bool interactive)
        {
            var builds = CollectBuilds(out var problems);
            if (builds.Count == 0)
            {
                Report(interactive, $"{EffectsRoot} にプレハブがありません。\nNo prefabs in {EffectsRoot}.", error: true);
                return false;
            }
            if (problems.Count > 0)
            {
                var text = "次のプレハブには、ゲームで動かないものが入っています (そのまま書き出しますが、その部分は動きません):\n" +
                           "These prefabs contain parts that will not work in the game:\n\n" + string.Join("\n", problems.Take(20));
                if (interactive && !EditorUtility.DisplayDialog("RusK Effect Kit", text, "書き出す (Build)", "やめる (Cancel)")) return false;
                Debug.LogWarning("[RusK] " + text);
            }

            Directory.CreateDirectory(BuildDir);
            var manifest = BuildPipeline.BuildAssetBundles(BuildDir, builds.ToArray(),
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);
            if (manifest == null)
            {
                Report(interactive, "書き出しに失敗しました。Console を見てください。\nBuild failed, see the Console.", error: true);
                return false;
            }

            // 今回のもの以外 (消したフォルダの古いバンドル) は Build から消す
            var names = new HashSet<string>(builds.Select(b => b.assetBundleName), StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.GetFiles(BuildDir, "*" + Extension))
                if (!names.Contains(Path.GetFileName(f)))
                {
                    File.Delete(f);
                    if (File.Exists(f + ".manifest")) File.Delete(f + ".manifest");
                }

            int count = builds.Sum(b => b.assetNames.Length);
            var msg = $"{builds.Count} 個のバンドル ({count} 個のエフェクト) を書き出しました。\nBuilt {builds.Count} bundle(s) with {count} effect(s).\n\n{Path.GetFullPath(BuildDir)}";

            var rusk = RuskFolder;
            if (!string.IsNullOrEmpty(rusk) && Directory.Exists(rusk))
            {
                var dest = Path.Combine(rusk, "effects");
                Directory.CreateDirectory(dest);
                foreach (var b in builds)
                    File.Copy(Path.Combine(BuildDir, b.assetBundleName), Path.Combine(dest, b.assetBundleName), true);
                msg += $"\n\nゲームにコピーしました / Copied to:\n{dest}\n" +
                       "ゲームの中では Effect Tuner の「自作エフェクトを読み込み直す」で反映されます。\n" +
                       "In game, press \"Reload custom effects\" in Effect Tuner.";
            }
            else
            {
                msg += "\n\nこれらの .bundle を、ゲームの RusK\\effects フォルダに入れてください (メニューの RusK > ゲームの RusK フォルダを選ぶ で、自動でコピーできます)。\n" +
                       "Put these .bundle files in the game's RusK\\effects folder (or use RusK > Set Game Folder to copy them automatically).";
            }
            Report(interactive, msg, error: false);
            return true;
        }

        private static List<AssetBundleBuild> CollectBuilds(out List<string> problems)
        {
            problems = new List<string>();
            var packs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in FindPrefabs())
            {
                var rel = path.Substring(EffectsRoot.Length + 1);
                int slash = rel.IndexOf('/');
                var pack = slash > 0 ? rel.Substring(0, slash) : LooseBundle;
                var bundle = SafeName(pack) + Extension;
                if (!packs.TryGetValue(bundle, out var list)) packs[bundle] = list = new List<string>();
                list.Add(path);
                problems.AddRange(Check(path));
            }
            return packs.Select(p => new AssetBundleBuild { assetBundleName = p.Key, assetNames = p.Value.ToArray() }).ToList();
        }

        private static IEnumerable<string> FindPrefabs()
        {
            if (!AssetDatabase.IsValidFolder(EffectsRoot)) return Enumerable.Empty<string>();
            return AssetDatabase.FindAssets("t:Prefab", new[] { EffectsRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>ゲームで動かないもの: 自分で書いたスクリプト、URP でないシェーダー</summary>
        private static IEnumerable<string> Check(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) yield break;
            var name = Path.GetFileNameWithoutExtension(path);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) { yield return $"{name}: スクリプトが見つからない部品 / missing script"; continue; }
                var asm = mb.GetType().Assembly.GetName().Name;
                if (!asm.StartsWith("UnityEngine", StringComparison.Ordinal) && !asm.StartsWith("Unity.", StringComparison.Ordinal))
                    yield return $"{name}: スクリプト {mb.GetType().Name} は動きません / custom script {mb.GetType().Name} will not run";
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                var tag = m.shader.name;
                if (tag == "Hidden/InternalErrorShader" || tag.StartsWith("Legacy Shaders/", StringComparison.Ordinal) ||
                    tag == "Standard" || tag.StartsWith("Particles/", StringComparison.Ordinal) || tag.StartsWith("Mobile/", StringComparison.Ordinal))
                    yield return $"{name}: マテリアル {m.name} のシェーダー {tag} は URP ではないので、ピンク色になります / not a URP shader (renders pink)";
            }
        }

        private static string SafeName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_').ToLowerInvariant();
        }

        private static string ResolveRuskFolder(string picked)
        {
            if (string.IsNullOrEmpty(picked)) return null;
            if (string.Equals(Path.GetFileName(picked.TrimEnd('/', '\\')), "RusK", StringComparison.OrdinalIgnoreCase) && Directory.Exists(picked))
                return picked;
            var inside = Path.Combine(picked, "RusK");
            return Directory.Exists(inside) ? inside : null;
        }

        private static void Report(bool interactive, string msg, bool error)
        {
            if (error) Debug.LogError("[RusK] " + msg);
            else Debug.Log("[RusK] " + msg);
            if (interactive) EditorUtility.DisplayDialog("RusK Effect Kit", msg, "OK");
        }

        // ---- URP ----

        /// <summary>URP の設定が無ければ作る (ゲームと同じ URP 14 でプレビュー・書き出しするため)</summary>
        [InitializeOnLoadMethod]
        private static void EnsurePipelineOnLoad() => EditorApplication.delayCall += EnsurePipeline;

        public static void EnsurePipeline()
        {
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset) return;
            const string dir = "Assets/RuskEffectKit/Settings";
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(dir + "/URP.asset");
            if (asset == null)
            {
                // URP のメニュー (Create > Rendering > URP Asset) と同じ作り方
                var type = typeof(UniversalRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.Universal.RendererType");
                var create = typeof(UniversalRenderPipelineAsset).GetMethod("CreateRendererAsset",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                var renderer = (ScriptableRendererData)create.Invoke(null,
                    new object[] { dir + "/URP_Renderer.asset", Enum.Parse(type, "UniversalRenderer"), false, "Renderer" });
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, dir + "/URP.asset");
            }
            GraphicsSettings.defaultRenderPipeline = asset;
            AssetDatabase.SaveAssets();
            Debug.Log("[RusK] URP を設定しました / URP set up");
        }
    }
}
