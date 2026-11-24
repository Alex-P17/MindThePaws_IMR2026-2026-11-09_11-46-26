using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace MindThePaws
{
    /// <summary>
    /// Fabrică de UI cozy pe Canvas World Space: sprite-uri 9-slice cu colțuri rotunjite generate procedural,
    /// panouri cu bordură fină și umbră moale, text lizibil (UI.Text, rasterizat mare și scalat mic → nu e pixelat).
    /// 1 unitate de canvas = PX metri.
    /// </summary>
    public static class CozyUI
    {
        public const float PX = 0.0005f;   // 2 unități de canvas pe milimetru

        // ------------------------------------------------------------ paletă
        public static readonly Color Cream = new Color(1f, 0.975f, 0.93f, 0.95f);
        public static readonly Color Lavender = new Color(0.94f, 0.91f, 1f, 0.95f);
        public static readonly Color Gold = new Color(0.98f, 0.79f, 0.4f, 1f);
        public static readonly Color GoldSoft = new Color(1f, 0.88f, 0.6f, 1f);
        public static readonly Color Ink = new Color(0.27f, 0.2f, 0.36f, 1f);
        public static readonly Color InkSoft = new Color(0.47f, 0.4f, 0.57f, 1f);
        public static readonly Color Mint = new Color(0.45f, 0.78f, 0.58f, 1f);

        public static Color MoodFill(CozyMood m)
        {
            switch (m)
            {
                case CozyMood.Chaos: return new Color(1f, 0.86f, 0.86f, 0.94f);
                case CozyMood.Blanket:
                case CozyMood.Garden: return new Color(1f, 0.94f, 0.78f, 0.94f);
                default: return new Color(0.86f, 0.93f, 1f, 0.94f);
            }
        }

        public static Color MoodBorder(CozyMood m)
        {
            switch (m)
            {
                case CozyMood.Chaos: return new Color(1f, 0.58f, 0.64f, 1f);
                case CozyMood.Blanket:
                case CozyMood.Garden: return Gold;
                default: return new Color(0.56f, 0.74f, 0.98f, 1f);
            }
        }

        // ------------------------------------------------------------ resurse procedurale
        static Font font;
        static Sprite rounded, shadow, check, circle, ring;

        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        /// Dreptunghi rotunjit alb, 9-slice (colțuri de 40 px).
        public static Sprite Rounded
        {
            get
            {
                if (rounded == null) rounded = MakeSprite(96, 44, (x, y, s) => Mathf.Clamp01(0.5f - SdRoundRect(x, y, s, s, 40f)));
                return rounded;
            }
        }

        /// Umbră moale (marginea se estompează pe ~22 px), 9-slice.
        public static Sprite Shadow
        {
            get
            {
                if (shadow == null) shadow = MakeSprite(128, 60, (x, y, s) =>
                {
                    float d = SdRoundRect(x, y, s - 44f, s - 44f, 30f);
                    float a = Mathf.Clamp01(1f - (d + 2f) / 22f);
                    return a * a;
                });
                return shadow;
            }
        }

        public static Sprite Circle
        {
            get
            {
                if (circle == null) circle = MakeSprite(64, 0, (x, y, s) =>
                {
                    float d = Mathf.Sqrt((x - s * 0.5f) * (x - s * 0.5f) + (y - s * 0.5f) * (y - s * 0.5f)) - (s * 0.5f - 1.5f);
                    return Mathf.Clamp01(0.5f - d);
                });
                return circle;
            }
        }

        public static Sprite Ring
        {
            get
            {
                if (ring == null) ring = MakeSprite(64, 0, (x, y, s) =>
                {
                    float r = Mathf.Sqrt((x - s * 0.5f) * (x - s * 0.5f) + (y - s * 0.5f) * (y - s * 0.5f));
                    float d = Mathf.Abs(r - (s * 0.5f - 6f)) - 4f;
                    return Mathf.Clamp01(0.5f - d);
                });
                return ring;
            }
        }

        /// Bifă (✓) desenată procedural — fontul nu are mereu glifa.
        public static Sprite Check
        {
            get
            {
                if (check == null) check = MakeSprite(64, 0, (x, y, s) =>
                {
                    var p = new Vector2(x, y);
                    float d = Mathf.Min(SegDist(p, new Vector2(14, 33), new Vector2(27, 19)), SegDist(p, new Vector2(27, 19), new Vector2(51, 46)));
                    return Mathf.Clamp01(0.5f - (d - 5f));
                });
                return check;
            }
        }

        static float SdRoundRect(float x, float y, float w, float h, float r)
        {
            float cx = Mathf.Abs(x - w * 0.5f) - (w * 0.5f - r);
            float cy = Mathf.Abs(y - h * 0.5f) - (h * 0.5f - r);
            float ox = Mathf.Max(cx, 0f), oy = Mathf.Max(cy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(cx, cy), 0f) - r;
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        static Sprite MakeSprite(int size, int border, System.Func<float, float, float, float> alpha)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            var px = new Color32[size * size];
            float off = border > 0 && size == 128 ? 22f : 0f;   // umbra are marginea transparentă
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    float a = alpha(i + 0.5f - off, j + 0.5f - off, size);
                    px[j * size + i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
        }

        // ------------------------------------------------------------ construcție

        public static Canvas MakeCanvas(string name, Transform parent, Vector2 sizePx, int sortingOrder = 0)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            c.sortingOrder = sortingOrder;
            var sc = go.AddComponent<CanvasScaler>();
            sc.dynamicPixelsPerUnit = 2f;
            sc.referencePixelsPerUnit = 100f;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizePx;
            rt.localScale = Vector3.one * PX;
            return c;
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        public static Image Img(string name, Transform parent, Sprite sprite, Color color, bool sliced)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            if (sliced)
            {
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f;
            }
            return img;
        }

        /// Panou: umbră moale + bordură fină + umplutură. Straturile ignoră layout-ul și umplu rădăcina.
        public sealed class Panel
        {
            public RectTransform root;
            public Image shadow, border, fill;

            public Image bow;

            public void SetColors(Color fillColor, Color borderColor)
            {
                if (fill != null) fill.color = fillColor;
                if (border != null) border.color = borderColor;
            }
        }

        public static Panel MakePanel(RectTransform root, Color fill, Color border, float borderPx = 3f, float cornerScale = 1f, bool withShadow = true)
        {
            var p = new Panel { root = root };
            if (withShadow)
            {
                p.shadow = Img("Shadow", root, Shadow, new Color(0.18f, 0.12f, 0.25f, 0.28f), true);
                Stretch(p.shadow.rectTransform, -26f);
                p.shadow.rectTransform.anchoredPosition = new Vector2(0f, -8f);
                p.shadow.pixelsPerUnitMultiplier = 1f / cornerScale;
                Ignore(p.shadow.gameObject);
            }
            p.border = Img("Border", root, Rounded, border, true);
            Stretch(p.border.rectTransform, 0f);
            p.border.pixelsPerUnitMultiplier = 1f / cornerScale;
            Ignore(p.border.gameObject);
            p.fill = Img("Fill", root, Rounded, fill, true);
            Stretch(p.fill.rectTransform, borderPx);
            p.fill.pixelsPerUnitMultiplier = 1f / cornerScale;
            Ignore(p.fill.gameObject);
            return p;
        }

        /// Card kawaii: umbră moale + pergament crem cu bordură dublă aur/lemn + (opțional) fundiță roz-aurie sus.
        /// fill = pergamentul (se nuanțează), border = null.
        public static Panel MakeCard(RectTransform root, Color tint, bool bow, float cornerScale = 1f)
        {
            var p = new Panel { root = root };
            p.shadow = Img("Shadow", root, Shadow, new Color(0.3f, 0.18f, 0.25f, 0.3f), true);
            Stretch(p.shadow.rectTransform, -26f);
            p.shadow.rectTransform.anchoredPosition = new Vector2(0f, -9f);
            p.shadow.pixelsPerUnitMultiplier = 1f / cornerScale;
            Ignore(p.shadow.gameObject);
            p.fill = Img("Parchment", root, CozyThemeArt.Parchment, tint, true);
            Stretch(p.fill.rectTransform, 0f);
            p.fill.pixelsPerUnitMultiplier = 1f / cornerScale;
            Ignore(p.fill.gameObject);
            if (bow)
            {
                p.bow = Img("Bow", root, CozyThemeArt.Bow, Color.white, false);
                var rt = p.bow.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, 4f);
                rt.sizeDelta = new Vector2(128f, 72f) * Mathf.Clamp(cornerScale, 0.6f, 1.2f);
                Ignore(p.bow.gameObject);
            }
            return p;
        }

        /// Buton-pastilă (turcoaz / roz) cu umbră subtilă dedesubt. fill = pastila, shadow = umbra.
        public static Panel MakePill(RectTransform root, Color color, float cornerScale = 1f)
        {
            var p = new Panel { root = root };
            p.shadow = Img("PillShadow", root, CozyThemeArt.Pill, new Color(0.35f, 0.22f, 0.3f, 0.28f), true);
            Stretch(p.shadow.rectTransform, 0f);
            p.shadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            p.shadow.pixelsPerUnitMultiplier = 1f / cornerScale;
            Ignore(p.shadow.gameObject);
            p.fill = Img("Pill", root, CozyThemeArt.Pill, color, true);
            Stretch(p.fill.rectTransform, 0f);
            p.fill.pixelsPerUnitMultiplier = 1f / cornerScale;
            Ignore(p.fill.gameObject);
            return p;
        }

        public static Image Icon(string name, Transform parent, Sprite sprite, float sizePx)
        {
            var img = Img(name, parent, sprite, Color.white, false);
            img.preserveAspect = true;
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = sizePx;
            le.preferredHeight = sizePx;
            le.minWidth = sizePx;
            le.minHeight = sizePx;
            img.rectTransform.sizeDelta = new Vector2(sizePx, sizePx);
            return img;
        }

        static void Ignore(GameObject go)
        {
            go.AddComponent<LayoutElement>().ignoreLayout = true;
        }

        public static Text Txt(string name, Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.lineSpacing = 1.05f;
            t.text = FixSize(text, size);
            return t;
        }

        static readonly Regex SizeTag = new Regex(@"<size=(\d+)>");

        /// Tag-urile vechi &lt;size=40&gt; (scrise pentru TextMesh cu fontSize 64) devin relative la fontul curent.
        public static string FixSize(string s, int baseSize)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf("<size=", System.StringComparison.Ordinal) < 0) return s;
            return SizeTag.Replace(s, m =>
            {
                int v = int.Parse(m.Groups[1].Value);
                return "<size=" + Mathf.Max(8, Mathf.RoundToInt(baseSize * v / 64f)) + ">";
            });
        }

        /// Etichetă world-space de sine stătătoare (cu sau fără panou), se dimensionează după text.
        public static CozyLabel Label(string name, string text, int fontSize, Color textColor, Color? panelFill = null, Color? panelBorder = null, float maxWidthPx = 0f)
        {
            var canvas = MakeCanvas(name, null, new Vector2(400, 100), 5);
            var root = (RectTransform)canvas.transform;
            var lbl = canvas.gameObject.AddComponent<CozyLabel>();
            var layout = canvas.gameObject.AddComponent<VerticalLayoutGroup>();
            int pad = panelFill.HasValue ? Mathf.RoundToInt(fontSize * 0.55f) : 4;
            layout.padding = new RectOffset(pad + 6, pad + 6, pad, pad);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fit = canvas.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = maxWidthPx > 0f ? ContentSizeFitter.FitMode.Unconstrained : ContentSizeFitter.FitMode.PreferredSize;
            if (maxWidthPx > 0f) root.sizeDelta = new Vector2(maxWidthPx, 100f);
            if (panelFill.HasValue) lbl.panel = MakePanel(root, panelFill.Value, panelBorder ?? Gold, 3f, Mathf.Clamp(fontSize / 40f, 0.5f, 1f));
            var t = Txt("Text", root, text, fontSize, textColor);
            if (maxWidthPx <= 0f) t.horizontalOverflow = HorizontalWrapMode.Overflow;
            lbl.uiText = t;
            lbl.text = text;
            return lbl;
        }

        // ------------------------------------------------------------ hitbox-uri fizice pentru butoane

        static int lastSyncFrame = -1;

        /// BoxCollider (trigger, layer „Ignore Raycast”) dimensionat după RectTransform. Se testează direct cu
        /// Collider.Raycast, deci nu depinde de EventSystem / InputSystemUIInputModule și nu încurcă raycast-urile pe suprafețe.
        public static BoxCollider AttachHitbox(RectTransform rt, float marginPx = 10f)
        {
            if (rt == null) return null;
            var go = new GameObject("Hitbox");
            go.layer = 2;
            go.transform.SetParent(rt, false);
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            go.AddComponent<HitboxMargin>().margin = marginPx;
            SyncHitbox(rt, bc);
            return bc;
        }

        public static void SyncHitbox(RectTransform rt, BoxCollider bc)
        {
            if (rt == null || bc == null) return;
            var r = rt.rect;
            var hm = bc.GetComponent<HitboxMargin>();
            float m = hm != null ? hm.margin : 0f;
            bc.center = new Vector3(r.center.x, r.center.y, 0f);
            bc.size = new Vector3(Mathf.Max(2f, r.width + 2f * m), Mathf.Max(2f, r.height + 2f * m), 40f);
        }

        /// Lovire buton: întâi hitbox-ul fizic, apoi (rezervă) intersecția plan-dreptunghi.
        public static bool RayHitsUI(RectTransform rt, BoxCollider bc, Ray ray, out Vector3 point, float marginPx = 0f)
        {
            point = default;
            if (rt == null || !rt.gameObject.activeInHierarchy) return false;
            if (bc != null && bc.enabled && bc.gameObject.activeInHierarchy)
            {
                SyncHitbox(rt, bc);
                if (lastSyncFrame != Time.frameCount)
                {
                    lastSyncFrame = Time.frameCount;
                    Physics.SyncTransforms();
                }
                if (bc.Raycast(ray, out RaycastHit hit, 30f))
                {
                    point = hit.point;
                    return true;
                }
            }
            return RayHits(rt, ray, out point, marginPx);
        }

        /// UnityEngine.UI.Button suplimentar (merge doar dacă scena are EventSystem; altfel e inofensiv).
        public static Button AddUnityButton(GameObject go, Graphic target, UnityEngine.Events.UnityAction onClick)
        {
            if (go == null) return null;
            if (target != null) target.raycastTarget = true;
            var b = go.AddComponent<Button>();
            b.targetGraphic = target;
            b.transition = Selectable.Transition.None;
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        /// Intersecția razei cu un RectTransform plat (pentru butoane țintite cu mâna / mouse-ul).
        public static bool RayHits(RectTransform rt, Ray ray, out Vector3 point, float marginPx = 0f)
        {
            point = default;
            if (rt == null || !rt.gameObject.activeInHierarchy) return false;
            var plane = new Plane(rt.forward, rt.position);
            if (!plane.Raycast(ray, out float d) || d < 0.02f) return false;
            point = ray.GetPoint(d);
            var local = rt.InverseTransformPoint(point);
            var r = rt.rect;
            r.xMin -= marginPx; r.yMin -= marginPx; r.xMax += marginPx; r.yMax += marginPx;
            return r.Contains(new Vector2(local.x, local.y));
        }
    }
}
