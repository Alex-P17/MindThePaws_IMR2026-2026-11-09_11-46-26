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

        /// Cană: pivotul e la bază, ca să stea fix pe suprafață.
        public static GameObject Mug(string name, Color color)
        {
            var root = new GameObject(name);
            var mat = MakeMat(color);
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.05f, 0), new Vector3(0.08f, 0.05f, 0.08f), mat);
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0.05f, 0.05f, 0), new Vector3(0.015f, 0.055f, 0.03f), mat);
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
    }
}
