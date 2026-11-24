using System.Collections.Generic;
using UnityEngine;

namespace MindThePaws
{
    /// <summary>
    /// Caută automat modele în Resources/MindThePaws/Models/&lt;cheie&gt; (FBX / prefab / .blend / .glb cu glTFast).
    /// Dacă există: îl instanțiază, scoate colliderele/rigidbody-urile, îl normalizează la dimensiunea potrivită
    /// (pivot la bază, centrat) și aplică fantomă / momeală / nuanță. Dacă nu există: întoarce null, fără nicio eroare,
    /// iar Graybox construiește varianta procedurală.
    /// </summary>
    public static class AutoAssetLoader
    {
        public const string ModelsPath = "MindThePaws/Models/";

        static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();

        /// Dimensiunea țintă (cea mai mare latură, în metri) pentru fiecare cheie.
        static readonly Dictionary<string, float> sizes = new Dictionary<string, float>
        {
            { "mug", 0.12f }, { "keys", 0.12f }, { "diary", 0.14f }, { "glasses", 0.13f }, { "glove", 0.12f }, { "lantern", 0.16f },
            { "corgi_mochi", 0.46f }, { "corgi_gogoasa", 0.5f }, { "corgi_fasolica", 0.46f }, { "corgi_thief", 0.28f }, { "corgi_clingy", 0.26f }, { "corgi_barker", 0.44f },
            { "spirit_cat", 0.26f }, { "fairy_lumi", 0.18f },
            { "paw", 0.09f }, { "flower", 0.30f }, { "star_jar", 0.22f }, { "constellation_star", 0.10f },
            { "bakery_plate", 0.62f }, { "cookie_star", 0.075f }, { "cookie_paw", 0.075f }, { "cookie_heart", 0.075f },
            { "kettle", 0.24f }, { "tea_cup", 0.14f }
        };

        public static float SizeFor(string key, float fallback = 0.15f)
        {
            return sizes.TryGetValue(key, out float s) ? s : fallback;
        }

        public static bool Has(string key)
        {
            return Prefab(key) != null;
        }

        static GameObject Prefab(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (cache.TryGetValue(key, out var p)) return p;
            GameObject found = null;
            try { found = Resources.Load<GameObject>(ModelsPath + key); }
            catch { found = null; }
            cache[key] = found;   // memorăm și lipsa, ca să nu căutăm la fiecare obiect
            return found;
        }

        /// Golește cache-ul (ex. după ce ai adăugat modele noi în timpul unei sesiuni din Editor).
        public static void ClearCache()
        {
            cache.Clear();
        }

        /// <param name="byHeight">true = normalizează după înălțime (flori), false = după latura cea mai mare.</param>
        public static GameObject TryModel(string key, string name, bool isGhost = false, bool isDecoy = false,
            float size = -1f, bool byHeight = false, Color? tint = null, float tintAmount = 0.5f)
        {
            var prefab = Prefab(key);
            if (prefab == null) return null;

            var root = new GameObject(name);
            var holder = new GameObject("Model_" + key);
            holder.transform.SetParent(root.transform, false);
            var inst = Object.Instantiate(prefab, holder.transform, false);
            inst.name = key;

            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            foreach (var rb in inst.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(rb);
            foreach (var cam in inst.GetComponentsInChildren<Camera>(true)) Object.Destroy(cam.gameObject);
            foreach (var l in inst.GetComponentsInChildren<Light>(true)) Object.Destroy(l.gameObject);

            Normalize(root.transform, holder.transform, size > 0f ? size : SizeFor(key), byHeight);

            if (isDecoy)
            {
                // nuanță cromatică decalată, ca la momelile procedurale
                var t = tint ?? new Color(0.55f, 0.85f, 1f);
                CozyFx.ApplyTint(root, Shift(t), 0.55f);
            }
            else if (tint.HasValue) CozyFx.ApplyTint(root, tint.Value, tintAmount);

            if (isGhost) CozyFx.ApplyGhost(root);
            return root;
        }

        static void Normalize(Transform root, Transform holder, float target, bool byHeight)
        {
            var rs = holder.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return;
            bool has = false;
            var b = new Bounds();
            foreach (var r in rs)
            {
                if (r is ParticleSystemRenderer) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!has) return;
            // root și holder sunt în origine, fără rotație → bounds = spațiul local al root-ului
            float dim = byHeight ? b.size.y : Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (dim < 1e-5f) return;
            float s = target / dim;
            holder.localScale = Vector3.one * s;
            holder.localPosition = -new Vector3(b.center.x, b.min.y, b.center.z) * s;
        }

        static Color Shift(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(Mathf.Repeat(h + 0.12f, 1f), Mathf.Max(0.35f, s), Mathf.Max(0.6f, v));
        }

        // ------------------------------------------------------------ audio

        public const string AudioPath = "MindThePaws/Audio/";
        static readonly Dictionary<string, AudioClip> audioCache = new Dictionary<string, AudioClip>();

        public static AudioClip TryAudio(string key)
        {
            if (audioCache.TryGetValue(key, out var c)) return c;
            AudioClip found = null;
            try { found = Resources.Load<AudioClip>(AudioPath + key); }
            catch { found = null; }
            audioCache[key] = found;
            return found;
        }
    }
}
