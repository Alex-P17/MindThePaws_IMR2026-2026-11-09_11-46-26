using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MindThePaws
{
    public class ChaosBehaviours : MonoBehaviour
    {
        public float lookAngle = 15f;
        public int bubbleCount = 10;
        public float zoomieSpeed = 1.1f;
        [Tooltip("Raza cercului pe care aleargă Mochi în jurul tău (m). Înainte 0.6 m: era aproape mereu sub / în spatele tău.")]
        public float zoomieRadius = 1.2f;
        public int distractionLookCount;
        public float distractionLookSeconds;

        class GazeTarget
        {
            public Transform t;
            public float lift;
            public bool looking;
        }

        public static readonly string[] PuppyNames = { "Mochi", "Gogoașă", "Fasolică" };
        public static readonly string[] PuppyRoles = { "Zoomie", "Hoțomanul", "Lipicioasa" };
        public static readonly string[] PuppyKeys = { "corgi_mochi", "corgi_gogoasa", "corgi_fasolica" };
        public static readonly Color[] CollarColors =
        {
            new Color(0.45f, 0.68f, 1f),   // Mochi: zgardă albastră
            new Color(1f, 0.55f, 0.75f),   // Gogoașă: zgardă roz
            new Color(0.45f, 0.82f, 0.55f) // Fasolică: zgardă verde
        };

        [Tooltip("Biscuit „Lătrătorul” (al 4-lea cățel, doar în faza de căutare).")]
        public bool includeBarker = true;

        Transform head;
        float floorY;
        List<Vector3> positions = new List<Vector3>();
        List<string> types = new List<string>();
        float intensity = 1f;
        bool running;
        bool frozen;
        int puppyStage;          // câți dintre cei 3 căței sunt în scenă
        bool extrasOn;           // pisica, bulele, mirajele, lătrătorul
        readonly bool[] petted = new bool[3];
        readonly GameObject[] puppyObjs = new GameObject[3];
        readonly CozyLabel[] tags = new CozyLabel[3];
        readonly List<GameObject> spawned = new List<GameObject>();
        readonly List<GameObject> puppies = new List<GameObject>();

        // bule de săpun spargibile („Pop!”) din faza de căutare
        sealed class PopBubble
        {
            public GameObject go;
            public float radius;
            public bool mirage;
            public float respawnAt = -1f;
        }
        readonly List<PopBubble> bubbles = new List<PopBubble>();
        public int PopCount { get; private set; }
        [Tooltip("După câte secunde reapare o bulă spartă.")]
        public float bubbleRespawnSeconds = 5f;
        readonly List<GazeTarget> gaze = new List<GazeTarget>();

        public bool Running => running;
        public bool Frozen => frozen;

        /// Pornește Ziua cu Zoomies. Cățeii apar apoi pe rând cu SpawnPuppy(0..2), iar restul haosului cu SpawnGuessExtras().
        public void Begin(Transform headTransform, float floor, List<Vector3> treasurePositions, List<string> treasureTypes, float chaosIntensity)
        {
            Clear();
            head = headTransform;
            floorY = floor;
            positions = new List<Vector3>(treasurePositions);
            types = new List<string>(treasureTypes);
            intensity = Mathf.Clamp(chaosIntensity, 0.25f, 1f);
            running = true;
            frozen = false;
            puppyStage = 0;
            extrasOn = false;
            for (int i = 0; i < 3; i++) petted[i] = false;
        }

        /// Toți cei 3 căței + pisica + bulele (folosit dacă vrei haosul complet dintr-o dată).
        public void BeginAll(Transform headTransform, float floor, List<Vector3> treasurePositions, List<string> treasureTypes, float chaosIntensity)
        {
            Begin(headTransform, floor, treasurePositions, treasureTypes, chaosIntensity);
            for (int i = 0; i < 3; i++) SpawnPuppy(i);
            SpawnGuessExtras();
        }

        public Transform Puppy(int i)
        {
            return i >= 0 && i < 3 && puppyObjs[i] != null ? puppyObjs[i].transform : null;
        }

        public void MarkPetted(int i)
        {
            if (i < 0 || i >= 3) return;
            petted[i] = true;
            if (tags[i] != null)
            {
                tags[i].text = PuppyNames[i] + " <size=40>· mângâiat</size>";
                tags[i].SetPanelColors(new Color(1f, 0.97f, 0.92f, 0.92f), CollarColors[i]);
            }
        }

        /// Puncte de „prindere” pe corpul cățelului (spate, cap, crupă) pentru raza mâinii / mouse-ului.
        public static Vector3[] BodyPoints(Transform t)
        {
            return new[]
            {
                t.TransformPoint(new Vector3(0f, 0.13f, 0f)),
                t.TransformPoint(new Vector3(0f, 0.2f, 0.2f)),
                t.TransformPoint(new Vector3(0f, 0.12f, -0.13f))
            };
        }

        /// Cățelul e fericit: val de inimioare, lătrat vesel, tors și dat din codiță.
        public static void Celebrate(Transform t, float pitch = 1.3f)
        {
            if (t == null) return;
            CozyFx.Hearts(t.position + Vector3.up * 0.28f, 14, 0.045f);
            AudioDirector.Sfx("bark", t.position + Vector3.up * 0.2f, 0.7f, pitch);
            AudioDirector.Sfx("purr", t.position, 0.6f);
            var w = t.GetComponent<HappyWag>();
            if (w == null) w = t.gameObject.AddComponent<HappyWag>();
            w.Play();
        }

        public void Celebrate(int i)
        {
            Celebrate(Puppy(i), 1.25f + 0.08f * i);
        }

        public void Highlight(int i, bool on)
        {
            if (i < 0 || i >= 3 || tags[i] == null) return;
            var c = CollarColors[i];
            tags[i].SetPanelColors(on ? Color.Lerp(Color.white, c, 0.35f) : new Color(1f, 1f, 1f, 0.88f), on ? CozyUI.Gold : c);
        }

        public Transform SpawnPuppy(int i)
        {
            if (!running || head == null || i < 0 || i >= 3) return null;
            if (puppyObjs[i] != null) return puppyObjs[i].transform;
            var center = new Vector3(head.position.x, floorY, head.position.z);
            GameObject go;
            switch (i)
            {
                case 0:
                {
                    // Mochi „Zoomie”: cercuri rapide în jurul picioarelor
                    go = Graybox.Corgi("Mochi_Zoomie", PuppyKeys[0]);
                    go.transform.position = center + new Vector3(zoomieRadius, 0f, 0f);
                    var zr = go.AddComponent<ZoomieRunner>();
                    zr.head = head;
                    zr.floorY = floorY;
                    zr.radius = zoomieRadius;
                    zr.speed = zoomieSpeed * Mathf.Lerp(0.7f, 1f, intensity);
                    break;
                }
                case 1:
                {
                    // Gogoașă „Hoțomanul”: fuge prin cameră cu o momeală (clonă a unei comori) în bot
                    go = Graybox.Corgi("Gogoasa_Thief", PuppyKeys[1]);
                    if (types.Count > 0)
                    {
                        var decoy = Graybox.TreasureByType(types[Random.Range(0, types.Count)], false, true);
                        decoy.transform.SetParent(go.transform, false);
                        decoy.transform.localPosition = new Vector3(0f, 0.2f, 0.27f);
                        decoy.transform.localScale = Vector3.one * 0.55f;
                    }
                    var f = head.forward; f.y = 0f;
                    if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
                    go.transform.position = center + Quaternion.Euler(0f, 70f, 0f) * f.normalized * 1.3f;
                    var tw = go.AddComponent<CorgiWander>();
                    tw.center = center;
                    tw.floorY = floorY;
                    tw.radius = 1.7f;
                    tw.speed = 0.8f * Mathf.Lerp(0.7f, 1f, intensity);
                    break;
                }
                default:
                {
                    // Fasolică „Lipicioasa”: stă lângă o ascunzătoare și dă din cap
                    go = Graybox.Corgi("Fasolica_Clingy", PuppyKeys[2]);
                    var p = positions.Count > 0 ? positions[Random.Range(0, positions.Count)] : center + Vector3.forward;
                    var toHead = head.position - p;
                    toHead.y = 0f;
                    var off = toHead.sqrMagnitude > 1e-4f ? toHead.normalized * 0.16f : Vector3.zero;
                    go.transform.position = new Vector3(p.x + off.x, floorY, p.z + off.z);
                    var cn = go.AddComponent<ClingyNod>();
                    cn.head = head;
                    break;
                }
            }
            puppyObjs[i] = go;
            Track(go, 0.15f, true);
            tags[i] = NameTag(go.transform, i);
            if (petted[i]) MarkPetted(i);
            puppyStage = Mathf.Max(puppyStage, i + 1);
            CozyFx.Burst(go.transform.position + Vector3.up * 0.15f, CollarColors[i], 16, 0.35f, 0.02f);
            AudioDirector.Sfx("bark", go.transform.position + Vector3.up * 0.2f, 0.55f, 1.15f + 0.08f * i);
            return go.transform;
        }

        CozyLabel NameTag(Transform puppy, int i)
        {
            var tag = CozyUI.Label("NameTag_" + PuppyNames[i], PuppyNames[i], 26, CozyUI.Ink, new Color(1f, 1f, 1f, 0.88f), CollarColors[i]);
            tag.transform.SetParent(puppy, false);
            tag.follow = puppy;
            tag.followOffset = new Vector3(0f, 0.42f, 0f);
            tag.faceTarget = head;
            return tag;
        }

        /// Faza de căutare: Pisicuța Spirit, bulele care plutesc în fața ochilor, Bulele-Miraj și (opțional) Lătrătorul.
        public void SpawnGuessExtras()
        {
            if (!running || head == null || extrasOn) return;
            extrasOn = true;

            if (includeBarker)
            {
                var barker = Graybox.Corgi("BarkerPuppy_Biscuit", "corgi_barker");
                var br = barker.AddComponent<BarkerRunner>();
                br.head = head;
                br.floorY = floorY;
                br.intensity = intensity;
                barker.transform.position = br.InitialSpot();
                Track(barker, 0.15f, true);
            }

            var cat = Graybox.SpiritCat("Neon Spirit Cat");
            var cf = cat.AddComponent<CatFlight>();
            cf.head = head;
            cf.speed = intensity;
            Track(cat, 0.12f, false);

            /// Bule care plutesc în fața ochilor în timpul ghicirii (ocluzie vizuală).
            int n = Mathf.RoundToInt(bubbleCount * intensity);
            var bubbleMat = CozyFx.TransparentMat(new Color(0.85f, 0.93f, 1f, 0.45f));
            for (int i = 0; i < n; i++)
            {
                var b = new GameObject("Bubble");
                float size = Random.Range(0.035f, 0.075f);
                Graybox.Prim(PrimitiveType.Sphere, b.transform, Vector3.zero, Vector3.one * size, bubbleMat);
                // reflexie mică, ca să arate a bulă de săpun
                Graybox.Prim(PrimitiveType.Sphere, b.transform, new Vector3(-size * 0.22f, size * 0.22f, -size * 0.3f), Vector3.one * size * 0.22f, CozyFx.TransparentMat(new Color(1f, 1f, 1f, 0.8f)));
                var f = b.AddComponent<BubbleFloater>();
                f.head = head;
                b.transform.position = f.Spot();
                spawned.Add(b);
                bubbles.Add(new PopBubble { go = b, radius = Mathf.Max(0.05f, size * 0.5f + 0.02f) });
            }

            if (types.Count > 0)
            {
                int m = Mathf.Max(1, Mathf.RoundToInt(2f * intensity));
                for (int i = 0; i < m; i++)
                {
                    var bub = Graybox.Bubble("MirageBubble", new Color(0.7f, 0.9f, 1f), 0.11f);
                    var copy = Graybox.TreasureByType(types[Random.Range(0, types.Count)], false, false);
                    copy.transform.SetParent(bub.transform, false);
                    copy.transform.localPosition = new Vector3(0f, -0.03f, 0f);
                    copy.transform.localScale = Vector3.one * 0.35f;
                    bub.transform.position = MirageSpot();
                    var f = bub.AddComponent<BubbleFloater>();
                    f.head = head;
                    f.mirage = true;
                    f.anchor = bub.transform.position;
                    Track(bub, 0f, false);
                    bubbles.Add(new PopBubble { go = bub, radius = 0.14f, mirage = true });
                }
            }
        }

        IEnumerator GrowIn(Transform t)
        {
            for (float e = 0f; e < 0.4f; e += Time.deltaTime)
            {
                if (t == null) yield break;
                t.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, e / 0.4f);
                yield return null;
            }
            if (t != null) t.localScale = Vector3.one;
        }

        /// Raza mâinii / mouse-ului pe o bulă de săpun: întoarce true dacă e țintită (și o sparge la pinch / click).
        public bool PopUnderRay(Ray ray, bool click, out Vector3 point)
        {
            point = default;
            if (!running || frozen) return false;
            int best = -1;
            float bestT = float.MaxValue;
            for (int i = 0; i < bubbles.Count; i++)
            {
                var b = bubbles[i];
                if (b == null || b.go == null || !b.go.activeInHierarchy) continue;
                var oc = b.go.transform.position - ray.origin;
                float t = Vector3.Dot(oc, ray.direction);
                if (t < 0.05f) continue;
                float dist = (oc - ray.direction * t).magnitude;
                if (dist <= b.radius && t < bestT)
                {
                    bestT = t;
                    best = i;
                }
            }
            if (best < 0) return false;
            var bb = bubbles[best];
            point = bb.go.transform.position;
            if (click)
            {
                Pop(bb);
                return true;
            }
            return true;
        }

        void Pop(PopBubble b)
        {
            if (b == null || b.go == null) return;
            var at = b.go.transform.position;
            PopCount++;
            CozyFx.Burst(at, new Color(0.85f, 0.95f, 1f), 16, 0.5f, 0.016f);
            CozyFx.Burst(at, new Color(1f, 0.9f, 0.98f), 10, 0.35f, 0.012f);
            AudioDirector.Sfx("poof", at, 0.6f, 1.8f);
            AudioDirector.Sfx("chime", at, 0.35f, 1.7f);
            Confetti(at, head);
            b.go.SetActive(false);
            b.respawnAt = Time.time + bubbleRespawnSeconds;
        }

        /// Confetti pastelat discret: la locul bulei sparte și o mică ploaie deasupra jucătorului.
        public static void Confetti(Vector3 at, Transform head)
        {
            Color[] cols =
            {
                new Color(1f, 0.7f, 0.8f), new Color(0.7f, 0.86f, 1f), new Color(1f, 0.9f, 0.6f), new Color(0.72f, 0.92f, 0.78f), new Color(0.85f, 0.76f, 1f)
            };
            for (int i = 0; i < cols.Length; i++)
                CozyFx.Burst(at, cols[i], 5, 0.7f, 0.012f, null, 0.35f);
            if (head != null)
            {
                var above = head.position + head.forward * 0.5f + Vector3.up * 0.45f;
                for (int i = 0; i < cols.Length; i++)
                    CozyFx.Burst(above + Random.insideUnitSphere * 0.25f, cols[i], 3, 0.25f, 0.01f, null, 0.25f);
            }
        }

        void Track(GameObject go, float lift, bool puppy)
        {
            spawned.Add(go);
            if (puppy) puppies.Add(go);
            gaze.Add(new GazeTarget { t = go.transform, lift = lift });
        }

        Vector3 MirageSpot()
        {
            for (int tries = 0; tries < 12; tries++)
            {
                var fwd = head.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                fwd.Normalize();
                var dir = Quaternion.Euler(0f, Random.Range(-45f, 45f), 0f) * fwd;
                var p = new Vector3(head.position.x, 0f, head.position.z) + dir * Random.Range(1f, 1.8f);
                p.y = floorY + Random.Range(0.9f, 1.5f);
                bool ok = true;
                for (int i = 0; i < positions.Count; i++)
                {
                    var d = positions[i] - p;
                    d.y = 0f;
                    if (d.magnitude < 0.6f) ok = false;
                }
                if (ok) return p;
            }
            return head.position + head.forward * 1.2f + Vector3.up * 0.2f;
        }

        void Update()
        {
            if (!running || frozen || head == null) return;
            for (int i = 0; i < bubbles.Count; i++)
            {
                var b = bubbles[i];
                if (b == null || b.go == null || b.go.activeSelf || b.respawnAt < 0f || Time.time < b.respawnAt) continue;
                b.respawnAt = -1f;
                var f = b.go.GetComponent<BubbleFloater>();
                if (b.mirage)
                {
                    var spot = MirageSpot();
                    b.go.transform.position = spot;
                    if (f != null) f.anchor = spot;
                }
                else if (f != null) b.go.transform.position = f.Spot();
                b.go.SetActive(true);
                StartCoroutine(GrowIn(b.go.transform));
            }
            bool any = false;
            for (int i = gaze.Count - 1; i >= 0; i--)
            {
                var g = gaze[i];
                if (g.t == null)
                {
                    gaze.RemoveAt(i);
                    continue;
                }
                var target = g.t.position + Vector3.up * g.lift;
                bool looking = Vector3.Angle(head.forward, target - head.position) < lookAngle;
                if (looking && !g.looking) distractionLookCount++;
                g.looking = looking;
                if (looking) any = true;
            }
            if (any) distractionLookSeconds += Time.deltaTime;
        }

        public void TakeCounters(out int count, out float seconds)
        {
            count = distractionLookCount;
            seconds = distractionLookSeconds;
            distractionLookCount = 0;
            distractionLookSeconds = 0f;
        }

        public void ResetCounters()
        {
            distractionLookCount = 0;
            distractionLookSeconds = 0f;
        }

        public void Freeze()
        {
            if (!running || frozen) return;
            frozen = true;
            for (int i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] == null) continue;
                var bs = spawned[i].GetComponentsInChildren<MonoBehaviour>();
                for (int j = 0; j < bs.Length; j++) bs[j].enabled = false;
            }
            for (int i = 0; i < puppies.Count; i++)
                if (puppies[i] != null) Lay(puppies[i].transform, true);
        }

        public void Resume(float newIntensity)
        {
            if (!running || head == null) return;
            int stage = puppyStage;
            bool extras = extrasOn;
            var pet = (bool[])petted.Clone();
            Begin(head, floorY, positions, types, newIntensity);
            for (int i = 0; i < 3; i++) petted[i] = pet[i];
            for (int i = 0; i < stage; i++) SpawnPuppy(i);
            if (extras) SpawnGuessExtras();
        }

        public void Clear()
        {
            for (int i = 0; i < spawned.Count; i++)
                if (spawned[i] != null) Destroy(spawned[i]);
            spawned.Clear();
            puppies.Clear();
            gaze.Clear();
            bubbles.Clear();
            for (int i = 0; i < 3; i++)
            {
                puppyObjs[i] = null;
                tags[i] = null;
            }
            puppyStage = 0;
            extrasOn = false;
            running = false;
            frozen = false;
        }

        void OnDestroy()
        {
            Clear();
        }

        public static void Lay(Transform t, bool lying)
        {
            var marker = t.GetComponent<LyingMarker>();
            bool isLying = marker != null;
            if (lying == isLying) return;
            // model din Blender cu Sleep_Lay: îl adoarme animația, nu mai rotim rădăcina pe o parte
            var na = t.GetComponent<NativeAnim>();
            if (na != null && na.HasSleep)
            {
                if (lying) t.gameObject.AddComponent<LyingMarker>();
                else Destroy(marker);
                na.SetSleeping(lying);
                return;
            }
            float yaw = t.eulerAngles.y;
            if (lying)
            {
                t.gameObject.AddComponent<LyingMarker>();
                t.rotation = Quaternion.Euler(0f, yaw, 90f);
                t.position += Vector3.up * 0.08f;
            }
            else
            {
                Destroy(marker);
                t.rotation = Quaternion.Euler(0f, yaw, 0f);
                t.position -= Vector3.up * 0.08f;
            }
        }
    }

    public sealed class LyingMarker : MonoBehaviour
    {
    }

    public sealed class ZoomieRunner : MonoBehaviour
    {
        public Transform head;
        public float floorY;
        public float speed = 1.1f;
        public float radius = 0.55f;
        float angle;

        void Update()
        {
            if (head == null) return;
            angle += speed / radius * Time.deltaTime;
            var c = new Vector3(head.position.x, floorY, head.position.z);
            var pos = c + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            var tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            pos.y += Mathf.Abs(Mathf.Sin(Time.time * 12f)) * 0.03f;
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
        }
    }

    public sealed class ClingyNod : MonoBehaviour
    {
        public Transform head;
        NativeAnim native;

        void Start()
        {
            native = GetComponent<NativeAnim>();
        }

        void Update()
        {
            if (head == null) return;
            var d = head.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) return;
            var yaw = Quaternion.LookRotation(d.normalized, Vector3.up);
            // dacă modelul are clipul Clingy_Nod, capul îl mișcă animația; aici doar se întoarce spre jucător
            float amp = native != null && native.HasSpecial ? 0f : 7f;
            var nod = Quaternion.Euler(Mathf.Sin(Time.time * 3.5f) * amp, 0f, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, yaw * nod, 6f * Time.deltaTime);
        }
    }

    public sealed class CatFlight : MonoBehaviour
    {
        public Transform head;
        public float speed = 1f;
        float t;
        float yaw;
        bool init;

        Vector3 Pos(float tt, Quaternion frame)
        {
            return head.position + frame * new Vector3(Mathf.Sin(tt) * 0.85f, Mathf.Sin(2f * tt) * 0.3f - 0.05f, 1.5f);
        }

        void Update()
        {
            if (head == null) return;
            var fwd = head.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            float headYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            if (!init)
            {
                yaw = headYaw;
                init = true;
            }
            yaw = Mathf.LerpAngle(yaw, headYaw, 0.8f * Time.deltaTime);
            t += Time.deltaTime * speed * 0.9f;
            var frame = Quaternion.Euler(0f, yaw, 0f);
            var p = Pos(t, frame);
            var dir = Pos(t + 0.05f, frame) - p;
            transform.position = p;
            if (dir.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up), 6f * Time.deltaTime);
        }
    }

    public sealed class BubbleFloater : MonoBehaviour
    {
        public Transform head;
        public bool mirage;
        public Vector3 anchor;
        float phase;

        void Awake()
        {
            phase = Random.value * 10f;
        }

        public Vector3 Spot()
        {
            var off = new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(-0.35f, 0.15f), Random.Range(0.35f, 0.8f));
            return head.TransformPoint(off);
        }

        void Update()
        {
            if (head == null) return;
            if (mirage)
            {
                transform.position = anchor + new Vector3(Mathf.Sin(Time.time * 0.5f + phase) * 0.08f, Mathf.Sin(Time.time * 0.8f + phase) * 0.06f, Mathf.Cos(Time.time * 0.4f + phase) * 0.08f);
                transform.Rotate(0f, 20f * Time.deltaTime, 0f);
                return;
            }
            transform.position += (Vector3.up * 0.05f + new Vector3(Mathf.Sin(Time.time + phase), 0f, Mathf.Cos(Time.time * 0.7f + phase)) * 0.03f) * Time.deltaTime;
            // dacă a plecat din câmpul vizual, reapare în fața ochilor
            var local = head.InverseTransformPoint(transform.position);
            if (local.z < 0.25f || local.z > 1.2f || Mathf.Abs(local.x) > 0.7f || local.y > 0.5f)
                transform.position = Spot();
        }
    }

    /// <summary>
    /// Biscuit "Lătrătorul": aleargă în jurul jucătorului în spate / lateral (110°–250° față de privire),
    /// se oprește periodic, se întoarce spre jucător, latră 3D și scoate 3 inele sonore roz-aurii din botic.
    /// </summary>
    public sealed class BarkerRunner : MonoBehaviour
    {
        public Transform head;
        public float floorY;
        public float intensity = 1f;
        public float radius = 1.25f;
        public float minAngle = 110f;
        public float maxAngle = 250f;
        public float runSpeed = 1.15f;
        public Vector3 snoutLocal = new Vector3(0f, 0.21f, 0.26f);

        enum S { Run, Turn, Bark, Rest }
        S state = S.Run;
        float rel, targetRel, yawRef, timer;
        bool init;
        Quaternion turnFrom;
        float turnT;
        static readonly Color Pink = new Color(1f, 0.55f, 0.78f, 1f);
        static readonly Color Gold = new Color(1f, 0.85f, 0.4f, 1f);

        float HeadYaw()
        {
            var f = head.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.01f) return yawRef;
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        Vector3 Pos(float r)
        {
            var dir = Quaternion.Euler(0f, yawRef + r, 0f) * Vector3.forward;
            return new Vector3(head.position.x, floorY, head.position.z) + dir * radius;
        }

        void Init()
        {
            if (init || head == null) return;
            init = true;
            yawRef = HeadYaw();
            rel = Random.Range(minAngle, maxAngle);
            PickTarget();
            timer = RunTime();
        }

        public Vector3 InitialSpot()
        {
            Init();
            return head != null ? Pos(rel) : transform.position;
        }

        float RunTime()
        {
            return Random.Range(2.2f, 4f) / Mathf.Clamp(intensity, 0.4f, 1f);
        }

        void PickTarget()
        {
            for (int i = 0; i < 8; i++)
            {
                targetRel = Random.Range(minAngle, maxAngle);
                if (Mathf.Abs(targetRel - rel) > 40f) return;
            }
        }

        void Update()
        {
            if (head == null) return;
            Init();
            float dt = Time.deltaTime;
            yawRef = Mathf.LerpAngle(yawRef, HeadYaw(), 0.6f * dt);
            switch (state)
            {
                case S.Run:
                {
                    float angSpeed = runSpeed * Mathf.Clamp(intensity, 0.4f, 1f) / radius * Mathf.Rad2Deg;
                    float prev = rel;
                    rel = Mathf.MoveTowards(rel, targetRel, angSpeed * dt);
                    if (Mathf.Abs(rel - targetRel) < 0.5f) PickTarget();
                    var p = Pos(rel);
                    var move = p - transform.position;
                    move.y = 0f;
                    transform.position = p + Vector3.up * Mathf.Abs(Mathf.Sin(Time.time * 11f)) * 0.025f;
                    if (move.sqrMagnitude > 1e-6f && Mathf.Abs(rel - prev) > 1e-4f)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(move.normalized, Vector3.up), 10f * dt);
                    timer -= dt;
                    if (timer <= 0f)
                    {
                        state = S.Turn;
                        turnFrom = transform.rotation;
                        turnT = 0f;
                        transform.position = Pos(rel);
                    }
                    break;
                }
                case S.Turn:
                {
                    turnT += dt / 0.35f;
                    var to = head.position - transform.position;
                    to.y = 0f;
                    if (to.sqrMagnitude > 1e-4f)
                        transform.rotation = Quaternion.Slerp(turnFrom, Quaternion.LookRotation(to.normalized, Vector3.up), Mathf.SmoothStep(0f, 1f, turnT));
                    if (turnT >= 1f)
                    {
                        state = S.Bark;
                        timer = 1.1f;
                        StartCoroutine(BarkRoutine());
                    }
                    break;
                }
                case S.Bark:
                    timer -= dt;
                    if (timer <= 0f)
                    {
                        state = S.Rest;
                        timer = 0.4f;
                    }
                    break;
                case S.Rest:
                    timer -= dt;
                    if (timer <= 0f)
                    {
                        state = S.Run;
                        timer = RunTime();
                        PickTarget();
                    }
                    break;
            }
        }

        IEnumerator BarkRoutine()
        {
            var basePos = transform.position;
            var snout = transform.TransformPoint(snoutLocal);
            AudioDirector.Sfx("bark", snout, 1f, Random.Range(0.95f, 1.12f));
            for (int i = 0; i < 3; i++)
            {
                StartCoroutine(Ring(i));
                // mic salt la fiecare "ham"
                for (float e = 0f; e < 0.15f; e += Time.deltaTime)
                {
                    if (state != S.Bark) break;
                    transform.position = basePos + Vector3.up * Mathf.Sin(e / 0.15f * Mathf.PI) * 0.03f;
                    yield return null;
                }
            }
            if (state == S.Bark) transform.position = basePos;
        }

        IEnumerator Ring(int index)
        {
            var snout = transform.TransformPoint(snoutLocal);
            var lr = CozyFx.Circle("BarkRing", transform, 0.04f, 0.012f, Color.white);
            var g = new Gradient();
            var a = index % 2 == 0 ? Pink : Gold;
            var b = index % 2 == 0 ? Gold : Pink;
            lr.transform.position = snout;
            var toHead = head.position - snout;
            if (toHead.sqrMagnitude < 1e-4f) toHead = transform.forward;
            lr.transform.rotation = Quaternion.LookRotation(toHead.normalized, Vector3.up);
            var travel = toHead.normalized * 0.35f;
            const float dur = 0.9f;
            for (float e = 0f; e < dur; e += Time.deltaTime)
            {
                if (lr == null) yield break;
                float k = e / dur;
                float ease = 1f - (1f - k) * (1f - k);
                CozyFx.SetCircle(lr, Mathf.Lerp(0.04f, 0.42f, ease));
                lr.transform.position = snout + travel * ease;
                lr.widthMultiplier = Mathf.Lerp(0.016f, 0.004f, k);
                float alpha = 1f - k;
                g.SetKeys(
                    new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 0.5f), new GradientColorKey(a, 1f) },
                    new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(alpha, 1f) });
                lr.colorGradient = g;
                yield return null;
            }
            if (lr != null) Destroy(lr.gameObject);
        }
    }

    /// <summary>Dă din codiță și se fâțâie de bucurie ~1.2 s (merge și pe modele fără codiță).</summary>
    public sealed class HappyWag : MonoBehaviour
    {
        Transform tail;
        Quaternion tailBase;
        Vector3 axis = Vector3.up;
        float left;

        public void Play()
        {
            if (tail == null)
            {
                foreach (var tr in GetComponentsInChildren<Transform>(true))
                    if (string.Equals(tr.name, "Tail", System.StringComparison.OrdinalIgnoreCase)) { tail = tr; tailBase = tr.localRotation; break; }
                // osul „tail” din Blender are axa Y de-a lungul cozii → dă din coadă în jurul lui Z; cubul graybox, în jurul lui Y
                axis = GetComponent<NativeAnim>() != null ? Vector3.forward : Vector3.up;
            }
            left = 1.2f;
            enabled = true;
        }

        void LateUpdate()
        {
            if (left <= 0f) return;
            left -= Time.deltaTime;
            float k = Mathf.Clamp01(left / 1.2f);
            float s = Mathf.Sin(Time.time * 28f);
            if (tail != null) tail.localRotation = (GetComponent<NativeAnim>() != null ? tail.localRotation : tailBase) * Quaternion.AngleAxis(s * 40f * k, axis);
            transform.rotation = transform.rotation * Quaternion.Euler(0f, Mathf.Sin(Time.time * 14f) * 6f * k * Time.deltaTime * 30f, 0f);
            if (left <= 0f && tail != null) tail.localRotation = tailBase;
        }
    }
}
