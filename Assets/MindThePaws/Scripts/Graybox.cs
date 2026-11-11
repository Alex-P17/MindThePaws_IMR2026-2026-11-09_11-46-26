using UnityEngine;

namespace MindThePaws
{
    /// <summary>
    /// Obiecte "graybox" construite din primitive Unity, ca să putem testa
    /// înainte să fie gata modelele din Blender. Le înlocuim cu prefab-uri mai târziu.
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
            var root = new GameObject(name);
            var orange = MakeMat(new Color(0.95f, 0.62f, 0.3f));
            var white = MakeMat(new Color(1f, 0.96f, 0.9f));
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 0.12f, 0), new Vector3(0.16f, 0.13f, 0.32f), orange);
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 0.08f, 0.02f), new Vector3(0.14f, 0.06f, 0.26f), white);
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 0.2f, 0.19f), new Vector3(0.12f, 0.11f, 0.11f), orange);
            Prim(PrimitiveType.Cube, root.transform, new Vector3(-0.04f, 0.28f, 0.19f), new Vector3(0.03f, 0.06f, 0.02f), orange);
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0.04f, 0.28f, 0.19f), new Vector3(0.03f, 0.06f, 0.02f), orange);
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0) ? -0.05f : 0.05f;
                float z = (i < 2) ? 0.1f : -0.1f;
                Prim(PrimitiveType.Cube, root.transform, new Vector3(x, 0.03f, z), new Vector3(0.04f, 0.06f, 0.04f), white);
            }
            return root;
        }

        public static TextMesh Text(string name, float characterSize)
        {
            var go = new GameObject(name);
            var tm = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            tm.fontSize = 64;
            tm.characterSize = characterSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            return tm;
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
                default: return "Cana";
            }
        }

        public static GameObject TreasureByType(string typeId, bool isGhost, bool isDecoy)
        {
            var c = TreasureColor(typeId);
            if (isDecoy) c = Shift(c);
            if (isGhost) c = Pale(c);
            string n = typeId + (isGhost ? "_ghost" : "") + (isDecoy ? "_decoy" : "");
            switch (typeId)
            {
                case "keys": return Keys(n, c);
                case "diary": return Diary(n, c);
                case "glasses": return CatGlasses(n, c);
                case "glove": return MushroomGlove(n, c);
                case "lantern": return MoonLantern(n, c);
                default: return Mug(n, c);
            }
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
            Prim(PrimitiveType.Sphere, t, new Vector3(-0.02f, 0.18f, 0.143f), Vector3.one * 0.014f, white);
            Prim(PrimitiveType.Sphere, t, new Vector3(0.02f, 0.18f, 0.143f), Vector3.one * 0.014f, white);
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
            Prim(PrimitiveType.Sphere, t, new Vector3(-0.009f, 0.124f, 0.02f), Vector3.one * 0.006f, eye);
            Prim(PrimitiveType.Sphere, t, new Vector3(0.009f, 0.124f, 0.02f), Vector3.one * 0.006f, eye);
            Prim(PrimitiveType.Cube, t, new Vector3(-0.032f, 0.1f, -0.02f), new Vector3(0.03f, 0.065f, 0.003f), new Vector3(0f, 0f, 25f), wing);
            Prim(PrimitiveType.Cube, t, new Vector3(0.032f, 0.1f, -0.02f), new Vector3(0.03f, 0.065f, 0.003f), new Vector3(0f, 0f, -25f), wing);
            return root;
        }

        public static GameObject MemoryFlower(string name, Color petalColor, float heightScale)
        {
            float h = 0.3f * Mathf.Clamp(heightScale, 0.4f, 1.5f);
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
            return root;
        }

        public static GameObject StarJar(string name)
        {
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
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.21f, 0f), new Vector3(0.1f, 0.01f, 0.1f), lid);
            return root;
        }
    }
}