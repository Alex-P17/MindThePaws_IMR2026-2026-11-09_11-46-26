using UnityEngine;

namespace MindThePaws
{
    /// <summary>
    /// Obiecte "graybox" construite din primitive Unity, ca să putem testa
    /// înainte să fie gata modelele din Blender.
    /// Fiecare fabrică verifică întâi Resources/MindThePaws/Models/&lt;cheie&gt; (AutoAssetLoader);
    /// dacă modelul există îl folosește, altfel construiește varianta procedurală.
    /// Personajele primesc automat mișcare procedurală (CozyAnimator).
    /// </summary>
    public static class Graybox
    {
        /// Dacă e setat (din Inspector, pe Game → Base Material), toate obiectele îl folosesc.
        public static Material Override;
        public static string ShaderInfo = "";
        static Shader cachedShader;

        /// Material colorat care sigur se desenează pe Quest (URP):
        /// 1) materialul din Inspector, 2) URP/Unlit, 3) Sprites/Default (inclus mereu în build).
        public static Material MakeMat(Color c)
        {
            Material m;
            if (Override != null)
            {
                m = new Material(Override);
            }
            else
            {
                if (cachedShader == null) cachedShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (cachedShader == null || !cachedShader.isSupported) cachedShader = Shader.Find("Sprites/Default");
                if (cachedShader == null) cachedShader = Shader.Find("Universal Render Pipeline/Lit");
                m = new Material(cachedShader);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            m.color = c;
            ShaderInfo = m.shader != null ? m.shader.name + (m.shader.isSupported ? "" : " (NESUPORTAT!)") : "fără shader!";
            return m;
        }

        public static GameObject Prim(PrimitiveType t, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(t);
            Object.Destroy(go.GetComponent<Collider>()); // nu vrem să ne lovim propria rază
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        public static GameObject Prim(PrimitiveType t, Transform parent, Vector3 localPos, Vector3 localScale, Vector3 euler, Material mat)
        {
            var go = Prim(t, parent, localPos, localScale, mat);
            go.transform.localEulerAngles = euler;
            return go;
        }

        /// Cană: pivotul e la bază, ca să stea fix pe suprafață.
        public static GameObject Mug(string name, Color color)
        {
            var root = new GameObject(name);
            var mat = MakeMat(color);
            var heart = MakeMat(Color.Lerp(color, new Color(1f, 0.45f, 0.65f), 0.7f));
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.05f, 0), new Vector3(0.08f, 0.05f, 0.08f), mat);
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0.05f, 0.05f, 0), new Vector3(0.015f, 0.055f, 0.03f), mat);
            Prim(PrimitiveType.Sphere, root.transform, new Vector3(-0.012f, 0.125f, 0f), Vector3.one * 0.02f, heart);
            Prim(PrimitiveType.Sphere, root.transform, new Vector3(0.014f, 0.145f, 0.004f), Vector3.one * 0.017f, heart);
            Prim(PrimitiveType.Sphere, root.transform, new Vector3(-0.004f, 0.165f, -0.004f), Vector3.one * 0.014f, heart);
            return root;
        }

        /// Lăbuță-marcaj: pernuță + 4 degețele.
        public static GameObject Paw(string name, Color color)
        {
            var model = AutoAssetLoader.TryModel("paw", name, tint: color, tintAmount: 0.5f);
            if (model != null) return model;
            var root = new GameObject(name);
            var mat = MakeMat(color);
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.004f, -0.005f), new Vector3(0.06f, 0.004f, 0.05f), mat);
            float[] xs = { -0.032f, -0.011f, 0.011f, 0.032f };
            float[] zs = { 0.028f, 0.044f, 0.044f, 0.028f };
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Sphere, root.transform, new Vector3(xs[i], 0.006f, zs[i]), new Vector3(0.022f, 0.01f, 0.022f), mat);
            return root;
        }

        /// Corgi-cub: corp + cap + urechi. Privește spre +Z.
        public static GameObject Corgi(string name)
        {
            return Corgi(name, "corgi_mochi");
        }

        /// modelKey: corgi_mochi, corgi_gogoasa, corgi_fasolica, corgi_barker
        /// (corgi_thief / corgi_clingy sunt acceptate ca nume vechi pentru Gogoașă / Fasolică).
        /// Dacă lipsește modelul specific, încearcă corgi_mochi (cu o nuanță), apoi graybox.
        public static GameObject Corgi(string name, string modelKey)
        {
            if (modelKey == "corgi_thief") modelKey = "corgi_gogoasa";
            if (modelKey == "corgi_clingy") modelKey = "corgi_fasolica";
            var model = AutoAssetLoader.TryModel(modelKey, name);
            if (model == null && modelKey == "corgi_gogoasa") model = AutoAssetLoader.TryModel("corgi_thief", name);
            if (model == null && modelKey == "corgi_fasolica") model = AutoAssetLoader.TryModel("corgi_clingy", name);
            if (model == null && modelKey != "corgi_mochi")
                model = AutoAssetLoader.TryModel("corgi_mochi", name, size: AutoAssetLoader.SizeFor(modelKey), tint: CorgiAccent(modelKey), tintAmount: 0.25f);
            if (model != null)
            {
                // model din Blender: dacă are animații (Idle / Run_Zoomie / Trotting_Decoy / Clingy_Nod / Sleep_Lay),
                // le redă NativeAnim, iar CozyAnimator rămâne doar o legănare discretă peste ele (fără dublarea mișcării)
                var na = NativeAnim.Setup(model, modelKey);
                CozyAnimator.Attach(model, CozyMotion.Waddle, na != null ? 0.15f : 1f);
                return model;
            }
            var go = CorgiGraybox(name, modelKey);
            CozyAnimator.Attach(go, CozyMotion.Waddle);
            return go;
        }

        static Color CorgiAccent(string key)
        {
            switch (key)
            {
                case "corgi_gogoasa": return new Color(1f, 0.55f, 0.75f);
                case "corgi_fasolica": return new Color(0.5f, 0.85f, 0.6f);
                case "corgi_barker": return new Color(1f, 0.55f, 0.7f);
                default: return new Color(0.95f, 0.62f, 0.3f);
            }
        }

        static GameObject CorgiGraybox(string name, string key)
        {
            var root = new GameObject(name);
            var orange = MakeMat(key == "corgi_fasolica" ? new Color(1f, 0.78f, 0.5f) : (key == "corgi_gogoasa" ? new Color(0.88f, 0.55f, 0.32f) : new Color(0.95f, 0.62f, 0.3f)));
            var white = MakeMat(new Color(1f, 0.96f, 0.9f));
            var pinkInner = MakeMat(new Color(1f, 0.75f, 0.8f));
            var t = root.transform;
            // corp și cap rotunjori (chibi)
            Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.12f, 0f), new Vector3(0.17f, 0.14f, 0.3f), orange);
            Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.095f, 0.02f), new Vector3(0.13f, 0.08f, 0.24f), white);
            Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.215f, 0.19f), new Vector3(0.15f, 0.135f, 0.13f), orange);
            Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.19f, 0.245f), new Vector3(0.075f, 0.05f, 0.05f), white);
            Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.2f, 0.268f), Vector3.one * 0.02f, MakeMat(new Color(0.28f, 0.2f, 0.25f)));
            // urechi mari, cu interior roz
            Prim(PrimitiveType.Sphere, t, new Vector3(-0.047f, 0.29f, 0.18f), new Vector3(0.045f, 0.075f, 0.026f), new Vector3(0f, 0f, 16f), orange);
            Prim(PrimitiveType.Sphere, t, new Vector3(0.047f, 0.29f, 0.18f), new Vector3(0.045f, 0.075f, 0.026f), new Vector3(0f, 0f, -16f), orange);
            Prim(PrimitiveType.Sphere, t, new Vector3(-0.046f, 0.287f, 0.192f), new Vector3(0.026f, 0.048f, 0.01f), new Vector3(0f, 0f, 16f), pinkInner);
            Prim(PrimitiveType.Sphere, t, new Vector3(0.046f, 0.287f, 0.192f), new Vector3(0.026f, 0.048f, 0.01f), new Vector3(0f, 0f, -16f), pinkInner);
            KawaiiFace(t, new Vector3(0f, 0.228f, 0.243f), 0.033f, 0.026f, 0.055f, -0.028f, -0.008f);
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0) ? -0.05f : 0.05f;
                float z = (i < 2) ? 0.1f : -0.1f;
                Prim(PrimitiveType.Capsule, t, new Vector3(x, 0.035f, z), new Vector3(0.042f, 0.035f, 0.042f), white);
            }
            // codiță (dă din ea când e fericit — vezi HappyWag)
            var tail = Prim(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.2f, -0.17f), new Vector3(0.03f, 0.03f, 0.075f), new Vector3(-35f, 0f, 0f), orange);
            tail.name = "Tail";
            // zgardă colorată: Mochi albastră, Gogoașă roz, Fasolică verde, Lătrătorul roz-auriu
            Color collar = key == "corgi_gogoasa" ? new Color(1f, 0.55f, 0.75f)
                : key == "corgi_fasolica" ? new Color(0.45f, 0.82f, 0.55f)
                : key == "corgi_barker" ? new Color(1f, 0.5f, 0.7f)
                : new Color(0.45f, 0.68f, 1f);
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.165f, 0.145f), new Vector3(0.135f, 0.026f, 0.05f), MakeMat(collar));
            Prim(PrimitiveType.Sphere, root.transform, new Vector3(0f, 0.145f, 0.175f), Vector3.one * 0.022f, MakeMat(new Color(1f, 0.85f, 0.35f)));
            if (key == "corgi_gogoasa")
            {
                // mască de hoțoman
                Prim(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.228f, 0.247f), new Vector3(0.13f, 0.03f, 0.006f), MakeMat(new Color(0.42f, 0.34f, 0.52f)));
            }
            else if (key == "corgi_fasolica")
            {
                // fundiță
                Prim(PrimitiveType.Cube, root.transform, new Vector3(0.03f, 0.3f, 0.2f), new Vector3(0.04f, 0.02f, 0.012f), new Vector3(0f, 0f, 30f), MakeMat(new Color(0.6f, 0.8f, 1f)));
            }
            return root;
        }

        static Material eyeMat, shineMat, blushMat;

        /// Față kawaii: ochi mari lucioși cu reflexie albă + obrăjori roz.
        /// center = mijlocul dintre ochi (local), eyeX = jumătatea distanței dintre ochi, eye = diametrul ochiului,
        /// blushX = distanța laterală a obrajilor, blushY / blushZ = decalajul obrajilor față de ochi.
        public static void KawaiiFace(Transform t, Vector3 center, float eyeX, float eye, float blushX, float blushY, float blushZ)
        {
            if (eyeMat == null) eyeMat = MakeMat(new Color(0.2f, 0.14f, 0.24f));
            if (shineMat == null) shineMat = MakeMat(Color.white);
            if (blushMat == null) blushMat = MakeMat(new Color(1f, 0.58f, 0.7f));
            for (int s = -1; s <= 1; s += 2)
            {
                var e = center + new Vector3(s * eyeX, 0f, 0f);
                Prim(PrimitiveType.Sphere, t, e, new Vector3(eye, eye * 1.12f, eye * 0.7f), eyeMat);
                Prim(PrimitiveType.Sphere, t, e + new Vector3(eye * 0.22f, eye * 0.25f, eye * 0.32f), Vector3.one * eye * 0.36f, shineMat);
                Prim(PrimitiveType.Sphere, t, e + new Vector3(-eye * 0.18f, -eye * 0.22f, eye * 0.33f), Vector3.one * eye * 0.16f, shineMat);
                Prim(PrimitiveType.Sphere, t, center + new Vector3(s * blushX, blushY, blushZ), new Vector3(eye * 0.95f, eye * 0.45f, eye * 0.3f), blushMat);
            }
        }

        public static LineRenderer Line(string name, Color color, float width)
        {
            var go = new GameObject(name);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.widthMultiplier = width;
            lr.sharedMaterial = MakeMat(color);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        static void Ring(Transform parent, Vector3 center, Vector3 a, Vector3 b, float radius, int count, float size, Material mat)
        {
            for (int i = 0; i < count; i++)
            {
                float ang = i * Mathf.PI * 2f / count;
                Prim(PrimitiveType.Sphere, parent, center + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * radius, Vector3.one * size, mat);
            }
        }

        static Color Pale(Color c)
        {
            return Color.Lerp(c, Color.white, 0.55f);
        }

        static Color Shift(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = Mathf.Repeat(h + 0.12f, 1f);
            return Color.HSVToRGB(h, s, v);
        }

        public static Color TreasureColor(string typeId)
        {
            switch (typeId)
            {
                case "keys": return new Color(0.96f, 0.8f, 0.35f);
                case "diary": return new Color(0.74f, 0.68f, 0.96f);
                case "glasses": return new Color(0.35f, 0.8f, 0.82f);
                case "glove": return new Color(0.93f, 0.3f, 0.4f);
                case "lantern": return new Color(0.55f, 0.6f, 0.88f);
                default: return new Color(0.96f, 0.66f, 0.75f);
            }
        }

        public static string TreasureLabel(string typeId)
        {
            switch (typeId)
            {
                case "keys": return "Cheile";
                case "diary": return "Jurnalul";
                case "glasses": return "Ochelarii";
                case "glove": return "Mănușa";
                case "lantern": return "Lanterna";
                default: return "Ceașca";      // „mug” = ceașca cu flori (modelul tea_cup)
            }
        }

        public static GameObject TreasureByType(string typeId, bool isGhost, bool isDecoy)
        {
            var c = TreasureColor(typeId);
            string n = typeId + (isGhost ? "_ghost" : "") + (isDecoy ? "_decoy" : "");
            var go = AutoAssetLoader.TryModel(typeId, n, isGhost, isDecoy, tint: isDecoy ? c : (Color?)null);
            // comoara „mug” din prima rundă folosește ceașca cu flori (aceeași ca în „Ceai cu Lumi”) dacă nu există un model „mug”
            if (go == null && typeId == "mug")
                go = AutoAssetLoader.TryModel("tea_cup", n, isGhost, isDecoy, size: AutoAssetLoader.SizeFor("mug"), tint: isDecoy ? c : (Color?)null);
            if (go == null)
            {
                if (isDecoy) c = Shift(c);
                if (isGhost) c = Pale(c);
                switch (typeId)
                {
                    case "keys": go = Keys(n, c); break;
                    case "diary": go = Diary(n, c); break;
                    case "glasses": go = CatGlasses(n, c); break;
                    case "glove": go = MushroomGlove(n, c); break;
                    case "lantern": go = MoonLantern(n, c); break;
                    default: go = Mug(n, c); break;
                }
                if (isGhost) CozyFx.ApplyGhost(go, 0.5f);
            }
            if (isGhost) CozyAnimator.Attach(go, CozyMotion.Pulse);
            else if (typeId == "lantern") CozyAnimator.Attach(go, CozyMotion.Hover, 0.6f);
            return go;
        }

        public static GameObject Keys(string name, Color color)
        {
            var root = new GameObject(name);
            var t = root.transform;
            var mat = MakeMat(color);
            var dark = MakeMat(Color.Lerp(color, Color.black, 0.25f));
            var pink = MakeMat(Color.Lerp(color, new Color(1f, 0.55f, 0.75f), 0.75f));
            Ring(t, new Vector3(-0.02f, 0.007f, 0f), Vector3.right, Vector3.forward, 0.026f, 12, 0.011f, dark);
            Prim(PrimitiveType.Cube, t, new Vector3(0.05f, 0.005f, 0.012f), new Vector3(0.075f, 0.006f, 0.009f), new Vector3(0f, -10f, 0f), mat);
            Prim(PrimitiveType.Cube, t, new Vector3(0.05f, 0.005f, -0.012f), new Vector3(0.075f, 0.006f, 0.009f), new Vector3(0f, 10f, 0f), mat);
            Prim(PrimitiveType.Cube, t, new Vector3(0.082f, 0.005f, 0.0095f), new Vector3(0.008f, 0.006f, 0.012f), mat);
            Prim(PrimitiveType.Cube, t, new Vector3(0.07f, 0.005f, 0.0085f), new Vector3(0.008f, 0.006f, 0.012f), mat);
            Prim(PrimitiveType.Cube, t, new Vector3(0.082f, 0.005f, -0.0095f), new Vector3(0.008f, 0.006f, 0.012f), mat);
            Prim(PrimitiveType.Cube, t, new Vector3(0.07f, 0.005f, -0.0085f), new Vector3(0.008f, 0.006f, 0.012f), mat);
            Prim(PrimitiveType.Sphere, t, new Vector3(-0.054f, 0.006f, 0f), Vector3.one * 0.008f, mat);
            Prim(PrimitiveType.Cylinder, t, new Vector3(-0.072f, 0.005f, 0f), new Vector3(0.022f, 0.003f, 0.018f), pink);
            Prim(PrimitiveType.Sphere, t, new Vector3(-0.085f, 0.006f, -0.008f), Vector3.one * 0.009f, pink);
            Prim(PrimitiveType.Sphere, t, new Vector3(-0.088f, 0.006f, 0f), Vector3.one * 0.009f, pink);
            Prim(PrimitiveType.Sphere, t, new Vector3(-0.085f, 0.006f, 0.008f), Vector3.one * 0.009f, pink);
            return root;
        }

        public static GameObject Star(string name, Color color, float size)
        {
            var root = new GameObject(name);
            var mat = MakeMat(color);
            for (int i = 0; i < 3; i++)
                Prim(PrimitiveType.Cube, root.transform, Vector3.zero, new Vector3(size, size * 0.34f, size * 0.12f), new Vector3(0f, 0f, i * 60f), mat);
            return root;
        }

        public static GameObject Diary(string name, Color color)
        {
            var root = new GameObject(name);
            var t = root.transform;
            var mat = MakeMat(color);
            var cream = MakeMat(Color.Lerp(color, new Color(1f, 0.97f, 0.9f), 0.85f));
            Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.004f, 0f), new Vector3(0.11f, 0.008f, 0.15f), mat);
            Prim(PrimitiveType.Cube, t, new Vector3(0.002f, 0.018f, 0f), new Vector3(0.098f, 0.02f, 0.14f), cream);
            Prim(PrimitiveType.Cube, t, new Vector3(-0.058f, 0.017f, 0f), new Vector3(0.012f, 0.034f, 0.15f), mat);
            var hinge = new GameObject("FrontCover");
            hinge.transform.SetParent(t, false);
            hinge.transform.localPosition = new Vector3(-0.055f, 0.03f, 0f);
            hinge.transform.localEulerAngles = new Vector3(0f, 0f, 10f);
            Prim(PrimitiveType.Cube, hinge.transform, new Vector3(0.055f, 0.004f, 0f), new Vector3(0.11f, 0.008f, 0.15f), mat);
            var star = Star("CoverStar", Color.Lerp(color, new Color(1f, 0.9f, 0.4f), 0.85f), 0.045f);
            star.transform.SetParent(hinge.transform, false);
            star.transform.localPosition = new Vector3(0.055f, 0.0095f, 0f);
            star.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
            return root;
        }

        public static GameObject CatGlasses(string name, Color color)
        {
            var root = new GameObject(name);
            var t = root.transform;
            var mat = MakeMat(color);
            var accent = MakeMat(Color.Lerp(color, Color.white, 0.5f));
            Ring(t, new Vector3(-0.04f, 0.036f, 0f), Vector3.right, Vector3.up, 0.028f, 14, 0.009f, mat);
            Ring(t, new Vector3(0.04f, 0.036f, 0f), Vector3.right, Vector3.up, 0.028f, 14, 0.009f, mat);
            Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.044f, 0f), new Vector3(0.025f, 0.006f, 0.006f), mat);
            Prim(PrimitiveType.Cube, t, new Vector3(-0.068f, 0.066f, 0f), new Vector3(0.022f, 0.022f, 0.007f), new Vector3(0f, 0f, 45f), accent);
            Prim(PrimitiveType.Cube, t, new Vector3(0.068f, 0.066f, 0f), new Vector3(0.022f, 0.022f, 0.007f), new Vector3(0f, 0f, 45f), accent);
            Prim(PrimitiveType.Cube, t, new Vector3(-0.07f, 0.036f, -0.035f), new Vector3(0.005f, 0.005f, 0.07f), mat);
            Prim(PrimitiveType.Cube, t, new Vector3(0.07f, 0.036f, -0.035f), new Vector3(0.005f, 0.005f, 0.07f), mat);
            return root;
        }

        public static GameObject MushroomGlove(string name, Color color)
        {
            var root = new GameObject(name);
            var t = root.transform;
            var glove = MakeMat(Color.Lerp(color, new Color(1f, 0.95f, 0.88f), 0.85f));
            var cap = MakeMat(color);
            var dot = MakeMat(Color.Lerp(color, Color.white, 0.95f));
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.012f, 0f), new Vector3(0.06f, 0.012f, 0.06f), glove);
            Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.05f, 0f), new Vector3(0.075f, 0.06f, 0.045f), glove);
            float[] fx = { -0.027f, -0.009f, 0.009f, 0.027f };
            float[] fh = { 0.03f, 0.036f, 0.034f, 0.028f };
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Capsule, t, new Vector3(fx[i], 0.075f + fh[i] * 0.5f, 0f), new Vector3(0.016f, fh[i] * 0.5f, 0.016f), glove);
            Prim(PrimitiveType.Capsule, t, new Vector3(0.05f, 0.06f, 0f), new Vector3(0.017f, 0.018f, 0.017f), new Vector3(0f, 0f, -55f), glove);
            Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.13f, 0f), new Vector3(0.1f, 0.05f, 0.1f), cap);
            float[] dx = { 0.025f, -0.02f, -0.015f, 0.03f, 0f };
            float[] dz = { 0.01f, 0.025f, -0.025f, -0.02f, 0f };
            for (int i = 0; i < 5; i++)
            {
                float y = 0.13f + 0.025f * Mathf.Sqrt(Mathf.Max(0f, 1f - (dx[i] * dx[i] + dz[i] * dz[i]) / 0.0025f)) - 0.002f;
                Prim(PrimitiveType.Sphere, t, new Vector3(dx[i], y, dz[i]), Vector3.one * 0.014f, dot);
            }
            return root;
        }

        public static GameObject MoonLantern(string name, Color color)
        {
            var root = new GameObject(name);
            var t = root.transform;
            var mat = MakeMat(color);
            var glow = MakeMat(Color.Lerp(color, new Color(1f, 0.88f, 0.4f), 0.85f));
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.006f, 0f), new Vector3(0.075f, 0.006f, 0.075f), mat);
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.07f, 0f), new Vector3(0.05f, 0.055f, 0.05f), glow);
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.128f, 0f), new Vector3(0.062f, 0.005f, 0.062f), mat);
            for (int i = 0; i < 9; i++)
            {
                float k = i / 8f;
                float ang = Mathf.Lerp(50f, 310f, k) * Mathf.Deg2Rad;
                float s = 0.008f + 0.016f * Mathf.Sin(k * Mathf.PI);
                Prim(PrimitiveType.Sphere, t, new Vector3(Mathf.Cos(ang) * 0.032f, 0.17f + Mathf.Sin(ang) * 0.032f, 0f), Vector3.one * s, glow);
            }
            return root;
        }

        public static GameObject SpiritCat(string name)
        {
            var model = AutoAssetLoader.TryModel("spirit_cat", name);
            var go = model != null ? model : SpiritCatGraybox(name);
            CozyAnimator.Attach(go, CozyMotion.Hover);
            return go;
        }

        static GameObject SpiritCatGraybox(string name)
        {
            var root = new GameObject(name);
            var t = root.transform;
            var cyan = MakeMat(new Color(0.25f, 0.95f, 1f));
            var light = MakeMat(new Color(0.65f, 1f, 1f));
            var pink = MakeMat(new Color(1f, 0.4f, 0.85f));
            var white = MakeMat(Color.white);
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0) ? -0.035f : 0.035f;
                float z = (i < 2) ? 0.06f : -0.06f;
                Prim(PrimitiveType.Cube, t, new Vector3(x, 0.035f, z), new Vector3(0.026f, 0.07f, 0.026f), cyan);
            }
            Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.1f, 0f), new Vector3(0.1f, 0.085f, 0.18f), cyan);
            Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.17f, 0.1f), Vector3.one * 0.09f, light);
            Prim(PrimitiveType.Cube, t, new Vector3(-0.032f, 0.225f, 0.1f), new Vector3(0.03f, 0.03f, 0.012f), new Vector3(0f, 0f, 45f), pink);
            Prim(PrimitiveType.Cube, t, new Vector3(0.032f, 0.225f, 0.1f), new Vector3(0.03f, 0.03f, 0.012f), new Vector3(0f, 0f, 45f), pink);
            KawaiiFace(t, new Vector3(0f, 0.18f, 0.138f), 0.02f, 0.02f, 0.034f, -0.014f, -0.006f);
            Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.163f, 0.147f), Vector3.one * 0.01f, pink);
            float[] ty = { 0.12f, 0.16f, 0.21f, 0.26f };
            float[] tz = { -0.1f, -0.14f, -0.155f, -0.14f };
            float[] ts = { 0.03f, 0.028f, 0.026f, 0.024f };
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Sphere, t, new Vector3(0f, ty[i], tz[i]), Vector3.one * ts[i], pink);
            return root;
        }

        public static GameObject FairyLumi(string name)
        {
            var model = AutoAssetLoader.TryModel("fairy_lumi", name);
            var go = model != null ? model : FairyLumiGraybox(name);
            CozyAnimator.Attach(go, CozyMotion.Hover);
            return go;
        }

        static GameObject FairyLumiGraybox(string name)
        {
            var root = new GameObject(name);
            var t = root.transform;
            var cloud = MakeMat(Color.white);
            var dress = MakeMat(new Color(1f, 0.75f, 0.88f));
            var skin = MakeMat(new Color(1f, 0.89f, 0.8f));
            var hair = MakeMat(new Color(0.8f, 0.65f, 0.95f));
            var wing = MakeMat(new Color(0.75f, 0.95f, 1f));
            var eye = MakeMat(new Color(0.25f, 0.2f, 0.35f));
            Prim(PrimitiveType.Sphere, t, new Vector3(-0.045f, 0.022f, 0f), Vector3.one * 0.055f, cloud);
            Prim(PrimitiveType.Sphere, t, new Vector3(0.045f, 0.022f, 0f), Vector3.one * 0.055f, cloud);
            Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.03f, 0.005f), Vector3.one * 0.07f, cloud);
            Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.02f, -0.03f), Vector3.one * 0.05f, cloud);
            Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.018f, 0.035f), Vector3.one * 0.045f, cloud);
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.078f, 0f), new Vector3(0.036f, 0.028f, 0.03f), dress);
            Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.126f, 0f), Vector3.one * 0.045f, skin);
            Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.134f, -0.007f), Vector3.one * 0.046f, hair);
            KawaiiFace(t, new Vector3(0f, 0.125f, 0.018f), 0.009f, 0.011f, 0.015f, -0.007f, -0.002f);
            Prim(PrimitiveType.Cube, t, new Vector3(-0.032f, 0.1f, -0.02f), new Vector3(0.03f, 0.065f, 0.003f), new Vector3(0f, 0f, 25f), wing);
            Prim(PrimitiveType.Cube, t, new Vector3(0.032f, 0.1f, -0.02f), new Vector3(0.03f, 0.065f, 0.003f), new Vector3(0f, 0f, -25f), wing);
            return root;
        }

        public static GameObject MemoryFlower(string name, Color petalColor, float heightScale)
        {
            float h = 0.3f * Mathf.Clamp(heightScale, 0.4f, 1.5f);
            var model = AutoAssetLoader.TryModel("flower", name, size: h, byHeight: true, tint: petalColor, tintAmount: 0.45f);
            var go = model != null ? model : MemoryFlowerGraybox(name, petalColor, h);
            CozyAnimator.Attach(go, CozyMotion.Sway);
            return go;
        }

        static GameObject MemoryFlowerGraybox(string name, Color petalColor, float h)
        {
            var root = new GameObject(name);
            var t = root.transform;
            var green = MakeMat(new Color(0.4f, 0.75f, 0.45f));
            var petal = MakeMat(petalColor);
            var yellow = MakeMat(new Color(1f, 0.85f, 0.3f));
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.012f, h * 0.5f, 0.012f), green);
            Prim(PrimitiveType.Sphere, t, new Vector3(-0.022f, h * 0.35f, 0f), new Vector3(0.04f, 0.01f, 0.018f), new Vector3(0f, 0f, 25f), green);
            Prim(PrimitiveType.Sphere, t, new Vector3(0.022f, h * 0.45f, 0f), new Vector3(0.04f, 0.01f, 0.018f), new Vector3(0f, 0f, -25f), green);
            var headGo = new GameObject("Head");
            headGo.transform.SetParent(t, false);
            headGo.transform.localPosition = new Vector3(0f, h, 0f);
            headGo.transform.localEulerAngles = new Vector3(35f, 0f, 0f);
            Prim(PrimitiveType.Sphere, headGo.transform, new Vector3(0f, 0.006f, 0f), Vector3.one * 0.036f, yellow);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f * Mathf.Deg2Rad;
                Prim(PrimitiveType.Sphere, headGo.transform, new Vector3(Mathf.Cos(a) * 0.038f, 0f, Mathf.Sin(a) * 0.038f), new Vector3(0.04f, 0.012f, 0.04f), petal);
            }
            return root;
        }

        public static GameObject Bubble(string name, Color color, float radius)
        {
            var root = new GameObject(name);
            var mat = MakeMat(color);
            float s = radius * 0.14f;
            Ring(root.transform, Vector3.zero, Vector3.right, Vector3.up, radius, 10, s, mat);
            Ring(root.transform, Vector3.zero, Vector3.right, Vector3.forward, radius, 10, s, mat);
            Ring(root.transform, Vector3.zero, Vector3.up, Vector3.forward, radius, 10, s, mat);
            // înveliș iridescent translucid
            var shell = Prim(PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one * radius * 2f, CozyFx.TransparentMat(new Color(0.8f, 0.9f, 1f, 0.18f)));
            shell.name = "Shell";
            CozyAnimator.Attach(root, CozyMotion.Hover, 0.8f);
            return root;
        }

        public static GameObject StarJar(string name)
        {
            var model = AutoAssetLoader.TryModel("star_jar", name);
            if (model != null) return model;
            var root = new GameObject(name);
            var t = root.transform;
            var glass = MakeMat(new Color(0.75f, 0.9f, 1f));
            var lid = MakeMat(new Color(1f, 0.82f, 0.4f));
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.003f, 0f), new Vector3(0.14f, 0.003f, 0.14f), glass);
            float[] hs = { 0.04f, 0.11f, 0.18f };
            for (int i = 0; i < 3; i++)
                Ring(t, new Vector3(0f, hs[i], 0f), Vector3.right, Vector3.forward, 0.07f, 10, 0.009f, glass);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                Prim(PrimitiveType.Cube, t, new Vector3(Mathf.Cos(a) * 0.07f, 0.1f, Mathf.Sin(a) * 0.07f), new Vector3(0.006f, 0.2f, 0.006f), glass);
            }
            // capacul auriu e un copil separat ("Lid"), ca să se poată ridica în animația promisiunii
            var lidT = new GameObject("Lid").transform;
            lidT.SetParent(t, false);
            lidT.localPosition = new Vector3(0f, 0.21f, 0f);
            Prim(PrimitiveType.Cylinder, lidT, Vector3.zero, new Vector3(0.1f, 0.01f, 0.1f), lid);
            Prim(PrimitiveType.Sphere, lidT, new Vector3(0f, 0.012f, 0f), new Vector3(0.03f, 0.018f, 0.03f), lid);
            // sticla: un cilindru translucid, ca steluța din interior să se vadă
            var shell = Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.105f, 0f), new Vector3(0.138f, 0.1f, 0.138f), CozyFx.TransparentMat(new Color(0.8f, 0.93f, 1f, 0.16f)));
            shell.name = "GlassShell";
            return root;
        }
            // ------------------------------------------------------------ mini-jocuri

        /// Steluță pentru Constelația Pierdută (model "constellation_star" sau miez + raze).
        public static GameObject ConstellationStar(string name, Color color)
        {
            var model = AutoAssetLoader.TryModel("constellation_star", name);
            GameObject go;
            if (model != null) go = model;
            else
            {
                go = new GameObject(name);
                var mat = MakeMat(color);
                Prim(PrimitiveType.Sphere, go.transform, Vector3.zero, Vector3.one * 0.06f, mat);
                var star = Star("Points", color, 0.1f);
                star.transform.SetParent(go.transform, false);
                foreach (var r in star.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
            }
            CozyAnimator.Attach(go, CozyMotion.Pulse, 0.8f);
            return go;
        }

        public static string CookieKey(int kind)
        {
            switch (kind)
            {
                case 0: return "cookie_star";
                case 1: return "cookie_paw";
                default: return "cookie_heart";
            }
        }

        public static string CookieLabel(int kind)
        {
            switch (kind)
            {
                case 0: return "Stea";
                case 1: return "Lăbuță";
                default: return "Inimioară";
            }
        }

        public static Color CookieColor(int kind)
        {
            switch (kind)
            {
                case 0: return new Color(1f, 0.84f, 0.45f);
                case 1: return new Color(0.92f, 0.68f, 0.5f);
                default: return new Color(1f, 0.6f, 0.72f);
            }
        }

        /// Biscuit: 0 = Stea, 1 = Lăbuță, 2 = Inimioară. Stă culcat, pivot la bază.
        public static GameObject Cookie(string name, int kind)
        {
            var model = AutoAssetLoader.TryModel(CookieKey(kind), name);
            if (model != null) return model;
            var root = new GameObject(name);
            var t = root.transform;
            var c = CookieColor(kind);
            var dough = MakeMat(c);
            var icing = MakeMat(Color.Lerp(c, Color.white, 0.6f));
            switch (kind)
            {
                case 0:
                    for (int i = 0; i < 3; i++)
                        Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.008f, 0f), new Vector3(0.075f, 0.016f, 0.026f), new Vector3(0f, i * 60f, 0f), dough);
                    Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.017f, 0f), new Vector3(0.025f, 0.006f, 0.025f), icing);
                    break;
                case 1:
                    Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.007f, 0f), new Vector3(0.07f, 0.007f, 0.07f), dough);
                    Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.015f, -0.008f), new Vector3(0.028f, 0.006f, 0.022f), icing);
                    float[] xs = { -0.02f, -0.007f, 0.007f, 0.02f };
                    float[] zs = { 0.01f, 0.02f, 0.02f, 0.01f };
                    for (int i = 0; i < 4; i++)
                        Prim(PrimitiveType.Sphere, t, new Vector3(xs[i], 0.015f, zs[i]), new Vector3(0.011f, 0.005f, 0.011f), icing);
                    break;
                default:
                    Prim(PrimitiveType.Cylinder, t, new Vector3(-0.013f, 0.008f, 0.008f), new Vector3(0.042f, 0.008f, 0.042f), dough);
                    Prim(PrimitiveType.Cylinder, t, new Vector3(0.013f, 0.008f, 0.008f), new Vector3(0.042f, 0.008f, 0.042f), dough);
                    Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.008f, -0.008f), new Vector3(0.042f, 0.016f, 0.042f), new Vector3(0f, 45f, 0f), dough);
                    Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.017f, 0.002f), new Vector3(0.026f, 0.005f, 0.022f), icing);
                    break;
            }
            return root;
        }

        /// Tăvița rotundă pastelată a Brutăriei Lunii (platourile se adaugă separat).
        public static GameObject BakeryTray(string name)
        {
            var model = AutoAssetLoader.TryModel("bakery_plate", name);
            if (model != null) return model;
            var root = new GameObject(name);
            var t = root.transform;
            var tray = MakeMat(new Color(0.8f, 0.74f, 0.96f));
            var rim = MakeMat(new Color(1f, 0.85f, 0.55f));
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.008f, 0f), new Vector3(0.6f, 0.008f, 0.6f), tray);
            Ring(t, new Vector3(0f, 0.018f, 0f), Vector3.right, Vector3.forward, 0.3f, 28, 0.024f, rim);
            // semilună decorativă pe margine
            var moon = MakeMat(new Color(1f, 0.92f, 0.6f));
            for (int i = 0; i < 7; i++)
            {
                float k = i / 6f;
                float ang = Mathf.Lerp(40f, 320f, k) * Mathf.Deg2Rad;
                float s = 0.01f + 0.02f * Mathf.Sin(k * Mathf.PI);
                Prim(PrimitiveType.Sphere, t, new Vector3(Mathf.Cos(ang) * 0.035f, 0.02f, -0.255f + Mathf.Sin(ang) * 0.035f), new Vector3(s, 0.008f, s), moon);
            }
            return root;
        }

        /// Farfurie de porțelan pastelat cu bordură aurie lucioasă (inel auriu cu „sclipiri” mai deschise).
        /// doubleRim = încă un inel auriu fin în interior (platoul de livrare).
        public static GameObject PorcelainPlate(string name, Color porcelain, Color gold, float diameter, bool doubleRim)
        {
            var root = new GameObject(name);
            var t = root.transform;
            var china = MakeMat(porcelain);
            var chinaShade = MakeMat(Color.Lerp(porcelain, new Color(0.85f, 0.8f, 0.9f), 0.25f));
            var goldMat = MakeMat(gold);
            var goldShine = MakeMat(Color.Lerp(gold, Color.white, 0.55f));
            float r = diameter * 0.5f;
            // talpă mică + farfurie + marginea ridicată
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.012f, 0f), new Vector3(diameter * 0.6f, 0.006f, diameter * 0.6f), chinaShade);
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.021f, 0f), new Vector3(diameter, 0.004f, diameter), china);
            Ring(t, new Vector3(0f, 0.027f, 0f), Vector3.right, Vector3.forward, r * 0.93f, 22, 0.014f, china);
            // inelul auriu exterior, cu mărgele lucioase alternante
            int beads = 26;
            for (int i = 0; i < beads; i++)
            {
                float a = i * Mathf.PI * 2f / beads;
                Prim(PrimitiveType.Sphere, t, new Vector3(Mathf.Cos(a) * r, 0.029f, Mathf.Sin(a) * r), Vector3.one * 0.011f, i % 3 == 0 ? goldShine : goldMat);
            }
            if (doubleRim)
                Ring(t, new Vector3(0f, 0.0235f, 0f), Vector3.right, Vector3.forward, r * 0.7f, 24, 0.006f, goldMat);
            return root;
        }

        /// Strălucire aurie: emisie pe materialele Lit (unde există) + culoare intensă pe cele Unlit.
        public static void Glow(GameObject go, Color color, float intensity)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;
                foreach (var m in r.materials)
                {
                    CozyFx.SetColor(m, color);
                    if (m.HasProperty("_EmissionColor"))
                    {
                        m.EnableKeyword("_EMISSION");
                        m.SetColor("_EmissionColor", color * intensity);
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                }
            }
        }

        public static GameObject Plate(string name, Color color, float diameter)
        {
            var root = new GameObject(name);
            var mat = MakeMat(color);
            var rim = MakeMat(Color.Lerp(color, Color.white, 0.5f));
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.022f, 0f), new Vector3(diameter, 0.005f, diameter), mat);
            Ring(root.transform, new Vector3(0f, 0.027f, 0f), Vector3.right, Vector3.forward, diameter * 0.5f, 16, 0.012f, rim);
            return root;
        }
            // ------------------------------------------------------------ Ceai cu Lumi

        /// Ceainic pastelat (model "kettle" sau procedural): corp rotund, cioc, toartă, capac separabil ("Lid"), abur.
        public static GameObject Kettle(string name)
        {
            var model = AutoAssetLoader.TryModel("kettle", name);
            if (model != null)
            {
                float top = 0.24f;
                foreach (var r in model.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y - model.transform.position.y);
                CozyFx.Steam(model.transform, new Vector3(0f, top, 0f));
                return model;
            }
            var root = new GameObject(name);
            var t = root.transform;
            var body = MakeMat(new Color(1f, 0.8f, 0.86f));
            var cream = MakeMat(new Color(1f, 0.95f, 0.86f));
            var mint = MakeMat(new Color(0.62f, 0.88f, 0.8f));
            var gold = MakeMat(new Color(1f, 0.84f, 0.42f));
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.008f, 0f), new Vector3(0.14f, 0.008f, 0.14f), cream);
            Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.095f, 0f), new Vector3(0.2f, 0.17f, 0.2f), body);
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.12f, 0f), new Vector3(0.198f, 0.006f, 0.198f), gold);
            // inimioară pe burtică
            Prim(PrimitiveType.Sphere, t, new Vector3(-0.012f, 0.1f, 0.096f), new Vector3(0.026f, 0.026f, 0.01f), mint);
            Prim(PrimitiveType.Sphere, t, new Vector3(0.012f, 0.1f, 0.096f), new Vector3(0.026f, 0.026f, 0.01f), mint);
            Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.088f, 0.096f), new Vector3(0.024f, 0.024f, 0.008f), new Vector3(0f, 0f, 45f), mint);
            // cioc (spre dreapta) + toartă (spre stânga)
            Prim(PrimitiveType.Capsule, t, new Vector3(0.115f, 0.115f, 0f), new Vector3(0.034f, 0.06f, 0.034f), new Vector3(0f, 0f, -55f), mint);
            Ring(t, new Vector3(-0.11f, 0.105f, 0f), Vector3.up, Vector3.right, 0.048f, 12, 0.02f, mint);
            // capac separabil
            var lid = new GameObject("Lid").transform;
            lid.SetParent(t, false);
            lid.localPosition = new Vector3(0f, 0.172f, 0f);
            Prim(PrimitiveType.Sphere, lid, Vector3.zero, new Vector3(0.12f, 0.03f, 0.12f), cream);
            Prim(PrimitiveType.Sphere, lid, new Vector3(0f, 0.022f, 0f), Vector3.one * 0.026f, gold);
            CozyFx.Steam(t, new Vector3(0.155f, 0.155f, 0f));
            return root;
        }

        /// Ceașcă de ceai caldă pe farfurioară (model "tea_cup" sau procedural), cu abur.
        public static GameObject TeaCup(string name)
        {
            var model = AutoAssetLoader.TryModel("tea_cup", name);
            if (model != null)
            {
                CozyFx.Steam(model.transform, new Vector3(0f, 0.09f, 0f), 0.7f);
                return model;
            }
            var root = new GameObject(name);
            var t = root.transform;
            var cream = MakeMat(new Color(1f, 0.96f, 0.9f));
            var lav = MakeMat(new Color(0.86f, 0.8f, 1f));
            var tea = MakeMat(new Color(0.86f, 0.6f, 0.36f));
            var gold = MakeMat(new Color(1f, 0.84f, 0.42f));
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.004f, 0f), new Vector3(0.13f, 0.004f, 0.13f), cream);
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.04f, 0f), new Vector3(0.075f, 0.034f, 0.075f), lav);
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.075f, 0f), new Vector3(0.079f, 0.003f, 0.079f), gold);
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.071f, 0f), new Vector3(0.066f, 0.002f, 0.066f), tea);
            Ring(t, new Vector3(0.05f, 0.045f, 0f), Vector3.up, Vector3.right, 0.018f, 9, 0.01f, lav);
            CozyFx.Steam(t, new Vector3(0f, 0.08f, 0f), 0.7f);
            return root;
        }
    }
}
