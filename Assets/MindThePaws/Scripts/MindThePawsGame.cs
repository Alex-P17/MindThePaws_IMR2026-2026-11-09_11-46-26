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
        enum State { Hide, Poof, Memorize, Guess, Result }

        [Header("Referințe (se găsesc singure dacă rămân goale)")]
        public OVRCameraRig cameraRig;
        public TelemetryLogger logger;

        [Header("Joc")]
        public float memorizeSeconds = 5f;
        public float pawSizeMeters = 0.10f;
        public float maxRayDistance = 6f;
        [Range(0f, 1f)] public float minSurfaceUpDot = 0.7f;   // doar suprafețe orizontale
        public float fallbackSurfaceBelowHead = 0.6f;          // plan virtual dacă nu există Space Setup

        public enum ChaosMode { Calm, Chaos, Alternate }

        [Header("Haos")]
        [Tooltip("Calm: fără distrageri. Chaos: mereu cu distrageri. Alternate: runde impare calm, pare haos (bun pentru studiu).")]
        public ChaosMode chaosMode = ChaosMode.Alternate;
        [Tooltip("Câți căței trebuie 'mângâiați' (priviți) înainte de ghicire. Apar mereu departe de locul cănii.")]
        public int attentionPuppies = 3;
        public float puppyLookSeconds = 0.8f;
        [Tooltip("Corgi care țopăie pe podea în timpul ghicirii.")]
        public int corgiCount = 3;
        public int bubbleCount = 10;

        bool ChaosThisRound => chaosMode == ChaosMode.Chaos || (chaosMode == ChaosMode.Alternate && trial % 2 == 0);
        readonly List<GameObject> bubbles = new List<GameObject>();

        [Header("Text")]
        public float labelCharacterSize = 0.004f;
        public float labelDistance = 0.9f;

        State state = State.Hide;
        Transform head;
        readonly List<OVRHand> hands = new List<OVRHand>();
        float nextHandSearch;
        bool wasPinching;
        float lastClickTime = -10f;

        GameObject mug, mugGhost, paw, pawGhost;
        LineRenderer aimLine, errorLine;
        TextMesh label;
        Material rayOk, rayBad;
        readonly List<GameObject> corgis = new List<GameObject>();

        Vector3 hiddenPos;
        string hiddenSurface = "";
        float hideTime, guessStartTime;
        int trial;
        float? fallbackY;
        string lastSurface = "";
        float startTime;
        TextMesh debugLabel;
        string debugInfo = "";
        string aimSource = "";
        GameObject reticle;
        bool aimIsGaze;

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

            mug = Graybox.Mug("Mug", new Color(0.96f, 0.66f, 0.75f));
            mugGhost = Graybox.Mug("MugGhost", new Color(1f, 0.88f, 0.92f));
            paw = Graybox.Paw("PawMarker", new Color(0.55f, 0.38f, 0.28f));
            pawGhost = Graybox.Paw("PawGhost", new Color(0.85f, 0.72f, 0.62f));
            mug.SetActive(false); mugGhost.SetActive(false); paw.SetActive(false); pawGhost.SetActive(false);

            aimLine = Graybox.Line("AimRay", Color.white, 0.008f);
            reticle = new GameObject("Reticle");
            Graybox.Prim(PrimitiveType.Sphere, reticle.transform, Vector3.zero, Vector3.one * 0.025f, Graybox.MakeMat(Color.white));
            reticle.SetActive(false);
            rayOk = aimLine.sharedMaterial;
            rayBad = Graybox.MakeMat(new Color(1f, 0.45f, 0.45f));
            errorLine = Graybox.Line("ErrorLine", new Color(1f, 0.85f, 0.3f), 0.006f);
            errorLine.enabled = false;

            label = Graybox.Text("Label", labelCharacterSize);
            debugLabel = Graybox.Text("DebugLabel", labelCharacterSize * 0.5f);
            debugLabel.color = new Color(1f, 1f, 0.6f);
            startTime = Time.time;
            StartTrial();
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

            try { UpdateLabelPose(); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        void Tick()
        {
            bool hasAim = GetAim(out Ray ray, out bool click);
            Vector3 point = default;
            bool hit = false;
            if (hasAim) hit = TryHitSurface(ray, out point, out _, out lastSurface);

            // raza + punctul de la capăt
            bool showRay = hasAim && (state == State.Hide || state == State.Guess);
            Vector3 end = hit ? point : ray.GetPoint(1.5f);
            aimLine.enabled = showRay && !aimIsGaze;   // la privire, raza ar fi chiar în ochi → doar punct
            if (aimLine.enabled)
            {
                aimLine.SetPosition(0, ray.origin);
                aimLine.SetPosition(1, end);
                aimLine.sharedMaterial = hit ? rayOk : rayBad;
            }
            reticle.SetActive(showRay);
            if (showRay)
            {
                reticle.transform.position = end;
                reticle.GetComponentInChildren<Renderer>().sharedMaterial = hit ? rayOk : rayBad;
            }

            Quaternion facing = YawTowardsHead(point);

            switch (state)
            {
                case State.Hide:
                    mugGhost.SetActive(hit);
                    if (hit) mugGhost.transform.SetPositionAndRotation(point, facing);
                    if (hit && click)
                    {
                        mugGhost.SetActive(false);
                        hiddenPos = point;
                        hiddenSurface = lastSurface;
                        mug.transform.SetPositionAndRotation(point, facing);
                        mug.transform.localScale = Vector3.one;
                        mug.SetActive(true);
                        StartCoroutine(PoofThenMemorize());
                    }
                    else if (Time.time - startTime > 5f && Room == null && trial == 1)
                    {
                        SetLabel("Ascunde cana:\nțintește o suprafață și fă pinch\n<size=40>(fără Space Setup → plan virtual)</size>");
                    }
                    break;

                case State.Guess:
                    pawGhost.SetActive(hit);
                    if (hit) pawGhost.transform.SetPositionAndRotation(point, facing);
                    if (hit && click) Evaluate(point);
                    break;

                case State.Result:
                    if (click) StartTrial();
                    break;
            }
        }

        // ---------------------------------------------------------------- stări

        void StartTrial()
        {
            trial++;
            state = State.Hide;
            fallbackY = null;
            mug.SetActive(false);
            paw.SetActive(false);
            errorLine.enabled = false;
            ClearCorgis();
            SetLabel($"{logger.ParticipantId} · Runda {trial} ({(ChaosThisRound ? "haos" : "calm")})\nAscunde cana:\nțintește o suprafață și fă pinch");
        }

        IEnumerator PoofThenMemorize()
        {
            state = State.Poof;
            SetLabel("Gata! Ține minte unde e...");
            yield return new WaitForSeconds(1.2f);

            SetLabel("Poof!");
            yield return ScaleBounce(mug.transform, 1f, 0f, 0.35f);
            SpawnPuff(hiddenPos + Vector3.up * 0.05f);
            mug.SetActive(false);
            hideTime = Time.time;

            state = State.Memorize;
            if (ChaosThisRound)
            {
                // Sarcina de distragere: cățeii apar departe de cană → omul TREBUIE să se uite în altă parte
                for (int i = 0; i < attentionPuppies; i++)
                    yield return AttentionPuppy(i + 1, attentionPuppies);
                SpawnCorgis();
                SpawnBubbles();
            }
            else
            {
                for (float t = memorizeSeconds; t > 0f; t -= Time.deltaTime)
                {
                    SetLabel($"Ține minte unde era cana...\n{Mathf.CeilToInt(t)}");
                    yield return null;
                }
            }

            state = State.Guess;
            guessStartTime = Time.time;
            SetLabel(ChaosThisRound
                ? "Unde era cana?\n<size=40>(nu te lăsa distrasă de căței!)</size>"
                : "Unde era cana?\nțintește locul și fă pinch");
        }

        /// Un cățel apare în spatele / în lateral față de cană. Trebuie privit ~1 s (sau click/pinch pe el).
        IEnumerator AttentionPuppy(int index, int total)
        {
            // direcția spre cană, văzută de sus
            var toMug = hiddenPos - head.position; toMug.y = 0f;
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

        /// Bule care plutesc în fața ochilor în timpul ghicirii (ocluzie vizuală).
        void SpawnBubbles()
        {
            var mat = Graybox.MakeMat(new Color(0.8f, 0.92f, 1f));
            for (int i = 0; i < bubbleCount; i++)
            {
                var b = new GameObject("Bubble");
                Graybox.Prim(PrimitiveType.Sphere, b.transform, Vector3.zero, Vector3.one * Random.Range(0.03f, 0.07f), mat);
                b.transform.position = BubbleSpot();
                bubbles.Add(b);
                StartCoroutine(BubbleFloat(b));
            }
        }

        Vector3 BubbleSpot()
        {
            var off = new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(-0.35f, 0.15f), Random.Range(0.35f, 0.8f));
            return head.TransformPoint(off);
        }

        IEnumerator BubbleFloat(GameObject b)
        {
            float phase = Random.value * 10f;
            while (b != null)
            {
                b.transform.position += (Vector3.up * 0.05f + new Vector3(Mathf.Sin(Time.time + phase), 0f, Mathf.Cos(Time.time * 0.7f + phase)) * 0.03f) * Time.deltaTime;
                // dacă a plecat din câmpul vizual, reapare în fața ochilor
                var local = head.InverseTransformPoint(b.transform.position);
                if (local.z < 0.25f || local.z > 1.2f || Mathf.Abs(local.x) > 0.7f || local.y > 0.5f)
                    b.transform.position = BubbleSpot();
                yield return null;
            }
        }

        void ClearBubbles()
        {
            foreach (var b in bubbles) if (b != null) Destroy(b);
            bubbles.Clear();
        }

        void Evaluate(Vector3 guess)
        {
            state = State.Result;
            pawGhost.SetActive(false);
            aimLine.enabled = false;
            ClearCorgis();

            paw.transform.SetPositionAndRotation(guess, YawTowardsHead(guess));
            paw.SetActive(true);

            float err = Vector3.Distance(hiddenPos, guess);
            var d = hiddenPos - guess; d.y = 0f;
            float errH = d.magnitude;
            float angle = Vector3.Angle(hiddenPos - head.position, guess - head.position);
            float paws = err / Mathf.Max(0.01f, pawSizeMeters);

            mug.transform.SetPositionAndRotation(hiddenPos, YawTowardsHead(hiddenPos));
            mug.SetActive(true);
            StartCoroutine(ScaleBounce(mug.transform, 0f, 1f, 0.35f));

            errorLine.enabled = true;
            errorLine.SetPosition(0, guess + Vector3.up * 0.01f);
            errorLine.SetPosition(1, hiddenPos + Vector3.up * 0.01f);

            string pawsTxt = paws < 1f ? "Sub o lăbuță" : $"La {paws:0.#} lăbuțe";
            SetLabel($"{pawsTxt} distanță ({err * 100f:0} cm)\n<size=40>pinch pentru runda următoare</size>");

            logger.LogTrial(trial, "mug", ChaosThisRound ? "chaos" : "calm", hiddenSurface,
                hiddenPos, guess, err, errH, angle,
                Time.time - hideTime, Time.time - guessStartTime);
        }

        // ---------------------------------------------------------------- input

        /// Raza + "click" (pinch / trigger / mouse). Întoarce false dacă nu avem cu ce ținti.
        bool GetAim(out Ray ray, out bool click)
        {
            ray = default;
            click = false;
            aimIsGaze = false;

            // caută mâinile periodic (building block-urile le pot crea după Start)
            if (Time.time >= nextHandSearch)
            {
                hands.Clear();
                hands.AddRange(FindObjectsByType<OVRHand>(FindObjectsSortMode.None));
                nextHandSearch = Time.time + 2f;
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
            if (!pressed || Time.time - lastClickTime < 0.4f) return false;
            lastClickTime = Time.time;
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
                debugLabel.text = $"{debugInfo}\ncameră MRUK: {(room != null ? "da" : "nu")} · suprafață: {lastSurface} · adâncime: {depthStatus} · t={Time.time:0}s\nshader: {Graybox.ShaderInfo} · cană-fantomă: {(mugGhost.activeSelf ? "ACTIVĂ" : "ascunsă")} · rază: {(aimLine.enabled ? "pornită" : "oprită")}";
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
                float k = e / dur;
                float s = Mathf.LerpUnclamped(from, to, k) + Mathf.Sin(k * Mathf.PI) * 0.25f;
                t.localScale = Vector3.one * Mathf.Max(0f, s);
                yield return null;
            }
            t.localScale = Vector3.one * to;
        }

        void SpawnPuff(Vector3 at)
        {
            var mat = Graybox.MakeMat(new Color(1f, 1f, 1f));
            for (int i = 0; i < 10; i++)
            {
                var root = new GameObject("Puff");
                root.transform.position = at;
                Graybox.Prim(PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one * 0.03f, mat);
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

        void SpawnCorgis()
        {
            float floorY = FloorY();
            var center = new Vector3(head.position.x, floorY, head.position.z);
            for (int i = 0; i < corgiCount; i++)
            {
                var c = Graybox.Corgi($"Corgi_{i}");
                var off = Random.insideUnitCircle.normalized * Random.Range(0.6f, 1.4f);
                c.transform.position = center + new Vector3(off.x, 0f, off.y);
                var w = c.AddComponent<CorgiWander>();
                w.center = center;
                w.floorY = floorY;
                w.radius = 1.5f;
                corgis.Add(c);
            }
        }

        void ClearCorgis()
        {
            foreach (var c in corgis) if (c != null) Destroy(c);
            corgis.Clear();
            ClearBubbles();
        }
    }
}
