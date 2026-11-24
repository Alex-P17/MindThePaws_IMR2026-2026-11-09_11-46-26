using UnityEngine;

namespace MindThePaws
{
    public enum CozyMotion
    {
        Hover,   // plutire lină + înclinare (Lumi, Pisicuța Spirit, Bule-Miraj, Felinar)
        Waddle,  // legănare laterală când se deplasează (căței)
        Pulse,   // pulsare blândă (fantome, stele)
        Sway     // legănat în vânt (flori)
    }

    /// <summary>
    /// Mișcare procedurală care merge la fel pe modelele din Blender și pe cele Graybox.
    /// Mută toți copiii obiectului sub un pivot intern ("CozyPivot") și animează doar pivotul,
    /// așa că nu se bate cap în cap cu scripturile care mișcă rădăcina (ZoomieRunner, CorgiWander, Lumi...).
    /// Se atașează singur din fabricile Graybox; nu trebuie pus în Inspector.
    /// </summary>
    public class CozyAnimator : MonoBehaviour
    {
        public CozyMotion motion = CozyMotion.Hover;
        public float strength = 1f;

        Transform pivot;
        Vector3 lastPos;
        float phase;
        float speedSmooth;
        float waddlePhase;

        public static CozyAnimator Attach(GameObject go, CozyMotion motion, float strength = 1f)
        {
            if (go == null) return null;
            var a = go.GetComponent<CozyAnimator>();
            if (a == null) a = go.AddComponent<CozyAnimator>();
            a.motion = motion;
            a.strength = strength;
            a.EnsurePivot();
            return a;
        }

        void Awake()
        {
            phase = Random.value * 10f;
            lastPos = transform.position;
        }

        void EnsurePivot()
        {
            if (pivot != null) return;
            var existing = transform.Find("CozyPivot");
            if (existing != null) { pivot = existing; return; }
            var p = new GameObject("CozyPivot").transform;
            int n = transform.childCount;
            var kids = new Transform[n];
            for (int i = 0; i < n; i++) kids[i] = transform.GetChild(i);
            p.SetParent(transform, false);
            for (int i = 0; i < n; i++) kids[i].SetParent(p, false);
            pivot = p;
        }

        void OnDisable()
        {
            if (pivot == null) return;
            pivot.localPosition = Vector3.zero;
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one;
        }

        void LateUpdate()
        {
            if (pivot == null) EnsurePivot();
            float t = Time.time + phase;
            float dt = Mathf.Max(Time.deltaTime, 1e-5f);
            switch (motion)
            {
                case CozyMotion.Hover:
                {
                    float k = strength;
                    pivot.localPosition = new Vector3(0f, Mathf.Sin(t * 1.6f) * 0.012f * k, 0f);
                    pivot.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.1f) * 4f * k, Mathf.Sin(t * 0.7f) * 3f * k, Mathf.Sin(t * 1.3f + 1f) * 5f * k);
                    break;
                }
                case CozyMotion.Waddle:
                {
                    var p = transform.position;
                    var v = p - lastPos; v.y = 0f;
                    lastPos = p;
                    float sp = Time.deltaTime > 0f ? v.magnitude / dt : 0f;
                    speedSmooth = Mathf.Lerp(speedSmooth, Mathf.Clamp01(sp / 0.8f), 1f - Mathf.Exp(-8f * dt));
                    waddlePhase += dt * (6f + 10f * speedSmooth);
                    float k = speedSmooth * strength;
                    // la oprire: respirație mică, ca să nu pară încremenit
                    float idle = (1f - speedSmooth) * Mathf.Sin(t * 2.2f);
                    pivot.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(waddlePhase)) * 0.012f * k, 0f);
                    pivot.localRotation = Quaternion.Euler(idle * 1.5f, Mathf.Sin(waddlePhase) * 4f * k, Mathf.Sin(waddlePhase) * 9f * k);
                    pivot.localScale = new Vector3(1f, 1f + idle * 0.015f, 1f);
                    break;
                }
                case CozyMotion.Pulse:
                {
                    float s = 1f + Mathf.Sin(t * 3f) * 0.06f * strength;
                    pivot.localScale = Vector3.one * s;
                    break;
                }
                case CozyMotion.Sway:
                {
                    float k = strength;
                    pivot.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.9f) * 3f * k, 0f, Mathf.Sin(t * 1.2f + 0.5f) * 4f * k);
                    break;
                }
            }
        }
    }
}
