using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MindThePaws
{
    public enum SlotState { Next, Active, Done }

    /// <summary>
    /// UI-ul cozy al jocului, construit în cod (fără Inspector), în stil kawaii (pergament, lemn cald, pastile):
    ///  - HudBadge: ecuson FIX în colțul stânga-sus, pe un Canvas Screen Space - Camera copil al camerei (fără urmărire);
    ///  - TreasureInventoryUI: tăviță de lemn jos cu 3 sloturi (iconiță, nume, stare), slotul curent cu ramă aurie;
    ///  - LumiDialogCard: cardul de dialog care stă lângă zâna Lumi;
    ///  - TransitionOverlay: carduri de tranziție între etape cu buton „Continuă” (pinch / click);
    ///  - CalibrationWidget: inimioara de puls + bara de calibrare.
    /// </summary>
    public class CozyUIManager : MonoBehaviour
    {
        public Transform head;
        public Transform lumi;
        public float hudDistance = 1.0f;
        [Tooltip("Distanța planului pentru ecusonul Screen Space - Camera.")]
        public float badgePlaneDistance = 0.8f;

        public HudBadge Badge { get; private set; }
        public TreasureInventoryUI Inventory { get; private set; }
        public LumiDialogCard Dialog { get; private set; }
        public TransitionOverlay Overlay { get; private set; }
        public CalibrationWidget Calibration { get; private set; }
        public PolaroidCard Polaroid { get; private set; }

        Transform hudRoot;
        Canvas hudCanvas, badgeCanvas, polaroidCanvas;
        Text debugText;
        bool hiddenForSafety;
        public bool DebugVisible { get; set; }

        static readonly Vector2 HudSize = new Vector2(1500f, 980f);

        void Awake()
        {
            hudRoot = new GameObject("CozyHUD").transform;
            hudCanvas = CozyUI.MakeCanvas("HudCanvas", hudRoot, HudSize, 20);
            var hrt = (RectTransform)hudCanvas.transform;

            // ecusonul: Screen Space - Camera (fără cameră încă = overlay în Editor; camera se leagă când o găsim)
            var bgo = new GameObject("BadgeCanvas", typeof(RectTransform));
            badgeCanvas = bgo.AddComponent<Canvas>();
            badgeCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            badgeCanvas.planeDistance = badgePlaneDistance;
            badgeCanvas.sortingOrder = 50;
            var scaler = bgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;
            Badge = new HudBadge((RectTransform)bgo.transform);

            Inventory = new TreasureInventoryUI(hrt);
            Dialog = new LumiDialogCard();
            Overlay = new TransitionOverlay();
            Calibration = new CalibrationWidget();
            // Polaroidul: tot Screen Space - Camera, fix în câmpul vizual (nu mai e un obiect 3D în grădină)
            var pgo = new GameObject("PolaroidCanvas", typeof(RectTransform));
            polaroidCanvas = pgo.AddComponent<Canvas>();
            polaroidCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            polaroidCanvas.planeDistance = 0.75f;
            polaroidCanvas.sortingOrder = 60;
            var pscaler = pgo.AddComponent<CanvasScaler>();
            pscaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            pscaler.referenceResolution = new Vector2(1600f, 900f);
            pscaler.matchWidthOrHeight = 0.5f;
            pscaler.referencePixelsPerUnit = 100f;
            pgo.AddComponent<GraphicRaycaster>();
            Polaroid = new PolaroidCard((RectTransform)pgo.transform);
            pgo.SetActive(false);
            hudRoot.gameObject.SetActive(false);   // nimic nu plutește în origine până nu găsim camera
            bgo.SetActive(false);

            var drt = CozyUI.Rect("Debug", hrt, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 20f), new Vector2(560f, 200f));
            debugText = CozyUI.Txt("DebugText", drt, "", 17, new Color(1f, 1f, 0.85f, 0.75f), TextAnchor.LowerRight);
            CozyUI.Stretch(debugText.rectTransform, 0f);
            debugText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.5f);
            SetMood(CozyMood.Calm);
        }

        public void SetMood(CozyMood m)
        {
            Badge.SetMood(m);
        }

        public void SetDebug(string text)
        {
            debugText.gameObject.SetActive(DebugVisible && !string.IsNullOrEmpty(text));
            if (DebugVisible) debugText.text = text;
        }

        /// Pătura Moale: rămâne doar ecusonul, restul dispare.
        public void HideForSafety(bool hide)
        {
            hiddenForSafety = hide;
            Inventory.SetSuspended(hide);
            Dialog.SetSuspended(hide);
            Overlay.SetSuspended(hide);
        }

        public void Say(string text)
        {
            Dialog.Say(text);
        }

        void LateUpdate()
        {
            if (head == null) return;
            if (hudRoot.parent != head)
            {
                hudRoot.SetParent(head, false);
                hudRoot.localPosition = new Vector3(0f, 0f, hudDistance);
                hudRoot.localRotation = Quaternion.identity;
                hudRoot.gameObject.SetActive(true);
                var cam = head.GetComponent<Camera>();
                if (cam == null) cam = Camera.main;
                Dialog.SetCamera(cam);
                Overlay.SetCamera(cam);
                // ecusonul devine copil direct al camerei, fix în colțul stânga-sus al vederii
                badgeCanvas.transform.SetParent(head, false);
                badgeCanvas.worldCamera = cam;
                badgeCanvas.planeDistance = badgePlaneDistance;
                badgeCanvas.gameObject.SetActive(true);
                polaroidCanvas.transform.SetParent(head, false);
                polaroidCanvas.worldCamera = cam;
                polaroidCanvas.planeDistance = 0.75f;
                polaroidCanvas.gameObject.SetActive(true);
            }
            float dt = Time.unscaledDeltaTime;
            Badge.Place((RectTransform)badgeCanvas.transform, UnityEngine.XR.XRSettings.isDeviceActive);
            Badge.Tick(dt);
            Inventory.Tick(dt);
            Dialog.Tick(head, lumi, dt, Overlay.Active || hiddenForSafety);
            Overlay.Tick(head, dt);
            Calibration.Tick(head, dt);
            Polaroid.Tick(dt);
        }

        void OnDestroy()
        {
            if (hudRoot != null) Destroy(hudRoot.gameObject);
            if (badgeCanvas != null) Destroy(badgeCanvas.gameObject);
            Dialog.Destroy();
            Overlay.Destroy();
            Calibration.Destroy();
            if (polaroidCanvas != null) Destroy(polaroidCanvas.gameObject);
        }
    }

    // ================================================================ ecuson stânga-sus

    public sealed class HudBadge
    {
        readonly RectTransform root;
        readonly CozyUI.Panel panel;
        readonly Text title, sub, timer;
        readonly RectTransform chip;
        readonly CozyUI.Panel chipPanel;
        float timeLeft = -1f;
        float pulse;

        public HudBadge(RectTransform canvas)
        {
            // ancorat fix în colțul stânga-sus: anchorMin = anchorMax = pivot = (0, 1)
            root = CozyUI.Rect("HudBadge", canvas, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(400f, 120f));
            panel = CozyUI.MakeCard(root, Color.white, false, 0.7f);
            var h = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(26, 28, 18, 20);
            h.spacing = 16f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            var fit = root.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CozyUI.Icon("Paw", root, CozyThemeArt.PawIcon, 46f);

            var col = CozyUI.Rect("Column", root, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(300f, 80f));
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = false;
            v.childForceExpandHeight = false;
            v.spacing = 2f;
            title = CozyUI.Txt("Title", col, "Mind the Paws", 30, CozyUI.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            sub = CozyUI.Txt("Sub", col, "", 21, CozyUI.InkSoft, TextAnchor.MiddleLeft);
            sub.horizontalOverflow = HorizontalWrapMode.Overflow;

            chip = CozyUI.Rect("TimerChip", root, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(120f, 56f));
            var cle = chip.gameObject.AddComponent<LayoutElement>();
            cle.preferredWidth = 118f;
            cle.preferredHeight = 54f;
            chipPanel = CozyUI.MakePill(chip, CozyThemeArt.Turquoise, 0.8f);
            timer = CozyUI.Txt("Timer", chip, "", 28, CozyUI.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            CozyUI.Stretch(timer.rectTransform, 0f);
            chip.gameObject.SetActive(false);
        }

        /// Margine mică în Editor; în cască, colțurile ecranului sunt în afara lentilelor, așa că păstrăm
        /// ecusonul în zona sigură (tot sus-stânga, dar unde se poate citi).
        public void Place(RectTransform canvas, bool xr)
        {
            var size = canvas.rect.size;
            var pos = xr ? new Vector2(size.x * 0.2f, -size.y * 0.22f) : new Vector2(24f, -24f);
            if ((root.anchoredPosition - pos).sqrMagnitude > 0.25f) root.anchoredPosition = pos;
        }

        public void SetMood(CozyMood m)
        {
            // pergament nuanțat după stare: bleu (Calm), roz-coral (Chaos), auriu cald (Pătura / Grădina)
            panel.SetColors(Color.Lerp(Color.white, CozyUI.MoodFill(m), 0.75f), Color.clear);
        }

        /// timerSeconds &lt; 0 = fără cronometru.
        public void Set(string stage, string subtitle, float timerSeconds = -1f)
        {
            if (title.text != stage) title.text = stage;
            if (sub.text != subtitle) sub.text = subtitle ?? "";
            sub.gameObject.SetActive(!string.IsNullOrEmpty(subtitle));
            timeLeft = timerSeconds;
            bool on = timerSeconds >= 0f;
            if (chip.gameObject.activeSelf != on) chip.gameObject.SetActive(on);
            if (on)
            {
                int s = Mathf.CeilToInt(timerSeconds);
                string t = s >= 60 ? (s / 60) + ":" + (s % 60).ToString("00") : "0:" + s.ToString("00");
                if (timer.text != t) timer.text = t;
            }
        }

        public void Tick(float dt)
        {
            if (timeLeft < 0f) return;
            // în ultimele 10 secunde, pastila pulsează ușor în roz
            if (timeLeft <= 10f)
            {
                pulse += dt * 4f;
                float k = 0.5f + 0.5f * Mathf.Sin(pulse);
                chipPanel.SetColors(Color.Lerp(CozyThemeArt.Turquoise, CozyThemeArt.PinkPill, k), Color.clear);
            }
            else chipPanel.SetColors(CozyThemeArt.Turquoise, Color.clear);
        }
    }

    // ================================================================ inventar jos

    public sealed class TreasureInventoryUI
    {
        readonly RectTransform root;
        readonly Text header;
        readonly Slot[] slots = new Slot[3];
        string[] ids = new string[0];
        bool visible, suspended;
        float t;

        sealed class Slot
        {
            public RectTransform rt;
            public Image inset, glow, check, icon;
            public Text name, state;
            public SlotState s;
        }

        public TreasureInventoryUI(RectTransform hud)
        {
            root = CozyUI.Rect("TreasureInventory", hud, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(840f, 290f));
            var shadow = CozyUI.Img("Shadow", root, CozyUI.Shadow, new Color(0.3f, 0.18f, 0.2f, 0.32f), true);
            CozyUI.Stretch(shadow.rectTransform, -26f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -9f);
            var frame = CozyUI.Img("WoodFrame", root, CozyThemeArt.WoodPanel, Color.white, true);
            CozyUI.Stretch(frame.rectTransform, 0f);

            // titlul pe o etichetă de pergament
            var tab = CozyUI.Rect("HeaderTab", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -2f), new Vector2(420f, 52f));
            CozyUI.MakeCard(tab, Color.white, false, 0.45f);
            header = CozyUI.Txt("Header", tab, "Comorile tale", 23, CozyUI.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            CozyUI.Stretch(header.rectTransform, 0f);

            for (int i = 0; i < 3; i++)
            {
                var s = new Slot();
                s.rt = CozyUI.Rect("Slot" + i, root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2((i - 1) * 262f, 26f), new Vector2(240f, 212f));
                s.glow = CozyUI.Img("Glow", s.rt, CozyUI.Shadow, CozyUI.Gold, true);
                CozyUI.Stretch(s.glow.rectTransform, -30f);
                s.inset = CozyUI.Img("Inset", s.rt, CozyThemeArt.SlotInset, Color.white, true);
                CozyUI.Stretch(s.inset.rectTransform, 0f);
                s.icon = CozyUI.Img("Icon", s.rt, CozyThemeArt.TreasureIcon("mug"), Color.white, false);
                s.icon.preserveAspect = true;
                var irt = s.icon.rectTransform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
                irt.pivot = new Vector2(0.5f, 1f);
                irt.anchoredPosition = new Vector2(0f, -10f);
                irt.sizeDelta = new Vector2(112f, 112f);
                s.name = CozyUI.Txt("Name", s.rt, "", 26, CozyUI.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                Place(s.name.rectTransform, new Vector2(0f, 62f), new Vector2(224f, 34f));
                s.state = CozyUI.Txt("State", s.rt, "", 21, CozyUI.InkSoft, TextAnchor.MiddleCenter);
                Place(s.state.rectTransform, new Vector2(0f, 28f), new Vector2(224f, 30f));
                s.check = CozyUI.Img("Check", s.rt, CozyThemeArt.CheckIcon, Color.white, false);
                s.check.rectTransform.anchorMin = s.check.rectTransform.anchorMax = new Vector2(1f, 1f);
                s.check.rectTransform.pivot = new Vector2(1f, 1f);
                s.check.rectTransform.anchoredPosition = new Vector2(-8f, -8f);
                s.check.rectTransform.sizeDelta = new Vector2(54f, 54f);
                slots[i] = s;
            }
            root.gameObject.SetActive(false);
        }

        static void Place(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        public void SetSuspended(bool s)
        {
            suspended = s;
            Apply();
        }

        void Apply()
        {
            root.gameObject.SetActive(visible && !suspended);
        }

        /// words: textul pentru Active / Next / Done (ex. „Activ”, „Urmează”, „Ascuns”).
        public void Show(string headerText, string[] typeIds, SlotState[] states, string activeWord, string nextWord, string doneWord)
        {
            if (typeIds == null || states == null) return;
            header.text = headerText;
            ids = typeIds;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                bool has = i < ids.Length && i < states.Length;
                s.rt.gameObject.SetActive(has);
                if (!has) continue;
                s.s = states[i];
                s.name.text = Graybox.TreasureLabel(ids[i]);
                s.icon.sprite = CozyThemeArt.TreasureIcon(ids[i]);
                switch (s.s)
                {
                    case SlotState.Active:
                        s.state.text = activeWord;
                        s.state.color = new Color(0.75f, 0.5f, 0.12f);
                        s.inset.color = Color.white;
                        s.icon.color = Color.white;
                        break;
                    case SlotState.Done:
                        s.state.text = doneWord;
                        s.state.color = new Color(0.28f, 0.58f, 0.4f);
                        s.inset.color = new Color(0.9f, 1f, 0.92f, 1f);
                        s.icon.color = new Color(1f, 1f, 1f, 0.85f);
                        break;
                    default:
                        s.state.text = nextWord;
                        s.state.color = CozyUI.InkSoft;
                        s.inset.color = new Color(1f, 1f, 1f, 0.8f);
                        s.icon.color = new Color(1f, 1f, 1f, 0.7f);
                        break;
                }
                s.check.gameObject.SetActive(s.s == SlotState.Done);
                s.glow.gameObject.SetActive(s.s == SlotState.Active);
            }
            visible = true;
            Apply();
        }

        public void Hide()
        {
            visible = false;
            Apply();
        }

        public void Tick(float dt)
        {
            if (!visible || suspended) return;
            t += dt;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s.glow.gameObject.activeSelf)
                {
                    var c = CozyUI.Gold;
                    c.a = 0.55f + 0.35f * Mathf.Sin(t * 3.2f);
                    s.glow.color = c;
                }
                // iconița activă „țopăie” puțin
                float bob = s.s == SlotState.Active ? Mathf.Abs(Mathf.Sin(t * 3f)) * 6f : 0f;
                s.icon.rectTransform.anchoredPosition = new Vector2(0f, -10f + bob);
            }
        }
    }

    // ================================================================ dialogul lui Lumi

    /// <summary>
    /// Cardul de dialog al lui Lumi (alb-crem, bordură aurie). Stă deasupra zânei:
    /// în joc, Lumi plutește jos-stânga în câmpul vizual; în Grădina Amintirilor (Anchored) cardul stă
    /// deasupra lui Lumi, în stânga grădinii, orientat spre jucător. Poate avea până la 3 butoane (pinch / click).
    /// </summary>
    public sealed class LumiDialogCard
    {
        readonly Canvas canvas;
        readonly RectTransform root, buttonsRoot;
        readonly CanvasGroup group;
        readonly Text nameText, body;
        readonly List<DialogButton> buttons = new List<DialogButton>();
        bool suspended;
        float alpha, pop, buttonsShownAt;
        string current = "";
        BoxCollider rootHitbox;
        Image nameIcon;
        Sprite buttonIcon;

        /// Iconița din dreptul numelui (implicit lăbuța roz).
        public void SetIcon(Sprite icon)
        {
            if (nameIcon != null) nameIcon.sprite = icon != null ? icon : CozyThemeArt.PawIcon;
        }

        public void SetCamera(Camera cam)
        {
            if (cam != null) canvas.worldCamera = cam;
        }

        sealed class DialogButton
        {
            public RectTransform rt;
            public BoxCollider hitbox;
            public CozyUI.Panel panel;
            public Text label;
            public bool primary;
            public float hover;
        }

        /// Grădina Amintirilor: cardul e ancorat deasupra lui Lumi (care stă fix în stânga grădinii), puțin mai mare.
        public bool Anchored;
        public float anchoredScale = 1.3f;
        public float anchoredLift = 0.2f;
        /// În grădină: punctul din lume unde stă MARGINEA DREAPTĂ-JOS a cardului (cardul crește spre stânga, nu spre centru).
        public Vector3? AnchorPoint;

        public int Chosen { get; private set; } = -1;
        public bool Hovering { get; private set; }
        public Vector3 HoverPoint { get; private set; }
        public bool Visible => alpha > 0.9f && canvas.gameObject.activeSelf;
        public bool HasButtons => buttons.Count > 0 && Visible;
        public Vector3 Position => root.position;

        public LumiDialogCard()
        {
            canvas = CozyUI.MakeCanvas("LumiDialog", null, new Vector2(600f, 160f), 25);
            root = (RectTransform)canvas.transform;
            root.pivot = new Vector2(0.5f, 0f);
            group = canvas.gameObject.AddComponent<CanvasGroup>();
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            CozyUI.MakeCard(root, Color.white, true, 0.8f);
            rootHitbox = CozyUI.AttachHitbox(root, 0f);
            var v = canvas.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(34, 34, 34, 28);
            v.spacing = 8f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            var fit = canvas.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            // rândul cu iconița (lăbuță roz / ceainic / inimioară / steluță) + numele
            var nameRow = CozyUI.Rect("NameRow", root, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(500f, 40f));
            var hr = nameRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hr.spacing = 10f;
            hr.childAlignment = TextAnchor.MiddleLeft;
            hr.childControlWidth = true;
            hr.childControlHeight = true;
            hr.childForceExpandWidth = false;
            hr.childForceExpandHeight = false;
            nameIcon = CozyUI.Icon("Icon", nameRow, CozyThemeArt.PawIcon, 40f);
            nameText = CozyUI.Txt("Name", nameRow, "Lumi", 24, new Color(0.72f, 0.48f, 0.16f), TextAnchor.MiddleLeft, FontStyle.Bold);
            nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            nameText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            body = CozyUI.Txt("Body", root, "", 31, CozyUI.Ink, TextAnchor.UpperLeft);

            buttonsRoot = CozyUI.Rect("Buttons", root, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(540f, 10f));
            var bv = buttonsRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            bv.padding = new RectOffset(0, 0, 8, 0);
            bv.spacing = 10f;
            bv.childControlWidth = true;
            bv.childControlHeight = true;
            bv.childForceExpandWidth = true;
            bv.childForceExpandHeight = false;
            buttonsRoot.gameObject.SetActive(false);
            canvas.gameObject.SetActive(false);
        }

        public void SetSuspended(bool s) { suspended = s; }

        /// Replică simplă, fără butoane.
        public void Say(string text)
        {
            if (buttons.Count > 0) ClearButtons();
            SetIcon(null);
            SetText("Lumi", text);
        }

        /// Replică cu titlu și butoane; Chosen devine indexul butonului apăsat.
        public void Show(string title, string text, string[] labels, bool firstIsPrimary = true, Sprite icon = null, Sprite perButtonIcon = null)
        {
            SetIcon(icon);
            buttonIcon = perButtonIcon;
            SetText(title, text);
            ClearButtons();
            Chosen = -1;
            if (labels != null)
                for (int i = 0; i < labels.Length; i++) AddButton(labels[i], firstIsPrimary && i == 0);
            buttonsRoot.gameObject.SetActive(buttons.Count > 0);
            buttonsShownAt = Time.unscaledTime;
        }

        /// Schimbă doar textul, păstrând butoanele.
        public void SetText(string title, string text)
        {
            title = string.IsNullOrEmpty(title) ? "Lumi" : title;
            if (nameText.text != title) nameText.text = title;
            text = text ?? "";
            if (text == current) return;
            if (StripTags(text) != StripTags(current)) pop = 1f;
            current = text;
            body.text = CozyUI.FixSize(text, body.fontSize);
        }

        public void Clear()
        {
            ClearButtons();
            SetText("Lumi", "");
            Chosen = -1;
        }

        void AddButton(string label, bool primary)
        {
            var rt = CozyUI.Rect("Button" + buttons.Count, buttonsRoot, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(540f, 70f));
            var lay = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            lay.padding = new RectOffset(30, 30, 16, 18);
            lay.spacing = 12f;
            lay.childAlignment = TextAnchor.MiddleCenter;
            lay.childControlWidth = true;
            lay.childControlHeight = true;
            lay.childForceExpandWidth = true;
            lay.childForceExpandHeight = false;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 66f;
            var b = new DialogButton { rt = rt, primary = primary };
            // butoane-pastilă: turcoaz pentru acțiunea principală, roz pentru celelalte
            b.panel = CozyUI.MakePill(rt, primary ? CozyThemeArt.Turquoise : CozyThemeArt.PinkPill, 0.7f);
            if (buttonIcon != null) CozyUI.Icon("Icon", rt, buttonIcon, 40f);
            b.label = CozyUI.Txt("Label", rt, label, 27, CozyUI.Ink, buttonIcon != null ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, FontStyle.Bold);
            b.label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            b.hitbox = CozyUI.AttachHitbox(rt, 8f);
            int index = buttons.Count;
            CozyUI.AddUnityButton(rt.gameObject, b.panel.fill, () => Choose(index));
            buttons.Add(b);
        }

        void ClearButtons()
        {
            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i] != null && buttons[i].rt != null) Object.Destroy(buttons[i].rt.gameObject);
            buttons.Clear();
            buttonsRoot.gameObject.SetActive(false);
            Hovering = false;
        }

        static string StripTags(string s) => s == null ? "" : System.Text.RegularExpressions.Regex.Replace(s, "<.*?>|\\d", "");

        /// Raza mâinii / mouse-ului pe butoane. Întoarce indexul butonului apăsat (sau -1).
        public int Feed(Ray ray, bool click)
        {
            Hovering = false;
            if (!HasButtons) return -1;
            int hit = -1;
            for (int i = 0; i < buttons.Count; i++)
            {
                var b = buttons[i];
                if (b == null || b.rt == null) continue;
                if (CozyUI.RayHitsUI(b.rt, b.hitbox, ray, out _, 8f)) { hit = i; break; }
            }
            bool onCard = hit >= 0 || CozyUI.RayHitsUI(root, rootHitbox, ray, out _);
            if (onCard)
            {
                Hovering = true;
                HoverPoint = hit >= 0 ? buttons[hit].rt.position : root.position;
            }
            for (int i = 0; i < buttons.Count; i++)
                buttons[i].hover = Mathf.MoveTowards(buttons[i].hover, i == hit ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            if (click && hit >= 0 && Choose(hit)) return hit;
            return -1;
        }

        /// Apăsare de buton (din pinch / trigger / click prin Feed sau din UnityEngine.UI.Button).
        bool Choose(int index)
        {
            if (index < 0 || index >= buttons.Count || Chosen >= 0) return false;
            if (Time.unscaledTime - buttonsShownAt <= 0.5f) return false;
            Chosen = index;
            AudioDirector.Sfx2D("chime", 0.5f, 1.15f);
            return true;
        }

        public void Tick(Transform head, Transform lumi, float dt, bool hidden)
        {
            bool want = !hidden && !suspended && !string.IsNullOrEmpty(current);
            alpha = Mathf.MoveTowards(alpha, want ? 1f : 0f, dt * 5f);
            bool on = alpha > 0.01f;
            if (canvas.gameObject.activeSelf != on) canvas.gameObject.SetActive(on);
            if (!on) return;
            group.alpha = alpha;
            pop = Mathf.MoveTowards(pop, 0f, dt * 4f);

            for (int i = 0; i < buttons.Count; i++)
            {
                var b = buttons[i];
                var baseC = b.primary ? CozyThemeArt.Turquoise : CozyThemeArt.PinkPill;
                b.panel.SetColors(Color.Lerp(baseC, Color.Lerp(baseC, Color.white, 0.45f), b.hover), Color.clear);
                b.rt.localScale = Vector3.one * (1f + 0.04f * b.hover);
            }

            bool lumiOn = lumi != null && lumi.gameObject.activeInHierarchy;
            Vector3 pos;
            float scale = 1f;
            Vector3 up = head.up;
            var pivot = new Vector2(0.5f, 0f);
            if (Anchored && AnchorPoint.HasValue)
            {
                pos = AnchorPoint.Value;
                pivot = new Vector2(1f, 0f);
                scale = anchoredScale;
                up = Vector3.up;
            }
            else if (Anchored && lumiOn)
            {
                pos = lumi.position + Vector3.up * anchoredLift;
                scale = anchoredScale;
                up = Vector3.up;
            }
            else if (lumiOn) pos = lumi.position + head.up * 0.12f + head.right * 0.09f;
            else pos = head.TransformPoint(new Vector3(-0.26f, -0.12f, 0.95f));
            if (root.pivot != pivot) root.pivot = pivot;
            root.position = pos;
            var d = pos - head.position;
            if (d.sqrMagnitude > 1e-6f) root.rotation = Quaternion.LookRotation(d.normalized, up);
            root.localScale = Vector3.one * CozyUI.PX * scale * (1f + 0.05f * Mathf.Sin(pop * Mathf.PI));
        }

        public void Destroy()
        {
            if (canvas != null) Object.Destroy(canvas.gameObject);
        }
    }

    // ================================================================ tranziții între etape

    public sealed class TransitionOverlay
    {
        readonly Canvas canvas;
        readonly RectTransform root, button;
        readonly CanvasGroup group;
        readonly Text kicker, title, body, buttonText, hint;
        readonly CozyUI.Panel buttonPanel;
        bool suspended, follow, snap, recenter;
        float shownAt, alpha, hoverT;
        BoxCollider cardHitbox, buttonHitbox;

        public void SetCamera(Camera cam)
        {
            if (cam != null) canvas.worldCamera = cam;
        }

        public bool Active { get; private set; }
        public bool Done { get; private set; }
        public bool Hovering { get; private set; }
        public Vector3 HoverPoint { get; private set; }
        public float distance = 1.15f;
        public float minShowSeconds = 0.8f;

        public TransitionOverlay()
        {
            canvas = CozyUI.MakeCanvas("TransitionOverlay", null, new Vector2(1180f, 500f), 30);
            root = (RectTransform)canvas.transform;
            group = canvas.gameObject.AddComponent<CanvasGroup>();
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            CozyUI.MakeCard(root, Color.white, true, 1f);
            cardHitbox = CozyUI.AttachHitbox(root, 0f);
            var v = canvas.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(70, 70, 62, 46);
            v.spacing = 14f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            var fit = canvas.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            kicker = CozyUI.Txt("Kicker", root, "", 26, new Color(0.78f, 0.56f, 0.18f), TextAnchor.MiddleCenter, FontStyle.Bold);
            title = CozyUI.Txt("Title", root, "", 54, CozyUI.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            body = CozyUI.Txt("Body", root, "", 36, CozyUI.InkSoft, TextAnchor.MiddleCenter);

            // rând cu butonul centrat
            var row = CozyUI.Rect("ButtonRow", root, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(400f, 130f));
            var rle = row.gameObject.AddComponent<LayoutElement>();
            rle.preferredHeight = 128f;
            button = CozyUI.Rect("Button", row, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -4f), new Vector2(440f, 104f));
            buttonPanel = CozyUI.MakePill(button, CozyThemeArt.Turquoise, 1f);
            buttonText = CozyUI.Txt("Label", button, "Continuă", 40, CozyUI.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            CozyUI.Stretch(buttonText.rectTransform, 0f);
            buttonHitbox = CozyUI.AttachHitbox(button, 24f);
            CozyUI.AddUnityButton(button.gameObject, buttonPanel.fill, () => Continue());
            hint = CozyUI.Txt("Hint", root, "pinch / click pentru a continua", 22, new Color(0.6f, 0.55f, 0.68f), TextAnchor.MiddleCenter);
            canvas.gameObject.SetActive(false);
        }

        public void SetSuspended(bool s) { suspended = s; }

        /// Card care plutește în fața jucătorului și îl urmează lin dacă se întoarce.
        public void Show(string kickerText, string titleText, string bodyText, string buttonLabel)
        {
            Setup(kickerText, titleText, bodyText, buttonLabel);
            follow = true;
            snap = true;
        }

        /// Card fix într-un loc din lume (ex. panoul din Grădina Amintirilor).
        public void ShowAt(string kickerText, string titleText, string bodyText, string buttonLabel, Vector3 pos, Quaternion rot)
        {
            Setup(kickerText, titleText, bodyText, buttonLabel);
            follow = false;
            root.SetPositionAndRotation(pos, rot);
        }

        void Setup(string k, string t, string b, string btn)
        {
            kicker.text = k ?? "";
            kicker.gameObject.SetActive(!string.IsNullOrEmpty(k));
            title.text = CozyUI.FixSize(t ?? "", title.fontSize);
            body.text = CozyUI.FixSize(b ?? "", body.fontSize);
            body.gameObject.SetActive(!string.IsNullOrEmpty(b));
            buttonText.text = btn;
            Active = true;
            Done = false;
            shownAt = Time.unscaledTime;
            if (!canvas.gameObject.activeSelf) alpha = 0f;
            canvas.gameObject.SetActive(true);
        }

        public void Hide()
        {
            Active = false;
            Hovering = false;
        }

        /// Pinch / click: pe buton sau oriunde (după o scurtă pauză, ca să nu sară din greșeală).
        public bool Feed(Ray ray, bool click)
        {
            Hovering = false;
            if (!Active || suspended) return false;
            bool onButton = CozyUI.RayHitsUI(button, buttonHitbox, ray, out Vector3 p, 20f);
            bool onCard = onButton || CozyUI.RayHitsUI(root, cardHitbox, ray, out p);
            if (onCard)
            {
                Hovering = true;
                HoverPoint = onButton ? button.position : p;
            }
            hoverT = Mathf.MoveTowards(hoverT, onButton ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            if (click && Continue()) return true;
            return onCard;
        }

        bool Continue()
        {
            if (!Active || Done || suspended || Time.unscaledTime - shownAt < minShowSeconds) return false;
            Done = true;
            AudioDirector.Sfx2D("chime", 0.55f, 1.1f);
            return true;
        }

        public void Tick(Transform head, float dt)
        {
            bool want = Active && !suspended;
            alpha = Mathf.MoveTowards(alpha, want ? 1f : 0f, dt * 4f);
            bool on = alpha > 0.01f;
            if (canvas.gameObject.activeSelf != on) canvas.gameObject.SetActive(on);
            if (!on) return;
            group.alpha = alpha;
            float s = CozyUI.PX * Mathf.Lerp(0.92f, 1f, Mathf.SmoothStep(0f, 1f, alpha));
            root.localScale = Vector3.one * s;

            var bc = Color.Lerp(CozyThemeArt.Turquoise, Color.Lerp(CozyThemeArt.Turquoise, Color.white, 0.45f), hoverT);
            buttonPanel.SetColors(bc, Color.clear);
            button.localScale = Vector3.one * (1f + 0.05f * hoverT + 0.02f * Mathf.Sin(Time.unscaledTime * 3f));

            if (!follow || head == null) return;
            var f = head.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            f.Normalize();
            var target = head.position + f * distance + Vector3.down * 0.04f;
            var toCard = root.position - head.position;
            if (snap)
            {
                root.position = target;
                snap = false;
            }
            else
            {
                // urmărire leneșă: doar când cardul iese din zona confortabilă, apoi se recentrează complet
                if (Vector3.Angle(head.forward, toCard) > 26f || Mathf.Abs(toCard.magnitude - distance) > 0.4f) recenter = true;
                if (recenter)
                {
                    root.position = Vector3.Lerp(root.position, target, 1f - Mathf.Exp(-3.5f * dt));
                    if ((root.position - target).sqrMagnitude < 0.0004f) recenter = false;
                }
            }
            var d = root.position - head.position;
            if (d.sqrMagnitude > 1e-6f) root.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        public void Destroy()
        {
            if (canvas != null) Object.Destroy(canvas.gameObject);
        }
    }

    // ================================================================ calibrarea pulsului de calm

    /// <summary>Inimioară care bate + bară de progres: „Se calibrează ritmul tău de calm... (68 BPM)”.</summary>
    public sealed class CalibrationWidget
    {
        readonly Canvas canvas;
        readonly RectTransform root, fill, heart;
        readonly Text label;
        readonly CanvasGroup group;
        bool active;
        float alpha, progress, bpm = 70f, beatPhase;

        public CalibrationWidget()
        {
            canvas = CozyUI.MakeCanvas("PulseCalibration", null, new Vector2(760f, 260f), 28);
            root = (RectTransform)canvas.transform;
            group = canvas.gameObject.AddComponent<CanvasGroup>();
            CozyUI.MakeCard(root, Color.white, true, 0.9f);

            var h = CozyUI.Img("Heart", root, CozyThemeArt.HeartIcon, Color.white, false);
            h.preserveAspect = true;
            heart = h.rectTransform;
            heart.anchorMin = heart.anchorMax = new Vector2(0f, 0.5f);
            heart.anchoredPosition = new Vector2(110f, 12f);
            heart.sizeDelta = new Vector2(130f, 130f);

            label = CozyUI.Txt("Label", root, "", 32, CozyUI.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            var lrt = label.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0.5f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = new Vector2(205f, 38f);
            lrt.sizeDelta = new Vector2(520f, 110f);

            var bar = CozyUI.Rect("Bar", root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(205f, -52f), new Vector2(510f, 34f));
            var bg = CozyUI.Img("BarBg", bar, CozyUI.Rounded, new Color(0.9f, 0.86f, 0.98f, 1f), true);
            CozyUI.Stretch(bg.rectTransform, 0f);
            bg.pixelsPerUnitMultiplier = 2.5f;
            var f = CozyUI.Img("BarFill", bar, CozyUI.Rounded, CozyUI.Gold, true);
            fill = f.rectTransform;
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(0f, 1f);
            fill.pivot = new Vector2(0f, 0.5f);
            fill.anchoredPosition = Vector2.zero;
            fill.sizeDelta = new Vector2(0f, 0f);
            f.pixelsPerUnitMultiplier = 2.5f;
            canvas.gameObject.SetActive(false);
        }

        public void Show(Vector3 pos, Transform head)
        {
            active = true;
            root.position = pos;
            if (head != null) root.rotation = Quaternion.LookRotation((pos - head.position).normalized, Vector3.up);
            canvas.gameObject.SetActive(true);
        }

        public void Set(float progress01, float bpmValue, string text)
        {
            progress = Mathf.Clamp01(progress01);
            bpm = Mathf.Max(30f, bpmValue);
            label.text = CozyUI.FixSize(text ?? "", label.fontSize);
        }

        public void Hide() { active = false; }

        public void Tick(Transform head, float dt)
        {
            alpha = Mathf.MoveTowards(alpha, active ? 1f : 0f, dt * 4f);
            bool on = alpha > 0.01f;
            if (canvas.gameObject.activeSelf != on) canvas.gameObject.SetActive(on);
            if (!on) return;
            group.alpha = alpha;
            root.localScale = Vector3.one * CozyUI.PX * Mathf.Lerp(0.9f, 1f, alpha);
            fill.sizeDelta = new Vector2(510f * progress, 0f);
            // bătaie dublă (lub-dub) în ritmul BPM-ului afișat
            beatPhase += dt * bpm / 60f;
            float ph = beatPhase - Mathf.Floor(beatPhase);
            float beat = Mathf.Exp(-Mathf.Pow((ph - 0.05f) / 0.05f, 2f)) + 0.6f * Mathf.Exp(-Mathf.Pow((ph - 0.25f) / 0.05f, 2f));
            heart.localScale = Vector3.one * (1f + 0.16f * beat);
            if (head != null)
            {
                var d = root.position - head.position;
                if (d.sqrMagnitude > 1e-6f) root.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            }
        }

        public void Destroy()
        {
            if (canvas != null) Object.Destroy(canvas.gameObject);
        }
    }

    // ================================================================ suvenir: „Polaroid cu Haita”

    /// <summary>
    /// Card-suvenir tip fotografie instantanee, pe un Canvas fix Screen Space - Camera (copil al camerei):
    /// stă ușor deasupra și în dreapta centrului privirii, deci nu mai taie prin grădină.
    /// Ramă albă (mai lată jos), „poza” pastelată cu fețișoarele celor 3 căței, steluța lui Lumi, ecusonul haitei,
    /// bandă adezivă roz, mesajul de final și butonul „Sesiune nouă” (pinch / click / rază, cu hitbox fizic).
    /// </summary>
    public sealed class PolaroidCard
    {
        readonly RectTransform root, button;
        readonly CanvasGroup group;
        readonly Text caption, buttonText;
        readonly CozyUI.Panel buttonPanel;
        readonly BoxCollider buttonHitbox, cardHitbox;
        bool active;
        float alpha, shownAt, hoverT;

        public bool Active => active;
        public bool Done { get; private set; }
        public bool Hovering { get; private set; }
        public Vector3 HoverPoint { get; private set; }
        /// Poziția pe ecran (unități de canvas, referință 1600×900) și scara cardului.
        public Vector2 screenOffset = new Vector2(150f, 40f);
        public float screenScale = 0.56f;

        public PolaroidCard(RectTransform screenCanvas)
        {
            root = CozyUI.Rect("PolaroidSouvenir", screenCanvas, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 980f));
            group = root.gameObject.AddComponent<CanvasGroup>();
            var shadow = CozyUI.Img("Shadow", root, CozyUI.Shadow, new Color(0.25f, 0.15f, 0.2f, 0.35f), true);
            CozyUI.Stretch(shadow.rectTransform, -30f);
            shadow.rectTransform.anchoredPosition = new Vector2(10f, -14f);
            var frame = CozyUI.Img("WhiteFrame", root, CozyUI.Rounded, new Color(1f, 0.995f, 0.985f, 1f), true);
            CozyUI.Stretch(frame.rectTransform, 0f);
            frame.pixelsPerUnitMultiplier = 3f;
            cardHitbox = CozyUI.AttachHitbox(root, 0f);

            // „poza”
            var photo = CozyUI.Rect("Photo", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(640f, 560f));
            var bg = CozyUI.Img("PhotoBg", photo, CozyThemeArt.PhotoBackground, Color.white, false);
            CozyUI.Stretch(bg.rectTransform, 0f);
            Color[] collars = { new Color(0.45f, 0.68f, 1f), new Color(1f, 0.55f, 0.75f), new Color(0.45f, 0.82f, 0.55f) };
            float[] px = { -190f, 0f, 190f };
            float[] py = { -30f, 10f, -30f };
            float[] rot = { 8f, 0f, -8f };
            string[] modelKeys = { "corgi_mochi", "corgi_gogoasa", "corgi_fasolica" };
            for (int i = 0; i < 3; i++)
            {
                // portretul randat din noul model 3D (Resources/MindThePaws/Icons); altfel fețișoara desenată procedural
                var portrait = CozyThemeArt.CorgiPortrait(modelKeys[i]);
                var face = CozyUI.Img("Corgi" + i, photo, portrait != null ? portrait : CozyThemeArt.CorgiFace(collars[i]), Color.white, false);
                face.preserveAspect = true;
                var rt = face.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(px[i], py[i] + (portrait != null ? 22f : 0f));
                rt.sizeDelta = portrait != null ? new Vector2(250f, 250f) : new Vector2(190f, 190f);
                rt.localRotation = Quaternion.Euler(0f, 0f, rot[i]);
                var tag = CozyUI.Txt("Name" + i, photo, ChaosBehaviours.PuppyNames[i], 30, CozyUI.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                var trt = tag.rectTransform;
                trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
                trt.anchoredPosition = new Vector2(px[i], py[i] - 118f);
                trt.sizeDelta = new Vector2(200f, 40f);
            }
            var star = CozyUI.Img("LumiStar", photo, CozyThemeArt.StarIcon, Color.white, false);
            star.rectTransform.anchorMin = star.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            star.rectTransform.anchoredPosition = new Vector2(0f, -78f);
            star.rectTransform.sizeDelta = new Vector2(96f, 96f);
            var heart = CozyUI.Img("Heart", photo, CozyThemeArt.HeartIcon, Color.white, false);
            heart.rectTransform.anchorMin = heart.rectTransform.anchorMax = new Vector2(1f, 1f);
            heart.rectTransform.anchoredPosition = new Vector2(-70f, -70f);
            heart.rectTransform.sizeDelta = new Vector2(70f, 70f);
            var paw = CozyUI.Img("Paw", photo, CozyThemeArt.PawIcon, Color.white, false);
            paw.rectTransform.anchorMin = paw.rectTransform.anchorMax = new Vector2(0f, 1f);
            paw.rectTransform.anchoredPosition = new Vector2(70f, -70f);
            paw.rectTransform.sizeDelta = new Vector2(66f, 66f);

            // ecusonul haitei
            var badge = CozyUI.Rect("PackBadge", photo, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(430f, 64f));
            CozyUI.MakePill(badge, CozyThemeArt.PinkPill, 0.8f);
            var bt = CozyUI.Txt("BadgeText", badge, "Haita din Mind the Paws", 28, CozyUI.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            CozyUI.Stretch(bt.rectTransform, 0f);

            // bandă adezivă roz, puțin strâmbă
            var tape = CozyUI.Img("Tape", root, CozyUI.Rounded, new Color(1f, 0.75f, 0.84f, 0.75f), true);
            var tp = tape.rectTransform;
            tp.anchorMin = tp.anchorMax = new Vector2(0.5f, 1f);
            tp.anchoredPosition = new Vector2(0f, 4f);
            tp.sizeDelta = new Vector2(220f, 58f);
            tp.localRotation = Quaternion.Euler(0f, 0f, -4f);
            tape.pixelsPerUnitMultiplier = 6f;

            // mesajul de dedesubt
            caption = CozyUI.Txt("Caption", root, "", 30, CozyUI.Ink, TextAnchor.MiddleCenter, FontStyle.Italic);
            var crt = caption.rectTransform;
            crt.anchorMin = new Vector2(0f, 0f);
            crt.anchorMax = new Vector2(1f, 0f);
            crt.pivot = new Vector2(0.5f, 0f);
            crt.anchoredPosition = new Vector2(0f, 130f);
            crt.sizeDelta = new Vector2(-60f, 230f);

            // butonul de final / reîncepere
            button = CozyUI.Rect("RestartButton", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(380f, 86f));
            buttonPanel = CozyUI.MakePill(button, CozyThemeArt.Turquoise, 0.9f);
            buttonText = CozyUI.Txt("Label", button, "Sesiune nouă", 34, CozyUI.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            CozyUI.Stretch(buttonText.rectTransform, 0f);
            buttonHitbox = CozyUI.AttachHitbox(button, 24f);
            CozyUI.AddUnityButton(button.gameObject, buttonPanel.fill, () => Press());
            root.gameObject.SetActive(false);
        }

        public void Show(string text, string buttonLabel = "Sesiune nouă")
        {
            caption.text = CozyUI.FixSize(text ?? "", caption.fontSize);
            buttonText.text = buttonLabel;
            active = true;
            Done = false;
            shownAt = Time.unscaledTime;
            alpha = 0f;
            root.gameObject.SetActive(true);
            AudioDirector.Sfx2D("chime", 0.6f, 1.2f);
        }

        public void Hide()
        {
            active = false;
            Hovering = false;
        }

        /// Raza mâinii / controllerului / mouse-ului pe card. Întoarce true dacă raza e pe card (consumă pinch-ul).
        public bool Feed(Ray ray, bool click)
        {
            Hovering = false;
            if (!active || alpha < 0.5f) return false;
            bool onButton = CozyUI.RayHitsUI(button, buttonHitbox, ray, out Vector3 p, 24f);
            bool onCard = onButton || CozyUI.RayHitsUI(root, cardHitbox, ray, out p);
            hoverT = Mathf.MoveTowards(hoverT, onButton ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            if (!onCard) return false;
            Hovering = true;
            HoverPoint = onButton ? button.position : p;
            if (click && onButton) Press();
            return true;
        }

        bool Press()
        {
            if (!active || Done || Time.unscaledTime - shownAt < 0.9f) return false;
            Done = true;
            AudioDirector.Sfx2D("chime", 0.6f, 1.1f);
            return true;
        }

        public void Tick(float dt)
        {
            alpha = Mathf.MoveTowards(alpha, active ? 1f : 0f, dt * 3f);
            bool on = alpha > 0.01f;
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
            if (!on) return;
            group.alpha = alpha;
            // apare ca o poză care „iese” și se legănă ușor; fix pe ecran, fără urmărire
            float t = Time.unscaledTime - shownAt;
            float pop = active ? Mathf.Clamp01(t / 0.5f) : alpha;
            float s = Mathf.Lerp(0.75f, 1f, 1f - (1f - pop) * (1f - pop)) * (1f + 0.04f * Mathf.Sin(Mathf.Clamp01(t / 0.9f) * Mathf.PI));
            root.anchoredPosition = screenOffset;
            root.localScale = Vector3.one * screenScale * s;
            root.localRotation = Quaternion.Euler(0f, 0f, -3f + Mathf.Sin(Time.unscaledTime * 0.9f) * 1.2f);
            var bc = Color.Lerp(CozyThemeArt.Turquoise, Color.Lerp(CozyThemeArt.Turquoise, Color.white, 0.45f), hoverT);
            buttonPanel.SetColors(bc, Color.clear);
            button.localScale = Vector3.one * (1f + 0.05f * hoverT);
        }
    }
}
