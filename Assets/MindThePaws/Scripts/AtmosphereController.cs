using UnityEngine;

namespace MindThePaws
{
    public enum CozyMood { Calm, Chaos, Blanket, Garden }

    /// <summary>
    /// Atmosfera jocului, adăugată automat de MindThePawsGame:
    ///  - particule ambientale în jurul jucătorului: praf de stele auriu (Calm), confetti jucăuș + bule iridescente (Chaos),
    ///    scântei calde (Pătura Moale), petale plutitoare (Grădina Amintirilor);
    ///  - sclipici la plasarea comorilor (Sparkle).
    /// UI-ul (ecuson, inventar, dialog, tranziții) e în CozyUIManager.
    /// Toate particulele merg pe timp ne-scalat, ca să continue și când Pătura Moale pune jocul pe pauză.
    /// </summary>
    public class AtmosphereController : MonoBehaviour
    {
        public Transform head;
        public bool ambientParticles = true;
        [Range(0f, 2f)] public float particleDensity = 1f;

        public CozyMood Mood { get; private set; } = CozyMood.Calm;

        ParticleSystem stardust, confetti, bubbles, embers, petals;
        GameObject fxRoot;

        // ------------------------------------------------------------ API

        public void SetMood(CozyMood mood)
        {
            Mood = mood;
            EnsureParticles();
            Toggle(stardust, mood == CozyMood.Calm);
            Toggle(confetti, mood == CozyMood.Chaos);
            Toggle(bubbles, mood == CozyMood.Chaos);
            Toggle(embers, mood == CozyMood.Blanket);
            Toggle(petals, mood == CozyMood.Garden);
        }

        /// Sclipici la plasarea unei comori (sau oriunde vrei un mic "ta-da").
        public void Sparkle(Vector3 at, Color? tint = null)
        {
            var c = tint ?? new Color(1f, 0.85f, 0.4f);
            CozyFx.Burst(at + Vector3.up * 0.04f, c, 26, 0.4f, 0.022f);
            CozyFx.Burst(at + Vector3.up * 0.04f, Color.white, 10, 0.25f, 0.014f);
        }

        // ------------------------------------------------------------ loop

        void LateUpdate()
        {
            if (head == null) return;
            if (fxRoot != null) fxRoot.transform.position = head.position;
        }

        // ------------------------------------------------------------ particule

        static void Toggle(ParticleSystem ps, bool on)
        {
            if (ps == null) return;
            if (on && !ps.isPlaying) ps.Play(true);
            else if (!on && ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void EnsureParticles()
        {
            if (!ambientParticles || fxRoot != null) return;
            fxRoot = new GameObject("CozyAtmosphere");
            if (head != null) fxRoot.transform.position = head.position;
            float d = Mathf.Max(0.05f, particleDensity);

            stardust = MakeSystem("Stardust", CozyFx.SoftDot, 14f * d, 6f, 9f, 0.006f, 0.016f, 0.03f,
                new Color(1f, 0.86f, 0.45f), new Color(1f, 0.95f, 0.75f), 2.2f, 0f, 0.05f);

            confetti = MakeSystem("Confetti", CozyFx.SoftDot, 22f * d, 2.5f, 4f, 0.01f, 0.022f, 0.25f,
                new Color(1f, 0.55f, 0.75f), new Color(0.55f, 0.85f, 1f), 1.8f, 0f, 0.35f);
            var cm = confetti.main;
            cm.startColor = new ParticleSystem.MinMaxGradient(RainbowGradient()) { mode = ParticleSystemGradientMode.RandomColor };

            bubbles = MakeSystem("IridescentBubbles", CozyFx.BubbleTex, 3f * d, 5f, 8f, 0.035f, 0.08f, 0.06f,
                Color.white, Color.white, 1.8f, -0.01f, 0.12f);
            var bcol = bubbles.colorOverLifetime;
            bcol.enabled = true;
            bcol.color = new ParticleSystem.MinMaxGradient(IridescentGradient());

            embers = MakeSystem("WarmEmbers", CozyFx.SoftDot, 10f * d, 5f, 8f, 0.008f, 0.02f, 0.04f,
                new Color(1f, 0.78f, 0.45f), new Color(1f, 0.9f, 0.7f), 1.6f, -0.004f, 0.05f);

            petals = MakeSystem("Petals", CozyFx.PetalTex, 9f * d, 6f, 9f, 0.02f, 0.035f, 0.05f,
                new Color(1f, 0.7f, 0.82f), new Color(1f, 0.92f, 0.95f), 2.2f, 0.012f, 0.1f);
            var pm = petals.main;
            pm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var rot = petals.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
            var psh = petals.shape;
            psh.position = new Vector3(0f, 0.6f, 0f);
        }

        ParticleSystem MakeSystem(string name, Texture tex, float rate, float lifeMin, float lifeMax,
            float sizeMin, float sizeMax, float speed, Color c0, Color c1, float radius, float gravity, float noise)
        {
            var go = new GameObject(name);
            go.transform.SetParent(fxRoot.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.useUnscaledTime = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);
            main.gravityModifier = gravity;
            main.maxParticles = Mathf.CeilToInt(rate * lifeMax * 1.3f) + 10;

            var em = ps.emission;
            em.rateOverTime = rate;

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius;
            sh.radiusThickness = 0.6f;   // nu chiar în fața ochilor

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(CozyFx.FadeGradient(Color.white, Color.white));

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var curve = new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.7f));
            sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

            if (noise > 0f)
            {
                var n = ps.noise;
                n.enabled = true;
                n.strength = noise;
                n.frequency = 0.4f;
                n.scrollSpeed = 0.2f;
                n.damping = true;
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = CozyFx.VertexColorMat(tex);
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.minParticleSize = 0f;
            r.maxParticleSize = 0.5f;
            return ps;
        }

        static Gradient RainbowGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.55f, 0.75f), 0f),
                    new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0.25f),
                    new GradientColorKey(new Color(0.55f, 0.95f, 0.75f), 0.5f),
                    new GradientColorKey(new Color(0.55f, 0.8f, 1f), 0.75f),
                    new GradientColorKey(new Color(0.8f, 0.6f, 1f), 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        static Gradient IridescentGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.75f, 0.9f, 1f), 0f),
                    new GradientColorKey(new Color(1f, 0.75f, 0.95f), 0.35f),
                    new GradientColorKey(new Color(0.8f, 1f, 0.85f), 0.65f),
                    new GradientColorKey(new Color(1f, 0.95f, 0.7f), 1f)
                },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0.9f, 0.8f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        void OnDestroy()
        {
            if (fxRoot != null) Destroy(fxRoot);
        }
    }
}
