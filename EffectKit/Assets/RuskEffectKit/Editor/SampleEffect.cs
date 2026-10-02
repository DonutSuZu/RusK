using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RusK.EffectKit
{
    /// <summary>見本のエフェクト (光・火花・輪が一度だけ出る) を Assets/Effects/Sample に作る</summary>
    internal static class SampleEffect
    {
        private const string Dir = RuskEffectKit.EffectsRoot + "/Sample";
        public const string Name = "RusK_Sample_Burst";

        public static string Create()
        {
            Directory.CreateDirectory(Dir);
            var dot = SoftDot(Dir + "/SoftDot.png");
            var mat = AdditiveMaterial(Dir + "/SoftDot_Add.mat", dot);

            var root = new GameObject(Name);
            try
            {
                // 1. 光: 大きな丸が一瞬ふくらんで消える
                var flash = Add(root, "Flash", mat);
                var main = flash.main;
                main.duration = 0.5f;
                main.loop = false;
                main.startLifetime = 0.25f;
                main.startSpeed = 0f;
                main.startSize = 2.5f;
                main.startColor = new Color(1f, 0.55f, 0.9f, 1f);
                Burst(flash, 1);
                var shape = flash.shape;
                shape.enabled = false;
                SizeCurve(flash, 0.3f, 1f);
                Fade(flash, Color.white, Color.white);

                // 2. 火花: 外に飛び散る (水色〜桃色)
                var sparks = Add(root, "Sparks", mat);
                main = sparks.main;
                main.duration = 0.5f;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 14f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.4f, 0.9f, 1f), new Color(1f, 0.5f, 0.85f));
                Burst(sparks, 40);
                shape = sparks.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.2f;
                var limit = sparks.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.dampen = 0.15f;
                limit.limit = 2f;
                SizeCurve(sparks, 1f, 0f);
                var r = sparks.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.04f;
                r.lengthScale = 1.5f;

                // 3. 輪: 地面に沿って広がる
                var ring = Add(root, "Ring", mat);
                ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                main = ring.main;
                main.duration = 0.5f;
                main.loop = false;
                main.startLifetime = 0.4f;
                main.startSpeed = 9f;
                main.startSize = 0.5f;
                main.startColor = new Color(0.45f, 0.85f, 1f, 1f);
                Burst(ring, 60);
                shape = ring.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.1f;
                shape.radiusThickness = 0f;
                SizeCurve(ring, 1f, 0.2f);
                Fade(ring, Color.white, Color.white);

                var path = $"{Dir}/{Name}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[RusK] 見本のエフェクトを作りました / Sample created: {path}");
                return path;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static ParticleSystem Add(GameObject root, string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            return ps;
        }

        private static void Burst(ParticleSystem ps, short count)
        {
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
        }

        private static void SizeCurve(ParticleSystem ps, float from, float to)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, from, 1f, to));
        }

        /// <summary>だんだん透明にする</summary>
        private static void Fade(ParticleSystem ps, Color from, Color to)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        /// <summary>ふんわりした白い丸のテクスチャ</summary>
        private static Texture2D SoftDot(string path)
        {
            if (!File.Exists(path))
            {
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>URP の Particles/Unlit、加算 (光るもの向け)</summary>
        private static Material AdditiveMaterial(string path, Texture tex)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f); // 半透明
            mat.SetFloat("_Blend", 2f);   // 加算
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
