using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MindThePaws
{
    public class StarConstellationTask : MonoBehaviour
    {
        public float totalSeconds = 18f;
        public float litSeconds = 0.6f;
        public float gapSeconds = 0.25f;
        public float hitRadius = 0.13f;
        public float distance = 1.8f;

        public bool Finished { get; private set; }
        public bool Hovering { get; private set; }
        public Vector3 HoverPoint { get; private set; }
        public string Status { get; private set; } = "";
        public string Hint { get; private set; } = "";
        public int CorrectSequences { get; private set; }
        public int Mistakes { get; private set; }
        public float Remaining => Mathf.Max(0f, totalSeconds - elapsed);

        enum Phase { Show, Input, Wait }

        static readonly Vector2[] Shape =
        {
            new Vector2(-0.55f, 0.05f),
            new Vector2(-0.25f, 0.32f),
            new Vector2(0.05f, 0.12f),
            new Vector2(0.35f, 0.38f),
            new Vector2(0.6f, 0f)
        };
        static readonly Color Gray = new Color(0.62f, 0.62f, 0.68f);
        static readonly Color Gold = new Color(1f, 0.82f, 0.25f);
        static readonly Color Red = new Color(1f, 0.5f, 0.5f);

        Transform head;
        GameObject starsRoot;
        readonly Transform[] stars = new Transform[5];
        readonly Material[] mats = new Material[5];
        readonly int[] seq = new int[3];
        Vector3 center;
        Phase phase = Phase.Show;
        int progress;
        float elapsed;
        bool counting;
        bool lastOk = true;

        public void Begin(Transform headTransform, List<Vector3> hidden, float seconds)
        {
            head = headTransform;
            totalSeconds = seconds;
            var mean = Vector3.zero;
            for (int i = 0; i < hidden.Count; i++)
            {
                var d = hidden[i] - head.position;
                d.y = 0f;
                mean += d;
            }
            Vector3 away;
            if (mean.sqrMagnitude > 1e-4f) away = -mean.normalized;
            else
            {
                away = -head.forward;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Vector3.back;
                away.Normalize();
            }
            var right = Vector3.Cross(Vector3.up, away);
            center = head.position + away * distance + Vector3.up * 0.45f;
            starsRoot = new GameObject("Stars");
            starsRoot.transform.SetParent(transform, false);
            for (int i = 0; i < stars.Length; i++)
            {
                mats[i] = Graybox.MakeMat(Gray);
                var s = Graybox.Prim(PrimitiveType.Sphere, starsRoot.transform, center + right * Shape[i].x + Vector3.up * Shape[i].y, Vector3.one * 0.09f, mats[i]);
                stars[i] = s.transform;
            }
            StartCoroutine(Run());
        }

        void Paint(int i, Color c)
        {
            var m = mats[i];
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            m.color = c;
        }

        void PaintAll(Color c)
        {
            for (int i = 0; i < stars.Length; i++) Paint(i, c);
        }

        void ChooseSequence()
        {
            var pool = new List<int> { 0, 1, 2, 3, 4 };
            for (int i = 0; i < 3; i++)
            {
                int k = Random.Range(0, pool.Count);
                seq[i] = pool[k];
                pool.RemoveAt(k);
            }
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
                if (fresh) ChooseSequence();
                phase = Phase.Show;
                progress = 0;
                Status = "Întoarce-te spre constelație!";
                while (!LookingAtStars()) yield return null;
                counting = true;
                Status = "Uită-te la stelele care se aprind...";
                yield return new WaitForSeconds(0.5f);
                for (int i = 0; i < 3; i++)
                {
                    Paint(seq[i], Gold);
                    yield return new WaitForSeconds(litSeconds);
                    Paint(seq[i], Gray);
                    yield return new WaitForSeconds(gapSeconds);
                }
                phase = Phase.Input;
                Status = "Atinge stelele în aceeași ordine (0/3)";
                while (phase == Phase.Input && elapsed < totalSeconds) yield return null;
                while (phase == Phase.Wait) yield return null;
                fresh = lastOk;
            }
            PaintAll(Gray);
            Status = "Gata!";
            Finished = true;
        }

        void Update()
        {
            if (Finished || head == null) return;
            if (counting) elapsed += Time.deltaTime;
            var f = head.forward;
            f.y = 0f;
            var to = center - head.position;
            to.y = 0f;
            if (f.sqrMagnitude < 0.01f || to.sqrMagnitude < 0.01f)
            {
                Hint = "";
                return;
            }
            float ang = Vector3.Angle(f, to);
            if (ang < 40f) Hint = "";
            else if (ang > 120f) Hint = "constelația e în spatele tău";
            else Hint = Vector3.Dot(head.right, to) > 0f ? "constelația e în dreapta" : "constelația e în stânga";
        }

        public void Feed(Ray ray, bool click)
        {
            Hovering = false;
            if (Finished || starsRoot == null || !starsRoot.activeSelf) return;
            int best = -1;
            float bestD = hitRadius;
            for (int i = 0; i < stars.Length; i++)
            {
                var oc = stars[i].position - ray.origin;
                float t = Vector3.Dot(oc, ray.direction);
                if (t < 0.05f) continue;
                float d = (oc - ray.direction * t).magnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            if (best >= 0)
            {
                Hovering = true;
                HoverPoint = stars[best].position;
            }
            if (phase != Phase.Input || !click || best < 0) return;
            if (best == seq[progress])
            {
                Paint(best, Gold);
                progress++;
                Status = "Bravo! (" + progress + "/3)";
                if (progress >= 3)
                {
                    CorrectSequences++;
                    lastOk = true;
                    phase = Phase.Wait;
                    StartCoroutine(Flash(Gold, -1, "Bravo! Încă una..."));
                }
            }
            else
            {
                Mistakes++;
                lastOk = false;
                phase = Phase.Wait;
                StartCoroutine(Flash(Red, best, "Hopa! Să încercăm din nou..."));
            }
        }

        IEnumerator Flash(Color c, int only, string text)
        {
            Status = text;
            if (only < 0) PaintAll(c);
            else Paint(only, c);
            yield return new WaitForSeconds(0.6f);
            PaintAll(Gray);
            yield return new WaitForSeconds(0.2f);
            phase = Phase.Show;
        }

        public void SetVisible(bool v)
        {
            if (starsRoot != null) starsRoot.SetActive(v);
        }

        public void End()
        {
            StopAllCoroutines();
            Finished = true;
            if (starsRoot != null) Destroy(starsRoot);
        }
    }
}