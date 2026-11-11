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
    /// Vertical slice "Mind the Paws":
    ///  1. Ascunde: țintești o suprafață reală (masa) cu raza din mână, pinch → cana e pusă acolo.
    ///  2. "Poof!" → cana dispare.
    ///  3. Ține minte: câteva secunde de pauză (opțional: corgi care țopăie).
    ///  4. Ghicește: raza + pinch → lăbuța apare; se calculează distanța.
    ///  5. Rezultat: "La 3 lăbuțe distanță (31 cm)", cana reapare în locul real, rând în CSV.
    ///     Pinch din nou → trial nou.
    /// Merge cu mâinile (pinch), cu controller-ul drept (trigger) sau cu mouse-ul în editor.
    /// </summary>
    public class MindThePawsGame : MonoBehaviour
    {
        enum State { Hide, Poof, Memorize, Guess, Result, Debriefing }

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

        [Header("Joc")]
        public float memorizeSeconds = 5f;
        public float pawSizeMeters = 0.10f;
        public float maxRayDistance = 6f;
        [Range(0f, 1f)] public float minSurfaceUpDot = 0.7f;   // doar suprafețe orizontale
        public float fallbackSurfaceBelowHead = 0.6f;          // plan virtual dacă nu există Space Setup
        public float minTreasureGapEditor = 0.6f;
        public float minTreasureGapMR = 0.8f;

        [Header("Constelația Pierdută")]
        [Range(10f, 60f)] public float starTaskSeconds = 18f;

        public enum ChaosMode { Calm, Chaos, Alternate }

        [Header("Haos")]
        [Tooltip("Calm: fără distrageri. Chaos: mereu cu distrageri. Alternate: runde impare calm, pare haos (bun pentru studiu).")]
        public ChaosMode chaosMode = ChaosMode.Alternate;
        [Tooltip("Câți căței trebuie 'mângâiați' (priviți) înainte de ghicire. Apar mereu departe de locul cănii.")]
        public int attentionPuppies = 3;
        public float puppyLookSeconds = 0.8f;
        public int bubbleCount = 10;

        bool ChaosFor(int rd) => chaosMode == ChaosMode.Chaos || (chaosMode == ChaosMode.Alternate && rd % 2 == 0);
        bool ChaosThisRound => ChaosFor(round);
        string[] CurrentSet => Sets[(round - 1) % Sets.Length];

        [Header("Text")]
        public float labelCharacterSize = 0.004f;
        public float labelDistance = 0.9f;

        State state = State.Hide;
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
        TextMesh label;
        Material rayOk, rayBad;
        readonly List<GameObject> corgis = new List<GameObject>();
        readonly List<GameObject> puffs = new List<GameObject>();

        ChaosBehaviours chaos;
        SafetyBlanket safety;
        StarConstellationTask starTask;
        float chaosIntensity = 1f;
        bool safetyTriggered;

        int round;
        int placedCount;
        int itemIndex;
        bool gapWarn;
        bool virtualNoteShown;
        float hideTime, lastGuessMark;
        float? fallbackY;
        string lastSurface = "";
        float startTime;
        TextMesh debugLabel;
        string debugInfo = "";
        string aimSource = "";
        GameObject reticle;
        bool aimIsGaze;
        bool lumiFollow, lumiSnap;

        GameObject garden, jar, jarStar;
        int debriefStep;

        public enum AimMode { Auto, Gaze }

        [Header("Țintire")]
        [Tooltip("Auto: mână → controller → privire. Gaze: mereu cu privirea (punct în fața ochilor) + pinch.")]
        public AimMode aimMode = AimMode.Auto;

        [Header("Adâncime live (Quest 3)")]
        [Tooltip("Raycast pe forma reală a camerei (canapea cu haine, perne), nu pe cutiile din Space Setup.")]
        public bool useLiveDepth = true;
        [Range(0f, 1f)] public float depthMinUpDot = 0.35f;   // haine/perne sunt denivelate → toleranță mai mare
        EnvironmentRaycastManager envRay;
        string depthStatus = "-";

        [Header("Material (opțional)")]
        [Tooltip("Un material URP Unlit din proiect. Dacă e gol, scriptul își alege singur shaderul.")]
        public Material baseMaterial;

        [Header("Debug")]
        public bool showDebug = true;

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

            pawGhost = Graybox.Paw("PawGhost", new Color(0.85f, 0.72f, 0.62f));
            pawGhost.SetActive(false);
            lumi = Graybox.FairyLumi("Lumi");
            lumi.SetActive(false);

            aimLine = Graybox.Line("AimRay", Color.white, 0.008f);
            reticle = new GameObject("Reticle");
            Graybox.Prim(PrimitiveType.Sphere, reticle.transform, Vector3.zero, Vector3.one * 0.025f, Graybox.MakeMat(Color.white));
            reticle.SetActive(false);
            rayOk = aimLine.sharedMaterial;
            rayBad = Graybox.MakeMat(new Color(1f, 0.55f, 0.1f));

            label = Graybox.Text("Label", labelCharacterSize);
            debugLabel = Graybox.Text("DebugLabel", labelCharacterSize * 0.5f);
            debugLabel.color = new Color(1f, 1f, 0.6f);
            startTime = Time.time;
            RestartSession();
        }

        // ---------------------------------------------------------------- loop

        void Update()
        {
            if (!started) return;   // Start a eșuat — vezi Console / log
            if (head == null) head = FindHead();
            if (head == null)
            {
                if (Time.frameCount % 120 == 0) Debug.LogError("[MindThePaws] Nu găsesc nicio cameră!");
                return;
            }
            safety.head = head;

            EditorMouseLook();

            try
            {
                Tick();
            }
            catch (System.Exception e)
            {
                // Orice eroare apare direct pe ecran, nu doar în Console
                debugInfo = "EROARE: " + e.GetType().Name + ": " + e.Message;
                Debug.LogException(e);
            }

            try
            {
                UpdateLabelPose();
                UpdateLumi();
            }
            catch (System.Exception e) { Debug.LogException(e); }
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

            bool starActive = state == State.Memorize && starTask != null && !starTask.Finished;
            bool needAim = state == State.Hide || state == State.Guess;
            Vector3 point = default;
            bool hit = false;
            if (hasAim && starActive)
            {
                starTask.Feed(ray, click);
                hit = starTask.Hovering;
                point = starTask.HoverPoint;
            }
            else if (hasAim && needAim) hit = TryHitSurface(ray, out point, out _, out lastSurface);

            bool tooClose = state == State.Hide && hit && placedCount > 0 && TooClose(point);
            bool valid = hit && !tooClose;

            // raza + punctul de la capăt
            bool showRay = hasAim && (needAim || starActive);
            Vector3 end = hit ? point : ray.GetPoint(1.5f);
            DrawAim(showRay, ray, end, valid);

            Quaternion facing = YawTowardsHead(point);

            switch (state)
            {
                case State.Hide:
                    if (tooClose != gapWarn)
                    {
                        gapWarn = tooClose;
                        ShowHideLabel();
                    }
                    ghost.SetActive(hit);
                    if (hit) ghost.transform.SetPositionAndRotation(point, facing);
                    if (valid && click) PlaceTreasure(point, facing);
                    else if (!virtualNoteShown && !gapWarn && Time.time - startTime > 5f && Room == null && round == 1 && placedCount == 0)
                    {
                        virtualNoteShown = true;
                        SetLabel("Ascunde prima comoară:\nțintește o suprafață și fă pinch\n<size=40>(fără Space Setup → plan virtual)</size>");
                    }
                    break;

                case State.Guess:
                    pawGhost.SetActive(hit);
                    if (hit) pawGhost.transform.SetPositionAndRotation(point, facing);
                    if (hit && click) GuessItem(point);
                    break;

                case State.Result:
                    if (click)
                    {
                        if (round >= Rounds) EnterDebriefing();
                        else StartRound();
                    }
                    break;

                case State.Debriefing:
                    if (click) AdvanceDebrief();
                    break;
            }
        }

        // ---------------------------------------------------------------- stări

        void RestartSession()
        {
            ClearGarden();
            round = 0;
            for (int i = 0; i < errorsAll.Length; i++) errorsAll[i] = float.NaN;
            StartRound();
        }

        void StartRound()
        {
            round++;
            state = State.Hide;
            fallbackY = null;
            placedCount = 0;
            itemIndex = 0;
            gapWarn = false;
            virtualNoteShown = false;
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
            RefreshGhost();
            SetLumi(false);
            safety.armed = true;
            ShowHideLabel();
        }

        void ClearRoundObjects()
        {
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
            ClearCorgis();
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

        void ShowHideLabel()
        {
            string itemName = Graybox.TreasureLabel(CurrentSet[Mathf.Min(placedCount, Items - 1)]);
            if (gapWarn)
                SetLabel($"Ascunde {itemName} · Comori ascunse: {placedCount}/{Items}\nPune comoara puțin mai departe\nde celelalte!");
            else
                SetLabel($"{logger.ParticipantId} · Runda {round}/{Rounds} ({(ChaosThisRound ? "haos" : "calm")})\nAscunde {itemName} · Comori ascunse: {placedCount}/{Items}\n<size=40>țintește o suprafață și fă pinch</size>");
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
            hiddenPos[i] = point;
            hiddenSurface[i] = lastSurface;
            treasures[i].transform.SetPositionAndRotation(point, facing);
            treasures[i].transform.localScale = Vector3.one;
            treasures[i].SetActive(true);
            placedCount++;
            gapWarn = false;
            ghost.SetActive(false);
            if (placedCount >= Items)
            {
                Destroy(ghost);
                ghost = null;
                SetLabel($"Comori ascunse: {Items}/{Items}");
                StartCoroutine(PoofThenMemorize());
            }
            else
            {
                RefreshGhost();
                ShowHideLabel();
            }
        }

        IEnumerator PoofThenMemorize()
        {
            state = State.Poof;
            SetLumi(true);
            for (float t = memorizeSeconds; t > 0f; t -= Time.deltaTime)
            {
                SetLabel($"Lumi: Uită-te bine în jur! ({Mathf.CeilToInt(t)}s)\n<size=40>toate cele 3 comori sunt vizibile</size>");
                yield return null;
            }

            SetLabel("Poof!");
            for (int i = 0; i < Items; i++) StartCoroutine(ScaleBounce(treasures[i].transform, 1f, 0f, 0.35f));
            yield return new WaitForSeconds(0.4f);
            for (int i = 0; i < Items; i++)
            {
                SpawnPuff(hiddenPos[i] + Vector3.up * 0.05f);
                treasures[i].SetActive(false);
            }
            hideTime = Time.time;
            lastGuessMark = Time.time;

            state = State.Memorize;
            SetLumi(false);
            yield return StarTaskRoutine();
            if (ChaosThisRound)
            {
                // Sarcina de distragere: cățeii apar departe de cană → omul TREBUIE să se uite în altă parte
                for (int i = 0; i < attentionPuppies; i++)
                    yield return AttentionPuppy(i + 1, attentionPuppies);
            }

            state = State.Guess;
            itemIndex = 0;
            SetLumi(true);
            if (ChaosThisRound)
            {
                chaos.bubbleCount = bubbleCount;
                chaos.Begin(head, FloorY(), new List<Vector3>(hiddenPos), new List<string>(CurrentSet), chaosIntensity);
                chaos.ResetCounters();
            }
            PromptGuess();
        }

        IEnumerator StarTaskRoutine()
        {
            var host = new GameObject("StarTaskHost");
            starTask = host.AddComponent<StarConstellationTask>();
            starTask.Begin(head, new List<Vector3>(hiddenPos), starTaskSeconds);
            while (!starTask.Finished)
            {
                SetLabel($"Constelația Pierdută · {Mathf.CeilToInt(starTask.Remaining)}s\n{starTask.Status}\n<size=40>{starTask.Hint}</size>");
                yield return null;
            }
            starTask.End();
            Destroy(host);
            starTask = null;
        }

        Vector3 MeanHidden()
        {
            var m = Vector3.zero;
            for (int i = 0; i < Items; i++) m += hiddenPos[i];
            return m / Items;
        }

        /// Un cățel apare în spatele / în lateral față de cană. Trebuie privit ~1 s (sau click/pinch pe el).
        IEnumerator AttentionPuppy(int index, int total)
        {
            // direcția spre cană, văzută de sus
            var toMug = MeanHidden() - head.position; toMug.y = 0f;
            float mugYaw = toMug.sqrMagnitude > 1e-4f ? Mathf.Atan2(toMug.x, toMug.z) * Mathf.Rad2Deg : head.eulerAngles.y;
            // între 120° și 240° față de cană → mereu "în spate", alternativ stânga/dreapta
            float yaw = mugYaw + 180f + (index % 2 == 0 ? 1f : -1f) * Random.Range(0f, 60f);
            var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float floorY = FloorY();
            var pos = new Vector3(head.position.x, floorY, head.position.z) + dir * Random.Range(1.0f, 1.5f);

            var pup = Graybox.Corgi($"AttentionPuppy_{index}");
            pup.transform.position = pos;
            pup.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up); // se uită spre om
            var w = pup.AddComponent<CorgiWander>();
            w.center = pos; w.floorY = floorY; w.radius = 0.25f; w.speed = 0.3f;
            corgis.Add(pup);
            yield return ScaleBounce(pup.transform, 0f, 1f, 0.3f);

            float looked = 0f;
            while (looked < puppyLookSeconds)
            {
                var target = pup.transform.position + Vector3.up * 0.15f;
                float angle = Vector3.Angle(head.forward, target - head.position);
                bool looking = angle < 18f;
                looked = looking ? looked + Time.deltaTime : Mathf.Max(0f, looked - Time.deltaTime * 0.5f);

                string where = angle > 100f ? "în spatele tău" : (Vector3.Dot(head.right, target - head.position) > 0 ? "în dreapta" : "în stânga");
                SetLabel(looking
                    ? $"Mângâi cățelul {index}/{total}... {Mathf.RoundToInt(100f * looked / puppyLookSeconds)}%"
                    : $"Un cățel {where}!\nuită-te la el ca să-l mângâi ({index}/{total})");
                yield return null;
            }

            SpawnPuff(pup.transform.position + Vector3.up * 0.15f);
            corgis.Remove(pup);
            yield return ScaleBounce(pup.transform, 1f, 0f, 0.25f);
            Destroy(pup);
        }

        void PromptGuess()
        {
            string itemName = Graybox.TreasureLabel(CurrentSet[itemIndex]);
            string hint = ChaosThisRound ? "(nu te lăsa distrasă de căței!)" : "țintește locul și fă pinch";
            SetLabel($"{itemIndex + 1}/{Items}: Unde ai ascuns {itemName}?\n<size=40>{hint}</size>");
        }

        void GuessItem(Vector3 guess)
        {
            int i = itemIndex;
            guessPos[i] = guess;
            paws[i].transform.SetPositionAndRotation(guess, YawTowardsHead(guess));
            paws[i].SetActive(true);

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
            errorsAll[(round - 1) * Items + i] = err;

            itemIndex++;
            if (itemIndex >= Items) ShowResult();
            else PromptGuess();
        }

        void ShowResult()
        {
            state = State.Result;
            pawGhost.SetActive(false);
            aimLine.enabled = false;
            reticle.SetActive(false);
            ClearCorgis();
            var set = CurrentSet;
            float sum = 0f;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Items; i++)
            {
                treasures[i].transform.SetPositionAndRotation(hiddenPos[i], YawTowardsHead(hiddenPos[i]));
                treasures[i].SetActive(true);
                StartCoroutine(ScaleBounce(treasures[i].transform, 0f, 1f, 0.35f));

                errorLines[i].enabled = true;
                errorLines[i].SetPosition(0, guessPos[i] + Vector3.up * 0.01f);
                errorLines[i].SetPosition(1, hiddenPos[i] + Vector3.up * 0.01f);

                float e = errorsAll[(round - 1) * Items + i];
                sum += e;
                sb.Append(Graybox.TreasureLabel(set[i])).Append(' ').Append((e * 100f).ToString("0")).Append(" cm");
                if (i < Items - 1) sb.Append(" · ");
            }
            float mean = sum / Items;
            float paws3 = mean / Mathf.Max(0.01f, pawSizeMeters);
            string pawsTxt = paws3 < 1f ? "sub o lăbuță" : paws3.ToString("0.#") + " lăbuțe";
            string next = round >= Rounds ? "Grădina Amintirilor" : "runda următoare";
            SetLabel($"Eroare medie: {pawsTxt} ({mean * 100f:0} cm)\n<size=40>{sb}</size>\n<size=40>pinch pentru {next}</size>");
        }

        // ---------------------------------------------------------------- siguranță

        void OnSafetyStart()
        {
            safetyTriggered = true;
            Time.timeScale = 0f;
            chaos.Freeze();
            for (int i = 0; i < corgis.Count; i++)
            {
                if (corgis[i] == null) continue;
                var w = corgis[i].GetComponent<CorgiWander>();
                if (w != null) w.enabled = false;
                ChaosBehaviours.Lay(corgis[i].transform, true);
            }
            if (starTask != null) starTask.SetVisible(false);
            label.gameObject.SetActive(false);
            aimLine.enabled = false;
            reticle.SetActive(false);
            if (ghost != null) ghost.SetActive(false);
            pawGhost.SetActive(false);
        }

        void OnSafetyFinish(bool continuePlay)
        {
            Time.timeScale = 1f;
            label.gameObject.SetActive(true);
            if (!continuePlay)
            {
                EnterDebriefing();
                return;
            }
            chaosIntensity = 0.5f;
            for (int i = 0; i < corgis.Count; i++)
            {
                if (corgis[i] == null) continue;
                var w = corgis[i].GetComponent<CorgiWander>();
                if (w != null) w.enabled = true;
                ChaosBehaviours.Lay(corgis[i].transform, false);
            }
            if (starTask != null) starTask.SetVisible(true);
            if (chaos.Running) chaos.Resume(chaosIntensity);
        }

        // ---------------------------------------------------------------- debriefing

        const string Card1 =
            "Lumi: Creierul nu poate ține totul\n" +
            "în față deodată: își împarte atenția.\n" +
            "Când ți-a fost distrasă atenția,\n" +
            "amintirea s-a estompat. Așa\n" +
            "funcționăm toți, nu e o greșeală.\n" +
            "<size=40>pinch → mai departe</size>";

        const string Card2 =
            "Lumi: Pentru oameni cu ADHD, autism\n" +
            "sau suprasolicitare senzorială,\n" +
            "fiecare zi seamănă cu Ziua cu Zoomies.\n" +
            "Dacă uită ceva, nu e lene sau\n" +
            "neatenție: creierul lor muncește mai mult.\n" +
            "<size=40>pinch → mai departe</size>";

        const string Card3 =
            "Lumi: Promit să am răbdare când\n" +
            "cineva uită ceva și să întreb\n" +
            "'cum te pot ajuta?'\n" +
            "în loc de 'cum de ai uitat?'.\n" +
            "Ți-am pus o steluță aurie în borcan!\n" +
            "<size=40>pinch → mai departe</size>";

        const string CardEnd =
            "Mulțumim că ai jucat!\n" +
            "Steluța ta e în borcanul cu promisiuni.\n" +
            "<size=40>pinch pentru o sesiune nouă</size>";

        void EnterDebriefing()
        {
            StopAllCoroutines();
            if (starTask != null)
            {
                starTask.End();
                Destroy(starTask.gameObject);
                starTask = null;
            }
            ClearRoundObjects();
            pawGhost.SetActive(false);
            aimLine.enabled = false;
            reticle.SetActive(false);
            Time.timeScale = 1f;
            label.gameObject.SetActive(true);
            state = State.Debriefing;
            safety.armed = false;
            debriefStep = 0;
            BuildGarden();
            SetLabel("Grădina Amintirilor\nRândul din față: prima rundă\nRândul din spate: a doua rundă\nFloare mai înaltă = ai nimerit mai bine\n<size=40>pinch → Lumi vrea să-ți spună ceva</size>");
        }

        string RowTitle(int r)
        {
            return ChaosFor(r + 1) ? "Ziua cu Zoomies" : "Ziua Liniștită";
        }

        void BuildGarden()
        {
            ClearGarden();
            var f = head.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            f.Normalize();
            var center = new Vector3(head.position.x, head.position.y - 0.75f, head.position.z) + f * 1.2f;
            garden = new GameObject("MemoryGarden");
            garden.transform.SetPositionAndRotation(center, Quaternion.LookRotation(-f, Vector3.up));
            var gt = garden.transform;
            Graybox.Prim(PrimitiveType.Cylinder, gt, new Vector3(0f, -0.01f, 0f), new Vector3(0.9f, 0.01f, 0.55f), Graybox.MakeMat(new Color(0.55f, 0.82f, 0.6f)));

            Color[] calmCols = { new Color(0.45f, 0.7f, 1f), new Color(0.4f, 0.9f, 0.8f), new Color(0.55f, 0.85f, 0.55f) };
            Color[] chaosCols = { new Color(1f, 0.5f, 0.75f), new Color(0.8f, 0.55f, 0.95f), new Color(1f, 0.6f, 0.9f) };

            for (int r = 0; r < Rounds; r++)
            {
                var cols = ChaosFor(r + 1) ? chaosCols : calmCols;
                float z = r == 0 ? 0.13f : -0.13f;
                float sum = 0f;
                int n = 0;
                for (int i = 0; i < Items; i++)
                {
                    float err = errorsAll[r * Items + i];
                    float hs = float.IsNaN(err) ? 0.4f : Mathf.Clamp(1.5f - err * 2f, 0.4f, 1.5f);
                    if (!float.IsNaN(err)) { sum += err; n++; }
                    var flower = Graybox.MemoryFlower("Flower_" + r + "_" + i, cols[i], hs);
                    flower.transform.SetParent(gt, false);
                    flower.transform.localPosition = new Vector3((i - 1) * 0.22f, 0f, z);
                    StartCoroutine(Grow(flower.transform, 0.3f * (r * Items + i)));
                }
                var tm = Graybox.Text("RowLabel_" + r, 0.0026f);
                tm.richText = true;
                string meanTxt = n > 0 ? (sum / n * 100f).ToString("0") + " cm eroare medie" : "fără date";
                tm.text = RowTitle(r) + "\n<size=40>" + meanTxt + "</size>";
                tm.color = cols[0];
                tm.transform.SetParent(gt, false);
                tm.transform.localPosition = new Vector3(0f, r == 0 ? 0.62f : 0.76f, z);
                tm.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }

            jar = Graybox.StarJar("PromiseJar");
            jar.transform.SetParent(gt, false);
            jar.transform.localPosition = new Vector3(-0.6f, 0f, 0f);
            var jl = Graybox.Text("JarLabel", 0.0024f);
            jl.text = "Borcanul cu promisiuni";
            jl.transform.SetParent(jar.transform, false);
            jl.transform.localPosition = new Vector3(0f, 0.28f, 0f);
            jl.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            lumi.SetActive(true);
            lumiFollow = false;
            lumi.transform.localScale = Vector3.one * 1.5f;
            lumi.transform.position = garden.transform.TransformPoint(new Vector3(0.62f, 0.25f, 0.05f));
            lumi.transform.rotation = YawTowardsHead(lumi.transform.position);
        }

        IEnumerator Grow(Transform t, float delay)
        {
            t.localScale = Vector3.zero;
            yield return new WaitForSeconds(delay);
            yield return ScaleBounce(t, 0f, 1f, 0.7f);
        }

        void AdvanceDebrief()
        {
            debriefStep++;
            if (debriefStep == 1) SetLabel(Card1);
            else if (debriefStep == 2) SetLabel(Card2);
            else if (debriefStep == 3)
            {
                SetLabel(Card3);
                AddPromiseStar();
            }
            else if (debriefStep == 4) SetLabel(CardEnd);
            else RestartSession();
        }

        void AddPromiseStar()
        {
            if (jar == null || jarStar != null) return;
            jarStar = Graybox.Star("PromiseStar", new Color(1f, 0.82f, 0.25f), 0.06f);
            jarStar.transform.SetParent(jar.transform, false);
            jarStar.transform.localPosition = new Vector3(0f, 0.09f, 0f);
            StartCoroutine(ScaleBounce(jarStar.transform, 0f, 1f, 0.6f));
        }

        void ClearGarden()
        {
            if (garden != null) Destroy(garden);
            garden = null;
            jar = null;
            jarStar = null;
            if (lumi != null)
            {
                lumi.SetActive(false);
                lumi.transform.localScale = Vector3.one;
            }
        }

        void SetLumi(bool on)
        {
            if (lumi == null) return;
            lumi.SetActive(on);
            lumiFollow = on;
            lumiSnap = on;
            lumi.transform.localScale = Vector3.one;
        }

        void UpdateLumi()
        {
            if (lumi == null || !lumi.activeSelf || !lumiFollow) return;
            var f = head.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            f.Normalize();
            var r = Vector3.Cross(Vector3.up, f);
            var target = head.position + f * 0.85f - r * 0.36f + Vector3.up * (-0.3f + Mathf.Sin(Time.time * 2f) * 0.02f);
            if (lumiSnap)
            {
                lumi.transform.position = target;
                lumiSnap = false;
            }
            else lumi.transform.position = Vector3.Lerp(lumi.transform.position, target, 4f * Time.deltaTime);
            lumi.transform.rotation = YawTowardsHead(lumi.transform.position);
        }

        // ---------------------------------------------------------------- input

        /// Raza + "click" (pinch / trigger / mouse). Întoarce false dacă nu avem cu ce ținti.
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
                OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.Hands) ||          // pinch prin OVRInput
                OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
                OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch) ||
                OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch);

            bool mouseDown = false;
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            bool xrActive = UnityEngine.XR.XRSettings.isDeviceActive;
            if (!xrActive && Mouse.current != null) mouseDown = Mouse.current.leftButton.wasPressedThisFrame;
#endif
            click = Debounce(pinchDown || buttonDown || mouseDown);

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

        // ---------------------------------------------------------------- suprafețe

        /// Adâncime live (forma reală: haine, perne) → MRUK (cutiile din Space Setup) → collider → plan virtual.
        bool TryHitSurface(Ray ray, out Vector3 point, out Vector3 normal, out string surface)
        {
            point = default; normal = Vector3.up; surface = "";

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
                    return normal.y >= depthMinUpDot;
                }
                depthStatus = ok ? "prea aproape (mâna?)" : eh.status.ToString();
            }

            var room = Room;
            if (room != null && room.Raycast(ray, maxRayDistance, out RaycastHit h, out MRUKAnchor anchor))
            {
                point = h.point; normal = h.normal;
                surface = anchor != null ? anchor.name : "room";
                return normal.y >= minSurfaceUpDot;
            }

            if (Physics.Raycast(ray, out RaycastHit ph, maxRayDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                point = ph.point; normal = ph.normal;
                surface = ph.collider.name;
                return normal.y >= minSurfaceUpDot;
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

        // ---------------------------------------------------------------- vizual

        Quaternion YawTowardsHead(Vector3 p)
        {
            if (head == null) return Quaternion.identity;
            var toHead = head.position - p; toHead.y = 0f;
            return toHead.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toHead.normalized, Vector3.up) : Quaternion.identity;
        }

        void SetLabel(string text)
        {
            // TextMesh înțelege <size=..> doar cu richText activ
            label.richText = true;
            label.text = text;
        }

        /// Textul e lipit de cameră (ca un HUD), deci e mereu în fața ochilor.
        void UpdateLabelPose()
        {
            if (label.transform.parent != head)
            {
                label.transform.SetParent(head, false);
                label.transform.localPosition = new Vector3(0f, -0.05f, labelDistance);
                label.transform.localRotation = Quaternion.identity;
                debugLabel.transform.SetParent(head, false);
                debugLabel.transform.localPosition = new Vector3(0f, -0.25f, labelDistance);
                debugLabel.transform.localRotation = Quaternion.identity;
                Debug.Log($"[MindThePaws] Text atașat de camera '{head.name}'");
            }

            debugLabel.gameObject.SetActive(showDebug);
            if (showDebug)
            {
                var room = Room;
                debugLabel.text = $"{debugInfo}\ncameră MRUK: {(room != null ? "da" : "nu")} · suprafață: {lastSurface} · adâncime: {depthStatus} · t={Time.time:0}s\nshader: {Graybox.ShaderInfo} · stare: {state} · rundă {round}/{Rounds} · siguranță: {(safetyTriggered ? "DA" : "nu")}";
            }
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
                float k = e / dur;
                go.transform.position = start + velocity * k;
                go.transform.localScale = Vector3.one * (1f + k) * (1f - k);
                yield return null;
            }
            Destroy(go);
        }

        void ClearCorgis()
        {
            foreach (var c in corgis) if (c != null) Destroy(c);
            corgis.Clear();
            if (chaos != null) chaos.Clear();
        }
    }
}