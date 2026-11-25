using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MindThePaws
{
    /// <summary>
    /// Portalul către Brutăria Lunii (90 s):
    ///  1. În fața jucătorului se deschide un portal circular (inele rotative mov-aurii + vortex de particule).
    ///     Pinch / click pe portal → jucătorul „pășește” înăuntru.
    ///  2. În jur apare Brutăria Lunii: cupolă caldă, tejghea pastelată de lemn/crem, felinare plutitoare,
    ///     norișori roz, platouri aurii cu Biscuit Stea / Lăbuță / Inimioară.
    ///  3. Comenzi secvențiale arătate pe tăbliță, apoi servite din memorie, în ordine. Dificultate progresivă:
    ///     Nivel 1 (0–25 s / primele 2 comenzi) = 2 biscuiți; Nivel 2 (25–55 s / comenzile 3–5) = 3 biscuiți, cu repetări;
    ///     Nivel 3 (55–90 s / comanda 6+) = 4–5 biscuiți, iar farfuriile își schimbă locul la fiecare comandă nouă.
    ///  4. La expirarea timpului, portalul reapare și îl readuce lin în camera reală.
    /// </summary>
    public class MoonBakeryTask : MonoBehaviour, IMemoryTask
    {
        public float totalSeconds = 90f;
        public float portalDistance = 1.25f;
        public float counterDistance = 0.8f;
        public float counterBelowHead = 0.5f;
        [Tooltip("Cât timp rămâne comanda vizibilă clar pe tăbliță (3.5–4 s), apoi se estompează: test de memorie de lucru.")]
        public float showSeconds = 3.75f;
        public float hitRadius = 0.09f;

        public string Title => "Brutăria Lunii";
        public bool Finished { get; private set; }
        public bool Hovering { get; private set; }
        public Vector3 HoverPoint { get; private set; }
        string status = "";
        string banner = "";
        float bannerUntil;
        public string Status => Time.time < bannerUntil && banner.Length > 0 ? banner + "\n<size=44>" + status + "</size>" : status;
        public int Level => level;
        public int MaxLevel { get; private set; } = 1;
        public int MaxSequenceLength { get; private set; }
        public string Hint { get; private set; } = "";
        public int CorrectSequences { get; private set; }
        public int Mistakes { get; private set; }
        public float Remaining => Mathf.Max(0f, totalSeconds - elapsed);

        enum Phase { Portal, Entering, Show, Input, Wait, Leaving, Done }

        static readonly Color Gold = new Color(1f, 0.82f, 0.32f);
        static readonly Color Purple = new Color(0.72f, 0.5f, 1f);
        static readonly Color Oops = new Color(1f, 0.55f, 0.62f);
        static readonly float[] PlateX = { -0.34f, 0f, 0.34f };
        const float PlateZ = 0.07f;
        static readonly Vector3 DeliveryLocal = new Vector3(0f, 0f, -0.13f);

        Transform head;
        GameObject root, portal, bakery, dome;
        Material domeMat;
        LineRenderer ringOuter, ringInner;
        Transform counterTop;
        Vector3 portalPos;
        Quaternion yaw;
        // grupul g = farfuria + grămăjoara + biscuitul + eticheta lui; slotOf[g] = poziția (0..2) pe tejghea
        readonly int[] plateKind = new int[3];
        readonly Transform[] cookies = new Transform[3];
        readonly Vector3[] cookiePos = new Vector3[3];
        readonly Transform[] groups = new Transform[3];
        readonly int[] slotOf = { 0, 1, 2 };
        int level = 1;
        readonly List<GameObject> served = new List<GameObject>();
        readonly List<int> order = new List<int>();
        readonly List<Transform> lanterns = new List<Transform>();
        Transform clouds;
        CozyLabel orderBoard;
        Phase phase = Phase.Portal;
        int progress;
        float elapsed;
        bool counting;
        bool lastOk = true;
        int hovered = -1;
        float portalHover;

        public void Begin(Transform headTransform, List<Vector3> hidden, float seconds)
        {
            head = headTransform;
            totalSeconds = seconds;
            var f = head.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            f.Normalize();
            yaw = Quaternion.LookRotation(f, Vector3.up);
            portalPos = head.position + f * portalDistance + Vector3.down * 0.05f;

            root = new GameObject("MoonBakeryTask");
            root.transform.SetParent(transform, false);
            BuildPortal();
            status = "Un portal magic s-a deschis!\nȚintește-l și fă pinch ca să intri în Brutăria Lunii.";
            AudioDirector.Sfx("grow", portalPos, 0.6f, 0.8f);
        }

        // ------------------------------------------------------------ portal

        void BuildPortal()
        {
            portal = new GameObject("MagicPortal");
            portal.transform.SetParent(root.transform, false);
            portal.transform.SetPositionAndRotation(portalPos, yaw);

            // miez strălucitor + vortex
            var core = CozyFx.Halo(portal.transform, portalPos, 0.62f, new Color(0.75f, 0.55f, 1f, 0.55f), out _);
            core.name = "PortalCore";
            core.localRotation = Quaternion.identity;
            var inner = CozyFx.Halo(portal.transform, portalPos, 0.32f, new Color(1f, 0.92f, 0.75f, 0.7f), out _);
            inner.localRotation = Quaternion.identity;
            inner.localPosition = new Vector3(0f, 0f, -0.01f);

            ringOuter = CozyFx.Circle("RingOuter", portal.transform, 0.34f, 0.022f, Color.white, 64);
            ringInner = CozyFx.Circle("RingInner", portal.transform, 0.28f, 0.012f, Color.white, 64);
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Purple, 0f), new GradientColorKey(Gold, 0.25f), new GradientColorKey(Purple, 0.5f), new GradientColorKey(Gold, 0.75f), new GradientColorKey(Purple, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            ringOuter.colorGradient = g;
            ringInner.colorGradient = g;

            var vgo = new GameObject("Vortex");
            vgo.transform.SetParent(portal.transform, false);
            var ps = vgo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 1.4f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.022f);
            main.startColor = new ParticleSystem.MinMaxGradient(Purple, Gold);
            main.maxParticles = 220;
            var em = ps.emission;
            em.rateOverTime = 110f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = 0.34f;
            sh.radiusThickness = 0.05f;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(2.4f);
            vel.radial = new ParticleSystem.MinMaxCurve(-0.22f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = CozyFx.FadeGradient(Color.white, Color.white);
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));
            var r = vgo.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = CozyFx.ParticleMat();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();

            portal.transform.localScale = Vector3.one * 0.01f;
            StartCoroutine(ScaleTo(portal.transform, 1f, 0.6f));
        }

        IEnumerator ScaleTo(Transform t, float to, float dur)
        {
            if (t == null) yield break;
            float from = t.localScale.x;
            for (float e = 0f; e < dur; e += Time.deltaTime)
            {
                if (t == null) yield break;
                float k = e / dur;
                float s = Mathf.LerpUnclamped(from, to, k) + Mathf.Sin(k * Mathf.PI) * 0.12f * Mathf.Max(to, from);
                t.localScale = Vector3.one * Mathf.Max(0.0001f, s);
                yield return null;
            }
            if (t != null) t.localScale = Vector3.one * Mathf.Max(0.0001f, to);
        }

        IEnumerator Enter()
        {
            phase = Phase.Entering;
            status = "Pășești prin portal...";
            AudioDirector.Sfx("poof", portalPos, 0.8f, 0.8f);
            AudioDirector.Sfx("chime", portalPos, 0.8f, 0.85f);
            CozyFx.Burst(portalPos, Purple, 40, 0.8f, 0.025f);
            BuildBakery();
            var start = portal.transform.position;
            for (float e = 0f; e < 0.9f; e += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, e / 0.9f);
                portal.transform.position = Vector3.Lerp(start, head.position + head.forward * 0.2f, k);
                portal.transform.localScale = Vector3.one * Mathf.Lerp(1f, 3.5f, k);
                SetDome(Mathf.Lerp(0f, 0.3f, k));
                bakery.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, k);
                yield return null;
            }
            portal.SetActive(false);
            SetDome(0.3f);
            bakery.transform.localScale = Vector3.one;
            AudioDirector.Sfx("grow", head.position, 0.6f, 1.1f);
            StartCoroutine(Run());
        }

        IEnumerator Leave()
        {
            phase = Phase.Leaving;
            status = "Timpul a expirat! Portalul te readuce acasă...";
            ClearServed(true);
            if (orderBoard != null) orderBoard.gameObject.SetActive(false);
            portal.SetActive(true);
            var f = head.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            f.Normalize();
            portal.transform.SetPositionAndRotation(head.position + f * 0.9f, Quaternion.LookRotation(f, Vector3.up));
            portal.transform.localScale = Vector3.one * 0.01f;
            AudioDirector.Sfx("grow", portal.transform.position, 0.6f, 0.75f);
            yield return ScaleTo(portal.transform, 1.6f, 0.6f);
            for (float e = 0f; e < 1.3f; e += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, e / 1.3f);
                SetDome(Mathf.Lerp(0.3f, 0f, k));
                bakery.transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.001f, k);
                yield return null;
            }
            bakery.SetActive(false);
            CozyFx.Burst(portal.transform.position, Gold, 30, 0.6f, 0.022f);
            AudioDirector.Sfx("poof", portal.transform.position, 0.8f, 1.1f);
            yield return ScaleTo(portal.transform, 0.001f, 0.45f);
            portal.SetActive(false);
            phase = Phase.Done;
            status = "Înapoi în camera ta! Ai servit " + CorrectSequences + (CorrectSequences == 1 ? " comandă." : " comenzi.");
            Finished = true;
        }

        void SetDome(float a)
        {
            if (domeMat == null) return;
            CozyFx.SetColor(domeMat, new Color(1f, 0.85f, 0.78f, a));
            dome.SetActive(a > 0.005f);
        }

        // ------------------------------------------------------------ decorul brutăriei

        void BuildBakery()
        {
            bakery = new GameObject("MoonBakery");
            bakery.transform.SetParent(root.transform, false);
            var basePos = new Vector3(head.position.x, head.position.y, head.position.z);
            bakery.transform.SetPositionAndRotation(basePos, yaw);
            var bt = bakery.transform;

            // cupolă caldă în jurul jucătorului
            domeMat = CozyFx.TransparentMat(new Color(1f, 0.85f, 0.78f, 0f));
            domeMat.renderQueue = 2900;
            dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(dome.GetComponent<Collider>());
            dome.name = "WarmDome";
            dome.transform.SetParent(bt, false);
            dome.transform.localScale = Vector3.one * 5.4f;
            var dmr = dome.GetComponent<MeshRenderer>();
            dmr.sharedMaterial = domeMat;
            dmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dome.SetActive(false);

            // tejghea: blat crem + corp de lemn pastelat cu scânduri și bordură roz
            var counter = new GameObject("Counter").transform;
            counter.SetParent(bt, false);
            counter.localPosition = new Vector3(0f, -counterBelowHead, counterDistance);
            // lemn cald procedural (CozyThemeArt): blat deschis, corp cireș pastel, scânduri mai închise
            var cream = CozyThemeArt.WoodMat(new Color(1f, 0.97f, 0.92f), 2f);
            var wood = CozyThemeArt.WoodMat(new Color(1f, 0.9f, 0.86f), 2.5f);
            var plank = CozyThemeArt.WoodMat(new Color(0.85f, 0.7f, 0.62f), 1f);
            var pink = Graybox.MakeMat(new Color(1f, 0.72f, 0.8f));
            Graybox.Prim(PrimitiveType.Cube, counter, new Vector3(0f, -0.025f, 0f), new Vector3(1.2f, 0.05f, 0.46f), cream);
            Graybox.Prim(PrimitiveType.Cube, counter, new Vector3(0f, -0.33f, 0.02f), new Vector3(1.12f, 0.56f, 0.4f), wood);
            for (int i = 0; i < 7; i++)
                Graybox.Prim(PrimitiveType.Cube, counter, new Vector3(-0.48f + i * 0.16f, -0.33f, -0.181f), new Vector3(0.012f, 0.54f, 0.01f), plank);
            Graybox.Prim(PrimitiveType.Cube, counter, new Vector3(0f, -0.075f, -0.183f), new Vector3(1.12f, 0.04f, 0.012f), pink);
            counterTop = counter;

            // platouri aurii cu biscuiți; amestecăm ce biscuit e pe ce platou
            var pool = new List<int> { 0, 1, 2 };
            for (int i = 0; i < 3; i++)
            {
                int k = Random.Range(0, pool.Count);
                plateKind[i] = pool[k];
                pool.RemoveAt(k);
            }
            for (int i = 0; i < 3; i++)
            {
                int kind = plateKind[i];
                // tot ce ține de o farfurie stă într-un grup, ca să se poată muta împreună (shuffle la Nivelul 3)
                var g = new GameObject("PlateGroup_" + Graybox.CookieKey(kind)).transform;
                g.SetParent(counter, false);
                g.localPosition = new Vector3(PlateX[i], 0f, 0f);
                groups[i] = g;
                slotOf[i] = i;
                var lp = new Vector3(0f, -0.022f, PlateZ);
                // porțelan alb-crem / roz-pudră cu inel auriu lucios, ca să se distingă de blatul de lemn
                var plate = Graybox.PorcelainPlate("Plate_" + Graybox.CookieLabel(kind), new Color(0.99f, 0.95f, 0.96f), new Color(0.95f, 0.80f, 0.42f), 0.18f, false);
                plate.transform.SetParent(g, false);
                plate.transform.localPosition = lp;
                for (int j = 0; j < 2; j++)
                {
                    // grămăjoară decorativă sub biscuitul activ
                    var deco = Graybox.Cookie("Stack_" + kind + "_" + j, kind);
                    deco.transform.SetParent(g, false);
                    deco.transform.localPosition = lp + new Vector3((j == 0 ? -0.035f : 0.035f), 0.03f, 0.03f);
                    deco.transform.localRotation = Quaternion.Euler(0f, 30f * j + 10f, 0f);
                }
                var c = Graybox.Cookie("Cookie_" + Graybox.CookieKey(kind), kind);
                c.transform.SetParent(g, false);
                c.transform.localPosition = lp + new Vector3(0f, 0.05f, -0.01f);
                c.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);    // înclinat spre jucător
                CozyAnimator.Attach(c, CozyMotion.Hover, 0.4f);
                cookies[i] = c.transform;
                cookiePos[i] = c.transform.position;

                var tag = CozyUI.Label("Tag_" + kind, Graybox.CookieLabel(kind), 30, CozyUI.Ink, new Color(1f, 0.98f, 0.94f, 0.95f), CozyUI.GoldSoft);
                tag.transform.SetParent(g, false);
                tag.transform.localPosition = new Vector3(0f, -0.04f, -0.24f);
                tag.transform.localRotation = Quaternion.identity;
            }
            // platoul de livrare: porțelan sidefat alb-lavandă cu margine dublă aurie
            var d = Graybox.PorcelainPlate("DeliveryPlate", new Color(0.94f, 0.92f, 1.0f), new Color(0.95f, 0.80f, 0.42f), 0.22f, true);
            d.transform.SetParent(counter, false);
            d.transform.localPosition = DeliveryLocal + new Vector3(0f, -0.022f, 0f);
            var dl = CozyUI.Label("DeliveryTag", "Livrare", 24, new Color(0.6f, 0.42f, 0.2f), null);
            dl.transform.SetParent(counter, false);
            dl.transform.localPosition = new Vector3(0.2f, -0.04f, -0.24f);

            // firma + tăblița cu comanda
            var sign = CozyUI.Label("BakerySign", "Brutăria Lunii", 46, CozyUI.Ink, new Color(1f, 0.92f, 0.96f, 0.95f), CozyUI.Gold);
            sign.transform.SetParent(counter, false);
            sign.transform.localPosition = new Vector3(0f, 0.62f, 0.3f);
            orderBoard = CozyUI.Label("OrderBoard", "", 38, CozyUI.Ink, CozyUI.Lavender, CozyUI.Gold);
            orderBoard.transform.SetParent(counter, false);
            orderBoard.transform.localPosition = new Vector3(0f, 0.36f, 0.26f);
            orderBoard.gameObject.SetActive(false);
            var moon = Graybox.TreasureByType("lantern", false, false);
            moon.transform.SetParent(counter, false);
            moon.transform.localPosition = new Vector3(0.48f, 0f, 0.12f);

            // felinare plutitoare în jur
            float[] angs = { -120f, -75f, -40f, 40f, 75f, 120f, 165f, -165f };
            for (int i = 0; i < angs.Length; i++)
            {
                var l = Graybox.TreasureByType("lantern", false, false);
                l.name = "FloatingLantern";
                l.transform.SetParent(bt, false);
                var dir = Quaternion.Euler(0f, angs[i], 0f) * Vector3.forward;
                l.transform.localPosition = dir * Random.Range(1.4f, 1.9f) + Vector3.up * Random.Range(0.05f, 0.45f);
                l.transform.localScale = Vector3.one * 1.4f;
                var a = CozyAnimator.Attach(l, CozyMotion.Hover, 2f);
                CozyFx.Halo(l.transform, l.transform.position + Vector3.up * 0.1f, 0.35f, new Color(1f, 0.85f, 0.5f, 0.3f), out _);
                var sp = CozyFx.Sparkler(l.transform, new Color(1f, 0.88f, 0.55f), 3f, 0.04f);
                sp.transform.localPosition = Vector3.up * 0.08f;
                lanterns.Add(l.transform);
            }

            // norișori roz
            clouds = new GameObject("PinkClouds").transform;
            clouds.SetParent(bt, false);
            var cloudMat = CozyFx.TransparentMat(new Color(1f, 0.8f, 0.88f, 0.6f));
            for (int c = 0; c < 6; c++)
            {
                var cl = new GameObject("Cloud").transform;
                cl.SetParent(clouds, false);
                var dir = Quaternion.Euler(0f, c * 60f + 20f, 0f) * Vector3.forward;
                cl.localPosition = dir * 2.0f + Vector3.up * Random.Range(0.55f, 0.9f);
                for (int p = 0; p < 5; p++)
                    Graybox.Prim(PrimitiveType.Sphere, cl, new Vector3((p - 2) * 0.09f, Mathf.Sin(p * 1.3f) * 0.03f, Random.Range(-0.03f, 0.03f)),
                        Vector3.one * (p == 2 ? 0.22f : Random.Range(0.12f, 0.17f)), cloudMat);
                CozyAnimator.Attach(cl.gameObject, CozyMotion.Hover, 1.5f);
            }

            // zahăr pudră care ninge ușor
            var sgo = new GameObject("SugarSnow");
            sgo.transform.SetParent(bt, false);
            sgo.transform.localPosition = new Vector3(0f, 1f, 0.4f);
            var ps = sgo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.014f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white, new Color(1f, 0.85f, 0.92f));
            main.gravityModifier = 0.02f;
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 25f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(3f, 0.1f, 3f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = CozyFx.FadeGradient(Color.white, Color.white);
            var nz = ps.noise;
            nz.enabled = true;
            nz.strength = 0.08f;
            nz.frequency = 0.5f;
            var r = sgo.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = CozyFx.ParticleMat();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        // ------------------------------------------------------------ comenzi

        static string OrderText(List<int> o, int revealed)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < o.Count; i++)
            {
                if (i > 0) sb.Append(" → ");
                sb.Append(i < revealed ? Graybox.CookieLabel(o[i]) : "?");
            }
            return sb.ToString();
        }

        /// Nivel 1: 2 biscuiți diferiți la rând; Nivel 2: 3 (se pot repeta); Nivel 3: 4–5 (se pot repeta).
        void ChooseOrder()
        {
            order.Clear();
            int len = level >= 3 ? Random.Range(4, 6) : (level == 2 ? 3 : 2);
            int prev = -1;
            for (int i = 0; i < len; i++)
            {
                int k = Random.Range(0, 3);
                if (level == 1)
                {
                    int guard = 0;
                    while (k == prev && guard++ < 10) k = Random.Range(0, 3);
                }
                order.Add(k);
                prev = k;
            }
            MaxSequenceLength = Mathf.Max(MaxSequenceLength, order.Count);
        }

        static string LengthText(int lvl) => lvl >= 3 ? "4–5 biscuiți" : (lvl == 2 ? "3 biscuiți" : "2 biscuiți");

        int LevelFor(int delivered, float time)
        {
            int byOrders = delivered >= 5 ? 3 : (delivered >= 2 ? 2 : 1);   // comanda 6+ → Nivel 3
            int byTime = time >= 55f ? 3 : (time >= 25f ? 2 : 1);
            return Mathf.Max(level, Mathf.Max(byOrders, byTime));   // nivelul nu scade niciodată
        }

        void MaybeLevelUp()
        {
            int next = Mathf.Clamp(LevelFor(CorrectSequences, elapsed), 1, 3);
            if (next <= level) return;
            level = next;
            MaxLevel = Mathf.Max(MaxLevel, level);
            banner = level >= 3
                ? "Nivel maxim! Comenzi de 4–5 biscuiți, iar farfuriile își schimbă locul!"
                : "Super! Nivel 2: comenzi de 3 biscuiți (se pot repeta)!";
            bannerUntil = Time.time + 3.5f;
            AudioDirector.Sfx("grow", counterTop != null ? counterTop.position : head.position, 0.55f, 1.3f);
            if (counterTop != null) CozyFx.Burst(counterTop.position + Vector3.up * 0.35f, Gold, 26, 0.45f, 0.022f);
        }

        /// Nivelul 3: cele 3 farfurioare (cu biscuiții și etichetele lor) își schimbă lin locurile între ele.
        IEnumerator ShufflePlates()
        {
            int[] perm = { 0, 1, 2 };
            for (int tries = 0; tries < 10; tries++)
            {
                for (int i = 2; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    int tmp = perm[i]; perm[i] = perm[j]; perm[j] = tmp;
                }
                bool moved = false;
                for (int g = 0; g < 3; g++) if (perm[g] != slotOf[g]) moved = true;
                if (moved) break;
            }
            var from = new Vector3[3];
            var to = new Vector3[3];
            for (int g = 0; g < 3; g++)
            {
                if (groups[g] == null) continue;
                from[g] = groups[g].localPosition;
                int slot = Mathf.Clamp(perm[g], 0, PlateX.Length - 1);
                to[g] = new Vector3(PlateX[slot], 0f, 0f);
                slotOf[g] = slot;
            }
            AudioDirector.Sfx("place", counterTop.position, 0.5f, 1.25f);
            const float dur = 0.75f;
            for (float e = 0f; e < dur; e += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, e / dur);
                for (int g = 0; g < 3; g++)
                {
                    if (groups[g] == null) continue;
                    // arc mic: cele care se mută se ridică puțin și ocolesc prin față / spate
                    float side = g % 2 == 0 ? 1f : -1f;
                    groups[g].localPosition = Vector3.Lerp(from[g], to[g], k)
                        + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.06f
                        + Vector3.forward * Mathf.Sin(k * Mathf.PI) * 0.05f * side;
                }
                yield return null;
            }
            for (int g = 0; g < 3; g++)
                if (groups[g] != null)
                {
                    groups[g].localPosition = to[g];
                    CozyFx.Burst(groups[g].position + Vector3.up * 0.05f, new Color(1f, 0.95f, 0.98f), 8, 0.2f, 0.012f);
                }
            AudioDirector.Sfx("chime", counterTop.position, 0.45f, 1.3f);
        }

        bool LookingAtCounter()
        {
            return Vector3.Angle(head.forward, counterTop.position - head.position) < 45f;
        }

        void Board(string text, float alpha = 1f)
        {
            if (orderBoard == null) return;
            orderBoard.gameObject.SetActive(!string.IsNullOrEmpty(text));
            orderBoard.text = text;
            orderBoard.SetAlpha(alpha);
        }

        /// Indicator discret de progres, fără numele biscuiților: ● serviți, ○ de servit.
        static string Dots(int total, int served)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < total; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(i < served ? '●' : '○');
            }
            return sb.ToString();
        }

        string ProgressBoard(int served) => $"<size=40>Comanda · Nivel {level}</size>\n" + Dots(order.Count, served);

        IEnumerator Run()
        {
            bool fresh = true;
            counting = true;
            while (elapsed < totalSeconds)
            {
                // urcarea de nivel după timp se aplică și după o greșeală (cu o comandă nouă)
                if (LevelFor(CorrectSequences, elapsed) > level) fresh = true;
                if (fresh)
                {
                    MaybeLevelUp();
                    ChooseOrder();
                }
                phase = Phase.Show;
                progress = 0;
                Board("");
                status = "Întoarce-te spre tejghea!";
                while (!LookingAtCounter() && elapsed < totalSeconds) yield return null;
                if (elapsed >= totalSeconds) break;
                if (level >= 3 && fresh)
                {
                    Board("<size=44>Nivel 3 · atenție la farfurii!</size>");
                    status = "Farfuriile își schimbă locul... uită-te bine la ce alegi!";
                    yield return ShufflePlates();
                }
                AudioDirector.Sfx("chime", counterTop.position, 0.5f, 0.8f);
                // comanda stă clar ~3.5–4 s (puțin mai mult pentru comenzile lungi), apoi se estompează
                float show = Mathf.Clamp(showSeconds + Mathf.Max(0, order.Count - 3) * 0.12f, 3.5f, 4f);
                for (float t = show; t > 0f && elapsed < totalSeconds; t -= Time.deltaTime)
                {
                    Board($"<size=44>Comanda nouă · Nivel {level} ({order.Count} biscuiți)</size>\n" + OrderText(order, order.Count));
                    status = $"Nivel {level} · comandă de {order.Count} biscuiți! Ține minte ordinea.";
                    yield return null;
                }
                // estompare lină a textului comenzii → rămâne doar indicatorul de progres
                string full = $"<size=44>Comanda nouă · Nivel {level} ({order.Count} biscuiți)</size>\n" + OrderText(order, order.Count);
                for (float t = 0f; t < 0.6f && elapsed < totalSeconds; t += Time.deltaTime)
                {
                    Board(full, 1f - t / 0.6f);
                    status = "Comanda dispare... acum ține-o minte!";
                    yield return null;
                }
                phase = Phase.Input;
                Board(ProgressBoard(0), 0.75f);
                status = $"Nivel {level} · servește comanda din memorie (0/{order.Count})";
                while (phase == Phase.Input && elapsed < totalSeconds) yield return null;
                while (phase == Phase.Wait) yield return null;
                fresh = lastOk;
            }
            counting = false;
            yield return Leave();
        }

        void Update()
        {
            if (head == null || Finished) return;
            float dt = Time.deltaTime;
            if (counting) elapsed += dt;
            if (portal != null && portal.activeSelf)
            {
                float spin = 1f + portalHover * 1.5f;
                ringOuter.transform.Rotate(0f, 0f, 70f * spin * dt, Space.Self);
                ringInner.transform.Rotate(0f, 0f, -110f * spin * dt, Space.Self);
                ringOuter.widthMultiplier = 0.022f + 0.008f * Mathf.Sin(Time.time * 4f) + 0.01f * portalHover;
            }
            if (clouds != null) clouds.Rotate(0f, 2.5f * dt, 0f, Space.World);
            for (int i = 0; i < cookies.Length && i < cookiePos.Length; i++)
                if (cookies[i] != null)
                {
                    cookiePos[i] = cookies[i].position;
                    float target = i == hovered && phase == Phase.Input ? 1.22f : 1f;
                    float cur = cookies[i].localScale.x;
                    cookies[i].localScale = Vector3.one * Mathf.Lerp(cur, target, 1f - Mathf.Exp(-12f * dt));
                }
            Vector3 focus = phase == Phase.Portal ? portalPos : (counterTop != null ? counterTop.position : portalPos);
            Hint = MemoryTaskUtil.DirectionHint(head, focus, phase == Phase.Portal ? "portalul" : "tejgheaua");
        }

        public void Feed(Ray ray, bool click)
        {
            Hovering = false;
            int prevHover = hovered;
            hovered = -1;
            if (Finished || root == null || !root.activeSelf) return;

            if (phase == Phase.Portal)
            {
                bool onPortal = MemoryTaskUtil.Pick(ray, new[] { portalPos }, 0.4f) == 0;
                portalHover = Mathf.MoveTowards(portalHover, onPortal ? 1f : 0f, Time.deltaTime * 6f);
                if (onPortal)
                {
                    Hovering = true;
                    HoverPoint = MemoryTaskUtil.Soft(ray, portalPos);
                    if (click) StartCoroutine(Enter());
                }
                return;
            }

            int best = MemoryTaskUtil.PickSticky(ray, cookiePos, hitRadius, prevHover);
            if (best >= cookies.Length || best >= plateKind.Length) best = -1;
            hovered = best;
            if (best >= 0)
            {
                Hovering = true;
                HoverPoint = MemoryTaskUtil.Soft(ray, cookiePos[best]);
            }
            if (phase != Phase.Input || !click || best < 0 || best >= plateKind.Length) return;
            if (progress < 0 || progress >= order.Count) return;

            int kind = plateKind[best];
            if (kind == order[progress])
            {
                StartCoroutine(FlyToDelivery(best, served.Count, progress + 1));
                progress++;
                Board(ProgressBoard(progress), 0.75f);
                status = $"Nivel {level} · Mmm! ({progress}/{order.Count})";
                if (progress >= order.Count)
                {
                    CorrectSequences++;
                    lastOk = true;
                    phase = Phase.Wait;
                    StartCoroutine(OrderDone());
                }
            }
            else
            {
                Mistakes++;
                lastOk = false;
                phase = Phase.Wait;
                AudioDirector.Sfx("oops", cookiePos[best], 0.6f);
                CozyFx.Burst(cookiePos[best], Oops, 10, 0.25f, 0.018f);
                StartCoroutine(Wobble(cookies[best]));
                StartCoroutine(Retry());
            }
        }

        IEnumerator FlyToDelivery(int plate, int stackIndex, int step)
        {
            if (plate < 0 || plate >= cookies.Length || cookies[plate] == null) yield break;
            var src = cookies[plate];
            var copy = Graybox.Cookie("Served_" + Graybox.CookieKey(plateKind[plate]), plateKind[plate]);
            copy.transform.SetParent(counterTop, false);
            copy.transform.SetPositionAndRotation(src.position, src.rotation);
            served.Add(copy);
            var start = copy.transform.localPosition;
            int si = Mathf.Clamp(stackIndex, 0, 6);
            var end = DeliveryLocal + new Vector3(Random.Range(-0.015f, 0.015f), 0.012f + si * 0.016f, Random.Range(-0.015f, 0.015f));
            var r0 = copy.transform.localRotation;
            var r1 = Quaternion.Euler(0f, Random.Range(-40f, 40f), 0f);
            const float dur = 0.45f;
            for (float e = 0f; e < dur; e += Time.deltaTime)
            {
                if (copy == null) yield break;
                float k = e / dur;
                float sk = k * k * (3f - 2f * k);
                copy.transform.localPosition = Vector3.Lerp(start, end, sk) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.14f;
                copy.transform.localRotation = Quaternion.Slerp(r0, r1, sk);
                if (Random.value < 0.35f) CozyFx.Burst(copy.transform.position, Color.white, 2, 0.08f, 0.01f);
                yield return null;
            }
            if (copy == null) yield break;
            copy.transform.localPosition = end;
            var world = copy.transform.position;
            CozyFx.Burst(world, Gold, 18, 0.35f, 0.02f);                       // sclipici auriu
            CozyFx.Burst(world, new Color(1f, 0.95f, 0.98f), 14, 0.25f, 0.012f); // zahăr pudră
            AudioDirector.Sfx("chime", world, 0.75f, 1f + 0.12f * step);
        }

        IEnumerator OrderDone()
        {
            status = "Comandă livrată! Clienții Lunii sunt fericiți!";
            yield return new WaitForSeconds(0.55f);
            var at = counterTop.TransformPoint(DeliveryLocal) + Vector3.up * 0.06f;
            CozyFx.Burst(at, Gold, 34, 0.5f, 0.026f);
            CozyFx.Burst(at, new Color(1f, 0.7f, 0.85f), 18, 0.4f, 0.02f);
            AudioDirector.Sfx("grow", at, 0.55f, 1.15f);
            yield return new WaitForSeconds(0.9f);
            ClearServed(true);
            yield return new WaitForSeconds(0.25f);
            phase = Phase.Show;
        }

        IEnumerator Retry()
        {
            Board($"<size=44>Comanda era · Nivel {level}</size>\n" + OrderText(order, order.Count));
            status = "Hopa! Uite comanda corectă, mai încercăm o dată.";
            yield return new WaitForSeconds(1.8f);
            ClearServed(true);
            phase = Phase.Show;
        }

        IEnumerator Wobble(Transform t)
        {
            if (t == null) yield break;
            var baseRot = t.localRotation;
            for (float e = 0f; e < 0.4f; e += Time.deltaTime)
            {
                if (t == null) yield break;
                t.localRotation = baseRot * Quaternion.Euler(0f, 0f, Mathf.Sin(e * 45f) * 14f * (1f - e / 0.4f));
                yield return null;
            }
            if (t != null) t.localRotation = baseRot;
        }

        void ClearServed(bool puff)
        {
            for (int i = 0; i < served.Count; i++)
            {
                if (served[i] == null) continue;
                if (puff) CozyFx.Burst(served[i].transform.position, Color.white, 6, 0.2f, 0.015f);
                Destroy(served[i]);
            }
            served.Clear();
        }

        public void SetVisible(bool v)
        {
            if (root != null) root.SetActive(v);
        }

        public void End()
        {
            StopAllCoroutines();
            Finished = true;
            ClearServed(false);
            if (root != null) Destroy(root);
        }
    }
}
