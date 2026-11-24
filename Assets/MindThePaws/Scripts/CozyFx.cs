using System.Collections.Generic;
using UnityEngine;

namespace MindThePaws
{
    /// <summary>
    /// Materiale transparente, texturi procedurale (punct moale, bulă, petală) și efecte mici (sclipici, inele).
    /// Folosește șabloanele din Resources/MindThePaws/Generated (create automat de AutoFolderSetup),
    /// iar dacă lipsesc, își construiește singur materialele.
    /// </summary>
    public static class CozyFx
    {
        static Material ghostUnlitTpl, ghostLitTpl, particleTpl;
        static bool templatesLoaded;
        static Texture2D softDot, bubbleTex, petalTex;
        static Material particleShared, lineShared;

        static void LoadTemplates()
        {
            if (templatesLoaded) return;
            templatesLoaded = true;
            ghostUnlitTpl = Resources.Load<Material>("MindThePaws/Generated/GhostUnlit");
            ghostLitTpl = Resources.Load<Material>("MindThePaws/Generated/GhostLit");
            particleTpl = Resources.Load<Material>("MindThePaws/Generated/ParticleUnlit");
        }

        public static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static Color GetColor(Material m)
        {
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        public static Texture GetTexture(Material m)
        {
            if (m == null) return null;
            if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) return m.GetTexture("_BaseMap");
            if (m.HasProperty("_MainTex")) return m.GetTexture("_MainTex");
            return null;
        }

        public static void SetTexture(Material m, Texture t)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
        }

        public static void MakeTransparent(Material m)
        {
            if (m == null) return;
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        /// Material unlit semi-transparent (fantome, carduri, halouri).
        public static Material TransparentMat(Color c)
        {
            LoadTemplates();
            Material m;
            if (ghostUnlitTpl != null) m = new Material(ghostUnlitTpl);
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null || !sh.isSupported) sh = Shader.Find("Sprites/Default");
                m = new Material(sh);
                MakeTransparent(m);
            }
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            SetColor(m, c);
            m.color = c;
            return m;
        }

        /// Copie transparentă a unui material existent (păstrează textura modelului din Blender).
        public static Material GhostOf(Material src, Color tintAlpha)
        {
            LoadTemplates();
            bool lit = src != null && src.shader != null && src.shader.name.Contains("Lit") && !src.shader.name.Contains("Unlit");
            var tpl = lit ? ghostLitTpl : ghostUnlitTpl;
            Material m;
            if (tpl != null) m = new Material(tpl);
            else if (src != null)
            {
                m = new Material(src);
                MakeTransparent(m);
            }
            else return TransparentMat(tintAlpha);
            var tex = GetTexture(src);
            if (tex != null) SetTexture(m, tex);
            var baseC = src != null ? GetColor(src) : Color.white;
            var c = Color.Lerp(baseC, Color.white, 0.45f) * new Color(tintAlpha.r, tintAlpha.g, tintAlpha.b, 1f);
            c.a = tintAlpha.a;
            SetColor(m, c);
            return m;
        }

        /// Material care respectă culorile per-vertex (particule, LineRenderer).
        public static Material VertexColorMat(Texture tex)
        {
            LoadTemplates();
            Material m;
            if (particleTpl != null) m = new Material(particleTpl);
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (sh == null || !sh.isSupported) sh = Shader.Find("Sprites/Default");
                m = new Material(sh);
                MakeTransparent(m);
            }
            if (tex != null) SetTexture(m, tex);
            SetColor(m, Color.white);
            return m;
        }

        public static Material ParticleMat()
        {
            if (particleShared == null) particleShared = VertexColorMat(SoftDot);
            return particleShared;
        }

        public static Material LineMat()
        {
            if (lineShared == null) lineShared = VertexColorMat(null);
            return lineShared;
        }

        // ------------------------------------------------------------ texturi procedurale

        public static Texture2D SoftDot
        {
            get
            {
                if (softDot == null) softDot = MakeTex(64, (x, y) =>
                {
                    float d = Mathf.Sqrt(x * x + y * y);
                    float a = Mathf.Clamp01(1f - d);
                    return new Color(1f, 1f, 1f, a * a * (3f - 2f * a));
                });
                return softDot;
            }
        }

        public static Texture2D BubbleTex
        {
            get
            {
                if (bubbleTex == null) bubbleTex = MakeTex(96, (x, y) =>
                {
                    float d = Mathf.Sqrt(x * x + y * y);
                    if (d > 1f) return new Color(1f, 1f, 1f, 0f);
                    float rim = Mathf.Pow(Mathf.Clamp01((d - 0.6f) / 0.4f), 2f) * Mathf.Clamp01((1f - d) * 12f);
                    float hx = x + 0.35f, hy = y - 0.35f;
                    float hl = Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy) / 0.22f);
                    float a = Mathf.Clamp01(0.08f + rim * 0.85f + hl * 0.9f);
                    return new Color(1f, 1f, 1f, a);
                });
                return bubbleTex;
            }
        }

        static Texture2D heartTex;
        static Material heartMat;

        public static Texture2D HeartTex
        {
            get
            {
                if (heartTex == null) heartTex = MakeTex(64, (x, y) =>
                {
                    float hx = x * 1.25f, hy = y * 1.25f + 0.2f;
                    float a = hx * hx + hy * hy - 1f;
                    float f = a * a * a - hx * hx * hy * hy * hy;   // inimă implicită: f <= 0 înăuntru
                    float alpha = Mathf.Clamp01(-f * 18f);
                    return new Color(1f, 1f, 1f, alpha);
                });
                return heartTex;
            }
        }

        /// Val de inimioare roz care urcă (mângâieri, bop pe nas).
        public static void Hearts(Vector3 pos, int count = 12, float size = 0.04f)
        {
            if (heartMat == null) heartMat = VertexColorMat(HeartTex);
            Burst(pos, new Color(1f, 0.55f, 0.75f), count, 0.35f, size, heartMat, -0.12f);
        }

        public static Texture2D PetalTex
        {
            get
            {
                if (petalTex == null) petalTex = MakeTex(64, (x, y) =>
                {
                    float yy = (y + 1f) * 0.5f;               // 0 jos, 1 sus
                    float w = Mathf.Sin(yy * Mathf.PI) * (0.55f + 0.25f * yy);
                    float e = Mathf.Abs(x) / Mathf.Max(0.001f, w);
                    float a = Mathf.Clamp01((1f - e) * 4f) * Mathf.Clamp01(yy * 6f) * Mathf.Clamp01((1f - yy) * 6f);
                    float shade = 0.85f + 0.15f * (1f - Mathf.Abs(x));
                    return new Color(shade, shade, shade, a);
                });
                return petalTex;
            }
        }

        static Texture2D MakeTex(int size, System.Func<float, float, Color> f)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            var px = new Color[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    float x = (i + 0.5f) / size * 2f - 1f;
                    float y = (j + 0.5f) / size * 2f - 1f;
                    px[j * size + i] = f(x, y);
                }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        static Texture2D skyTex;

        /// Fundal celest eliptic: bleumarin-mov în centru, se topește spre margini, cu stele mărunte „pictate”.
        public static Texture2D SkyTex
        {
            get
            {
                if (skyTex != null) return skyTex;
                var rnd = new System.Random(21);
                var dots = new Vector3[70];
                for (int i = 0; i < dots.Length; i++)
                    dots[i] = new Vector3((float)rnd.NextDouble() * 2f - 1f, (float)rnd.NextDouble() * 2f - 1f, 0.004f + (float)rnd.NextDouble() * 0.01f);
                skyTex = MakeTex(256, (x, y) =>
                {
                    float d = Mathf.Sqrt(x * x + y * y);
                    float edge = Mathf.Clamp01((1f - d) / 0.35f);
                    edge = edge * edge * (3f - 2f * edge);
                    var top = new Color(0.16f, 0.14f, 0.38f);
                    var bottom = new Color(0.42f, 0.27f, 0.55f);
                    var c = Color.Lerp(bottom, top, (y + 1f) * 0.5f);
                    float star = 0f;
                    for (int i = 0; i < dots.Length; i++)
                    {
                        float dx = x - dots[i].x, dy = y - dots[i].y;
                        star = Mathf.Max(star, Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / dots[i].z));
                    }
                    c = Color.Lerp(c, new Color(1f, 0.95f, 0.85f), star);
                    c.a = edge * Mathf.Lerp(0.62f, 0.95f, star);
                    return c;
                });
                return skyTex;
            }
        }

        /// Stea 3D cu fațete (vârf în față, spate turtit). Submesh 0 = fațete luminoase, 1 = fațete umbrite.
        public static Mesh StarMesh(float outer, float inner, float depth, int points = 5)
        {
            int n = points * 2;
            var v = new Vector3[n + 2];
            v[0] = new Vector3(0f, 0f, -depth);
            v[1] = new Vector3(0f, 0f, depth * 0.6f);
            for (int i = 0; i < n; i++)
            {
                float a = (90f + i * 180f / points) * Mathf.Deg2Rad;
                float r = i % 2 == 0 ? outer : inner;
                v[i + 2] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
            }
            var lit = new List<int>();
            var dark = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int a = i + 2, b = (i + 1) % n + 2;
                var front = i % 2 == 0 ? lit : dark;
                front.Add(0); front.Add(b); front.Add(a);
                dark.Add(1); dark.Add(a); dark.Add(b);
            }
            var m = new Mesh { name = "StarMesh" };
            m.vertices = v;
            m.subMeshCount = 2;
            m.SetTriangles(lit, 0);
            m.SetTriangles(dark, 1);
            m.RecalculateBounds();
            m.RecalculateNormals();
            return m;
        }

        /// Sclipici care cade continuu dintr-un punct (stele, felinare).
        public static ParticleSystem Sparkler(Transform parent, Color color, float rate, float radius = 0.03f)
        {
            var go = new GameObject("Sparkler");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.005f, 0.013f);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.7f));
            main.gravityModifier = 0.025f;
            main.maxParticles = 80;
            var em = ps.emission;
            em.rateOverTime = rate;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeGradient(Color.white, Color.white);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = ParticleMat();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// Abur cald care urcă încet (ceainic, ceașcă de ceai).
        public static ParticleSystem Steam(Transform parent, Vector3 localPos, float strength = 1f)
        {
            var go = new GameObject("Steam");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.55f), new Color(1f, 0.92f, 0.85f, 0.4f));
            main.gravityModifier = -0.01f;
            main.maxParticles = 60;
            var em = ps.emission;
            em.rateOverTime = 9f * strength;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 8f;
            sh.radius = 0.01f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeGradient(Color.white, Color.white);
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
            var nz = ps.noise;
            nz.enabled = true;
            nz.strength = 0.03f;
            nz.frequency = 1.2f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = ParticleMat();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        /// Quad întors mereu spre cameră, cu textură moale (halou de lumină).
        public static Transform Halo(Transform parent, Vector3 pos, float size, Color color, out Material mat)
        {
            mat = TransparentMat(color);
            SetTexture(mat, SoftDot);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = "Halo";
            q.transform.SetParent(parent, false);
            q.transform.position = pos;
            q.transform.localScale = Vector3.one * size;
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return q.transform;
        }

        // ------------------------------------------------------------ nuanțe pe un obiect întreg

        /// Pune aceeași culoare pe toate materialele (instanțe proprii) dintr-un obiect.
        public static void SetTint(GameObject go, Color c)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;
                var ms = r.materials;
                for (int i = 0; i < ms.Length; i++)
                {
                    var keepA = GetColor(ms[i]).a;
                    var cc = c;
                    if (c.a >= 0.999f) cc.a = keepA;
                    SetColor(ms[i], cc);
                }
            }
        }

        /// Face tot obiectul translucid, fantomatic.
        public static void ApplyGhost(GameObject go, float alpha = 0.42f)
        {
            if (go == null) return;
            var cache = new Dictionary<Material, Material>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    var src = ms[i];
                    if (src != null && cache.TryGetValue(src, out var done)) { ms[i] = done; continue; }
                    var g = GhostOf(src, new Color(0.9f, 0.97f, 1f, alpha));
                    if (src != null) cache[src] = g;
                    ms[i] = g;
                }
                r.sharedMaterials = ms;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// Amestecă culoarea tuturor materialelor spre o nuanță (momeli, lăbuțe colorate).
        public static void ApplyTint(GameObject go, Color tint, float amount)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;
                var ms = r.materials;
                for (int i = 0; i < ms.Length; i++)
                {
                    var c = GetColor(ms[i]);
                    var n = Color.Lerp(c, tint, amount);
                    n.a = c.a;
                    SetColor(ms[i], n);
                }
            }
        }

        // ------------------------------------------------------------ efecte

        /// Explozie scurtă de sclipici (particule aurii / colorate) care se distruge singură.
        public static void Burst(Vector3 pos, Color color, int count = 24, float speed = 0.45f, float size = 0.025f, Material mat = null, float gravity = -0.05f)
        {
            var go = new GameObject("CozyBurst");
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.useUnscaledTime = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.6f));
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count + 8;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission;
            em.enabled = false;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.02f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeGradient(Color.white, Color.white);
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat != null ? mat : ParticleMat();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            ps.Emit(count);
        }

        public static Gradient FadeGradient(Color a, Color b)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        /// Cerc din LineRenderer în planul XY local.
        public static LineRenderer Circle(string name, Transform parent, float radius, float width, Color color, int segments = 48)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = segments;
            lr.widthMultiplier = width;
            lr.sharedMaterial = LineMat();
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.startColor = color;
            lr.endColor = color;
            SetCircle(lr, radius);
            return lr;
        }

        public static void SetCircle(LineRenderer lr, float radius)
        {
            int n = lr.positionCount;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
        }

        /// Dreptunghi cu colțuri rotunjite (în planul XY, vizibil dinspre -Z), centrat în origine.
        public static Mesh RoundedRect(Mesh mesh, float w, float h, float radius, int cornerSegs = 6)
        {
            if (mesh == null) mesh = new Mesh { name = "RoundedRect" };
            radius = Mathf.Min(radius, w * 0.5f, h * 0.5f);
            var pts = new List<Vector3>();
            float hx = w * 0.5f - radius, hy = h * 0.5f - radius;
            // sensul acelor de ceas (privit dinspre -Z, x spre dreapta, y în sus)
            Vector2[] centers = { new Vector2(-hx, hy), new Vector2(hx, hy), new Vector2(hx, -hy), new Vector2(-hx, -hy) };
            float[] startAng = { 180f, 90f, 0f, -90f };
            for (int c = 0; c < 4; c++)
                for (int s = 0; s <= cornerSegs; s++)
                {
                    float a = (startAng[c] - 90f * s / cornerSegs) * Mathf.Deg2Rad;
                    pts.Add(new Vector3(centers[c].x + Mathf.Cos(a) * radius, centers[c].y + Mathf.Sin(a) * radius, 0f));
                }
            var verts = new Vector3[pts.Count + 1];
            verts[0] = Vector3.zero;
            for (int i = 0; i < pts.Count; i++) verts[i + 1] = pts[i];
            var tris = new int[pts.Count * 3];
            for (int i = 0; i < pts.Count; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % pts.Count + 1;
            }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
