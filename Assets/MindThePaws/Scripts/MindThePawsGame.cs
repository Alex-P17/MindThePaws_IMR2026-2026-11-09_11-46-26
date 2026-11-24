using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Meta.XR;
using Meta.XR.MRUtilityKit;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MindThePaws
{
    /// <summary>
    /// "Mind the Paws" — fluxul complet al unei sesiuni (2 runde: Ziua Liniștită + Ziua cu Zoomies):
    ///  Tutorial cu Lumi („Bop the Snoot” + Pătura Moale) →
    ///  [Tranziție: bun venit] → Ascunde 3 comori (inventar jos) → 5 s de memorare → Poof!
    ///  → [Tranziție: provocarea] → mini-joc de 90 s (Constelația Pierdută / Portalul către Brutăria Lunii)
    ///  → [Tranziție: căutarea] → (haos: Mochi, Gogoașă, Fasolică cer atenție pe rând) → Ghicește cu lăbuța
    ///  → rezultat → runda următoare → [Tranziție: grădina] → Grădina Amintirilor pe podea + cartonașele lui Lumi.
    /// UI: CozyUIManager (ecuson stânga-sus, inventar jos, dialogul lui Lumi, carduri de tranziție) — fără TextMesh.
    /// Merge cu mâinile (pinch), cu controller-ele (trigger) sau cu mouse-ul în Editor (click dreapta = privire, click stânga = pinch).
    /// </summary>
    public class MindThePawsGame : MonoBehaviour
    {
        enum State { Tutorial, Intro, Hide, Poof, Transition, Memorize, Distract, Guess, Result, Debriefing }

        const int Rounds = 2;
        const int Items = 3;
        static readonly string[][] Sets = new string[][]
        {
            new string[] { "mug", "keys", "diary" },
            new string[] { "glasses", "glove", "lantern" }
        };

        [Header("Referințe (se găsesc singure dacă rămân goale)")]
        public OVRCameraRig cameraRig;
        public TelemetryLogger logger;

        [Header("Tutorial")]
        [Tooltip("Tutorialul cu Lumi înainte de Runda 1 (se poate sări din butonul din dialog).")]
        public bool runTutorial = true;
        public int snootBops = 3;

        [Header("Joc")]
        public float memorizeSeconds = 5f;
        public float pawSizeMeters = 0.10f;
        public float maxRayDistance = 6f;
        [Range(0f, 1f)] public float minSurfaceUpDot = 0.7f;   // doar suprafețe orizontale
        public float fallbackSurfaceBelowHead = 0.6f;          // plan virtual dacă nu există Space Setup
        public float minTreasureGapEditor = 0.6f;
        public float minTreasureGapMR = 0.8f;

        public enum MemoryTaskMode { Constellation, Bakery, BothAlternating }

        [Header("Provocarea de memorie de lucru")]
        [Tooltip("BothAlternating: Runda 1 = Constelația Pierdută, Runda 2 = Portalul către Brutăria Lunii.")]
        public MemoryTaskMode memoryTaskMode = MemoryTaskMode.BothAlternating;
        [Tooltip("Durata provocării (secunde), pentru ambele mini-jocuri.")]
        [Range(10f, 180f)] public float challengeDurationSeconds = 90f;
        [Tooltip("Tastatură (Editor): 1 = Constelația, 2 = Brutăria, 0 = alternativ, Tab = comută instant, F1 = debug.")]
        public bool keyboardShortcuts = true;

        public enum ChaosMode { Calm, Chaos, Alternate }

        [Header("Haos")]
        [Tooltip("Calm: fără distrageri. Chaos: mereu cu distrageri. Alternate: runda 1 calmă, runda 2 cu haos (bun pentru studiu).")]
        public ChaosMode chaosMode = ChaosMode.Alternate;
        [Tooltip("Cât timp (s) trebuie privit fiecare cățel ca să fie „mângâiat”.")]
        public float puppyLookSeconds = 0.8f;
        public int bubbleCount = 10;

        bool ChaosFor(int rd) => chaosMode == ChaosMode.Chaos || (chaosMode == ChaosMode.Alternate && rd % 2 == 0);
        bool ChaosThisRound => ChaosFor(round);
        // runda e 0 în tutorial: indexul trebuie ținut mereu în [0, Sets.Length) (înainte: Sets[-1] → IndexOutOfRange în fiecare cadru)
        static string[] SetFor(int rd)
        {
            int n = Sets.Length;
            int k = (Mathf.Max(1, rd) - 1) % n;
            return Sets[(k + n) % n];
        }
        string[] CurrentSet => SetFor(round);
        static int ErrIndex(int rd, int item) => (rd - 1) * Items + item;
        bool ValidErr(int idx) => idx >= 0 && idx < errorsAll.Length;
        string StageName(int rd) => ChaosFor(rd) ? "Ziua cu Zoomies" : "Ziua Liniștită";

        State state = State.Intro;
        Transform head;
        readonly List<OVRHand> hands = new List<OVRHand>();
        float nextHandSearch;
        bool wasPinching;
        float lastClickTime = -10f;

        readonly GameObject[] treasures = new GameObject[Items];
        readonly GameObject[] paws = new GameObject[Items];
        readonly LineRenderer[] errorLines = new LineRenderer[Items];
        readonly Vector3[] hiddenPos = new Vector3[Items];
        readonly Vector3[] guessPos = new Vector3[Items];
        readonly string[] hiddenSurface = new string[Items];
        readonly float[] errorsAll = new float[Rounds * Items];
        GameObject ghost, pawGhost, lumi;
        LineRenderer aimLine;
        Material rayOk, rayBad;
        readonly List<GameObject> puffs = new List<GameObject>();
        readonly List<GameObject> surfaceDust = new List<GameObject>();     // praf de stele pe mobilier (faza Ascunde)
        readonly List<GameObject> resultLabels = new List<GameObject>();    // „23 cm · 2 lăbuțe” lângă liniile de eroare
        bool lastSurfaceNotFlat;     // raza a lovit ceva, dar nu e o suprafață orizontală
        string noteLine = "";        // o replică temporară a lui Lumi (scanare, haos mai blând...)
        float noteUntil;

        ChaosBehaviours chaos;
        SafetyBlanket safety;
        IMemoryTask memTask;
        GameObject memTaskHost;
        MemoryTaskKind memTaskKind;
        bool memTaskSwitch;
        AudioDirector audioDir;
        AtmosphereController atmosphere;
        CozyUIManager ui;
        CozyMood mood = CozyMood.Calm;
        CozyMood moodBeforeSafety = CozyMood.Calm;
        bool listenerChecked;
        string toast = "";
        float toastUntil;
        float chaosIntensity = 1f;
        bool safetyTriggered;

        int round;
        int placedCount;
        int itemIndex;
        bool gapWarn;
        float hideTime, lastGuessMark;
        float? fallbackY;
        string lastSurface = "";
        string debugInfo = "";
        string aimSource = "";
        GameObject reticle;
        bool aimIsGaze;
        bool lumiFollow = true, lumiSnap = true;
        string lumiLine = "";       // replica lui Lumi impusă de etapa curentă (altfel se calculează în UpdateHud)
        float badgeTimer = -1f;
        string phaseName = "";

        GameObject garden, jar, jarStar;
        CozyLabel promiseLabel, jarLabel;
        Transform jarGlow;
        Material jarGlowMat;
        [Header("Grădina Amintirilor")]
        [Tooltip("Înălțimea de repaus a Borcanului cu Stele (m).")]
        public float jarHeight = 0.2f;
        [Tooltip("De câte ori se mărește borcanul când vine în fața jucătorului pentru promisiune.")]
        public float jarFocusScale = 1.7f;
        [Tooltip("Cât de departe în dreapta centrului grădinii stă piedestalul borcanului (m).")]
        public float jarSideOffset = 0.95f;
        float jarBaseScale = 1f;
        Vector3 jarHomePos;
        Quaternion jarHomeRot;

        // dialog cu butoane (tutorial, grădină): când e activ, Lumi nu mai primește replici automate
        bool asking;

        // tutorial „Bop the Snoot”
        string tutorialStep = "";
        GameObject tutorialPup;
        Transform snootMarker, snootHalo;
        Material snootHaloMat;
        bool snootActive, snootBopped, snootHover;
        bool tutorialSafetyTested;

        // „Ceai cu Lumi”: ceainic, ceașcă și calibrarea pulsului de calm
        GameObject kettle, teaCup;
        bool cupActive, cupClicked, cupHover, cupHolding;
        bool holdNow;   // pinch / trigger / click stânga ȚINUT apăsat în acest cadru
        [Tooltip("Cât timp trebuie ținută ceașca (pinch / click ținut) ca să se măsoare pulsul de calm.")]
        public float teaHoldSeconds = 4.5f;

        /// Senzor extern de puls (opțional): întoarce BPM-ul curent sau ≤ 0 dacă nu e disponibil.
        /// Dacă rămâne null, pulsul de calm e simulat (Quest 3 nu are senzor de puls).
        public static System.Func<float> ExternalHeartRate;
        [Header("Ceai cu Lumi")]
        public float pulseCalibrationSeconds = 5f;

        // mângâierea cățeilor în Ziua cu Zoomies (pinch / click pe cățel)
        int petIndex = -1;
        bool petClicked, petHover;

        // grădina interactivă
        readonly GameObject[] flowers = new GameObject[Rounds * Items];
        readonly float[] flowerH = new float[Rounds * Items];
        readonly Transform[] flowerHalos = new Transform[Rounds * Items];
        readonly Material[] flowerHaloMats = new Material[Rounds * Items];
        bool flowersInteractive;
        int selectedFlower = -1, hoveredFlower = -1, flowerPicked = -1;

        static readonly string[] Promises =
        {
            "Voi avea răbdare când cineva uită unde și-a pus lucrurile.",
            "Voi oferi liniște fără presiune celor copleșiți de stimuli.",
            "Voi fi mai blând(ă) cu mine în zilele agitate."
        };

        public enum AimMode { Auto, Gaze }

        [Header("Țintire")]
        [Tooltip("Auto: mână → controller → mouse (Editor) → privire. Gaze: mereu cu privirea + pinch.")]
        public AimMode aimMode = AimMode.Auto;

        [Header("Adâncime live (Quest 3)")]
        [Tooltip("Raycast pe forma reală a camerei (canapea cu haine, perne), nu pe cutiile din Space Setup.")]
        public bool useLiveDepth = true;
        [Range(0f, 1f)] public float depthMinUpDot = 0.35f;
        EnvironmentRaycastManager envRay;
        string depthStatus = "-";

        [Header("Material (opțional)")]
        [Tooltip("Un material URP Unlit din proiect. Dacă e gol, scriptul își alege singur shaderul.")]
        public Material baseMaterial;

        [Header("Debug")]
        [Tooltip("Text mic de diagnostic în colțul dreapta-jos (F1 îl comută în timpul jocului).")]
        public bool debugOverlay = false;
        [HideInInspector] public bool showDebug = true;   // câmp vechi, păstrat doar pentru compatibilitatea scenei

        MRUKRoom Room => MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;

        bool started;

        void Start()
        {
            Debug.Log("[MindThePaws] Start…");
            try
            {
                Setup();
                started = true;
                Debug.Log($"[MindThePaws] Start OK · CameraRig: {(cameraRig != null ? cameraRig.name : "NU")} · cameră: {(FindHead() != null ? FindHead().name : "NU")}");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[MindThePaws] Start a EȘUAT: " + e);
            }
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
        }

        void Setup()
        {
            if (baseMaterial != null) Graybox.Override = baseMaterial;
            if (useLiveDepth)
            {
                if (EnvironmentRaycastManager.IsSupported)
                {
                    envRay = FindFirstObjectByType<EnvironmentRaycastManager>();
                    if (envRay == null) envRay = gameObject.AddComponent<EnvironmentRaycastManager>();
                }
                else depthStatus = "nesuportat";
            }
            if (cameraRig == null) cameraRig = FindFirstObjectByType<OVRCameraRig>();
            if (logger == null) logger = GetComponent<TelemetryLogger>();
            if (logger == null) logger = gameObject.AddComponent<TelemetryLogger>();

            chaos = GetComponent<ChaosBehaviours>();
            if (chaos == null) chaos = gameObject.AddComponent<ChaosBehaviours>();
            chaos.bubbleCount = bubbleCount;

            safety = GetComponent<SafetyBlanket>();
            if (safety == null) safety = gameObject.AddComponent<SafetyBlanket>();
            safety.cameraRig = cameraRig;
            safety.onStart = () => OnSafetyStart();
            safety.onFinish = c => OnSafetyFinish(c);

            // audio 3D, atmosferă și UI: se atașează singure
            audioDir = GetComponent<AudioDirector>();
            if (audioDir == null) audioDir = gameObject.AddComponent<AudioDirector>();
            atmosphere = GetComponent<AtmosphereController>();
            if (atmosphere == null) atmosphere = gameObject.AddComponent<AtmosphereController>();
            ui = GetComponent<CozyUIManager>();
            if (ui == null) ui = gameObject.AddComponent<CozyUIManager>();
            ui.DebugVisible = debugOverlay;

            pawGhost = Graybox.Paw("PawGhost", new Color(0.85f, 0.72f, 0.62f));
            CozyFx.ApplyGhost(pawGhost, 0.6f);
            pawGhost.SetActive(false);
            lumi = Graybox.FairyLumi("Lumi");
            lumi.SetActive(false);              // apare abia când avem camera (în tutorial: iese din ceainic)
            ui.lumi = lumi.transform;

            aimLine = Graybox.Line("AimRay", Color.white, 0.006f);
            aimLine.enabled = false;
            reticle = new GameObject("Reticle");
            Graybox.Prim(PrimitiveType.Sphere, reticle.transform, Vector3.zero, Vector3.one * 0.022f, Graybox.MakeMat(Color.white));
            reticle.SetActive(false);
            rayOk = aimLine.sharedMaterial;
            rayBad = Graybox.MakeMat(new Color(1f, 0.7f, 0.45f));   // portocaliu-pastelat: suprafață invalidă

            RestartSession();
        }

        void Update()
        {
            if (!started) return;
            if (head == null) head = FindHead();
            if (head == null)
            {
                if (Time.frameCount % 120 == 0) Debug.LogError("[MindThePaws] Nu găsesc nicio cameră!");
                return;
            }
            safety.head = head;
            atmosphere.head = head;
            ui.head = head;
            if (!listenerChecked)
            {
                listenerChecked = true;
                audioDir.EnsureListener(head);
            }

            EditorMouseLook();
            HandleShortcuts();

            try
            {
                Tick();
            }
            catch (System.Exception e)
            {
                debugInfo = "EROARE: " + e.GetType().Name + ": " + e.Message;
                Debug.LogException(e);
            }

            Safe(UpdateLumi);
            Safe(UpdateHud);
            Safe(UpdateTutorialFx);
            Safe(UpdateGarden);
        }

        void Safe(System.Action a)
        {
            try { a(); }
            catch (System.Exception e)
            {
                debugInfo = "EROARE: " + e.GetType().Name + ": " + e.Message;
                Debug.LogException(e);
            }
        }

        void DrawAim(bool show, Ray ray, Vector3 end, bool ok)
        {
            aimLine.enabled = show && !aimIsGaze;   // la privire, raza ar fi chiar în ochi → doar punct
            if (aimLine.enabled)
            {
                aimLine.SetPosition(0, ray.origin);
                aimLine.SetPosition(1, end);
                aimLine.sharedMaterial = ok ? rayOk : rayBad;
            }
            reticle.SetActive(show);
            if (show)
            {
                reticle.transform.position = end;
                reticle.GetComponentInChildren<Renderer>().sharedMaterial = ok ? rayOk : rayBad;
            }
        }

        void Tick()
        {
            bool hasAim = GetAim(out Ray ray, out bool click);

            if (safety.Running)
            {
                if (hasAim) safety.Feed(ray, click);
                Vector3 sEnd = safety.Hovering ? safety.HoverPoint : ray.GetPoint(1.2f);
                DrawAim(hasAim && safety.Choosing, ray, sEnd, safety.Hovering);
                return;
            }

            // carduri de tranziție / panoul din grădină: au prioritate la pinch / click
            if (ui.Overlay.Active)
            {
                if (hasAim) ui.Overlay.Feed(ray, click);
                if (ghost != null) ghost.SetActive(false);
                pawGhost.SetActive(false);
                DrawAim(hasAim, ray, ui.Overlay.Hovering ? ui.Overlay.HoverPoint : ray.GetPoint(1.1f), true);
                return;
            }

            // Polaroidul suvenir (Screen Space - Camera): butonul „Sesiune nouă” prin rază + pinch / click
            if (hasAim && ui.Polaroid.Active && ui.Polaroid.Feed(ray, click))
            {
                if (ghost != null) ghost.SetActive(false);
                pawGhost.SetActive(false);
                DrawAim(true, ray, ui.Polaroid.HoverPoint, true);
                return;
            }

            // butoanele din cardul lui Lumi (tutorial, grădină)
            if (hasAim && ui.Dialog.HasButtons)
            {
                ui.Dialog.Feed(ray, click);
                if (ui.Dialog.Hovering)
                {
                    if (ghost != null) ghost.SetActive(false);
                    pawGhost.SetActive(false);
                    DrawAim(true, ray, ui.Dialog.HoverPoint, true);
                    return;
                }
            }

            if (state == State.Tutorial || state == State.Distract || state == State.Debriefing)
            {
                TickPointing(hasAim, ray, click);
                return;
            }

            bool taskActive = state == State.Memorize && memTask != null && !memTask.Finished;
            bool needAim = state == State.Hide || state == State.Guess;
            Vector3 point = default;
            bool hit = false;
            if (hasAim && taskActive)
            {
                memTask.Feed(ray, click);
                hit = memTask.Hovering;
                point = memTask.HoverPoint;
            }
            else if (hasAim && needAim)
            {
                // Ziua cu Zoomies: bulele de săpun din fața ochilor se sparg la atingere („Pop!”)
                if (state == State.Guess && ChaosThisRound && chaos.Running && chaos.PopUnderRay(ray, click, out Vector3 bubblePoint))
                {
                    pawGhost.SetActive(false);
                    DrawAim(true, ray, bubblePoint, true);
                    if (click) logger.LogEvent("bubble_pop", "total=" + chaos.PopCount);
                    return;
                }
                hit = TryHitSurface(ray, out point, out _, out lastSurface);
            }
            else lastSurfaceNotFlat = false;

            bool tooClose = state == State.Hide && hit && placedCount > 0 && TooClose(point);
            bool valid = hit && !tooClose;
            gapWarn = tooClose;
            bool notFlat = needAim && !hit && lastSurfaceNotFlat;

            bool showRay = hasAim && (needAim || taskActive);
            Vector3 end = hit || notFlat ? point : ray.GetPoint(1.5f);   // pe suprafețe strâmbe: cursorul stă pe ele, portocaliu
            DrawAim(showRay, ray, end, valid);

            Quaternion facing = YawTowardsHead(point);

            switch (state)
            {
                case State.Hide:
                    if (ghost != null)
                    {
                        ghost.SetActive(hit);
                        if (hit) ghost.transform.SetPositionAndRotation(point, facing);
                    }
                    if (valid && click && placedCount < Items) PlaceTreasure(point, facing);
                    break;

                case State.Guess:
                    pawGhost.SetActive(hit);
                    if (hit) pawGhost.transform.SetPositionAndRotation(point, facing);
                    if (hit && click && itemIndex < Items) GuessItem(point);
                    break;

                default:
                    if (ghost != null) ghost.SetActive(false);
                    pawGhost.SetActive(false);
                    break;
            }
        }

        /// Țintire liberă: nasul lui Mochi (tutorial), cățeii de mângâiat (haos), florile din grădină.
        void TickPointing(bool hasAim, Ray ray, bool click)
        {
            if (ghost != null) ghost.SetActive(false);
            pawGhost.SetActive(false);
            if (!hasAim)
            {
                DrawAim(false, ray, ray.origin, false);
                return;
            }
            Vector3 end = ray.GetPoint(1.5f);
            bool hit = false;

            if (state == State.Tutorial && cupActive && teaCup != null)
            {
                var c = teaCup.transform.position + Vector3.up * 0.05f;
                // odată prinsă, ceașca rămâne „în mână” cu o toleranță mai mare (ea se mișcă spre jucător)
                hit = MemoryTaskUtil.Pick(ray, new[] { c }, cupHolding ? 0.28f : 0.13f) == 0;
                cupHover = hit;
                cupHolding = hit && holdNow;
                if (hit)
                {
                    end = c;
                    if (click) cupClicked = true;
                }
            }
            else if (state == State.Tutorial && snootActive && snootMarker != null)
            {
                hit = MemoryTaskUtil.Pick(ray, new[] { snootMarker.position }, 0.09f) == 0;
                snootHover = hit;
                if (hit)
                {
                    end = snootMarker.position;
                    if (click) snootBopped = true;
                }
            }
            else if (state == State.Distract && petIndex >= 0)
            {
                var pt = chaos.Puppy(petIndex);
                petHover = false;
                if (pt != null)
                {
                    var pts = ChaosBehaviours.BodyPoints(pt);
                    int k = MemoryTaskUtil.Pick(ray, pts, 0.22f);   // hitbox generos, ca să-l prinzi și când aleargă
                    if (k >= 0)
                    {
                        hit = true;
                        petHover = true;
                        end = pts[k];
                        if (click) petClicked = true;
                    }
                }
                chaos.Highlight(petIndex, true);
            }
            else if (state == State.Debriefing && flowersInteractive)
            {
                var tops = new Vector3[flowers.Length];
                for (int i = 0; i < flowers.Length; i++) tops[i] = FlowerTop(i);
                hoveredFlower = MemoryTaskUtil.Pick(ray, tops, 0.1f);
                if (hoveredFlower >= 0)
                {
                    hit = true;
                    end = tops[hoveredFlower];
                    if (click) flowerPicked = hoveredFlower;
                }
            }

            bool show = state != State.Debriefing || flowersInteractive || hit;
            DrawAim(show, ray, end, hit);
        }

        // ================================================================ UI (ecuson, inventar, Lumi)

        void SetMood(CozyMood m)
        {
            mood = m;
            atmosphere.SetMood(m);
            ui.SetMood(m);
            AudioDirector.Music(m);
        }

        void UpdateHud()
        {
            // ecuson stânga-sus
            if (safety.Running) ui.Badge.Set("Pătura Moale", "respirăm împreună");
            else if (state == State.Tutorial) ui.Badge.Set("Tutorial cu Lumi", tutorialStep);
            else if (state == State.Debriefing) ui.Badge.Set("Grădina Amintirilor", "cu Lumi");
            else
            {
                float timer = state == State.Memorize && memTask != null ? memTask.Remaining : badgeTimer;
                string ph = state == State.Memorize && memTask != null ? memTask.Title + " · Nivel " + memTask.Level : phaseName;
                ui.Badge.Set(StageName(round), $"Runda {round}/{Rounds}" + (string.IsNullOrEmpty(ph) ? "" : " · " + ph), timer);
            }

            // inventar jos
            var set = CurrentSet;
            if (state == State.Hide)
            {
                var st = new SlotState[Items];
                for (int i = 0; i < Items; i++) st[i] = i < placedCount ? SlotState.Done : (i == placedCount ? SlotState.Active : SlotState.Next);
                ui.Inventory.Show($"Comorile tale · ascunse {placedCount}/{Items}", set, st, "Activ", "Urmează", "Ascuns");
            }
            else if (state == State.Guess)
            {
                var st = new SlotState[Items];
                for (int i = 0; i < Items; i++) st[i] = i < itemIndex ? SlotState.Done : (i == itemIndex ? SlotState.Active : SlotState.Next);
                ui.Inventory.Show($"Unde le-ai ascuns? · {itemIndex}/{Items}", set, st, "Caută", "Urmează", "Marcat");
            }
            else ui.Inventory.Hide();

            // replica lui Lumi
            string line = lumiLine;
            if (state == State.Hide)
            {
                string itemName = Graybox.TreasureLabel(set[Mathf.Min(placedCount, Items - 1)]);
                if (gapWarn) line = $"Hopa, e prea aproape de altă comoară!\n<size=48>Mut-o puțin mai departe.</size>";
                else if (lastSurfaceNotFlat) line = "Suprafața asta nu e plană.\n<size=48>Așază comoara pe o suprafață orizontală: birou, masă, pat.</size>";
                else
                {
                    line = $"Ascunde {itemName}: țintește o suprafață (masă, canapea, raft) și fă pinch.";
                    if (Room == null && round == 1 && placedCount == 0) line += "\n<size=44>(fără Space Setup folosesc un plan virtual)</size>";
                }
            }
            else if (state == State.Guess && itemIndex >= 0 && itemIndex < Items)
            {
                string itemName = Graybox.TreasureLabel(set[itemIndex]);
                line = $"Unde ai ascuns {itemName}? Țintește locul și fă pinch.";
                if (ChaosThisRound) line += "\n<size=44>Cățeii încearcă să te încurce: concentrează-te!</size>";
            }
            else if (state == State.Memorize && memTask != null)
            {
                line = memTask.Status + (string.IsNullOrEmpty(memTask.Hint) ? "" : "\n<size=44>(" + memTask.Hint + ")</size>");
            }
            if (Time.time < noteUntil && !string.IsNullOrEmpty(noteLine) && state != State.Debriefing)
                line = noteLine + (string.IsNullOrEmpty(line) ? "" : "\n<size=44>" + line + "</size>");
            if (!asking) ui.Say(line);

            // debug discret
            bool toastOn = Time.unscaledTime < toastUntil;
            ui.DebugVisible = debugOverlay || toastOn;
            if (ui.DebugVisible)
            {
                var room = Room;
                string dbg = toastOn ? ">> " + toast : "";
                if (debugOverlay)
                    dbg += $"\n{debugInfo}\nMRUK: {(room != null ? "da" : "nu")} · suprafață: {lastSurface} · adâncime: {depthStatus}\n" +
                           $"stare: {state} · mini-joc: {memoryTaskMode} · {audioDir.Summary} · shader: {Graybox.ShaderInfo}";
                ui.SetDebug(dbg.Trim());
            }
            else ui.SetDebug("");
        }

        IEnumerator Transition(string kicker, string title, string body, string button)
        {
            var prev = state;
            if (state != State.Debriefing) state = State.Transition;
            ui.Overlay.Show(kicker, title, body, button);
            while (!ui.Overlay.Done) yield return null;
            ui.Overlay.Hide();
            yield return new WaitForSeconds(0.25f);
            if (state == State.Transition) state = prev;
        }

        // ================================================================ sesiune și runde

        void RestartSession()
        {
            StopAllCoroutines();
            ClearGarden();
            DestroyMemTask();
            ui.Overlay.Hide();
            round = 0;
            asking = false;
            ui.Dialog.Clear();
            CleanupTutorial();
            safetyTriggered = false;
            chaosIntensity = 1f;
            logger.ResetSession();
            ui.Polaroid.Hide();
            noteUntil = 0f;
            for (int i = 0; i < errorsAll.Length; i++) errorsAll[i] = float.NaN;
            StartCoroutine(SessionFlow());
        }

        IEnumerator SessionFlow()
        {
            while (head == null) yield return null;
            if (runTutorial) yield return TutorialFlow();
            while (round < Rounds)
            {
                yield return RoundFlow();
                if (round < Rounds)
                {
                    yield return Transition($"Runda {round}/{Rounds} · {StageName(round)}", "Runda s-a încheiat!",
                        ResultsText(round) + "\nUrmează " + StageName(round + 1) + ".", "Runda următoare");
                }
            }
            yield return DebriefFlow();
        }

        void PrepareRound()
        {
            round++;
            fallbackY = null;
            placedCount = 0;
            itemIndex = 0;
            gapWarn = false;
            badgeTimer = -1f;
            ClearRoundObjects();
            var set = CurrentSet;
            for (int i = 0; i < Items; i++)
            {
                treasures[i] = Graybox.TreasureByType(set[i], false, false);
                treasures[i].SetActive(false);
                paws[i] = Graybox.Paw("Paw_" + set[i], Color.Lerp(Graybox.TreasureColor(set[i]), new Color(0.45f, 0.3f, 0.22f), 0.5f));
                paws[i].SetActive(false);
                errorLines[i] = Graybox.Line("Err_" + set[i], Graybox.TreasureColor(set[i]), 0.006f);
                errorLines[i].enabled = false;
            }
            safety.armed = true;
            SetLumi(true);
            logger.SetMemoryTask("", 0, 0, 0, 0);
        }

        IEnumerator RoundFlow()
        {
            PrepareRound();
            state = State.Intro;
            phaseName = "Bun venit";
            lumiLine = "";
            SetMood(ChaosThisRound ? CozyMood.Chaos : CozyMood.Calm);
            string welcome = ChaosThisRound
                ? "Ai 3 comori în inventar. Ascunde-le pe rând în camera ta.\n<size=44>Azi cățeii sunt plini de energie: Mochi, Gogoașă și Fasolică vor încerca să-ți distragă atenția!</size>"
                : "Ai 3 comori în inventar. Ascunde-le pe rând în camera ta.";
            yield return Transition($"Runda {round}/{Rounds}", $"Bine ai venit în {StageName(round)}!", welcome, "Începe");

            // ---- ascunde
            state = State.Hide;
            phaseName = "Ascunde comorile";
            SetMood(CozyMood.Calm);
            RefreshGhost();
            SprinkleSurfaceStardust();
            while (placedCount < Items) yield return null;
            ClearSurfaceDust();

            // ---- memorare + Poof
            state = State.Poof;
            phaseName = "Memorează";
            lumiLine = "Uită-te bine în jur!\n<size=48>Ține minte unde stă fiecare comoară.</size>";
            for (float t = memorizeSeconds; t > 0f; t -= Time.deltaTime)
            {
                badgeTimer = t;
                yield return null;
            }
            badgeTimer = -1f;
            lumiLine = "Poof!";
            for (int i = 0; i < Items; i++) StartCoroutine(ScaleBounce(treasures[i].transform, 1f, 0f, 0.35f));
            yield return new WaitForSeconds(0.4f);
            for (int i = 0; i < Items; i++)
            {
                SpawnPuff(hiddenPos[i] + Vector3.up * 0.05f);
                AudioDirector.Sfx("poof", hiddenPos[i], 0.9f, Random.Range(0.92f, 1.1f));
                treasures[i].SetActive(false);
            }
            hideTime = Time.time;
            lastGuessMark = Time.time;
            yield return new WaitForSeconds(0.6f);

            int secs = Mathf.RoundToInt(challengeDurationSeconds);
            string duration = secs == 60 ? "1 minut" : secs + " de secunde";   // 90 → „90 de secunde”
            var kind = TaskKindFor(round);
            yield return Transition(StageName(round), "Toate comorile au dispărut!",
                $"Acum urmează o provocare de {duration} ca să-ți testăm memoria spațială.\n<size=44>{(kind == MemoryTaskKind.Bakery ? "Portalul către Brutăria Lunii" : "Constelația Pierdută")}</size>",
                "Începe Provocarea");

            // ---- provocarea de 90 s
            state = State.Memorize;
            phaseName = TaskTitle(kind);
            SetMood(ChaosThisRound ? CozyMood.Chaos : CozyMood.Calm);
            yield return MemoryTaskRoutine();

            yield return Transition(StageName(round), "Timpul a expirat!", "Hai să vedem dacă îți mai amintești unde ai ascuns cele 3 comori.", "Începe Căutarea");

            // ---- haos: cei 3 căței cer atenție pe rând, apoi rămân toți activi
            if (ChaosThisRound)
            {
                state = State.Distract;
                phaseName = "Cățeii vor atenție";
                SetMood(CozyMood.Chaos);
                chaos.bubbleCount = bubbleCount;
                chaos.Begin(head, FloorY(), new List<Vector3>(hiddenPos), new List<string>(CurrentSet), chaosIntensity);
                logger.SetChaosIntensity(chaosIntensity);
                for (int i = 0; i < 3; i++) yield return PetPuppy(i);
                chaos.SpawnGuessExtras();
                chaos.ResetCounters();
            }

            // ---- ghicește
            lumiLine = "";
            state = State.Guess;
            phaseName = "Caută comorile";
            itemIndex = 0;
            lastGuessMark = Time.time;
            while (itemIndex < Items) yield return null;

            // ---- rezultat
            ShowResult();
            yield return new WaitForSeconds(1.6f);
        }

        /// Cățelul i cere atenție: jucătorul trebuie să-l atingă cu raza și să facă pinch / click pe el.
        IEnumerator PetPuppy(int i)
        {
            if (i < 0 || i >= ChaosBehaviours.PuppyNames.Length) yield break;
            chaos.SpawnPuppy(i);
            petIndex = i;
            petClicked = false;
            petHover = false;
            string name = ChaosBehaviours.PuppyNames[i];
            string role = ChaosBehaviours.PuppyRoles[i];
            while (!petClicked)
            {
                var pt = chaos.Puppy(i);
                if (pt == null)
                {
                    if (!chaos.Running) { petIndex = -1; yield break; }
                    chaos.SpawnPuppy(i);
                    yield return null;
                    continue;
                }
                var to = pt.position + Vector3.up * 0.15f - head.position;
                float angle = Vector3.Angle(head.forward, to);
                string where = angle < 35f ? "în fața ta" : angle > 100f ? "în spatele tău" : (Vector3.Dot(head.right, to) > 0 ? "în dreapta" : "în stânga");
                lumiLine = petHover
                    ? $"Acum! Fă pinch ca să-l mângâi pe {name}."
                    : $"{name} „{role}” vrea atenție ({i + 1}/3)!\n<size=48>E {where}: atinge-l cu raza și fă pinch / click.</size>";
                yield return null;
            }
            petIndex = -1;
            chaos.Highlight(i, false);
            chaos.MarkPetted(i);
            chaos.Celebrate(i);
            lumiLine = $"{name} e fericit și dă din codiță! ({i + 1}/3)";
            yield return new WaitForSeconds(0.9f);
        }

        void Note(string text, float seconds)
        {
            noteLine = text;
            noteUntil = Time.time + seconds;
        }

        /// Verifică scanarea camerei (MRUK): presară praf de stele pe mobilierul scanat (masă, birou, canapea, pat,
        /// dulăpioare). Fără Space Setup → modul fallback: un plan virtual sub mâini, cu praf de stele pe el.
        void SprinkleSurfaceStardust()
        {
            ClearSurfaceDust();
            var room = Room;
            int found = 0;
            if (room != null && room.Anchors != null)
            {
                var wanted = MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.BED | MRUKAnchor.SceneLabels.STORAGE;
                foreach (var a in room.Anchors)
                {
                    if (a == null || !a.HasAnyLabel(wanted) || found >= 10) continue;
                    if (!AnchorTop(a, out Vector3 c, out Vector2 size)) continue;
                    surfaceDust.Add(StardustPatch(c, size));
                    found++;
                }
            }
            if (found > 0)
            {
                Note($"Am găsit {found} " + (found == 1 ? "suprafață scanată" : "suprafețe scanate") + " în camera ta: le-am presărat cu praf de stele!", 6f);
                logger.LogEvent("room_scan", "mruk surfaces=" + found);
                return;
            }
            // fallback: planul virtual pe care se ascund comorile când nu există Space Setup
            var f = head.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            f.Normalize();
            float y = fallbackY ?? (head.position.y - fallbackSurfaceBelowHead);
            surfaceDust.Add(StardustPatch(new Vector3(head.position.x, y, head.position.z) + f * 0.9f, new Vector2(1.4f, 1.0f)));
            Note(room == null
                ? "Nu găsesc o cameră scanată (Space Setup), așa că rulez în modul fallback: ascunde comorile pe planul virtual presărat cu praf de stele."
                : "Camera e scanată, dar nu văd mobilier: am presărat praf de stele pe un plan virtual în fața ta.", 7f);
            logger.LogEvent("room_scan", room == null ? "fallback (no MRUK room)" : "fallback (no furniture anchors)");
        }

        /// Partea de sus a unei ancore MRUK (volum sau plan) ca centru + dimensiuni în lume.
        static bool AnchorTop(MRUKAnchor a, out Vector3 center, out Vector2 size)
        {
            center = default;
            size = default;
            var pts = new List<Vector3>();
            if (a.VolumeBounds.HasValue)
            {
                var b = a.VolumeBounds.Value;
                for (int i = 0; i < 8; i++)
                    pts.Add(a.transform.TransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z)));
            }
            else if (a.PlaneRect.HasValue)
            {
                var r = a.PlaneRect.Value;
                pts.Add(a.transform.TransformPoint(new Vector3(r.xMin, r.yMin, 0f)));
                pts.Add(a.transform.TransformPoint(new Vector3(r.xMax, r.yMin, 0f)));
                pts.Add(a.transform.TransformPoint(new Vector3(r.xMin, r.yMax, 0f)));
                pts.Add(a.transform.TransformPoint(new Vector3(r.xMax, r.yMax, 0f)));
            }
            if (pts.Count == 0) return false;
            var min = pts[0];
            var max = pts[0];
            foreach (var q in pts)
            {
                min = Vector3.Min(min, q);
                max = Vector3.Max(max, q);
            }
            size = new Vector2(max.x - min.x, max.z - min.z);
            if (size.x < 0.15f || size.y < 0.15f) return false;
            center = new Vector3((min.x + max.x) * 0.5f, max.y, (min.z + max.z) * 0.5f);
            return true;
        }

        GameObject StardustPatch(Vector3 top, Vector2 size)
        {
            var go = new GameObject("SurfaceStardust");
            go.transform.position = top + Vector3.up * 0.02f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.04f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.86f, 0.45f), new Color(1f, 0.97f, 0.85f));
            main.gravityModifier = -0.01f;
            main.maxParticles = 160;
            var em = ps.emission;
            em.rateOverTime = Mathf.Clamp(size.x * size.y * 40f, 8f, 50f);
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(Mathf.Min(size.x, 2.5f), 0.01f, Mathf.Min(size.y, 2.5f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = CozyFx.FadeGradient(Color.white, Color.white);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = CozyFx.ParticleMat();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            CozyFx.Burst(top + Vector3.up * 0.03f, new Color(1f, 0.88f, 0.5f), 18, 0.35f, 0.018f);
            return go;
        }

        void ClearSurfaceDust()
        {
            for (int i = 0; i < surfaceDust.Count; i++) if (surfaceDust[i] != null) Destroy(surfaceDust[i]);
            surfaceDust.Clear();
        }

        void ClearResultLabels()
        {
            for (int i = 0; i < resultLabels.Count; i++) if (resultLabels[i] != null) Destroy(resultLabels[i]);
            resultLabels.Clear();
        }

        /// 1 lăbuță = 10 cm (Mathf.RoundToInt(cm / 10)).
        static string PawText(float cm)
        {
            int n = Mathf.RoundToInt(cm / 10f);
            if (n <= 0) return "sub o lăbuță";
            return n == 1 ? "1 lăbuță" : n + " lăbuțe";
        }

        void ClearRoundObjects()
        {
            ClearSurfaceDust();
            ClearResultLabels();
            for (int i = 0; i < Items; i++)
            {
                if (treasures[i] != null) Destroy(treasures[i]);
                if (paws[i] != null) Destroy(paws[i]);
                if (errorLines[i] != null) Destroy(errorLines[i].gameObject);
                treasures[i] = null;
                paws[i] = null;
                errorLines[i] = null;
            }
            if (ghost != null) Destroy(ghost);
            ghost = null;
            if (chaos != null) chaos.Clear();
            for (int i = 0; i < puffs.Count; i++)
                if (puffs[i] != null) Destroy(puffs[i]);
            puffs.Clear();
        }

        void RefreshGhost()
        {
            if (ghost != null) Destroy(ghost);
            ghost = Graybox.TreasureByType(CurrentSet[Mathf.Min(placedCount, Items - 1)], true, false);
            ghost.SetActive(false);
        }

        bool TooClose(Vector3 p)
        {
            float gap = Application.isEditor && !UnityEngine.XR.XRSettings.isDeviceActive ? minTreasureGapEditor : minTreasureGapMR;
            for (int i = 0; i < placedCount; i++)
                if (Vector3.Distance(hiddenPos[i], p) < gap) return true;
            return false;
        }

        void PlaceTreasure(Vector3 point, Quaternion facing)
        {
            int i = placedCount;
            if (i < 0 || i >= Items || treasures[i] == null) return;
            hiddenPos[i] = point;
            hiddenSurface[i] = lastSurface;
            treasures[i].transform.SetPositionAndRotation(point, facing);
            treasures[i].transform.localScale = Vector3.one;
            treasures[i].SetActive(true);
            StartCoroutine(ScaleBounce(treasures[i].transform, 0.4f, 1f, 0.3f));
            atmosphere.Sparkle(point, Graybox.TreasureColor(CurrentSet[i]));
            AudioDirector.Sfx("place", point, 0.9f, 0.95f + 0.08f * i);
            placedCount++;
            gapWarn = false;
            if (placedCount >= Items)
            {
                if (ghost != null) Destroy(ghost);
                ghost = null;
            }
            else RefreshGhost();
        }

        // ---------------------------------------------------------------- mini-jocuri de memorie

        MemoryTaskKind TaskKindFor(int rd)
        {
            switch (memoryTaskMode)
            {
                case MemoryTaskMode.Constellation: return MemoryTaskKind.Constellation;
                case MemoryTaskMode.Bakery: return MemoryTaskKind.Bakery;
                default: return rd % 2 == 1 ? MemoryTaskKind.Constellation : MemoryTaskKind.Bakery;
            }
        }

        static string TaskName(MemoryTaskKind k) => k == MemoryTaskKind.Constellation ? "Constelația" : "Brutăria";
        static string TaskTitle(MemoryTaskKind k) => k == MemoryTaskKind.Constellation ? "Constelația Pierdută" : "Brutăria Lunii";

        static MemoryTaskKind Other(MemoryTaskKind k) => k == MemoryTaskKind.Constellation ? MemoryTaskKind.Bakery : MemoryTaskKind.Constellation;

        void CreateMemTask(MemoryTaskKind kind, float seconds)
        {
            memTaskKind = kind;
            phaseName = TaskTitle(kind);
            if (kind == MemoryTaskKind.Bakery)
            {
                memTaskHost = new GameObject("BakeryTaskHost");
                memTask = memTaskHost.AddComponent<MoonBakeryTask>();
            }
            else
            {
                memTaskHost = new GameObject("StarTaskHost");
                memTask = memTaskHost.AddComponent<StarConstellationTask>();
            }
            memTask.Begin(head, new List<Vector3>(hiddenPos), seconds);
        }

        void DestroyMemTask()
        {
            if (memTask != null)
            {
                // nivelul maxim și secvența maximă atinse merg pe rândurile de trial ale rundei
                logger.SetMemoryTask(memTask.Title, memTask.MaxLevel, memTask.MaxSequenceLength, memTask.CorrectSequences, memTask.Mistakes);
                memTask.End();
            }
            if (memTaskHost != null) Destroy(memTaskHost);
            memTask = null;
            memTaskHost = null;
        }

        IEnumerator MemoryTaskRoutine()
        {
            float budget = challengeDurationSeconds;
            var kind = TaskKindFor(round);
            while (true)
            {
                CreateMemTask(kind, budget);
                while (!memTask.Finished && !memTaskSwitch) yield return null;
                if (memTaskSwitch && !memTask.Finished)
                {
                    // comutare instant din tastatură: continuăm cu timpul rămas
                    memTaskSwitch = false;
                    budget = Mathf.Max(8f, memTask.Remaining);
                    var next = TaskKindFor(round);
                    if (next == memTaskKind) next = Other(memTaskKind);
                    DestroyMemTask();
                    kind = next;
                    continue;
                }
                break;
            }
            memTaskSwitch = false;
            yield return new WaitForSeconds(0.6f);
            DestroyMemTask();
        }

        /// Scurtături: 1 = Constelația, 2 = Brutăria, 0 = alternativ, Tab = comută, F1 = debug.
        void HandleShortcuts()
        {
#if ENABLE_INPUT_SYSTEM
            if (!keyboardShortcuts || Keyboard.current == null) return;
            var kb = Keyboard.current;
            if (kb.f1Key.wasPressedThisFrame) debugOverlay = !debugOverlay;
            MemoryTaskMode? m = null;
            bool active = memTask != null && !memTask.Finished;
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) m = MemoryTaskMode.Constellation;
            else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) m = MemoryTaskMode.Bakery;
            else if (kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame) m = MemoryTaskMode.BothAlternating;
            else if (kb.tabKey.wasPressedThisFrame)
            {
                if (active) m = Other(memTaskKind) == MemoryTaskKind.Bakery ? MemoryTaskMode.Bakery : MemoryTaskMode.Constellation;
                else m = (MemoryTaskMode)(((int)memoryTaskMode + 1) % 3);
            }
            if (m == null) return;
            memoryTaskMode = m.Value;
            string modeName = memoryTaskMode == MemoryTaskMode.BothAlternating ? "alternativ (R1 Constelația, R2 Brutăria)" : TaskName(TaskKindFor(round));
            ShowToast("Mini-joc: " + modeName);
            if (active && TaskKindFor(round) != memTaskKind) memTaskSwitch = true;
#endif
        }

        void ShowToast(string text)
        {
            toast = text;
            toastUntil = Time.unscaledTime + 2.5f;
            Debug.Log("[MindThePaws] " + text);
        }

        // ---------------------------------------------------------------- ghicire și rezultat

        Vector3 MeanHidden()
        {
            var m = Vector3.zero;
            for (int i = 0; i < Items; i++) m += hiddenPos[i];
            return m / Items;
        }

        void GuessItem(Vector3 guess)
        {
            int i = itemIndex;
            if (i < 0 || i >= Items || paws[i] == null) return;
            guessPos[i] = guess;
            paws[i].transform.SetPositionAndRotation(guess, YawTowardsHead(guess));
            paws[i].SetActive(true);
            StartCoroutine(ScaleBounce(paws[i].transform, 0.3f, 1f, 0.25f));
            AudioDirector.Sfx("place", guess, 0.7f, 0.85f);
            CozyFx.Burst(guess + Vector3.up * 0.02f, new Color(0.95f, 0.8f, 0.65f), 10, 0.25f, 0.016f);

            float err = Vector3.Distance(hiddenPos[i], guess);
            var d = hiddenPos[i] - guess; d.y = 0f;
            float errH = d.magnitude;
            float angle = Vector3.Angle(hiddenPos[i] - head.position, guess - head.position);
            chaos.TakeCounters(out int dc, out float ds);

            logger.LogTrial(round, i + 1, CurrentSet[i], ChaosThisRound ? "chaos" : "calm", hiddenSurface[i],
                hiddenPos[i], guess, err, errH, angle,
                Time.time - hideTime, Time.time - lastGuessMark,
                dc, ds, safetyTriggered);
            lastGuessMark = Time.time;
            int ei = ErrIndex(round, i);
            if (ValidErr(ei)) errorsAll[ei] = err;
            itemIndex++;
        }

        string ResultsText(int rd)
        {
            rd = Mathf.Clamp(rd, 1, Rounds);
            var set = SetFor(rd);
            float sum = 0f;
            int n = 0;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Items; i++)
            {
                int ei = ErrIndex(rd, i);
                float e = ValidErr(ei) ? errorsAll[ei] : float.NaN;
                if (i > 0) sb.Append("  ·  ");
                sb.Append(Graybox.TreasureLabel(set[i])).Append(' ');
                if (float.IsNaN(e)) sb.Append("–");
                else
                {
                    sb.Append((e * 100f).ToString("0")).Append(" cm (").Append(PawText(e * 100f)).Append(')');
                    sum += e;
                    n++;
                }
            }
            if (n == 0) return "Nu avem încă rezultate pentru această rundă.";
            float mean = sum / n;
            float pawsN = mean / Mathf.Max(0.01f, pawSizeMeters);
            string pawsTxt = PawText(mean * 100f);
            return $"Ai fost în medie la {pawsTxt} distanță ({mean * 100f:0} cm).\n<size=44>{sb}</size>";
        }

        void ShowResult()
        {
            state = State.Result;
            phaseName = "Rezultat";
            pawGhost.SetActive(false);
            aimLine.enabled = false;
            reticle.SetActive(false);
            chaos.Clear();
            SetMood(CozyMood.Calm);
            AudioDirector.Sfx2D("chime", 0.6f);
            for (int i = 0; i < Items; i++)
            {
                treasures[i].transform.SetPositionAndRotation(hiddenPos[i], YawTowardsHead(hiddenPos[i]));
                treasures[i].SetActive(true);
                StartCoroutine(ScaleBounce(treasures[i].transform, 0f, 1f, 0.35f));
                atmosphere.Sparkle(hiddenPos[i], Graybox.TreasureColor(CurrentSet[i]));
                errorLines[i].enabled = true;
                errorLines[i].SetPosition(0, guessPos[i] + Vector3.up * 0.01f);
                errorLines[i].SetPosition(1, hiddenPos[i] + Vector3.up * 0.01f);
                int ei = ErrIndex(round, i);
                float cm = (ValidErr(ei) ? errorsAll[ei] : 0f) * 100f;
                var lbl = CozyUI.Label("ErrorLabel_" + i, $"{Graybox.TreasureLabel(CurrentSet[i])}\n{cm:0} cm · {PawText(cm)}", 24, CozyUI.Ink, Color.white, Graybox.TreasureColor(CurrentSet[i]));
                lbl.transform.position = (guessPos[i] + hiddenPos[i]) * 0.5f + Vector3.up * 0.14f;
                lbl.faceTarget = head;
                resultLabels.Add(lbl.gameObject);
            }
            lumiLine = "Uite unde erau comorile!\n<size=48>Liniile arată cât de aproape ai fost (1 lăbuță = 10 cm).</size>";
        }

        // ---------------------------------------------------------------- siguranță

        void OnSafetyStart()
        {
            safetyTriggered = true;
            Time.timeScale = 0f;
            chaos.Freeze();
            if (memTask != null) memTask.SetVisible(false);
            moodBeforeSafety = mood;
            SetMood(CozyMood.Blanket);
            AudioDirector.Sfx2D("purr", 0.5f);
            ui.HideForSafety(true);
            if (lumi != null) lumi.SetActive(false);
            aimLine.enabled = false;
            reticle.SetActive(false);
            if (ghost != null) ghost.SetActive(false);
            pawGhost.SetActive(false);
        }

        /// 1 = Continuă (aceeași intensitate), 2 = Mergi la Debriefing, 3 = Haos mai blând (o treaptă mai puțin).
        void OnSafetyFinish(int choice)
        {
            Time.timeScale = 1f;
            ui.HideForSafety(false);
            if (lumi != null) lumi.SetActive(true);
            lumiSnap = true;
            string name = choice == SafetyBlanket.ChoiceDebrief ? "debrief" : (choice == SafetyBlanket.ChoiceGentler ? "gentler" : "continue");
            if (choice == SafetyBlanket.ChoiceGentler) chaosIntensity = Mathf.Max(0.25f, chaosIntensity - 0.25f);
            logger.AddSafetyChoice(state == State.Tutorial ? "tutorial_" + name : name, chaosIntensity);
            if (state == State.Tutorial)
            {
                // în tutorial, Pătura Moale e doar exersată: ne întoarcem mereu la Lumi
                tutorialSafetyTested = true;
                SetMood(moodBeforeSafety);
                return;
            }
            if (choice == SafetyBlanket.ChoiceDebrief)
            {
                EnterDebriefing();
                return;
            }
            if (choice == SafetyBlanket.ChoiceGentler)
                Note($"Am făcut haosul mai blând (treapta {Mathf.RoundToInt(chaosIntensity * 4f)}/4). Ești în siguranță.", 5f);
            if (memTask != null) memTask.SetVisible(true);
            if (chaos.Running) chaos.Resume(chaosIntensity);
            SetMood(moodBeforeSafety);
        }

        // ---------------------------------------------------------------- tutorial

        IEnumerator TutorialFlow()
        {
            state = State.Tutorial;
            tutorialStep = "Ceai cu Lumi";
            SetMood(CozyMood.Calm);
            safety.armed = true;
            asking = true;
            bool skip = false;

            // ---- pasul 0: Lumi iese din ceainic
            var table = TablePoint(1.1f);
            kettle = Graybox.Kettle("Kettle");
            kettle.transform.SetPositionAndRotation(table, YawTowardsHead(table));
            StartCoroutine(ScaleBounce(kettle.transform, 0f, 1f, 0.5f));
            AudioDirector.Sfx("place", table, 0.6f, 0.8f);
            yield return new WaitForSeconds(0.9f);
            yield return LumiFromKettle();

            ui.Dialog.Show("Lumi · Bun venit!",
                "Bună! Eu sunt Lumi, zâna din ceainic. Înainte de joc, hai să bem un ceai cald și să exersăm puțin împreună!",
                new[] { "Hai să începem!", "Sari peste tutorial" }, true, CozyThemeArt.KettleIcon);
            while (ui.Dialog.Chosen < 0) yield return null;
            skip = ui.Dialog.Chosen == 1;

            // ---- ceaiul și pulsul de calm (baseline)
            if (!skip)
            {
                tutorialStep = "Ceai cu Lumi";
                var right = Vector3.Cross(Vector3.up, (table - head.position).normalized);
                var cupPos = table + right.normalized * 0.24f;
                teaCup = Graybox.TeaCup("TeaCup");
                teaCup.transform.SetPositionAndRotation(cupPos, YawTowardsHead(cupPos));
                StartCoroutine(ScaleBounce(teaCup.transform, 0f, 1f, 0.4f));
                CozyFx.Burst(cupPos + Vector3.up * 0.06f, new Color(1f, 0.85f, 0.5f), 12, 0.25f, 0.015f);
                yield return HoldTeaCalibration(table, cupPos);
                if (ui.Dialog.Chosen == 0) skip = true;
                ClearTeaSet();
            }

            // ---- pasul 1: Bop the Snoot (țintire + pinch)
            if (!skip)
            {
                tutorialStep = "Bop the Snoot";
                SpawnTutorialPup();
                int bops = 0;
                ui.Dialog.Show("Tutorial · Bop the Snoot", SnootText(0), new[] { "Sari peste tutorial" }, false);
                while (bops < snootBops)
                {
                    if (ui.Dialog.Chosen == 0) { skip = true; break; }
                    if (snootBopped)
                    {
                        snootBopped = false;
                        bops++;
                        var at = snootMarker.position;
                        CozyFx.Hearts(at + Vector3.up * 0.05f, 12, 0.04f);
                        CozyFx.Burst(at, new Color(1f, 0.85f, 0.45f), 12, 0.3f, 0.016f);
                        ChaosBehaviours.Celebrate(tutorialPup.transform, 1.35f + 0.1f * bops);
                        AudioDirector.Sfx("chime", at, 0.6f, 1f + 0.15f * bops);
                        ui.Dialog.SetText("Tutorial · Bop the Snoot", SnootText(bops));
                        if (bops < snootBops) yield return HopTutorialPup();
                    }
                    yield return null;
                }
                if (!skip)
                {
                    ui.Dialog.SetText("Tutorial · Bop the Snoot", "Perfect! Așa țintești și selectezi orice în joc: raza + pinch (sau click stânga).");
                    yield return new WaitForSeconds(1.4f);
                }
                CleanupTutorial();
            }

            // ---- pasul 2: Pătura Moale
            if (!skip)
            {
                tutorialStep = "Pătura Moale";
                tutorialSafetyTested = false;
                ui.Dialog.Show("Tutorial · Pătura Moale",
                    "Dacă vreodată simți că sunt prea mulți stimuli, ridică palma stângă spre față (sau apasă Space / butonul B) ca să activezi Pătura Moale.\n<size=44>Încearcă o dată acum, sau începe direct Runda 1.</size>",
                    new[] { "Am înțeles, începe Runda 1" }, true, CozyThemeArt.HeartIcon);
                bool praised = false;
                while (ui.Dialog.Chosen < 0)
                {
                    if (tutorialSafetyTested && !praised)
                    {
                        praised = true;
                        ui.Dialog.SetText("Tutorial · Pătura Moale",
                            "Bravo! Pătura Moale e mereu aici pentru tine: jocul se oprește, respirăm împreună și alegi tu cum continuăm.");
                    }
                    yield return null;
                }
            }

            CleanupTutorial();
            asking = false;
            ui.Dialog.Clear();
            tutorialStep = "";
            if (lumi != null && !lumi.activeSelf) SetLumi(true);
        }

        /// Un punct pe masă / suprafață la ~distance m în fața jucătorului (altfel planul virtual de sub cap).
        Vector3 TablePoint(float distance)
        {
            var f = head.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            f.Normalize();
            var above = head.position + f * distance + Vector3.up * 0.2f;
            if (TryHitSurface(new Ray(above, Vector3.down), out Vector3 p, out _, out _) && p.y < head.position.y - 0.25f && p.y > head.position.y - 1.4f)
                return p;
            return new Vector3(above.x, head.position.y - fallbackSurfaceBelowHead, above.z);
        }

        IEnumerator LumiFromKettle()
        {
            var lid = kettle != null ? FindChild(kettle.transform, "Lid") : null;
            var top = kettle != null ? kettle.transform.position + Vector3.up * 0.2f : head.position + head.forward;
            AudioDirector.Sfx("chime", top, 0.8f, 0.9f);
            AudioDirector.Sfx("grow", top, 0.5f, 1.1f);
            CozyFx.Burst(top, new Color(1f, 0.85f, 0.4f), 26, 0.4f, 0.02f);
            Vector3 lid0 = lid != null ? lid.localPosition : Vector3.zero;
            Quaternion lidR0 = lid != null ? lid.localRotation : Quaternion.identity;
            for (float e = 0f; e < 0.7f; e += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, e / 0.7f);
                if (lid != null)
                {
                    lid.localPosition = lid0 + new Vector3(-0.04f * k, 0.1f * k, 0f);
                    lid.localRotation = lidR0 * Quaternion.Euler(0f, 0f, 35f * k);
                }
                if (Random.value < 0.4f) CozyFx.Burst(top, new Color(1f, 0.88f, 0.5f), 3, 0.2f, 0.014f);
                yield return null;
            }

            // Lumi iese și zboară lin în locul ei din stânga
            lumi.SetActive(true);
            lumi.transform.localScale = Vector3.one * 0.15f;
            lumiFollow = false;
            var start = top;
            for (float e = 0f; e < 1.4f; e += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, e / 1.4f);
                var end = LumiHudTarget();
                var mid = (start + end) * 0.5f + Vector3.up * 0.25f;
                var p = (1 - k) * (1 - k) * start + 2f * (1 - k) * k * mid + k * k * end;
                lumi.transform.position = p;
                lumi.transform.localScale = Vector3.one * Mathf.Lerp(0.15f, 1f, k);
                lumi.transform.rotation = YawTowardsHead(p);
                if (Random.value < 0.5f) CozyFx.Burst(p, new Color(1f, 0.9f, 0.6f), 2, 0.1f, 0.012f);
                yield return null;
            }
            lumi.transform.localScale = Vector3.one;
            lumiFollow = true;
            lumiSnap = false;
            CozyFx.Burst(lumi.transform.position, new Color(0.9f, 0.8f, 1f), 14, 0.3f, 0.016f);

            // capacul revine
            for (float e = 0f; e < 0.5f; e += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, e / 0.5f);
                if (lid != null)
                {
                    lid.localPosition = Vector3.Lerp(lid0 + new Vector3(-0.04f, 0.1f, 0f), lid0, k);
                    lid.localRotation = Quaternion.Slerp(lidR0 * Quaternion.Euler(0f, 0f, 35f), lidR0, k);
                }
                yield return null;
            }
            if (lid != null) AudioDirector.Sfx("place", lid.position, 0.4f, 1.3f);
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        const string HoldHint = "Ține apăsat pe ceașca de ceai (pinch ținut sau click stânga ținut) ca s-o bei încet. Între timp îți ascult ritmul de calm.";
        const string HoldAgain = "Ține ceașca puțin mai mult ca să-ți pot măsura ritmul de calm...";

        /// Hold-to-Drink / Hold-to-Measure: jucătorul ține ceașca apăsată ~4.5 s. Cât timp o ține, ceașca plutește spre el
        /// cu aburi-inimioare, inimioara bate și bara se umple; dacă îi dă drumul, ceașca revine pe masă și progresul scade lin.
        IEnumerator HoldTeaCalibration(Vector3 table, Vector3 cupHome)
        {
            tutorialStep = "Ritmul de calm";
            ui.Dialog.Show("Ceai cu Lumi", HoldHint, new[] { "Sari peste tutorial" }, false, CozyThemeArt.CupIcon);
            var widgetPos = table + Vector3.up * 0.42f;
            ui.Calibration.Show(widgetPos, head);
            cupActive = true;
            cupHolding = false;
            float progress = 0f;
            bool external = ExternalHeartRate != null;
            float target = Random.Range(63f, 72f);
            float shown = target + Random.Range(6f, 10f);
            float sum = 0f;
            int n = 0;
            bool wasHolding = false;
            bool everHeld = false;
            float steamTimer = 0f;
            var t = teaCup != null ? teaCup.transform : null;
            var homeRot = t != null ? t.rotation : Quaternion.identity;

            while (progress < 1f)
            {
                if (ui.Dialog.Chosen == 0 || t == null)
                {
                    cupActive = false;
                    cupHolding = false;
                    ui.Calibration.Hide();
                    yield break;
                }
                float dt = Time.deltaTime;
                bool holding = cupHolding;
                if (holding)
                {
                    everHeld = true;
                    progress = Mathf.Min(1f, progress + dt / Mathf.Max(0.5f, teaHoldSeconds));
                    var f = head.forward; f.y = 0f;
                    if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
                    var near = head.position + f.normalized * 0.45f + Vector3.down * 0.24f;
                    var goal = Vector3.Lerp(cupHome, near, 0.8f) + Vector3.up * Mathf.Sin(Time.time * 2.4f) * 0.008f;
                    t.position = Vector3.Lerp(t.position, goal, 1f - Mathf.Exp(-3f * dt));
                    t.rotation = Quaternion.Slerp(t.rotation, homeRot * Quaternion.Euler(-12f, 0f, 0f), 1f - Mathf.Exp(-4f * dt));
                    steamTimer -= dt;
                    if (steamTimer <= 0f)
                    {
                        steamTimer = 0.28f;
                        CozyFx.Hearts(t.position + Vector3.up * 0.09f, 2, 0.022f);   // aburi în formă de inimioară
                    }

                    float live = -1f;
                    if (external)
                    {
                        try { live = ExternalHeartRate(); } catch { live = -1f; }
                    }
                    if (live > 0f)
                    {
                        shown = Mathf.Lerp(shown, live, 1f - Mathf.Exp(-3f * dt));
                        sum += live;
                        n++;
                    }
                    else shown = Mathf.Lerp(shown, target + Mathf.Sin(Time.time * 1.3f) * 1.2f, 1f - Mathf.Exp(-1.2f * dt));

                    ui.Dialog.SetText("Ceai cu Lumi", "Bea încet... Așa! Ține ceașca în continuare.");
                    ui.Calibration.Set(progress, shown, $"Se calibrează ritmul tău de calm... ({Mathf.RoundToInt(shown)} BPM)");
                }
                else
                {
                    progress = Mathf.Max(0f, progress - dt * 0.22f);
                    t.position = Vector3.Lerp(t.position, cupHome, 1f - Mathf.Exp(-4f * dt));
                    t.rotation = Quaternion.Slerp(t.rotation, homeRot, 1f - Mathf.Exp(-5f * dt));
                    if (wasHolding) AudioDirector.Sfx("oops", t.position, 0.25f, 1.4f);
                    ui.Dialog.SetText("Ceai cu Lumi", everHeld ? HoldAgain : HoldHint);
                    ui.Calibration.Set(progress, shown, progress > 0.01f
                        ? $"Ține ceașca ca să continui... ({Mathf.RoundToInt(progress * 100f)}%)"
                        : "Ține apăsat pe ceașcă ca să începi măsurarea");
                }
                wasHolding = holding;
                yield return null;
            }

            cupActive = false;
            cupHolding = false;
            float bpm = n > 0 ? sum / n : Mathf.Round(shown);
            logger.SetBaseline(bpm, n > 0 ? "external" : "simulated");
            ui.Calibration.Set(1f, bpm, $"Gata! Ritmul tău de calm: {Mathf.RoundToInt(bpm)} BPM");
            ui.Dialog.SetText("Ceai cu Lumi", "Mulțumesc! Acum știu cum arată liniștea ta. O să ne ajute mai târziu.");
            CozyFx.Hearts(widgetPos, 14, 0.04f);
            AudioDirector.Sfx("grow", widgetPos, 0.5f, 1.2f);
            AudioDirector.Sfx("chime", t.position, 0.6f, 0.8f);
            for (float e = 0f; e < 0.6f; e += Time.deltaTime)
            {
                if (t == null) break;
                t.position = Vector3.Lerp(t.position, cupHome, 1f - Mathf.Exp(-6f * Time.deltaTime));
                t.rotation = Quaternion.Slerp(t.rotation, homeRot, 1f - Mathf.Exp(-6f * Time.deltaTime));
                yield return null;
            }
            yield return new WaitForSeconds(1.4f);
            ui.Calibration.Hide();
        }

        void ClearTeaSet()
        {
            cupActive = false;
            cupClicked = false;
            cupHolding = false;
            if (teaCup != null)
            {
                SpawnPuff(teaCup.transform.position + Vector3.up * 0.04f);
                Destroy(teaCup);
            }
            if (kettle != null)
            {
                SpawnPuff(kettle.transform.position + Vector3.up * 0.1f);
                AudioDirector.Sfx("poof", kettle.transform.position, 0.6f, 1.2f);
                Destroy(kettle);
            }
            teaCup = null;
            kettle = null;
            if (ui != null) ui.Calibration.Hide();
        }

        string SnootText(int bops)
        {
            if (bops == 0)
                return "El e Mochi! Țintește-i nasul strălucitor cu raza mâinii (sau cu mouse-ul) și fă pinch / click stânga.\n<size=44>Bop the Snoot: 0/" + snootBops + "</size>";
            if (bops < snootBops)
                return "Boop! Lui Mochi îi place! S-a mutat puțin: mai fă o dată.\n<size=44>Bop the Snoot: " + bops + "/" + snootBops + "</size>";
            return "Boop! Mochi e în culmea fericirii!\n<size=44>Bop the Snoot: " + bops + "/" + snootBops + "</size>";
        }

        Vector3 TutorialSpot(float lateral, float depth)
        {
            var f = head.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            f.Normalize();
            var r = Vector3.Cross(Vector3.up, f);
            return new Vector3(head.position.x, FloorY(), head.position.z) + f * depth + r * lateral;
        }

        void SpawnTutorialPup()
        {
            CleanupTutorial();
            tutorialPup = Graybox.Corgi("Mochi_Tutorial", "corgi_mochi");
            var pos = TutorialSpot(0f, 1.2f);
            tutorialPup.transform.SetPositionAndRotation(pos, YawTowardsHead(pos));
            var tag = CozyUI.Label("NameTag_Mochi", "Mochi", 26, CozyUI.Ink, new Color(1f, 1f, 1f, 0.9f), ChaosBehaviours.CollarColors[0]);
            tag.transform.SetParent(tutorialPup.transform, false);
            tag.follow = tutorialPup.transform;
            tag.followOffset = new Vector3(0f, 0.42f, 0f);
            tag.faceTarget = head;

            // marcajul strălucitor de pe nas
            var m = new GameObject("SnootMarker");
            m.transform.SetParent(tutorialPup.transform, false);
            m.transform.localPosition = new Vector3(0f, 0.2f, 0.276f);   // vârful năsucului (corgi chibi)
            Graybox.Prim(PrimitiveType.Sphere, m.transform, Vector3.zero, Vector3.one * 0.028f, Graybox.MakeMat(new Color(1f, 0.6f, 0.75f)));
            CozyAnimator.Attach(m, CozyMotion.Pulse, 2f);
            snootMarker = m.transform;
            snootHalo = CozyFx.Halo(tutorialPup.transform, m.transform.position, 0.12f, new Color(1f, 0.85f, 0.5f, 0.5f), out snootHaloMat);
            var sp = CozyFx.Sparkler(m.transform, new Color(1f, 0.85f, 0.55f), 6f, 0.02f);
            sp.transform.localPosition = Vector3.zero;
            CozyFx.Burst(pos + Vector3.up * 0.2f, ChaosBehaviours.CollarColors[0], 16, 0.35f, 0.02f);
            AudioDirector.Sfx("bark", pos + Vector3.up * 0.2f, 0.6f, 1.3f);
            snootActive = true;
            snootBopped = false;
        }

        IEnumerator HopTutorialPup()
        {
            snootActive = false;
            if (tutorialPup == null) yield break;
            var t = tutorialPup.transform;
            var from = t.position;
            var to = TutorialSpot(Random.Range(-0.45f, 0.45f), Random.Range(1.0f, 1.4f));
            if ((to - from).magnitude < 0.2f) to = TutorialSpot(from.x > head.position.x ? -0.35f : 0.35f, 1.2f);
            yield return new WaitForSeconds(0.35f);
            for (float e = 0f; e < 0.5f; e += Time.deltaTime)
            {
                if (t == null) yield break;
                float k = e / 0.5f;
                t.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, k)) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.15f;
                var d = to - from; d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(d.normalized, Vector3.up), 10f * Time.deltaTime);
                yield return null;
            }
            if (t == null) yield break;
            t.position = to;
            for (float e = 0f; e < 0.3f; e += Time.deltaTime)
            {
                if (t == null) yield break;
                t.rotation = Quaternion.Slerp(t.rotation, YawTowardsHead(to), 12f * Time.deltaTime);
                yield return null;
            }
            snootBopped = false;
            snootActive = true;
        }

        void UpdateTutorialFx()
        {
            if (snootHalo == null || snootMarker == null) return;
            snootHalo.position = snootMarker.position;
            var d = snootHalo.position - head.position;
            if (d.sqrMagnitude > 1e-6f) snootHalo.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            float s = (snootHover ? 0.2f : 0.13f) * (1f + 0.12f * Mathf.Sin(Time.time * 5f));
            snootHalo.localScale = Vector3.one * s;
            CozyFx.SetColor(snootHaloMat, new Color(1f, 0.85f, 0.5f, snootActive ? (snootHover ? 0.8f : 0.5f) : 0.15f));
        }

        void CleanupTutorial()
        {
            ClearTeaSet();
            if (tutorialPup != null) Destroy(tutorialPup);
            tutorialPup = null;
            snootMarker = null;
            snootHalo = null;
            snootActive = false;
            snootBopped = false;
            snootHover = false;
        }

        // ---------------------------------------------------------------- debriefing: Grădina Amintirilor

        // cele 3 idei-cheie ale debriefingului
        const string Card1 =
            "Atenția divizată și memoria de lucru: creierul nu poate ține totul în față deodată. " +
            "Memoria de lucru e ca o tăviță mică: când cățeii, bulele și zgomotul cer atenție, unele comori alunecă de pe ea. " +
            "Așa funcționăm toți, nu e o greșeală.";

        const string Card2 =
            "Cine trăiește asta zilnic: pentru persoanele neurodivergente (cu ADHD, autism) sau cu sensibilitate senzorială, " +
            "fiecare zi poate semăna cu Ziua cu Zoomies. Creierul lor primește mai mult zgomot, nu are mai puțină voință. " +
            "Dacă uită ceva, nu e lene: creierul lor muncește mai mult.";

        const string Card3 =
            "Ce ajută în viața reală: liniște (mai puțini stimuli deodată), rutine (un loc fix pentru chei și cană), " +
            "răbdare (timp fără grabă) și empatie (să întrebi „cum te pot ajuta?” în loc de „cum de ai uitat?”).";

        void EnterDebriefing()
        {
            StopAllCoroutines();
            StartCoroutine(DebriefFlow());
        }

        IEnumerator DebriefFlow()
        {
            DestroyMemTask();
            ClearRoundObjects();
            pawGhost.SetActive(false);
            aimLine.enabled = false;
            reticle.SetActive(false);
            Time.timeScale = 1f;
            ui.HideForSafety(false);
            ui.Inventory.Hide();
            safety.armed = false;
            lumiLine = "";
            badgeTimer = -1f;
            state = State.Transition;
            SetMood(CozyMood.Garden);
            int lastRound = Mathf.Clamp(round, 1, Rounds);
            string summary = ResultsText(lastRound);
            yield return Transition("Final de joc", "Runda s-a încheiat!",
                "Lumi a pregătit Grădina Amintirilor tale.\n<size=44>" + summary.Replace("\n", " ") + "</size>", "Vezi Grădina");

            state = State.Debriefing;
            BuildGarden();
            asking = true;
            yield return new WaitForSeconds(0.8f);

            // ---- turul florilor (butoane + atingere directă pe flori)
            ui.Dialog.Show("Lumi · Grădina Amintirilor",
                "Fiecare floare e o comoară. Rândul din față: Ziua Liniștită, rândul din spate: Ziua cu Zoomies. O floare mai înaltă înseamnă că ai nimerit mai aproape.\n<size=44>Atinge orice floare cu raza, sau pornește turul.</size>",
                new[] { "Începe turul florilor" });
            flowersInteractive = true;
            flowerPicked = -1;
            int k = -1;
            while (ui.Dialog.Chosen < 0 && flowerPicked < 0) yield return null;
            k = flowerPicked >= 0 ? flowerPicked : 0;
            flowerPicked = -1;
            while (true)
            {
                SelectFlower(k);
                bool last = k >= flowers.Length - 1;
                ui.Dialog.Show(FlowerTitle(k), FlowerText(k), last ? new[] { "Mai departe" } : new[] { "Floarea următoare", "Am înțeles florile" });
                while (ui.Dialog.Chosen < 0 && flowerPicked < 0) yield return null;
                if (flowerPicked >= 0)
                {
                    k = flowerPicked;
                    flowerPicked = -1;
                    continue;
                }
                if (ui.Dialog.Chosen == 0 && !last)
                {
                    k++;
                    continue;
                }
                break;
            }
            SelectFlower(-1);
            flowersInteractive = false;

            // ---- atenția și neurodivergența
            yield return Ask("Lumi · 1/3 · Atenția divizată", Card1, "Mai departe");
            yield return Ask("Lumi · 2/3 · Cine trăiește asta zilnic", Card2, "Mai departe");
            yield return Ask("Lumi · 3/3 · Ce ajută în viața reală", Card3, "Mai departe");

            // ---- promisiunea empatică
            ui.Dialog.Show("Lumi · Borcanul cu promisiuni", "Alege o promisiune și o punem împreună în borcanul cu stele:", Promises, false, CozyThemeArt.StarIcon, CozyThemeArt.StarIcon);
            while (ui.Dialog.Chosen < 0) yield return null;
            int choice = Mathf.Clamp(ui.Dialog.Chosen, 0, Promises.Length - 1);
            logger.LogEvent("promise", Promises[choice]);
            ui.Dialog.Show("Lumi · Borcanul cu promisiuni", "„" + Promises[choice] + "”\nO steluță aurie zboară spre borcan...", null);
            yield return AnimatePromiseIntoJar(choice);

            // suvenirul: „Polaroid cu Haita”, fix în câmpul vizual (ușor sus-dreapta), cu butonul de final pe el
            string date = System.DateTime.Now.ToString("dd.MM.yyyy");
            ui.Polaroid.Show("Mochi, Gogoașă și Fasolică îți mulțumesc!\n<size=40>„" + Promises[choice] + "”\nMind the Paws · " + date + "</size>", "Sesiune nouă");
            logger.LogEvent("polaroid", "shown");
            ui.Dialog.Show("Mulțumim că ai jucat!",
                "Uite! O poză cu haita, ca amintire de azi. Steluța ta strălucește în borcan, iar promisiunea ta rămâne lângă el.",
                new[] { "Sesiune nouă" });
            while (!ui.Polaroid.Done && ui.Dialog.Chosen < 0) yield return null;
            yield return new WaitForSeconds(0.3f);
            ui.Polaroid.Hide();
            asking = false;
            RestartSession();
        }

        IEnumerator Ask(string title, string body, params string[] buttons)
        {
            ui.Dialog.Show(title, body, buttons);
            while (ui.Dialog.Chosen < 0) yield return null;
            yield return new WaitForSeconds(0.15f);
        }

        // ---------------------------------------------------------------- flori

        Vector3 FlowerTop(int k)
        {
            if (k < 0 || k >= flowers.Length) return new Vector3(0f, -1000f, 0f);
            var f = flowers[k];
            if (f == null) return new Vector3(0f, -1000f, 0f);
            return f.transform.position + Vector3.up * flowerH[k] * Mathf.Max(0.2f, f.transform.localScale.y);
        }

        void SelectFlower(int k)
        {
            selectedFlower = k;
            if (k < 0 || k >= flowers.Length || flowers[k] == null) return;
            var top = FlowerTop(k);
            CozyFx.Burst(top, new Color(1f, 0.88f, 0.55f), 14, 0.25f, 0.016f);
            AudioDirector.Sfx("chime", top, 0.5f, 0.9f + 0.06f * k);
        }

        string FlowerTitle(int k)
        {
            k = Mathf.Clamp(k, 0, flowers.Length - 1);
            int r = k / Items, i = k % Items;
            return Graybox.TreasureLabel(Sets[r % Sets.Length][i]) + " — " + StageName(r + 1);
        }

        string FlowerText(int k)
        {
            k = Mathf.Clamp(k, 0, flowers.Length - 1);
            int r = k / Items;
            bool chaosRound = ChaosFor(r + 1);
            float err = ValidErr(k) ? errorsAll[k] : float.NaN;
            string tail = $"\n<size=44>Floarea {k + 1}/{flowers.Length} · poți atinge orice floare cu raza</size>";
            if (float.IsNaN(err))
                return "Comoara asta nu a fost căutată în sesiunea de azi, așa că floarea a rămas un boboc. Data viitoare o vom vedea înflorind!" + tail;
            int cm = Mathf.RoundToInt(err * 100f);
            if (cm < 25)
                return chaosRound
                    ? $"Ai nimerit la doar {cm} cm, chiar și cu cățeii zburdând în jur! Ai reușit totuși să fixezi reperul spațial, așa că floarea a crescut înaltă și înflorită." + tail
                    : $"Ai nimerit la doar {cm} cm. Liniștea i-a permis creierului să fixeze bine reperul spațial, așa că floarea a crescut înaltă și înflorită." + tail;
            return chaosRound
                ? $"Ai fost la {cm} cm de comoară. Suprasolicitarea senzorială și distragerile (cățeii, bulele, zgomotul) au fragmentat memoria de lucru, de aceea floarea a rămas mai mică. E la fel de valoroasă: arată cât de greu e să ții minte când totul îți cere atenția." + tail
                : $"Ai fost la {cm} cm de comoară. Provocarea de un minut a încărcat memoria de lucru și reperul s-a estompat, de aceea floarea a rămas mai mică. E la fel de valoroasă: așa funcționează memoria tuturor." + tail;
        }

        void UpdateGarden()
        {
            if (garden == null) return;
            if (jarGlow != null)
            {
                var gd = jarGlow.position - head.position;
                if (gd.sqrMagnitude > 1e-6f) jarGlow.rotation = Quaternion.LookRotation(gd.normalized, Vector3.up);
                float b = 0.5f + 0.5f * Mathf.Sin(Time.time * 2f);
                jarGlow.localScale = Vector3.one * (0.2f + 0.05f * b);   // relativ la borcan
                CozyFx.SetColor(jarGlowMat, new Color(1f, 0.85f, 0.45f, 0.4f + 0.25f * b));
            }
            for (int k = 0; k < flowers.Length; k++)
            {
                var h = flowerHalos[k];
                if (h == null || flowers[k] == null) continue;
                h.position = FlowerTop(k);
                var d = h.position - head.position;
                if (d.sqrMagnitude > 1e-6f) h.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
                bool sel = k == selectedFlower;
                bool hov = flowersInteractive && k == hoveredFlower;
                float a = sel ? 0.45f + 0.15f * Mathf.Sin(Time.time * 3f) : (hov ? 0.3f : 0f);
                float s = sel ? 0.22f : 0.16f;
                h.localScale = Vector3.one * s;
                CozyFx.SetColor(flowerHaloMats[k], new Color(1f, 0.92f, 0.6f, a));
                h.gameObject.SetActive(a > 0.01f);
            }
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float k) => (1 - k) * (1 - k) * a + 2f * (1 - k) * k * b + k * k * c;

        /// Animația cinematografică a promisiunii:
        ///  1. borcanul se ridică de pe piedestal, vine în fața jucătorului (~1.05 m, între ochi și piept) și se mărește la ~1.7×;
        ///  2. capacul se ridică, steluța aurie zboară pe un arc luminos în borcan (val de sclipici + sunet cristalin);
        ///  3. capacul se închide, borcanul mai strălucește ~1 s, apoi se micșorează și se așază înapoi pe piedestal,
        ///     cu steluța strălucind în interior.
        IEnumerator AnimatePromiseIntoJar(int choice)
        {
            choice = Mathf.Clamp(choice, 0, Promises.Length - 1);
            if (jar == null) yield break;
            var jt = jar.transform;
            var lid = FindChild(jt, "Lid");
            Vector3 lid0 = lid != null ? lid.localPosition : Vector3.zero;
            Quaternion lidR0 = lid != null ? lid.localRotation : Quaternion.identity;
            if (jarLabel != null) jarLabel.gameObject.SetActive(false);
            if (promiseLabel != null) Destroy(promiseLabel.gameObject);
            promiseLabel = null;

            // ---- 1. borcanul vine în centrul câmpului vizual și se mărește
            var f = head.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            f.Normalize();
            var focusPos = head.position + f * 1.05f + Vector3.down * (0.18f + jarHeight * jarFocusScale * 0.5f);
            var focusRot = Quaternion.LookRotation(-f, Vector3.up);
            var startPos = jt.position;
            var startRot = jt.rotation;
            float big = jarBaseScale * jarFocusScale;
            AudioDirector.Sfx("grow", startPos, 0.55f, 0.9f);
            CozyFx.Burst(startPos + Vector3.up * 0.05f, new Color(1f, 0.9f, 0.6f), 14, 0.3f, 0.016f);
            const float travel = 1.2f;
            for (float e = 0f; e < travel; e += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, e / travel);
                var mid = (startPos + focusPos) * 0.5f + Vector3.up * 0.25f;
                jt.position = Bezier(startPos, mid, focusPos, k);
                jt.rotation = Quaternion.Slerp(startRot, focusRot, k);
                jt.localScale = Vector3.one * Mathf.Lerp(jarBaseScale, big, k);
                yield return null;
            }
            jt.SetPositionAndRotation(focusPos, focusRot);
            jt.localScale = Vector3.one * big;

            // ---- 2. capacul se ridică, steluța zboară înăuntru
            AudioDirector.Sfx("chime", jt.position, 0.6f, 0.85f);
            for (float e = 0f; e < 0.5f; e += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, e / 0.5f);
                if (lid != null)
                {
                    lid.localPosition = lid0 + new Vector3(-0.03f * k, 0.07f * k, 0f);
                    lid.localRotation = lidR0 * Quaternion.Euler(0f, 0f, 28f * k);
                }
                yield return null;
            }

            if (jarStar != null) Destroy(jarStar);
            jarStar = Graybox.Star("PromiseStar", new Color(1f, 0.82f, 0.25f), 0.06f);
            jarStar.transform.localScale = Vector3.one * 1.8f;
            Graybox.Glow(jarStar, new Color(1f, 0.86f, 0.32f), 2.2f);
            CozyAnimator.Attach(jarStar, CozyMotion.Pulse, 1.4f);
            var trailHalo = CozyFx.Halo(null, Vector3.zero, 0.16f, new Color(1f, 0.85f, 0.45f, 0.6f), out _);
            CozyFx.Sparkler(jarStar.transform, new Color(1f, 0.85f, 0.5f), 20f, 0.02f);
            var sStart = lumi != null && lumi.activeInHierarchy ? lumi.transform.position + Vector3.up * 0.12f : head.position + head.right * -0.4f;
            var mouth = jt.TransformPoint(new Vector3(0f, 0.3f, 0f));     // deasupra gurii borcanului
            var inside = jt.TransformPoint(new Vector3(0f, 0.1f, 0f));    // mijlocul borcanului
            var sMid = (sStart + mouth) * 0.5f + Vector3.up * 0.35f;
            AudioDirector.Sfx("grow", sStart, 0.6f, 1.25f);
            const float fly = 1.3f;
            for (float e = 0f; e < fly + 0.35f; e += Time.deltaTime)
            {
                if (jarStar == null) yield break;
                Vector3 p;
                if (e < fly) p = Bezier(sStart, sMid, mouth, Mathf.SmoothStep(0f, 1f, e / fly));
                else p = Vector3.Lerp(mouth, inside, Mathf.SmoothStep(0f, 1f, (e - fly) / 0.35f));
                jarStar.transform.position = p;
                jarStar.transform.rotation = Quaternion.Euler(0f, e * 360f, e * 120f);
                if (trailHalo != null)
                {
                    trailHalo.position = p;
                    var d = p - head.position;
                    if (d.sqrMagnitude > 1e-6f) trailHalo.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
                }
                if (Random.value < 0.6f) CozyFx.Burst(p, new Color(1f, 0.85f, 0.4f), 3, 0.12f, 0.014f);
                yield return null;
            }
            if (trailHalo != null) Destroy(trailHalo.gameObject);
            if (jarStar == null) yield break;

            // steluța rămâne în borcan (copil al lui, ca să se micșoreze odată cu el)
            jarStar.transform.SetParent(jt, true);
            jarStar.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            jarStar.transform.rotation = Quaternion.LookRotation(jt.position - head.position, Vector3.up);
            atmosphere.Sparkle(inside, new Color(1f, 0.82f, 0.25f));
            CozyFx.Burst(inside, Color.white, 28, 0.45f, 0.02f);
            CozyFx.Burst(inside, new Color(1f, 0.85f, 0.4f), 30, 0.55f, 0.022f);
            AudioDirector.Sfx("chime", inside, 0.95f, 1.15f);

            // lumină caldă + halou care respiră + sclipici care plutesc în borcan — toate legate de borcan
            var lightGo = new GameObject("JarLight");
            lightGo.transform.SetParent(jt, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            var lt = lightGo.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.color = new Color(1f, 0.82f, 0.45f);
            lt.range = 0.7f;
            lt.intensity = 1.6f;
            lt.shadows = LightShadows.None;
            jarGlow = CozyFx.Halo(jt, inside, 0.2f, new Color(1f, 0.85f, 0.45f, 0.55f), out jarGlowMat);
            InsideJarSparkles(jt, new Vector3(0f, 0.1f, 0f), 0.05f);

            // ---- 3. capacul se închide, borcanul strălucește ~1 s, apoi se întoarce pe piedestal
            for (float e = 0f; e < 0.45f; e += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, e / 0.45f);
                if (lid != null)
                {
                    lid.localPosition = Vector3.Lerp(lid0 + new Vector3(-0.03f, 0.07f, 0f), lid0, k);
                    lid.localRotation = Quaternion.Slerp(lidR0 * Quaternion.Euler(0f, 0f, 28f), lidR0, k);
                }
                yield return null;
            }
            if (lid != null)
            {
                lid.localPosition = lid0;
                lid.localRotation = lidR0;
                AudioDirector.Sfx("place", lid.position, 0.5f, 1.3f);
            }
            for (float e = 0f; e < 1.0f; e += Time.deltaTime)
            {
                jt.localScale = Vector3.one * big * (1f + 0.03f * Mathf.Sin(e * Mathf.PI * 3f));
                if (Random.value < 0.3f) CozyFx.Burst(inside + Random.insideUnitSphere * 0.08f, new Color(1f, 0.92f, 0.6f), 2, 0.08f, 0.012f);
                yield return null;
            }
            jt.localScale = Vector3.one * big;

            var backFrom = jt.position;
            var backRot = jt.rotation;
            for (float e = 0f; e < travel; e += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, e / travel);
                var mid = (backFrom + jarHomePos) * 0.5f + Vector3.up * 0.2f;
                jt.position = Bezier(backFrom, mid, jarHomePos, k);
                jt.rotation = Quaternion.Slerp(backRot, jarHomeRot, k);
                jt.localScale = Vector3.one * Mathf.Lerp(big, jarBaseScale, k);
                yield return null;
            }
            jt.SetPositionAndRotation(jarHomePos, jarHomeRot);
            jt.localScale = Vector3.one * jarBaseScale;
            CozyFx.Burst(jarHomePos, new Color(1f, 0.9f, 0.7f), 12, 0.25f, 0.014f);
            AudioDirector.Sfx("place", jarHomePos, 0.5f, 1f);

            // eticheta aurie cu promisiunea, deasupra capacului
            var gt = garden != null ? garden.transform : jt.parent;
            promiseLabel = CozyUI.Label("PromiseLabel", "<size=48>Borcanul cu promisiuni</size>\n„" + Promises[choice] + "”", 30, new Color(0.45f, 0.3f, 0.08f), new Color(1f, 0.9f, 0.6f, 0.96f), CozyUI.Gold, 520f);
            promiseLabel.transform.SetParent(gt, false);
            promiseLabel.transform.position = jarHomePos + Vector3.up * (jarHeight + 0.14f);
            promiseLabel.faceTarget = head;
            yield return new WaitForSeconds(0.6f);
        }

        /// Stele mici și sclipici care plutesc încet în interiorul borcanului.
        void InsideJarSparkles(Transform parent, Vector3 localCenter, float radius)
        {
            var go = new GameObject("JarSparkles");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.005f, 0.03f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.025f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.97f, 0.85f));
            main.gravityModifier = -0.004f;
            main.maxParticles = 60;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;   // se micșorează odată cu borcanul
            var em = ps.emission;
            em.rateOverTime = 14f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = CozyFx.FadeGradient(Color.white, Color.white);
            var nz = ps.noise;
            nz.enabled = true;
            nz.strength = 0.03f;
            nz.frequency = 0.8f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = CozyFx.VertexColorMat(CozyFx.SoftDot);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        string RowTitle(int r)
        {
            return StageName(r + 1);
        }

        /// Grădina stă pe podea (FloorY), la ~1.35 m în fața jucătorului: strat rotund de iarbă pastelată,
        /// bordură de pietricele, felinare mici, petale care urcă. Centrul privirii rămâne liber pentru grădină:
        /// Lumi stă la nivelul pieptului ~0.85 m în stânga grădinii, cu cardul ei de dialog deasupra ei;
        /// borcanul cu promisiuni e în dreapta.
        void BuildGarden()
        {
            ClearGarden();
            var f = head.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            f.Normalize();
            float floor = FloorY();
            var center = new Vector3(head.position.x, floor, head.position.z) + f * 1.35f;
            garden = new GameObject("MemoryGarden");
            garden.transform.SetPositionAndRotation(center, Quaternion.LookRotation(-f, Vector3.up));
            var gt = garden.transform;

            // platformă de lemn cald + strat de iarbă pastelată + bordură din bușteni mici de lemn
            Graybox.Prim(PrimitiveType.Cylinder, gt, new Vector3(0f, 0.012f, 0f), new Vector3(1.34f, 0.012f, 1.04f), CozyThemeArt.WoodMat(new Color(1f, 0.92f, 0.86f), 3f));
            Graybox.Prim(PrimitiveType.Cylinder, gt, new Vector3(0f, 0.028f, 0f), new Vector3(1.2f, 0.006f, 0.92f), Graybox.MakeMat(new Color(0.62f, 0.86f, 0.62f)));
            var logA = CozyThemeArt.WoodMat(new Color(1f, 0.93f, 0.88f), 0.5f);
            var logB = CozyThemeArt.WoodMat(new Color(0.94f, 0.82f, 0.74f), 0.5f);
            const int logs = 30;
            for (int i = 0; i < logs; i++)
            {
                float a = i * Mathf.PI * 2f / logs;
                var p = new Vector3(Mathf.Cos(a) * 0.64f, 0.035f, Mathf.Sin(a) * 0.49f);
                var tangent = new Vector3(-Mathf.Sin(a) * 0.64f, 0f, Mathf.Cos(a) * 0.49f);
                float yaw = Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg;
                Graybox.Prim(PrimitiveType.Capsule, gt, p, new Vector3(0.045f, 0.06f, 0.045f), new Vector3(90f, yaw, 0f), i % 2 == 0 ? logA : logB);
            }

            // felinare mici pe margine
            float[] lx = { 0.5f, -0.5f, 0.42f, -0.42f };
            float[] lz = { 0.32f, 0.32f, -0.36f, -0.36f };
            for (int i = 0; i < 4; i++)
            {
                var l = Graybox.TreasureByType("lantern", false, false);
                l.name = "GardenLantern";
                l.transform.SetParent(gt, false);
                l.transform.localPosition = new Vector3(lx[i], 0.03f, lz[i]);
                l.transform.localScale = Vector3.one * 0.8f;
                CozyFx.Halo(l.transform, l.transform.position + Vector3.up * 0.09f, 0.3f, new Color(1f, 0.86f, 0.5f, 0.3f), out _);
            }

            // petale care urcă încet din grădină
            var pgo = new GameObject("RisingPetals");
            pgo.transform.SetParent(gt, false);
            pgo.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            var ps = pgo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.032f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.72f, 0.84f), new Color(1f, 0.92f, 0.96f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.012f;
            main.maxParticles = 120;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 12f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = 0.55f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-1f, 1f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = CozyFx.FadeGradient(Color.white, Color.white);
            var nz = ps.noise;
            nz.enabled = true;
            nz.strength = 0.05f;
            nz.frequency = 0.6f;
            var pr = pgo.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = CozyFx.VertexColorMat(CozyFx.PetalTex);
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();

            // flori: rândul din față = runda 1, rândul din spate = runda 2
            Color[] calmCols = { new Color(0.45f, 0.7f, 1f), new Color(0.4f, 0.9f, 0.8f), new Color(0.55f, 0.85f, 0.55f) };
            Color[] chaosCols = { new Color(1f, 0.5f, 0.75f), new Color(0.8f, 0.55f, 0.95f), new Color(1f, 0.6f, 0.9f) };
            for (int r = 0; r < Rounds; r++)
            {
                var cols = ChaosFor(r + 1) ? chaosCols : calmCols;
                float z = r == 0 ? 0.16f : -0.16f;
                float sum = 0f;
                int n = 0;
                for (int i = 0; i < Items; i++)
                {
                    int ei = r * Items + i;
                    float err = ValidErr(ei) ? errorsAll[ei] : float.NaN;
                    float hs = float.IsNaN(err) ? 0.4f : Mathf.Clamp(1.5f - err * 2f, 0.4f, 1.5f);
                    if (!float.IsNaN(err)) { sum += err; n++; }
                    var flower = Graybox.MemoryFlower("Flower_" + r + "_" + i, cols[i], hs);
                    flower.transform.SetParent(gt, false);
                    flower.transform.localPosition = new Vector3((i - 1) * 0.24f, 0.03f, z);
                    int k = r * Items + i;
                    flowers[k] = flower;
                    flowerH[k] = 0.3f * Mathf.Clamp(hs, 0.4f, 1.5f);
                    flowerHalos[k] = CozyFx.Halo(gt, flower.transform.position, 0.16f, new Color(1f, 0.92f, 0.6f, 0f), out flowerHaloMats[k]);
                    flowerHalos[k].gameObject.SetActive(false);
                    StartCoroutine(Grow(flower.transform, 0.3f * k));
                }
                // tăbliță mică pe țăruș, la capătul rândului
                var stake = Graybox.Prim(PrimitiveType.Cylinder, gt, new Vector3(0.5f, 0.09f, z), new Vector3(0.014f, 0.09f, 0.014f), CozyThemeArt.WoodMat(new Color(0.9f, 0.76f, 0.66f), 0.5f));
                stake.name = "SignStake";
                // tăbliță de lemn în spatele etichetei
                var board = Graybox.Prim(PrimitiveType.Cube, gt, new Vector3(0.5f, 0.22f, z), new Vector3(0.015f, 0.1f, 0.22f), CozyThemeArt.WoodMat(new Color(1f, 0.93f, 0.86f), 1f));
                board.name = "SignBoard";
                string meanTxt = n > 0 ? (sum / n * 100f).ToString("0") + " cm" : "fără date";
                var sign = CozyUI.Label("RowSign_" + r, RowTitle(r) + "\n<size=44>" + meanTxt + " eroare medie</size>", 26, CozyUI.Ink, Color.Lerp(Color.white, cols[0], 0.25f), cols[0]);
                sign.transform.SetParent(gt, false);
                sign.transform.localPosition = new Vector3(0.5f, 0.3f, z);
                sign.faceTarget = head;
            }

            // borcanul cu stele: în afara grădinii, pe propria măsuță rotundă de lemn cu perniță pastelată,
            // ~0.95 m în dreapta centrului (simetric cu Lumi din stânga). Înălțime de repaus ~20 cm.
            var jarSide = Vector3.Cross(Vector3.up, f);    // dreapta jucătorului
            var tableBase = center + jarSide * jarSideOffset;                       // pe podea
            const float tableHeight = 0.3f;   // măsuță mai joasă, mai proporțională cu borcanul de 20 cm
            var woodTop = CozyThemeArt.WoodMat(new Color(1f, 0.93f, 0.88f), 1f);
            var woodLeg = CozyThemeArt.WoodMat(new Color(0.92f, 0.78f, 0.68f), 0.5f);
            var pedestal = new GameObject("JarPedestal").transform;
            pedestal.SetParent(gt, false);
            pedestal.position = tableBase;
            pedestal.rotation = Quaternion.LookRotation(-f, Vector3.up);
            Graybox.Prim(PrimitiveType.Cylinder, pedestal, new Vector3(0f, 0.012f, 0f), new Vector3(0.2f, 0.012f, 0.2f), woodLeg);       // talpa
            Graybox.Prim(PrimitiveType.Cylinder, pedestal, new Vector3(0f, tableHeight * 0.5f, 0f), new Vector3(0.06f, tableHeight * 0.5f, 0.06f), woodLeg);   // piciorul, mai scurt și puțin mai gros
            Graybox.Prim(PrimitiveType.Cylinder, pedestal, new Vector3(0f, tableHeight, 0f), new Vector3(0.28f, 0.015f, 0.28f), woodTop);  // blatul
            var trim = Graybox.MakeMat(new Color(1f, 0.84f, 0.45f));
            for (int i = 0; i < 18; i++)
            {
                float a = i * Mathf.PI * 2f / 18f;
                Graybox.Prim(PrimitiveType.Sphere, pedestal, new Vector3(Mathf.Cos(a) * 0.14f, tableHeight + 0.012f, Mathf.Sin(a) * 0.14f), Vector3.one * 0.014f, trim);
            }
            // pernița pastelată pe care stă borcanul
            Graybox.Prim(PrimitiveType.Sphere, pedestal, new Vector3(0f, tableHeight + 0.028f, 0f), new Vector3(0.2f, 0.035f, 0.2f), Graybox.MakeMat(new Color(1f, 0.8f, 0.88f)));
            Graybox.Prim(PrimitiveType.Sphere, pedestal, new Vector3(0f, tableHeight + 0.042f, 0f), new Vector3(0.06f, 0.012f, 0.06f), Graybox.MakeMat(new Color(1f, 0.92f, 0.95f)));

            jar = Graybox.StarJar("PromiseJar");
            jar.transform.SetParent(gt, false);
            jar.transform.localScale = Vector3.one;
            // scala de repaus din înălțimea reală (merge și pentru un model "star_jar" din Blender)
            float rawH = 0f;
            foreach (var r in jar.GetComponentsInChildren<Renderer>())
                rawH = Mathf.Max(rawH, r.bounds.max.y - jar.transform.position.y);
            jarBaseScale = rawH > 0.01f ? jarHeight / rawH : 1f;
            jar.transform.localScale = Vector3.one * jarBaseScale;
            jarHomePos = tableBase + Vector3.up * (tableHeight + 0.045f);
            jarHomeRot = Quaternion.LookRotation(-f, Vector3.up);
            jar.transform.SetPositionAndRotation(jarHomePos, jarHomeRot);
            jarLabel = CozyUI.Label("JarLabel", "Borcanul cu promisiuni", 24, CozyUI.Ink, CozyUI.Cream, CozyUI.Gold);
            jarLabel.transform.SetParent(gt, false);
            jarLabel.transform.position = jarHomePos + Vector3.up * (jarHeight + 0.09f);
            jarLabel.faceTarget = head;

            // Lumi la nivelul pieptului, ~1.25 m în stânga centrului grădinii. Cardul ei are marginea dreaptă
            // la ≥ 0.45 m în stânga marginii grădinii (raza ~0.66 m) și crește spre stânga, niciodată spre flori.
            lumi.SetActive(true);
            lumiFollow = false;
            lumi.transform.localScale = Vector3.one * 1.5f;
            var side = Vector3.Cross(Vector3.up, f);      // dreapta jucătorului
            float chestY = head.position.y - 0.35f;
            lumi.transform.position = new Vector3(center.x, chestY, center.z) - side * 1.25f;
            lumi.transform.rotation = YawTowardsHead(lumi.transform.position);
            const float gardenHalfWidth = 0.66f;
            ui.Dialog.AnchorPoint = new Vector3(center.x, chestY + 0.2f, center.z) - side * (gardenHalfWidth + 0.47f);
            ui.Dialog.Anchored = true;
        }

        IEnumerator Grow(Transform t, float delay)
        {
            t.localScale = Vector3.zero;
            yield return new WaitForSeconds(delay);
            if (t != null)
            {
                AudioDirector.Sfx("grow", t.position, 0.45f, Random.Range(0.9f, 1.2f));
                CozyFx.Burst(t.position + Vector3.up * 0.05f, new Color(1f, 0.8f, 0.9f), 8, 0.2f, 0.015f);
            }
            yield return ScaleBounce(t, 0f, 1f, 0.7f);
        }

        void ClearGarden()
        {
            if (garden != null) Destroy(garden);
            garden = null;
            jar = null;
            jarStar = null;
            promiseLabel = null;
            jarLabel = null;
            jarGlow = null;
            jarGlowMat = null;
            flowersInteractive = false;
            selectedFlower = hoveredFlower = flowerPicked = -1;
            for (int k = 0; k < flowers.Length; k++)
            {
                flowers[k] = null;
                flowerHalos[k] = null;
                flowerHaloMats[k] = null;
            }
            if (ui != null)
            {
                ui.Dialog.Anchored = false;
                ui.Dialog.AnchorPoint = null;
            }
            if (lumi != null)
            {
                lumi.transform.localScale = Vector3.one;
                lumiFollow = true;
                lumiSnap = true;
            }
        }

        void SetLumi(bool on)
        {
            if (lumi == null) return;
            lumi.SetActive(on);
            lumiFollow = true;
            lumiSnap = true;
            lumi.transform.localScale = Vector3.one;
        }

        /// Lumi plutește jos-stânga în câmpul vizual (niciodată în centru); cardul ei de dialog stă deasupra ei.
        /// Cadrul folosește yaw-ul capului și un pitch limitat, ca să rămână stabilă în cadru (fără oscilații).
        Vector3 LumiHudTarget()
        {
            var e = head.eulerAngles;
            float pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, e.x), -25f, 35f);
            var frame = Quaternion.Euler(pitch, e.y, 0f);
            return head.position + frame * new Vector3(-0.34f, -0.27f, 0.95f);
        }

        void UpdateLumi()
        {
            if (lumi == null || head == null || !lumi.activeSelf || !lumiFollow) return;
            var target = LumiHudTarget();
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (lumiSnap || (lumi.transform.position - target).sqrMagnitude > 2.25f)
            {
                lumi.transform.position = target;
                lumiSnap = false;
            }
            else lumi.transform.position = Vector3.Lerp(lumi.transform.position, target, 1f - Mathf.Exp(-6f * dt));
            lumi.transform.rotation = YawTowardsHead(lumi.transform.position);
        }

        // ================================================================ input

        bool GetAim(out Ray ray, out bool click)
        {
            ray = default;
            click = false;
            aimIsGaze = false;

            // caută mâinile periodic (building block-urile le pot crea după Start)
            if (Time.unscaledTime >= nextHandSearch)
            {
                hands.Clear();
                hands.AddRange(FindObjectsByType<OVRHand>(FindObjectsSortMode.None));
                nextHandSearch = Time.unscaledTime + 2f;
            }

            // ---- "click": orice pinch, butonul A / trigger de pe controller, mouse în editor
            OVRHand best = null;
            int tracked = 0;
            bool pinchingNow = false;
            foreach (var h in hands)
            {
                if (h == null || !h.IsTracked) continue;
                tracked++;
                if (h.GetFingerIsPinching(OVRHand.HandFinger.Index) ||
                    h.GetFingerPinchStrength(OVRHand.HandFinger.Index) > 0.85f)
                    pinchingNow = true;
                if (best == null || IsRightHand(h)) best = h;
            }
            bool pinchDown = pinchingNow && !wasPinching;
            wasPinching = pinchingNow;

            bool buttonDown =
                OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.Hands) ||
                OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
                OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch) ||
                OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch);

            bool mouseDown = false;
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            bool xrActive = UnityEngine.XR.XRSettings.isDeviceActive;
            if (!xrActive && Mouse.current != null) mouseDown = Mouse.current.leftButton.wasPressedThisFrame;
#endif
            click = Debounce(pinchDown || buttonDown || mouseDown);

            holdNow = pinchingNow ||
                OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.Hands) ||
                OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
                OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch) ||
                OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch);
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            if (!xrActive && Mouse.current != null && Mouse.current.leftButton.isPressed) holdNow = true;
#endif

            string inputs = $"mâini: {hands.Count} găsite, {tracked} urmărite · pinch: {(pinchingNow ? "DA" : "nu")}";

            // ---- raza
            if (aimMode == AimMode.Auto)
            {
                // 1) mâna (dreapta dacă e urmărită)
                if (best != null)
                {
                    if (best.IsPointerPoseValid && best.PointerPose != null)
                    {
                        ray = new Ray(best.PointerPose.position, best.PointerPose.forward);
                        aimSource = "mână (pointer)";
                    }
                    else
                    {
                        ray = ShoulderRay(best.transform.position, IsRightHand(best));
                        aimSource = "mână (umăr→mână)";
                    }
                    debugInfo = inputs + " · țintire: " + aimSource;
                    return true;
                }

                // 2) controller drept
                if (cameraRig != null && OVRInput.IsControllerConnected(OVRInput.Controller.RTouch))
                {
                    var t = cameraRig.rightControllerAnchor != null ? cameraRig.rightControllerAnchor : cameraRig.rightHandAnchor;
                    ray = new Ray(t.position, t.forward);
                    debugInfo = inputs + " · țintire: controller drept";
                    return true;
                }

                // 3) mâna dreaptă fără OVRHand (doar ancora din Camera Rig)
                if (cameraRig != null && cameraRig.rightHandAnchor != null &&
                    OVRInput.IsControllerConnected(OVRInput.Controller.RHand))
                {
                    ray = ShoulderRay(cameraRig.rightHandAnchor.position, true);
                    debugInfo = inputs + " · țintire: ancoră mână dreaptă";
                    return true;
                }
            }

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            // 4) mouse în editor, fără cască
            if (!xrActive && Camera.main != null && Mouse.current != null)
            {
                ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
                debugInfo = inputs + " · țintire: mouse";
                return true;
            }
#endif

            // 5) privirea — merge mereu: punctul din fața ochilor + pinch / buton
            ray = new Ray(head.position, head.forward);
            aimIsGaze = true;
            debugInfo = inputs + " · țintire: privire";
            return true;
        }

        /// Fără cască (în editor): ține apăsat click dreapta și mișcă mouse-ul ca să te rotești.
        float lookYaw, lookPitch;
        void EditorMouseLook()
        {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            if (UnityEngine.XR.XRSettings.isDeviceActive || Mouse.current == null || cameraRig == null) return;
            if (!Mouse.current.rightButton.isPressed) return;
            var d = Mouse.current.delta.ReadValue() * 0.15f;
            lookYaw += d.x;
            lookPitch = Mathf.Clamp(lookPitch - d.y, -80f, 80f);
            cameraRig.transform.rotation = Quaternion.Euler(lookPitch, lookYaw, 0f);
#endif
        }

        /// Rază de la un "umăr" aproximat (sub cap, puțin lateral) prin mână — merge mereu.
        Ray ShoulderRay(Vector3 handPos, bool right)
        {
            var r = head.right; r.y = 0f; r.Normalize();
            var shoulder = head.position + Vector3.down * 0.2f + r * (right ? 0.18f : -0.18f);
            var dir = handPos - shoulder;
            if (dir.sqrMagnitude < 1e-4f) dir = head.forward;
            return new Ray(handPos, dir.normalized);
        }

        bool IsRightHand(OVRHand h)
        {
            if (cameraRig != null && cameraRig.rightHandAnchor != null && h.transform.IsChildOf(cameraRig.rightHandAnchor)) return true;
            return h.name.ToLowerInvariant().Contains("right");
        }

        bool Debounce(bool pressed)
        {
            if (!pressed || Time.unscaledTime - lastClickTime < 0.4f) return false;
            lastClickTime = Time.unscaledTime;
            return true;
        }

        // ================================================================ suprafețe

        /// Adâncime live (forma reală: haine, perne) → MRUK (cutiile din Space Setup) → collider → plan virtual.
        bool TryHitSurface(Ray ray, out Vector3 point, out Vector3 normal, out string surface)
        {
            point = default; normal = Vector3.up; surface = "";
            lastSurfaceNotFlat = false;

            if (envRay != null)
            {
                // Senzorul de adâncime vede și mâna ta → pornim raza la 25 cm în fața mâinii
                // și ignorăm loviturile prea apropiate (degete, braț).
                const float skip = 0.25f;
                var r2 = new Ray(ray.origin + ray.direction * skip, ray.direction);
                bool ok = envRay.Raycast(r2, out EnvironmentRaycastHit eh, maxRayDistance);
                if (ok && IsFinite(eh.point) && Vector3.Distance(ray.origin, eh.point) > skip + 0.05f)
                {
                    point = eh.point;
                    normal = IsFinite(eh.normal) && eh.normal.sqrMagnitude > 0.01f ? eh.normal.normalized : Vector3.up;
                    surface = "adâncime live";
                    depthStatus = $"Hit {Vector3.Distance(ray.origin, point):0.00}m";
                    lastSurfaceNotFlat = normal.y < depthMinUpDot;
                    return !lastSurfaceNotFlat;
                }
                depthStatus = ok ? "prea aproape (mâna?)" : eh.status.ToString();
            }

            var room = Room;
            if (room != null && room.Raycast(ray, maxRayDistance, out RaycastHit h, out MRUKAnchor anchor))
            {
                point = h.point; normal = h.normal;
                surface = anchor != null ? anchor.name : "room";
                lastSurfaceNotFlat = normal.y < minSurfaceUpDot;
                return !lastSurfaceNotFlat;
            }

            if (Physics.Raycast(ray, out RaycastHit ph, maxRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                point = ph.point; normal = ph.normal;
                surface = ph.collider.name;
                lastSurfaceNotFlat = normal.y < minSurfaceUpDot;
                return !lastSurfaceNotFlat;
            }

            if (fallbackY == null) fallbackY = head.position.y - fallbackSurfaceBelowHead;
            var plane = new Plane(Vector3.up, new Vector3(0f, fallbackY.Value, 0f));
            if (plane.Raycast(ray, out float dist) && dist <= maxRayDistance)
            {
                point = ray.GetPoint(dist); normal = Vector3.up;
                surface = "virtual_plane";
                return true;
            }
            return false;
        }

        static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        float FloorY()
        {
            var r = Room;
            if (r != null && r.FloorAnchors != null && r.FloorAnchors.Count > 0 && r.FloorAnchors[0] != null)
                return r.FloorAnchors[0].transform.position.y;
            return head.position.y - 1.6f;
        }

        // ================================================================ vizual

        Quaternion YawTowardsHead(Vector3 p)
        {
            if (head == null) return Quaternion.identity;
            var toHead = head.position - p; toHead.y = 0f;
            return toHead.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toHead.normalized, Vector3.up) : Quaternion.identity;
        }

        /// Camera care chiar randează: CenterEyeAnchor → Camera.main → prima cameră activă.
        Transform FindHead()
        {
            if (cameraRig != null && cameraRig.centerEyeAnchor != null) return cameraRig.centerEyeAnchor;
            if (Camera.main != null) return Camera.main.transform;
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c.isActiveAndEnabled) return c.transform;
            return null;
        }

        IEnumerator ScaleBounce(Transform t, float from, float to, float dur)
        {
            for (float e = 0f; e < dur; e += Time.deltaTime)
            {
                if (t == null) yield break;
                float k = e / dur;
                float s = Mathf.LerpUnclamped(from, to, k) + Mathf.Sin(k * Mathf.PI) * 0.25f;
                t.localScale = Vector3.one * Mathf.Max(0f, s);
                yield return null;
            }
            if (t != null) t.localScale = Vector3.one * to;
        }

        void SpawnPuff(Vector3 at)
        {
            puffs.RemoveAll(p => p == null);
            var mat = Graybox.MakeMat(new Color(1f, 1f, 1f));
            for (int i = 0; i < 10; i++)
            {
                var root = new GameObject("Puff");
                root.transform.position = at;
                Graybox.Prim(PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one * 0.03f, mat);
                puffs.Add(root);
                StartCoroutine(PuffMove(root, Random.onUnitSphere * 0.25f + Vector3.up * 0.1f));
            }
        }

        IEnumerator PuffMove(GameObject go, Vector3 velocity)
        {
            const float dur = 0.5f;
            var start = go.transform.position;
            for (float e = 0f; e < dur; e += Time.deltaTime)
            {
                if (go == null) yield break;
                float k = e / dur;
                go.transform.position = start + velocity * k;
                go.transform.localScale = Vector3.one * (1f + k) * (1f - k);
                yield return null;
            }
            if (go != null) Destroy(go);
        }
    }
}
