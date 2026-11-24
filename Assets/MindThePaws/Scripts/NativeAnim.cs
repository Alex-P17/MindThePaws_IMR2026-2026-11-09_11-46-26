using UnityEngine;

namespace MindThePaws
{
    /// <summary>
    /// Redă animațiile native ale unui model din Blender (componentă Animation, clipuri Legacy importate de
    /// CorgiModelImport): Idle, mers/alergare (Run_Zoomie / Trotting_Decoy), Clingy_Nod și Sleep_Lay.
    /// Alege singur clipul după cum se mișcă cățelul și după comportamentul atașat (ClingyNod → Clingy_Nod),
    /// iar Pătura Moale îl adoarme cu Sleep_Lay (funcționează și cu jocul pe pauză, prin Animation.Sample).
    ///
    /// Fără conflicte:
    ///  - CozyAnimator mișcă doar pivotul „CozyPivot” (părintele modelului), nu oasele → pentru modelele animate
    ///    Graybox îi reduce intensitatea (doar o legănare discretă peste animația nativă);
    ///  - ClingyNod nu mai dă singur din cap dacă există clipul Clingy_Nod;
    ///  - ChaosBehaviours.Lay folosește Sleep_Lay în loc să culce rădăcina pe o parte;
    ///  - HappyWag rotește osul „tail” în LateUpdate, după evaluarea animației.
    /// </summary>
    public class NativeAnim : MonoBehaviour
    {
        public Animation anim;
        public string idle, move, special, sleep;
        public float runSpeedFor1x = 0.8f;

        string current;
        bool sleeping;
        Vector3 lastPos;
        float speed;

        public bool HasClips => anim != null && anim.GetClipCount() > 0;
        public bool HasSleep => !string.IsNullOrEmpty(sleep);
        public bool HasSpecial => !string.IsNullOrEmpty(special);
        public bool Sleeping => sleeping;

        /// Atașează controlul dacă modelul are clipuri; altfel întoarce null (rămâne mișcarea procedurală).
        public static NativeAnim Setup(GameObject root, string modelKey)
        {
            if (root == null) return null;
            var a = root.GetComponentInChildren<Animation>(true);
            if (a == null || a.GetClipCount() == 0) return null;
            var na = root.GetComponent<NativeAnim>();
            if (na == null) na = root.AddComponent<NativeAnim>();
            na.anim = a;
            a.playAutomatically = false;
            foreach (AnimationState s in a)
                s.wrapMode = WrapMode.Loop;
            na.idle = na.Find("Idle");
            na.sleep = na.Find("Sleep_Lay", "Sleep");
            switch (modelKey)
            {
                case "corgi_gogoasa":
                case "corgi_thief":
                    na.move = na.Find("Trotting_Decoy", "Trot", "Run_Zoomie", "Run");
                    break;
                case "corgi_fasolica":
                case "corgi_clingy":
                    na.special = na.Find("Clingy_Nod", "Nod");
                    na.move = na.Find("Trotting_Decoy", "Run_Zoomie", "Trot", "Run");
                    break;
                default:
                    na.move = na.Find("Run_Zoomie", "Run", "Trotting_Decoy", "Trot");
                    break;
            }
            if (na.idle == null)
                foreach (AnimationState s in a) { na.idle = s.name; break; }
            na.Play(na.idle, 0f);
            return na;
        }

        /// Caută un clip după nume (acceptă și prefixe de tip „Corgi_Mochi|Idle”), fără diferențe de majuscule.
        public string Find(params string[] names)
        {
            if (anim == null) return null;
            foreach (var n in names)
                foreach (AnimationState s in anim)
                {
                    string sn = s.name;
                    if (string.Equals(sn, n, System.StringComparison.OrdinalIgnoreCase) ||
                        sn.EndsWith("|" + n, System.StringComparison.OrdinalIgnoreCase) ||
                        sn.EndsWith("_" + n, System.StringComparison.OrdinalIgnoreCase))
                        return sn;
                }
            return null;
        }

        void Play(string clip, float fade)
        {
            if (anim == null || string.IsNullOrEmpty(clip) || clip == current) return;
            if (anim[clip] == null) return;
            if (fade > 0f) anim.CrossFade(clip, fade);
            else anim.Play(clip);
            current = clip;
        }

        void OnEnable()
        {
            lastPos = transform.position;
        }

        void Update()
        {
            if (!HasClips || sleeping) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            var d = transform.position - lastPos;
            d.y = 0f;
            lastPos = transform.position;
            speed = Mathf.Lerp(speed, d.magnitude / dt, 1f - Mathf.Exp(-8f * dt));

            string want;
            if (HasSpecial && GetComponent<ClingyNod>() != null) want = special;
            else if (speed > 0.15f && !string.IsNullOrEmpty(move)) want = move;
            else want = idle;
            Play(want, 0.25f);
            if (want == move && anim[move] != null)
                anim[move].speed = Mathf.Clamp(speed / Mathf.Max(0.1f, runSpeedFor1x), 0.6f, 1.8f);
        }

        /// Pătura Moale: culcă cățelul cu Sleep_Lay. Merge și când Time.timeScale = 0 (Sample fixează poza).
        public void SetSleeping(bool on)
        {
            if (!HasClips) return;
            sleeping = on;
            if (on && HasSleep && anim[sleep] != null)
            {
                anim.Stop();
                var st = anim[sleep];
                st.enabled = true;
                st.weight = 1f;
                st.time = st.length * 0.5f;
                anim.Play(sleep);
                anim.Sample();
                current = sleep;
            }
            else if (!on)
            {
                current = null;
                Play(idle, 0.3f);
            }
        }
    }
}
