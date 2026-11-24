#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MindThePaws.EditorTools
{
    /// <summary>
    /// Import automat pentru cățeii din Blender (Assets/Resources/MindThePaws/Models/corgi_*.fbx):
    ///  - animație Legacy (componentă Animation cu clipurile Idle / Run_Zoomie / Trotting_Decoy / Clingy_Nod / Sleep_Lay),
    ///    ca jocul să le poată reda fără Animator Controller (vezi NativeAnim.cs);
    ///  - toate clipurile în buclă;
    ///  - scara din fișier (1 unitate = 1 m), fără conversie suplimentară de axe (scriptul Blender exportă deja corect).
    /// Nu atinge scena și nici ProjectSettings.
    /// </summary>
    public class CorgiModelImport : AssetPostprocessor
    {
        static bool IsCorgi(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            path = path.Replace('\\', '/');
            string file = Path.GetFileName(path).ToLowerInvariant();
            return path.Contains("/Resources/MindThePaws/Models/") && file.StartsWith("corgi_");
        }

        void OnPreprocessModel()
        {
            if (!IsCorgi(assetPath)) return;
            var mi = assetImporter as ModelImporter;
            if (mi == null) return;
            mi.animationType = ModelImporterAnimationType.Legacy;
            mi.importAnimation = true;
            mi.useFileScale = true;
            mi.globalScale = 1f;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
        }

        void OnPreprocessAnimation()
        {
            if (!IsCorgi(assetPath)) return;
            var mi = assetImporter as ModelImporter;
            if (mi == null) return;
            var clips = mi.clipAnimations;
            if (clips == null || clips.Length == 0) clips = mi.defaultClipAnimations;
            bool changed = false;
            foreach (var c in clips)
            {
                if (c.wrapMode != WrapMode.Loop || !c.loopTime)
                {
                    c.wrapMode = WrapMode.Loop;
                    c.loopTime = true;
                    changed = true;
                }
            }
            if (changed || mi.clipAnimations.Length == 0) mi.clipAnimations = clips;
        }
    }
}
#endif
