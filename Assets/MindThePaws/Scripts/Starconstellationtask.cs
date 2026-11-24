using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MindThePaws
{
    /// <summary>
    /// Constelația Pierdută (90 s): pe o boltă celestă translucidă bleumarin-mov, în direcția opusă comorilor,
    /// stelele 3D se aprind pe rând; jucătorul le atinge în aceeași ordine. Mereu aceleași 5 stele.
    /// Dificultate progresivă (prin lungimea și complexitatea modelului, nu prin stele noi):
    ///  Nivel 1 (0–25 s / primele 2 reușite) = 3 pași, fără repetări;
    ///  Nivel 2 (25–55 s / 2–4 reușite) = 4 pași, o stea se poate repeta;
    ///  Nivel 3 (55–90 s / 5+ reușite) = 5–6 pași, cu cel puțin o stea repetată, afișare mai alertă.
    /// Stelele au halou, sclipici care cade din ele, pulsare și sunet cristalin; perechile ghicite sunt unite
    /// cu linii aurii strălucitoare, iar constelația completă explodează într-un val de sclipici.
    /// </summary>
    public class StarConstellationTask : MonoBehaviour, IMemoryTask
    {
        public float totalSeconds = 90f;
        public float litSeconds = 0.65f;
        public float gapSeconds = 0.28f;
        public float hitRadius = 0.14f;
        public float distance = 1.7f;

        public string Title => "Constelația Pierdută";
        public bool Finished { get; private set; }
        public bool Hovering { get; private set; }
        public Vector3 HoverPoint { get; private set; }
        string status = "";
        string banner = "";
        float bannerUntil;
        /// Replica pentru Lumi: mesajul de nivel nou (câteva secunde) + starea curentă.
        public string Status => Time.time < bannerUntil && banner.Length > 0 ? banner + "\n<size=44>" + status + "</size>" : status;
        public int Level => level;
        public int MaxLevel { get; private set; } = 1;
        public int MaxSequenceLength { get; private set; }
        public string Hint { get; private set; } = "";
        public int CorrectSequences { get; private set; }
        public int Mistakes { get; private set; }
        public float Remaining => Mathf.Max(0f, totalSeconds - elapsed);

        enum Phase { Show, Input, Wait }

        /// Mereu exact aceleași 5 stele pe boltă (la ≥ 0.3 m una de alta, ca raza de 0.14 m să nu prindă două deodată).
        static readonly Vector2[] Shape =
        {
            new Vector2(-0.56f, 0.02f),
            new Vector2(-0.26f, 0.3f),
            new Vector2(0.04f, 0.08f),
            new Vector2(0.34f, 0.36f),
            new Vector2(0.6f, -0.02f)
        };
        const int MaxStars = 5;
        static readonly float[] LitPerLevel = { 0.65f, 0.55f, 0.45f };
        static readonly float[] GapPerLevel = { 0.3f, 0.26f, 0.2f };
        static readonly Color Dim = new Color(0.72f, 0.74f, 0.95f);
        static readonly Color Gold = new Color(1f, 0.83f, 0.3f);
        static readonly Color Red = new Color(1f, 0.52f, 0.6f);

        sealed class StarVis
        {
            public Transform t;
            public Vector3 pos;
            public Material lit, dark;      // graybox (null dacă e model din Blender)
            public Transform halo;
            public Material haloMat;
            public ParticleSystem sparkle;
            public float glow, glowTarget, hover;
            public Color color = Dim;
        }

        Transform head;
        GameObject root;
        Transform skyPanel;
        Quaternion facing;
        Vector3 right;
        readonly StarVis[] stars = new StarVis[MaxStars];
        readonly List<GameObject> links = new List<GameObject>();
        readonly List<int> seq = new List<int>();
        int level = 1;
        int activeStars = 5;
        Vector3 center;
        Phase phase = Phase.Show;
        int progress;
        float elapsed;
        bool counting;
        bool lastOk = true;
        int hovered = -1;

        public void Begin(Transform headTransform, List<Vector3> hidden, float seconds)
        {
            head = headTransform;
            totalSeconds = seconds;
            var away = MemoryTaskUtil.AwayFromHidden(head, hidden);
            right = Vector3.Cross(Vector3.up, away);
            center = head.position + away * distance + Vector3.up * 0.3f;
            facing = Quaternion.LookRotation(away, Vector3.up);   // +Z se îndepărtează de jucător → fața spre el

            root = new GameObject("LostConstellation");
            root.transform.SetParent(transform, false);
            BuildSky();
            for (int i = 0; i < stars.Length; i++) stars[i] = BuildStar(i);
            for (int i = 0; i < stars.Length; i++) SetStarVisible(i, i < activeStars);
            StartCoroutine(Run());
        }

        void BuildSky()
        {
            // bolta celestă: elipsă translucidă cu gradient + ramă lavandă + praf de stele animat
            var skyMat = CozyFx.TransparentMat(Color.white);
            CozyFx.SetTexture(skyMat, CozyFx.SkyTex);
            skyMat.renderQueue = 2950;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = "CelestialVault";
            q.transform.SetParent(root.transform, false);
            q.transform.SetPositionAndRotation(center + Away() * 0.12f + Vector3.up * 0.14f, facing);
            q.transform.localScale = new Vector3(1.9f, 1.15f, 1f);
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = skyMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            skyPanel = q.transform;

            var frame = CozyFx.Circle("VaultFrame", root.transform, 1f, 0.006f, new Color(0.85f, 0.8f, 1f, 0.5f), 72);
            frame.transform.SetPositionAndRotation(skyPanel.position - Away() * 0.005f, facing);
            frame.transform.localScale = new Vector3(0.9f, 0.53f, 1f);

            var dustGo = new GameObject("VaultStardust");
            dustGo.transform.SetParent(root.transform, false);
            dustGo.transform.SetPositionAndRotation(skyPanel.position - Away() * 0.03f, facing);
            var ps = dustGo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.02f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.016f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.92f, 0.7f), new Color(0.8f, 0.85f, 1f));
            main.maxParticles = 120;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var em = ps.emission;
            em.rateOverTime = 28f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(1.6f, 0.9f, 0.05f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = CozyFx.FadeGradient(Color.white, Color.white);
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.3f)));
            var r = dustGo.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = CozyFx.ParticleMat();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();

            // fără linii pre-desenate: stelele plutesc libere în nebuloasă; liniile aurii apar doar între stelele ghicite
        }

        Vector3 Away() => facing * Vector3.forward;

        Vector3 StarPos(int i)
        {
            i = Mathf.Clamp(i, 0, Shape.Length - 1);
            return center + right * Shape[i].x + Vector3.up * Shape[i].y;
        }

        bool Valid(int i) => i >= 0 && i < stars.Length && stars[i] != null;

        void SetStarVisible(int i, bool on)
        {
            if (!Valid(i)) return;
            var s = stars[i];
            if (s.t != null) s.t.gameObject.SetActive(on);
            if (s.halo != null) s.halo.gameObject.SetActive(on);
            if (s.sparkle != null) s.sparkle.gameObject.SetActive(on);
        }

        // ------------------------------------------------------------ niveluri

        int LevelFor(int correct, float time)
        {
            int bySuccess = correct >= 5 ? 3 : (correct >= 2 ? 2 : 1);
            int byTime = time >= 55f ? 3 : (time >= 25f ? 2 : 1);
            return Mathf.Max(level, Mathf.Max(bySuccess, byTime));   // nivelul nu scade niciodată
        }

        /// Nivel 1: 3 pași; Nivel 2: 4 pași; Nivel 3: 5 sau 6 pași.
        int SeqLen => level >= 3 ? Random.Range(5, 7) : (level == 2 ? 4 : 3);

        /// Între secvențe: dacă jucătorul a urcat de nivel, apar stele noi pe boltă și Lumi îl încurajează.
        IEnumerator MaybeLevelUp()
        {
            int next = LevelFor(CorrectSequences, elapsed);
            if (next <= level) yield break;
            level = Mathf.Clamp(next, 1, 3);
            MaxLevel = Mathf.Max(MaxLevel, level);
            // aceleași 5 stele: crește doar lungimea și complexitatea modelului
            banner = level >= 3
                ? "Nivel maxim: modele de 5–6 pași, cu stele care se repetă!"
                : "Super! Constelațiile devin mai complexe: 4 pași, iar o stea poate apărea de două ori!";
            bannerUntil = Time.time + 3.4f;
            AudioDirector.Sfx("grow", center, 0.55f, 1.3f);
            for (int i = 0; i < activeStars && i < stars.Length; i++)
                if (Valid(i)) CozyFx.Burst(stars[i].pos, new Color(0.9f, 0.88f, 1f), 10, 0.3f, 0.016f);
            status = $"Nivel {level}: modelele devin mai lungi!";
            yield return new WaitForSeconds(1f);
        }

        StarVis BuildStar(int i)
        {
            var s = new StarVis { pos = StarPos(i) };
            GameObject go;
            if (AutoAssetLoader.Has("constellation_star"))
            {
                go = Graybox.ConstellationStar("Star_" + i, Dim);
            }
            else
            {
                go = new GameObject("Star_" + i);
                go.AddComponent<MeshFilter>().sharedMesh = CozyFx.StarMesh(0.055f, 0.024f, 0.022f);
                var mr = go.AddComponent<MeshRenderer>();
                s.lit = Graybox.MakeMat(Dim);
                s.dark = Graybox.MakeMat(Dim * 0.78f);
                mr.sharedMaterials = new[] { s.lit, s.dark };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                CozyAnimator.Attach(go, CozyMotion.Pulse, 0.7f);
            }
            go.transform.SetParent(root.transform, false);
            go.transform.SetPositionAndRotation(s.pos, facing * Quaternion.Euler(0f, 0f, Random.Range(-12f, 12f)));
            s.t = go.transform;
            s.halo = CozyFx.Halo(root.transform, s.pos, 0.2f, new Color(1f, 0.9f, 0.6f, 0.2f), out s.haloMat);
            s.halo.position = s.pos + Away() * 0.02f;
            s.sparkle = CozyFx.Sparkler(root.transform, new Color(1f, 0.9f, 0.6f), 2f, 0.025f);
            s.sparkle.transform.position = s.pos;
            return s;
        }

        GameObject Line(Vector3 a, Vector3 b, Color c, float width, bool track)
        {
            var go = new GameObject("Link");
            go.transform.SetParent(root.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.widthMultiplier = width;
            lr.sharedMaterial = CozyFx.LineMat();
            lr.startColor = c;
            lr.endColor = c;
            lr.numCapVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (track) links.Add(go);
            return go;
        }

        /// Linie aurie strălucitoare: strălucire largă + miez cu gradient + particule pe traseu.
        IEnumerator LightLink(int ia, int ib)
        {
            if (!Valid(ia) || !Valid(ib)) yield break;
            var a = stars[ia].pos;
            var b = stars[ib].pos;
            var glowGo = Line(a, a, new Color(1f, 0.8f, 0.35f, 0.3f), 0.05f, true);
            var coreGo = Line(a, a, Color.white, 0.012f, true);
            var core = coreGo.GetComponent<LineRenderer>();
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Gold, 0f), new GradientColorKey(new Color(1f, 0.98f, 0.85f), 0.5f), new GradientColorKey(Gold, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            core.colorGradient = g;
            var glow = glowGo.GetComponent<LineRenderer>();
            const float dur = 0.3f;
            for (float e = 0f; e < dur; e += Time.deltaTime)
            {
                if (core == null) yield break;
                var p = Vector3.Lerp(a, b, e / dur);
                core.SetPosition(1, p);
                glow.SetPosition(1, p);
                if (Random.value < 0.6f) CozyFx.Burst(p, Gold, 3, 0.12f, 0.012f);
                yield return null;
            }
            if (core == null) yield break;
            core.SetPosition(1, b);
            glow.SetPosition(1, b);
        }

        void ClearLinks()
        {
            for (int i = 0; i < links.Count; i++) if (links[i] != null) Destroy(links[i]);
            links.Clear();
        }

        void Paint(int i, Color c)
        {
            if (i < 0 || i >= stars.Length) return;
            var s = stars[i];
            if (s == null) return;
            s.color = c;
            if (s.lit != null)
            {
                CozyFx.SetColor(s.lit, c);
                s.lit.color = c;
                var d = c * 0.78f; d.a = 1f;
                CozyFx.SetColor(s.dark, d);
                s.dark.color = d;
            }
            else CozyFx.SetTint(s.t.gameObject, c);
            if (c != Dim && s.glowTarget > 0.5f) s.glow = 0.25f;   // stea atinsă din nou: strălucirea „repornește”
            s.glowTarget = c == Dim ? 0f : 1f;
            var em = s.sparkle.emission;
            em.rateOverTime = c == Dim ? 2f : 16f;
            var main = s.sparkle.main;
            main.startColor = new ParticleSystem.MinMaxGradient(c, Color.Lerp(c, Color.white, 0.7f));
        }

        void PaintAll(Color c)
        {
            for (int i = 0; i < activeStars && i < stars.Length; i++) Paint(i, c);
        }

        /// Nivel 1: fără repetări. Nivel 2: o stea se poate repeta. Nivel 3: cel puțin o stea repetată (ex. 2 → 4 → 2 → 5).
        /// Niciodată aceeași stea de două ori la rând (n-ar putea fi distinsă la afișare).
        void ChooseSequence()
        {
            seq.Clear();
            int count = Mathf.Clamp(activeStars, 1, stars.Length);
            int n = SeqLen;
            if (level <= 1)
            {
                var pool = new List<int>();
                for (int i = 0; i < count; i++) pool.Add(i);
                n = Mathf.Min(n, pool.Count);
                for (int i = 0; i < n; i++)
                {
                    int k = Random.Range(0, pool.Count);
                    seq.Add(pool[k]);
                    pool.RemoveAt(k);
                }
            }
            else
            {
                bool needRepeat = level >= 3;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    seq.Clear();
                    var used = new HashSet<int>();
                    int repeats = 0;
                    int maxRepeats = level >= 3 ? 2 : 1;
                    for (int i = 0; i < n; i++)
                    {
                        int pick = -1;
                        for (int guard = 0; guard < 30; guard++)
                        {
                            int c = Random.Range(0, count);
                            if (seq.Count > 0 && seq[seq.Count - 1] == c) continue;   // nu de două ori la rând
                            bool isRepeat = used.Contains(c);
                            if (isRepeat && repeats >= maxRepeats) continue;
                            pick = c;
                            break;
                        }
                        if (pick < 0) pick = (seq.Count > 0 ? seq[seq.Count - 1] + 1 : 0) % count;
                        if (used.Contains(pick)) repeats++;
                        used.Add(pick);
                        seq.Add(pick);
                    }
                    if (!needRepeat || repeats > 0) break;
                }
            }
            MaxSequenceLength = Mathf.Max(MaxSequenceLength, seq.Count);
        }

        bool LookingAtStars()
        {
            return Vector3.Angle(head.forward, center - head.position) < 40f;
        }

        IEnumerator Run()
        {
            bool fresh = true;
            while (!counting || elapsed < totalSeconds)
            {
                // urcarea de nivel după timp se aplică și după o greșeală (cu o secvență nouă, de lungimea nouă)
                if (counting && LevelFor(CorrectSequences, elapsed) > level) fresh = true;
                if (fresh)
                {
                    if (counting) yield return MaybeLevelUp();
                    ChooseSequence();
                }
                phase = Phase.Show;
                progress = 0;
                ClearLinks();
                status = "Întoarce-te spre bolta cu stele!";
                while (!LookingAtStars()) yield return null;
                counting = true;
                status = $"Nivel {level} · uită-te la cei {seq.Count} pași ai constelației...";
                yield return new WaitForSeconds(0.5f);
                int li = Mathf.Clamp(level - 1, 0, LitPerLevel.Length - 1);
                float lit = Mathf.Min(litSeconds, LitPerLevel[li]);
                float gap = Mathf.Min(gapSeconds, GapPerLevel[li]);
                for (int i = 0; i < seq.Count; i++)
                {
                    int si = seq[i];
                    if (!Valid(si)) continue;
                    Paint(si, Gold);
                    AudioDirector.Sfx("chime", stars[si].pos, 0.5f, 0.9f + 0.12f * si);
                    yield return new WaitForSeconds(lit);
                    Paint(si, Dim);
                    yield return new WaitForSeconds(gap);
                }
                phase = Phase.Input;
                status = $"Nivel {level} · atinge stelele în aceeași ordine (0/{seq.Count})" + (level >= 2 ? "\n<size=44>atenție: o stea poate apărea de mai multe ori</size>" : "");
                while (phase == Phase.Input && elapsed < totalSeconds) yield return null;
                while (phase == Phase.Wait) yield return null;
                fresh = lastOk;
            }
            PaintAll(Dim);
            status = "Gata! Ai aprins " + CorrectSequences + (CorrectSequences == 1 ? " constelație." : " constelații.");
            Finished = true;
        }

        void Update()
        {
            if (head == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < stars.Length; i++)
            {
                var s = stars[i];
                if (s == null || s.halo == null || i >= activeStars) continue;
                s.glow = Mathf.Lerp(s.glow, s.glowTarget, 1f - Mathf.Exp(-10f * dt));
                s.hover = Mathf.Lerp(s.hover, i == hovered && phase == Phase.Input ? 1f : 0f, 1f - Mathf.Exp(-12f * dt));
                float breathe = 1f + Mathf.Sin(Time.time * 2.4f + i * 1.3f) * 0.08f;
                s.halo.localScale = Vector3.one * Mathf.Lerp(0.16f, 0.36f, s.glow) * breathe * (1f + 0.25f * s.hover);
                var hd = s.halo.position - head.position;
                if (hd.sqrMagnitude > 1e-6f) s.halo.rotation = Quaternion.LookRotation(hd.normalized, Vector3.up);
                var c = Color.Lerp(new Color(0.85f, 0.88f, 1f), s.color == Dim ? Gold : s.color, s.glow);
                c.a = Mathf.Lerp(0.18f, 0.55f, Mathf.Max(s.glow, s.hover * 0.6f));
                CozyFx.SetColor(s.haloMat, c);
                float want = 1f + 0.22f * s.hover + 0.15f * s.glow;
                s.t.localScale = Vector3.one * Mathf.MoveTowards(s.t.localScale.x, want, dt * 3f);
                s.t.Rotate(0f, 0f, 12f * dt, Space.Self);
            }
            if (Finished) return;
            if (counting) elapsed += dt;
            Hint = MemoryTaskUtil.DirectionHint(head, center, "bolta cu stele");
        }

        public void Feed(Ray ray, bool click)
        {
            Hovering = false;
            hovered = -1;
            if (Finished || root == null || !root.activeSelf) return;
            var pts = new Vector3[stars.Length];
            for (int i = 0; i < stars.Length; i++) pts[i] = stars[i] != null && i < activeStars ? stars[i].pos : new Vector3(0f, -1000f, 0f);
            int best = MemoryTaskUtil.Pick(ray, pts, hitRadius);
            hovered = best;
            if (best >= 0)
            {
                Hovering = true;
                HoverPoint = pts[best];
            }
            if (phase != Phase.Input || !click || best < 0 || best >= stars.Length || best >= activeStars) return;
            if (progress < 0 || progress >= seq.Count) return;
            if (best == seq[progress])
            {
                Paint(best, Gold);
                CozyFx.Burst(pts[best], Gold, 18, 0.35f, 0.02f);
                AudioDirector.Sfx("chime", pts[best], 0.75f, 1.05f + 0.14f * progress);
                if (progress > 0 && progress - 1 < seq.Count) StartCoroutine(LightLink(seq[progress - 1], best));
                progress++;
                status = $"Bravo! ({progress}/{seq.Count})";
                if (progress >= seq.Count)
                {
                    CorrectSequences++;
                    lastOk = true;
                    phase = Phase.Wait;
                    StartCoroutine(Complete());
                }
            }
            else
            {
                Mistakes++;
                lastOk = false;
                phase = Phase.Wait;
                AudioDirector.Sfx("oops", pts[best], 0.6f);
                StartCoroutine(Oops(best));
            }
        }

        /// Constelația completă: linia finală, apoi un val de sclipici care pornește din centru.
        IEnumerator Complete()
        {
            status = "Constelație completă!";
            yield return new WaitForSeconds(0.4f);
            PaintAll(Gold);
            AudioDirector.Sfx("grow", center, 0.6f, 1.25f);
            for (int i = 0; i < activeStars && i < stars.Length; i++) if (Valid(i)) CozyFx.Burst(stars[i].pos, Gold, 22, 0.45f, 0.022f);
            CozyFx.Burst(center, Color.white, 30, 0.7f, 0.02f);
            var ring = CozyFx.Circle("Wave", root.transform, 0.1f, 0.02f, Gold, 64);
            ring.transform.SetPositionAndRotation(center + Vector3.up * 0.12f - Away() * 0.03f, facing);
            for (float e = 0f; e < 0.9f; e += Time.deltaTime)
            {
                float k = e / 0.9f;
                CozyFx.SetCircle(ring, Mathf.Lerp(0.1f, 1.1f, 1f - (1f - k) * (1f - k)));
                var c = Color.Lerp(Gold, Color.white, k);
                c.a = 1f - k;
                ring.startColor = ring.endColor = c;
                ring.widthMultiplier = Mathf.Lerp(0.025f, 0.004f, k);
                yield return null;
            }
            Destroy(ring.gameObject);
            PaintAll(Dim);
            ClearLinks();
            status = "Bravo! Urmează o constelație nouă...";
            yield return new WaitForSeconds(0.35f);
            phase = Phase.Show;
        }

        IEnumerator Oops(int wrong)
        {
            status = "Hopa! Să încercăm din nou...";
            Paint(wrong, Red);
            if (Valid(wrong)) CozyFx.Burst(stars[wrong].pos, Red, 10, 0.2f, 0.016f);
            yield return new WaitForSeconds(0.7f);
            PaintAll(Dim);
            ClearLinks();
            yield return new WaitForSeconds(0.2f);
            phase = Phase.Show;
        }

        public void SetVisible(bool v)
        {
            if (root != null) root.SetActive(v);
        }

        public void End()
        {
            StopAllCoroutines();
            Finished = true;
            ClearLinks();
            if (root != null) Destroy(root);
        }
    }
}
