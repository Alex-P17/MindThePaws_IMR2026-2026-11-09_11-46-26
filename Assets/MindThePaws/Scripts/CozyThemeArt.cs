using System.Collections.Generic;
using UnityEngine;

namespace MindThePaws
{
    /// <summary>
    /// Arta kawaii-cozy generată la pornire (fără asset-uri): lemn cald pastel, panouri de pergament cu bordură dublă,
    /// butoane-pastilă, fundiță, plus iconițe 128×128 desenate anti-aliased (contur prună, highlight alb).
    /// Totul e memorat după prima folosire.
    /// </summary>
    public static class CozyThemeArt
    {
        // ------------------------------------------------------------ paletă
        public static readonly Color Outline = new Color(0.36f, 0.24f, 0.34f, 1f);
        public static readonly Color Turquoise = new Color(0.5f, 0.86f, 0.82f, 1f);
        public static readonly Color PinkPill = new Color(1f, 0.72f, 0.8f, 1f);
        public static readonly Color WoodLight = new Color(0.96f, 0.84f, 0.7f, 1f);
        public static readonly Color WoodWarm = new Color(0.9f, 0.72f, 0.56f, 1f);

        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        static Texture2D wood;

        // ================================================================ lemn

        /// Lemn cald de stejar/cireș pastel, cu fibră fină și îmbinări de scânduri (se repetă fără cusătură pe orizontală).
        public static Texture2D WoodTexture
        {
            get
            {
                if (wood != null) return wood;
                const int n = 256;
                wood = new Texture2D(n, n, TextureFormat.RGBA32, true);
                wood.wrapMode = TextureWrapMode.Repeat;
                wood.filterMode = FilterMode.Trilinear;
                var px = new Color[n * n];
                var light = new Color(0.97f, 0.86f, 0.73f);
                var dark = new Color(0.86f, 0.68f, 0.54f);
                for (int y = 0; y < n; y++)
                {
                    int plank = y / 64;
                    float py = (y % 64) / 64f;
                    for (int x = 0; x < n; x++)
                    {
                        float u = x / (float)n;
                        // fibra: valuri lungi pe orizontală, deformate cu zgomot
                        float warp = Mathf.PerlinNoise(u * 4f + plank * 7.3f, py * 2f + plank) * 3.5f;
                        float grain = Mathf.Sin((py * 18f + warp) * Mathf.PI);
                        grain = Mathf.Pow(Mathf.Abs(grain), 6f);
                        float fine = Mathf.PerlinNoise(u * 60f, y * 0.9f) * 0.12f;
                        var c = Color.Lerp(light, dark, grain * 0.55f + fine);
                        // nuanță ușor diferită pe fiecare scândură
                        c = Color.Lerp(c, new Color(1f, 0.82f, 0.76f), (plank % 2) * 0.12f);
                        // îmbinarea dintre scânduri
                        float seam = Mathf.Clamp01(1f - Mathf.Min(y % 64, 63 - y % 64) / 2.5f);
                        c = Color.Lerp(c, new Color(0.72f, 0.55f, 0.45f), seam * 0.7f);
                        c.a = 1f;
                        px[y * n + x] = c;
                    }
                }
                wood.SetPixels(px);
                wood.Apply(true, false);   // rămâne lizibilă: WoodPanel o eșantionează
                return wood;
            }
        }

        /// Material de lemn (respectă materialul de bază din Inspector dacă e setat) cu nuanță și repetare.
        public static Material WoodMat(Color tint, float tiling = 1f)
        {
            var m = Graybox.MakeMat(tint);
            CozyFx.SetTexture(m, WoodTexture);
            m.mainTextureScale = new Vector2(tiling, tiling);
            if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            return m;
        }

        /// Pune lemn pe toate randerele unui obiect (păstrează nuanța fiecăruia, amestecată cu lemnul).
        public static void ApplyWood(GameObject go, float tiling = 1f, float keepColor = 0.35f)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;
                var ms = r.materials;
                for (int i = 0; i < ms.Length; i++)
                {
                    var c = CozyFx.GetColor(ms[i]);
                    CozyFx.SetTexture(ms[i], WoodTexture);
                    if (ms[i].HasProperty("_BaseMap")) ms[i].SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
                    ms[i].mainTextureScale = new Vector2(tiling, tiling);
                    var t = Color.Lerp(Color.white, c, keepColor);
                    t.a = 1f;
                    CozyFx.SetColor(ms[i], t);
                }
            }
        }

        // ================================================================ sprite-uri UI (9-slice)

        /// Pergament crem-cald cu bordură dublă (aur + lemn), colțuri rotunjite. Se nuanțează cu Image.color.
        public static Sprite Parchment => Cached("parchment", () =>
        {
            const int n = 128;
            var p = new Painter(n);
            float R = 36f;
            p.Shape((x, y) => P.RoundRect(x, y, n * 0.5f, n * 0.5f, n * 0.5f - 1f, n * 0.5f - 1f, R), new Color(0.98f, 0.8f, 0.45f));
            p.Shape((x, y) => P.RoundRect(x, y, n * 0.5f, n * 0.5f, n * 0.5f - 6f, n * 0.5f - 6f, R - 5f), new Color(1f, 0.97f, 0.9f));
            p.Shape((x, y) => P.RoundRect(x, y, n * 0.5f, n * 0.5f, n * 0.5f - 9f, n * 0.5f - 9f, R - 8f), new Color(0.78f, 0.6f, 0.46f));
            p.Shape((x, y) => P.RoundRect(x, y, n * 0.5f, n * 0.5f, n * 0.5f - 11f, n * 0.5f - 11f, R - 10f), new Color(1f, 0.965f, 0.9f));
            // fibră de hârtie foarte fină
            p.Modulate((x, y, c) =>
            {
                float g = 0.97f + 0.03f * Mathf.PerlinNoise(x * 0.3f, y * 0.3f);
                return new Color(c.r * g, c.g * g, c.b * g, c.a);
            });
            return p.ToSprite(new Vector4(46, 46, 46, 46));
        });

        /// Ramă de lemn rotunjită (tăvița inventarului, sloturi).
        public static Sprite WoodPanel => Cached("woodpanel", () =>
        {
            const int n = 128;
            var w = WoodTexture;
            var p = new Painter(n);
            p.Shape((x, y) => P.RoundRect(x, y, n * 0.5f, n * 0.5f, n * 0.5f - 1f, n * 0.5f - 1f, 30f), new Color(0.7f, 0.52f, 0.42f));
            p.ShapeTex((x, y) => P.RoundRect(x, y, n * 0.5f, n * 0.5f, n * 0.5f - 5f, n * 0.5f - 5f, 26f), (x, y) => w.GetPixelBilinear(x / n, y / n * 0.5f));
            // lumină sus, umbră jos
            p.Modulate((x, y, c) =>
            {
                float k = Mathf.Lerp(0.9f, 1.06f, y / n);
                return new Color(Mathf.Min(1f, c.r * k), Mathf.Min(1f, c.g * k), Mathf.Min(1f, c.b * k), c.a);
            });
            return p.ToSprite(new Vector4(40, 40, 40, 40));
        });

        /// Interiorul unui slot: crem adâncit în lemn.
        public static Sprite SlotInset => Cached("slotinset", () =>
        {
            const int n = 96;
            var p = new Painter(n);
            p.Shape((x, y) => P.RoundRect(x, y, n * 0.5f, n * 0.5f, n * 0.5f - 1f, n * 0.5f - 1f, 24f), new Color(0.78f, 0.6f, 0.48f));
            p.Shape((x, y) => P.RoundRect(x, y - 1.5f, n * 0.5f, n * 0.5f, n * 0.5f - 5f, n * 0.5f - 6f, 20f), new Color(1f, 0.965f, 0.9f));
            return p.ToSprite(new Vector4(32, 32, 32, 32));
        });

        /// Buton-pastilă alb (se nuanțează turcoaz / roz), cu highlight sus și ton mai închis jos.
        public static Sprite Pill => Cached("pill", () =>
        {
            const int w = 128, h = 72;
            var p = new Painter(w, h);
            p.Shape((x, y) => P.RoundRect(x, y, w * 0.5f, h * 0.5f, w * 0.5f - 1f, h * 0.5f - 1f, h * 0.5f - 1f), Color.white);
            p.Modulate((x, y, c) =>
            {
                float k = Mathf.Lerp(0.86f, 1f, y / h);
                float hl = Mathf.Clamp01(1f - Mathf.Abs(y - h * 0.78f) / 5f) * Mathf.Clamp01((x - 18f) / 10f) * Mathf.Clamp01((w - 18f - x) / 10f);
                return new Color(Mathf.Min(1f, c.r * k + hl * 0.25f), Mathf.Min(1f, c.g * k + hl * 0.25f), Mathf.Min(1f, c.b * k + hl * 0.25f), c.a);
            });
            return p.ToSprite(new Vector4(36, 34, 36, 34));
        });

        /// Fundiță roz-aurie pentru partea de sus a cardurilor.
        public static Sprite Bow => Cached("bow", () =>
        {
            const int w = 128, h = 72;
            var p = new Painter(w, h);
            var pink = new Color(1f, 0.62f, 0.74f);
            var gold = new Color(1f, 0.83f, 0.42f);
            p.Shape((x, y) => P.Rotated(x, y, w * 0.5f - 26f, h * 0.5f + 4f, 20f, (a, b) => P.Ellipse(a, b, 0f, 0f, 30f, 19f)), pink, 3.5f, Outline);
            p.Shape((x, y) => P.Rotated(x, y, w * 0.5f + 26f, h * 0.5f + 4f, -20f, (a, b) => P.Ellipse(a, b, 0f, 0f, 30f, 19f)), pink, 3.5f, Outline);
            p.Shape((x, y) => P.Capsule(x, y, w * 0.5f - 6f, h * 0.5f - 2f, w * 0.5f - 18f, 8f, 6f), pink, 3.5f, Outline);
            p.Shape((x, y) => P.Capsule(x, y, w * 0.5f + 6f, h * 0.5f - 2f, w * 0.5f + 18f, 8f, 6f), pink, 3.5f, Outline);
            p.Shape((x, y) => P.Circle(x, y, w * 0.5f, h * 0.5f + 2f, 12f), gold, 3.5f, Outline);
            p.Shape((x, y) => P.Circle(x, y, w * 0.5f - 4f, h * 0.5f + 6f, 3.5f), new Color(1f, 1f, 1f, 0.9f));
            return p.ToSprite(Vector4.zero);
        });

        // ================================================================ iconițe 128×128

        static readonly Color IPink = new Color(1f, 0.66f, 0.76f);
        static readonly Color IPinkLight = new Color(1f, 0.86f, 0.9f);
        static readonly Color IGold = new Color(1f, 0.83f, 0.38f);
        static readonly Color ILav = new Color(0.78f, 0.72f, 0.98f);
        static readonly Color ITeal = new Color(0.45f, 0.82f, 0.84f);
        static readonly Color ICream = new Color(1f, 0.96f, 0.88f);
        static readonly Color IRed = new Color(0.95f, 0.38f, 0.45f);
        static readonly Color IWhite = new Color(1f, 1f, 1f, 0.95f);
        const float OL = 5f;

        /// Iconiță randată din Blender (Resources/MindThePaws/Icons/&lt;cheie&gt;.png, fundal transparent), dacă există.
        public static Sprite IconFromResources(string key)
        {
            string ck = "res_" + key;
            if (sprites.TryGetValue(ck, out var cached)) return cached;
            Sprite sp = null;
            var tex = Resources.Load<Texture2D>("MindThePaws/Icons/" + key);
            if (tex != null)
                sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprites[ck] = sp;        // memorăm și lipsa
            return sp;
        }

        /// Portretul unui cățel (randare a modelului 3D din Blender) pentru poza Polaroid; null dacă nu există.
        public static Sprite CorgiPortrait(string modelKey) => IconFromResources(modelKey);

        public static Sprite TreasureIcon(string typeId)
        {
            var rendered = IconFromResources(typeId);          // ex. mug.png = ceașca cu flori
            if (rendered != null) return rendered;
            switch (typeId)
            {
                case "keys": return Cached("icon_keys", DrawKeys);
                case "diary": return Cached("icon_diary", DrawDiary);
                case "glasses": return Cached("icon_glasses", DrawGlasses);
                case "glove": return Cached("icon_glove", DrawGlove);
                case "lantern": return Cached("icon_lantern", DrawLantern);
                default: return Cached("icon_mug", DrawMug);
            }
        }

        public static Sprite PawIcon => Cached("icon_paw", () =>
        {
            var p = new Painter(128);
            p.Shape((x, y) => P.Ellipse(x, y, 64f, 48f, 30f, 25f), IPink, OL, Outline);
            float[] tx = { 30f, 50f, 78f, 98f };
            float[] ty = { 76f, 94f, 94f, 76f };
            for (int i = 0; i < 4; i++)
            {
                float cx = tx[i], cy = ty[i];
                p.Shape((x, y) => P.Ellipse(x, y, cx, cy, 12f, 14f), IPink, OL, Outline);
            }
            p.Shape((x, y) => P.Ellipse(x, y, 54f, 56f, 8f, 5f), IWhite);
            return p.ToSprite(Vector4.zero);
        });

        public static Sprite HeartIcon => Cached("icon_heart", () =>
        {
            var p = new Painter(128);
            p.Shape((x, y) => P.Heart(x, y, 64f, 60f, 44f), new Color(1f, 0.5f, 0.66f), OL, Outline);
            p.Shape((x, y) => P.Ellipse(x, y, 45f, 76f, 10f, 7f), IWhite);
            return p.ToSprite(Vector4.zero);
        });

        public static Sprite StarIcon => Cached("icon_star", () =>
        {
            var p = new Painter(128);
            p.Shape((x, y) => P.Star(x, y, 64f, 62f, 52f, 23f), IGold, OL, Outline);
            p.Shape((x, y) => P.Ellipse(x, y, 52f, 76f, 8f, 5f), IWhite);
            // obrăjori + ochișori: steluța e și ea kawaii
            p.Shape((x, y) => P.Circle(x, y, 54f, 60f, 4f), Outline);
            p.Shape((x, y) => P.Circle(x, y, 74f, 60f, 4f), Outline);
            p.Shape((x, y) => P.Ellipse(x, y, 46f, 52f, 6f, 3.5f), IPink);
            p.Shape((x, y) => P.Ellipse(x, y, 82f, 52f, 6f, 3.5f), IPink);
            return p.ToSprite(Vector4.zero);
        });

        public static Sprite KettleIcon => Cached("icon_kettle", () =>
        {
            var p = new Painter(128);
            p.Shape((x, y) => P.Annulus(x, y, 26f, 58f, 16f, 5f), ITeal, OL, Outline);
            p.Shape((x, y) => P.Capsule(x, y, 92f, 54f, 114f, 76f, 7f), ITeal, OL, Outline);
            p.Shape((x, y) => P.Ellipse(x, y, 64f, 50f, 40f, 32f), IPink, OL, Outline);
            p.Shape((x, y) => P.Ellipse(x, y, 64f, 82f, 22f, 8f), ICream, OL, Outline);
            p.Shape((x, y) => P.Circle(x, y, 64f, 94f, 6f), IGold, OL, Outline);
            p.Shape((x, y) => P.Heart(x, y, 64f, 46f, 14f), IPinkLight);
            p.Shape((x, y) => P.Ellipse(x, y, 46f, 64f, 8f, 5f), IWhite);
            return p.ToSprite(Vector4.zero);
        });

        public static Sprite CupIcon => Cached("icon_cup", () =>
        {
            var p = new Painter(128);
            p.Shape((x, y) => P.Ellipse(x, y, 64f, 26f, 48f, 10f), ICream, OL, Outline);
            p.Shape((x, y) => P.Annulus(x, y, 94f, 58f, 13f, 5f), ILav, OL, Outline);
            p.Shape((x, y) => P.RoundRect(x, y, 60f, 56f, 30f, 26f, 14f), ILav, OL, Outline);
            p.Shape((x, y) => P.Ellipse(x, y, 60f, 80f, 28f, 6f), new Color(0.86f, 0.6f, 0.36f), 3f, Outline);
            p.Shape((x, y) => P.Heart(x, y, 60f, 52f, 12f), IPinkLight);
            // abur în formă de inimioară
            p.Shape((x, y) => P.Heart(x, y, 52f, 106f, 9f), new Color(1f, 1f, 1f, 0.9f), 3f, Outline);
            p.Shape((x, y) => P.Heart(x, y, 74f, 112f, 7f), new Color(1f, 1f, 1f, 0.9f), 3f, Outline);
            return p.ToSprite(Vector4.zero);
        });

        /// Fețișoară de corgi chibi, cu zgarda în culoarea cățelului (Mochi albastră, Gogoașă roz, Fasolică verde).
        public static Sprite CorgiFace(Color collar)
        {
            string key = "corgi_" + ColorUtility.ToHtmlStringRGB(collar);
            return Cached(key, () =>
            {
                var p = new Painter(128);
                var fur = new Color(0.98f, 0.66f, 0.36f);
                var cream = new Color(1f, 0.96f, 0.9f);
                p.Shape((x, y) => P.Triangle(x, y, 22f, 70f, 30f, 118f, 56f, 86f), fur, OL, Outline);
                p.Shape((x, y) => P.Triangle(x, y, 106f, 70f, 98f, 118f, 72f, 86f), fur, OL, Outline);
                p.Shape((x, y) => P.Triangle(x, y, 32f, 80f, 35f, 104f, 48f, 88f), IPink);
                p.Shape((x, y) => P.Triangle(x, y, 96f, 80f, 93f, 104f, 80f, 88f), IPink);
                p.Shape((x, y) => P.RoundRect(x, y, 64f, 22f, 34f, 8f, 7f), collar, OL, Outline);
                p.Shape((x, y) => P.Ellipse(x, y, 64f, 62f, 42f, 36f), fur, OL, Outline);
                p.Shape((x, y) => P.Ellipse(x, y, 64f, 48f, 24f, 17f), cream);
                p.Shape((x, y) => P.Ellipse(x, y, 64f, 56f, 6f, 4.5f), Outline);
                p.Shape((x, y) => P.Circle(x, y, 47f, 68f, 6.5f), Outline);
                p.Shape((x, y) => P.Circle(x, y, 81f, 68f, 6.5f), Outline);
                p.Shape((x, y) => P.Circle(x, y, 49f, 71f, 2.4f), Color.white);
                p.Shape((x, y) => P.Circle(x, y, 83f, 71f, 2.4f), Color.white);
                p.Shape((x, y) => P.Ellipse(x, y, 36f, 56f, 7f, 4f), IPink);
                p.Shape((x, y) => P.Ellipse(x, y, 92f, 56f, 7f, 4f), IPink);
                p.Shape((x, y) => P.Circle(x, y, 64f, 18f, 6f), IGold, 3f, Outline);
                return p.ToSprite(Vector4.zero);
            });
        }

        /// Fundal pastelat pentru „poza” Polaroid: gradient piersică → lavandă, cu steluțe și inimioare mici.
        public static Sprite PhotoBackground => Cached("photo_bg", () =>
        {
            const int w = 256, h = 200;
            var p = new Painter(w, h);
            p.Shape((x, y) => -1f, Color.white);
            p.Modulate((x, y, c) =>
            {
                float k = y / h;
                var a = new Color(1f, 0.86f, 0.8f);
                var b = new Color(0.84f, 0.82f, 1f);
                var col = Color.Lerp(a, b, k);
                float glow = Mathf.Clamp01(1f - Mathf.Sqrt((x - w * 0.5f) * (x - w * 0.5f) + (y - h * 0.4f) * (y - h * 0.4f)) / 140f);
                col = Color.Lerp(col, new Color(1f, 0.97f, 0.88f), glow * 0.5f);
                col.a = 1f;
                return col;
            });
            var rnd = new System.Random(5);
            for (int i = 0; i < 16; i++)
            {
                float cx = (float)rnd.NextDouble() * w, cy = h * 0.45f + (float)rnd.NextDouble() * h * 0.55f;
                float sz = 4f + (float)rnd.NextDouble() * 5f;
                if (i % 3 == 0) p.Shape((x, y) => P.Heart(x, y, cx, cy, sz * 1.6f), new Color(1f, 0.72f, 0.82f, 0.8f));
                else p.Shape((x, y) => P.Star(x, y, cx, cy, sz, sz * 0.45f), new Color(1f, 0.95f, 0.7f, 0.9f));
            }
            // iarbă pastelată jos
            p.Shape((x, y) => y - (34f + Mathf.Sin(x * 0.08f) * 4f), new Color(0.7f, 0.9f, 0.7f));
            return p.ToSprite(Vector4.zero);
        });

        public static Sprite CheckIcon => Cached("icon_check", () =>
        {
            var p = new Painter(128);
            p.Shape((x, y) => P.Circle(x, y, 64f, 64f, 52f), new Color(0.5f, 0.84f, 0.6f), OL, Outline);
            p.Shape((x, y) => Mathf.Min(P.Capsule(x, y, 38f, 66f, 56f, 46f, 8f), P.Capsule(x, y, 56f, 46f, 92f, 84f, 8f)), Color.white);
            return p.ToSprite(Vector4.zero);
        });

        static Sprite DrawMug()
        {
            var p = new Painter(128);
            p.Shape((x, y) => P.Annulus(x, y, 94f, 60f, 16f, 6f), IPink, OL, Outline);
            p.Shape((x, y) => P.RoundRect(x, y, 58f, 58f, 34f, 38f, 14f), IPink, OL, Outline);
            p.Shape((x, y) => P.Ellipse(x, y, 58f, 94f, 30f, 6f), new Color(0.75f, 0.5f, 0.35f), 3f, Outline);
            p.Shape((x, y) => P.Heart(x, y, 58f, 54f, 16f), Color.white);
            p.Shape((x, y) => P.Ellipse(x, y, 36f, 76f, 5f, 10f), IWhite);
            return p.ToSprite(Vector4.zero);
        }

        static Sprite DrawKeys()
        {
            var p = new Painter(128);
            p.Shape((x, y) => P.Annulus(x, y, 40f, 82f, 20f, 6f), IGold, OL, Outline);
            p.Shape((x, y) => P.Rotated(x, y, 76f, 56f, -40f, (a, b) => P.RoundRect(a, b, 0f, 0f, 30f, 6f, 4f)), IGold, OL, Outline);
            p.Shape((x, y) => P.Rotated(x, y, 92f, 30f, -40f, (a, b) => P.RoundRect(a, b, 0f, 0f, 6f, 11f, 2f)), IGold, OL, Outline);
            p.Shape((x, y) => P.Rotated(x, y, 80f, 40f, -40f, (a, b) => P.RoundRect(a, b, 0f, 0f, 5f, 9f, 2f)), IGold, OL, Outline);
            // brelocul-lăbuță
            p.Shape((x, y) => P.Circle(x, y, 30f, 40f, 15f), IPink, OL, Outline);
            p.Shape((x, y) => P.Circle(x, y, 20f, 58f, 6f), IPink, 3.5f, Outline);
            p.Shape((x, y) => P.Circle(x, y, 32f, 62f, 6f), IPink, 3.5f, Outline);
            p.Shape((x, y) => P.Circle(x, y, 43f, 56f, 6f), IPink, 3.5f, Outline);
            return p.ToSprite(Vector4.zero);
        }

        static Sprite DrawDiary()
        {
            var p = new Painter(128);
            p.Shape((x, y) => P.RoundRect(x, y, 66f, 62f, 38f, 46f, 10f), ILav, OL, Outline);
            p.Shape((x, y) => P.RoundRect(x, y, 34f, 62f, 8f, 46f, 6f), new Color(0.62f, 0.55f, 0.9f), OL, Outline);
            p.Shape((x, y) => P.RoundRect(x, y, 104f, 62f, 4f, 40f, 2f), ICream, 3f, Outline);
            p.Shape((x, y) => P.Star(x, y, 70f, 64f, 22f, 10f), IGold, 4f, Outline);
            return p.ToSprite(Vector4.zero);
        }

        static Sprite DrawGlasses()
        {
            var p = new Painter(128);
            p.Shape((x, y) => P.Triangle(x, y, 14f, 78f, 24f, 104f, 40f, 82f), ITeal, OL, Outline);
            p.Shape((x, y) => P.Triangle(x, y, 114f, 78f, 104f, 104f, 88f, 82f), ITeal, OL, Outline);
            p.Shape((x, y) => P.Capsule(x, y, 50f, 62f, 78f, 62f, 4f), ITeal, OL, Outline);
            p.Shape((x, y) => P.Annulus(x, y, 34f, 58f, 22f, 6f), ITeal, OL, Outline);
            p.Shape((x, y) => P.Annulus(x, y, 94f, 58f, 22f, 6f), ITeal, OL, Outline);
            p.Shape((x, y) => P.Circle(x, y, 34f, 58f, 16f), new Color(0.85f, 0.95f, 1f, 0.7f));
            p.Shape((x, y) => P.Circle(x, y, 94f, 58f, 16f), new Color(0.85f, 0.95f, 1f, 0.7f));
            p.Shape((x, y) => P.Ellipse(x, y, 28f, 64f, 5f, 3f), IWhite);
            p.Shape((x, y) => P.Ellipse(x, y, 88f, 64f, 5f, 3f), IWhite);
            return p.ToSprite(Vector4.zero);
        }

        static Sprite DrawGlove()
        {
            var p = new Painter(128);
            for (int i = 0; i < 4; i++)
            {
                float cx = 40f + i * 15f;
                p.Shape((x, y) => P.Capsule(x, y, cx, 46f, cx, 70f, 8f), ICream, OL, Outline);
            }
            p.Shape((x, y) => P.Capsule(x, y, 92f, 34f, 104f, 50f, 8f), ICream, OL, Outline);
            p.Shape((x, y) => P.RoundRect(x, y, 62f, 30f, 30f, 18f, 12f), ICream, OL, Outline);
            // căciulița-ciupercă
            p.Shape((x, y) => Mathf.Max(P.Ellipse(x, y, 62f, 84f, 46f, 30f), 80f - y), IRed, OL, Outline);
            p.Shape((x, y) => P.Circle(x, y, 44f, 94f, 7f), Color.white);
            p.Shape((x, y) => P.Circle(x, y, 70f, 104f, 6f), Color.white);
            p.Shape((x, y) => P.Circle(x, y, 86f, 90f, 5f), Color.white);
            return p.ToSprite(Vector4.zero);
        }

        static Sprite DrawLantern()
        {
            var p = new Painter(128);
            p.Shape((x, y) => P.RoundRect(x, y, 64f, 16f, 30f, 6f, 4f), ILav, OL, Outline);
            p.Shape((x, y) => P.RoundRect(x, y, 64f, 46f, 24f, 26f, 10f), new Color(1f, 0.9f, 0.55f), OL, Outline);
            p.Shape((x, y) => P.RoundRect(x, y, 64f, 76f, 30f, 5f, 4f), ILav, OL, Outline);
            // semiluna
            p.Shape((x, y) => Mathf.Max(P.Circle(x, y, 64f, 102f, 18f), -P.Circle(x, y, 74f, 108f, 15f)), IGold, OL, Outline);
            p.Shape((x, y) => P.Ellipse(x, y, 52f, 52f, 5f, 10f), IWhite);
            return p.ToSprite(Vector4.zero);
        }

        // ================================================================ infrastructură

        static Sprite Cached(string key, System.Func<Sprite> make)
        {
            if (sprites.TryGetValue(key, out var s) && s != null) return s;
            s = make();
            sprites[key] = s;
            return s;
        }

        /// Pictor mic anti-aliased: forme SDF (distanță în pixeli, negativ înăuntru), compuse peste, cu contur opțional.
        sealed class Painter
        {
            readonly int w, h;
            readonly Color[] px;

            public Painter(int size) : this(size, size) { }

            public Painter(int width, int height)
            {
                w = width;
                h = height;
                px = new Color[w * h];
            }

            public void Shape(System.Func<float, float, float> sdf, Color fill, float outline = 0f, Color outlineColor = default)
            {
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < w; i++)
                    {
                        float d = sdf(i + 0.5f, j + 0.5f);
                        if (d > outline + 1f) continue;
                        int k = j * w + i;
                        if (outline > 0f) Over(k, outlineColor, Mathf.Clamp01(0.5f - (d - outline)));
                        Over(k, fill, Mathf.Clamp01(0.5f - d));
                    }
            }

            public void ShapeTex(System.Func<float, float, float> sdf, System.Func<float, float, Color> color)
            {
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < w; i++)
                    {
                        float d = sdf(i + 0.5f, j + 0.5f);
                        if (d > 1f) continue;
                        Over(j * w + i, color(i + 0.5f, j + 0.5f), Mathf.Clamp01(0.5f - d));
                    }
            }

            public void Modulate(System.Func<float, float, Color, Color> f)
            {
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < w; i++)
                    {
                        int k = j * w + i;
                        if (px[k].a <= 0f) continue;
                        px[k] = f(i + 0.5f, j + 0.5f, px[k]);
                    }
            }

            void Over(int k, Color c, float a)
            {
                a *= c.a;
                if (a <= 0f) return;
                var d = px[k];
                float outA = a + d.a * (1f - a);
                if (outA <= 1e-5f) return;
                px[k] = new Color(
                    (c.r * a + d.r * d.a * (1f - a)) / outA,
                    (c.g * a + d.g * d.a * (1f - a)) / outA,
                    (c.b * a + d.b * d.a * (1f - a)) / outA,
                    outA);
            }

            public Sprite ToSprite(Vector4 border)
            {
                var t = new Texture2D(w, h, TextureFormat.RGBA32, true);   // mipmap: iconițe/pergament clare de la distanță
                t.wrapMode = TextureWrapMode.Clamp;
                t.filterMode = FilterMode.Trilinear;
                t.anisoLevel = 2;
                t.SetPixels(px);
                t.Apply(true, true);
                return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            }
        }

        /// Primitive SDF (în pixeli).
        static class P
        {
            public static float Circle(float x, float y, float cx, float cy, float r)
            {
                float dx = x - cx, dy = y - cy;
                return Mathf.Sqrt(dx * dx + dy * dy) - r;
            }

            public static float Ellipse(float x, float y, float cx, float cy, float a, float b)
            {
                float dx = (x - cx) / a, dy = (y - cy) / b;
                return (Mathf.Sqrt(dx * dx + dy * dy) - 1f) * Mathf.Min(a, b);
            }

            public static float Annulus(float x, float y, float cx, float cy, float r, float halfThick)
            {
                return Mathf.Abs(Circle(x, y, cx, cy, r)) - halfThick;
            }

            public static float RoundRect(float x, float y, float cx, float cy, float hw, float hh, float r)
            {
                r = Mathf.Min(r, Mathf.Min(hw, hh));
                float qx = Mathf.Abs(x - cx) - (hw - r);
                float qy = Mathf.Abs(y - cy) - (hh - r);
                float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
            }

            public static float Capsule(float x, float y, float ax, float ay, float bx, float by, float r)
            {
                float px = x - ax, py = y - ay, vx = bx - ax, vy = by - ay;
                float t = Mathf.Clamp01((px * vx + py * vy) / Mathf.Max(1e-5f, vx * vx + vy * vy));
                float dx = px - vx * t, dy = py - vy * t;
                return Mathf.Sqrt(dx * dx + dy * dy) - r;
            }

            public static float Triangle(float x, float y, float ax, float ay, float bx, float by, float cx, float cy)
            {
                float d = Mathf.Min(Seg(x, y, ax, ay, bx, by), Mathf.Min(Seg(x, y, bx, by, cx, cy), Seg(x, y, cx, cy, ax, ay)));
                bool inside = SameSide(x, y, ax, ay, bx, by, cx, cy) && SameSide(x, y, bx, by, cx, cy, ax, ay) && SameSide(x, y, cx, cy, ax, ay, bx, by);
                return inside ? -d : d;
            }

            static float Seg(float x, float y, float ax, float ay, float bx, float by) => Capsule(x, y, ax, ay, bx, by, 0f);

            static bool SameSide(float x, float y, float ax, float ay, float bx, float by, float cx, float cy)
            {
                float c1 = (bx - ax) * (y - ay) - (by - ay) * (x - ax);
                float c2 = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                return c1 * c2 >= 0f;
            }

            /// Stea cu n vârfuri (aproximare polară, suficientă pentru anti-aliasing).
            public static float Star(float x, float y, float cx, float cy, float outer, float inner, int n = 5)
            {
                float dx = x - cx, dy = y - cy;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx) - Mathf.PI * 0.5f;
                float seg = Mathf.PI * 2f / n;
                float a = Mathf.Repeat(ang, seg) / seg;          // 0..1 într-un segment
                float k = Mathf.Abs(a - 0.5f) * 2f;              // 1 la vârf, 0 între vârfuri
                float edge = Mathf.Lerp(inner, outer, k * k * 0.35f + k * 0.65f);
                return (r - edge) * 0.8f;
            }

            /// Inimioară = două cercuri + un pătrat rotit (SDF aproape exact).
            public static float Heart(float x, float y, float cx, float cy, float size)
            {
                float r = size * 0.5f;
                float c1 = Circle(x, y, cx - r * 0.72f, cy + r * 0.35f, r);
                float c2 = Circle(x, y, cx + r * 0.72f, cy + r * 0.35f, r);
                float sq = Rotated(x, y, cx, cy - r * 0.35f, 45f, (a, b) => RoundRect(a, b, 0f, 0f, r * 1.02f, r * 1.02f, r * 0.15f));
                return Mathf.Min(Mathf.Min(c1, c2), sq);
            }

            public static float Rotated(float x, float y, float cx, float cy, float deg, System.Func<float, float, float> local)
            {
                float rad = -deg * Mathf.Deg2Rad;
                float dx = x - cx, dy = y - cy;
                float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
                return local(dx * c - dy * s, dx * s + dy * c);
            }
        }
    }
}
