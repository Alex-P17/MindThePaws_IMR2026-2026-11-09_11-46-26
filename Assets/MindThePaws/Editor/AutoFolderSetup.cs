#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MindThePaws.EditorTools
{
    /// <summary>
    /// Rulează singur la fiecare deschidere / recompilare a proiectului:
    ///  - creează Assets/Resources/MindThePaws/Models și Audio (cu un README),
    ///  - generează în Resources/MindThePaws/Generated materialele-șablon transparente
    ///    (ca variantele de shader transparente să fie incluse sigur în build-ul de Quest).
    /// Nu atinge scena și nici ProjectSettings.
    /// </summary>
    [InitializeOnLoad]
    public static class AutoFolderSetup
    {
        const string Root = "Assets/Resources/MindThePaws";
        const string Models = Root + "/Models";
        const string Audio = Root + "/Audio";
        const string Generated = Root + "/Generated";

        static AutoFolderSetup()
        {
            EditorApplication.delayCall += Run;
        }

        [MenuItem("Tools/Mind the Paws/Recreează folderele și materialele")]
        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                bool changed = false;
                changed |= EnsureFolder("Assets", "Resources");
                changed |= EnsureFolder("Assets/Resources", "MindThePaws");
                changed |= EnsureFolder(Root, "Models");
                changed |= EnsureFolder(Root, "Audio");
                changed |= EnsureFolder(Root, "Generated");

                changed |= EnsureReadme(Models,
                    "Pune aici modelele (.fbx / .blend / prefab; .glb doar cu pachetul glTFast).\n" +
                    "Numele fișierului = cheia, fără extensie. Jocul le preia automat, le scalează și le scoate colliderele.\n\n" +
                    "Comori: mug, keys, diary, glasses, glove, lantern\n" +
                    "Personaje: corgi_mochi, corgi_gogoasa, corgi_fasolica, corgi_barker, spirit_cat, fairy_lumi\n" +
                    "Mediu & mini-jocuri: paw, flower, star_jar, constellation_star, bakery_plate, cookie_star, cookie_paw, cookie_heart, kettle, tea_cup\n\n" +
                    "Orientare: modelul trebuie să privească spre +Z (în Blender: Forward -Y, Up Z la export FBX, sau Apply Transform).\n" +
                    "Pivotul nu contează: jocul îl mută singur la baza modelului.\n");
                changed |= EnsureReadme(Audio,
                    "Pune aici sunetele (.wav / .ogg / .mp3). Numele fișierului = cheia, fără extensie.\n" +
                    "calm_music, chaos_music, poof, place, bark, purr, chime, grow, breath\n" +
                    "(opțional: oops)\n" +
                    "Ce lipsește se sintetizează automat la pornire.\n");

                changed |= EnsureMaterial(Generated + "/GhostUnlit.mat", "Universal Render Pipeline/Unlit", false);
                changed |= EnsureMaterial(Generated + "/GhostLit.mat", "Universal Render Pipeline/Lit", false);
                changed |= EnsureMaterial(Generated + "/ParticleUnlit.mat", "Universal Render Pipeline/Particles/Unlit", true);

                if (changed)
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    Debug.Log("[MindThePaws] Folderele Resources/MindThePaws (Models, Audio, Generated) sunt gata.");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MindThePaws] AutoFolderSetup: " + e.Message);
            }
        }

        static bool EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (AssetDatabase.IsValidFolder(path)) return false;
            AssetDatabase.CreateFolder(parent, name);
            return true;
        }

        static bool EnsureReadme(string folder, string text)
        {
            string path = folder + "/README.txt";
            if (File.Exists(path)) return false;
            File.WriteAllText(path, text);
            return true;
        }

        static bool EnsureMaterial(string path, string shaderName, bool particles)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return false;
            var shader = Shader.Find(shaderName);
            if (shader == null) return false;
            var m = new Material(shader);
            MakeTransparent(m);
            if (particles && m.HasProperty("_ColorMode")) m.SetFloat("_ColorMode", 0f); // multiply cu vertex color
            AssetDatabase.CreateAsset(m, path);
            return true;
        }

        /// Aceleași setări ca în CozyFx.MakeTransparent (URP 17).
        static void MakeTransparent(Material m)
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
}
#endif
