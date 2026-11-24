using System.Collections.Generic;
using UnityEngine;

namespace MindThePaws
{
    /// Mini-joc de memorie de lucru jucat între "Poof!" și ghicire.
    /// Implementat de StarConstellationTask (Constelația Pierdută) și MoonBakeryTask (Brutăria Lunii).
    public interface IMemoryTask
    {
        string Title { get; }
        bool Finished { get; }
        bool Hovering { get; }
        Vector3 HoverPoint { get; }
        string Status { get; }
        string Hint { get; }
        int CorrectSequences { get; }
        int Mistakes { get; }
        float Remaining { get; }
        /// Nivelul curent de dificultate (1–3) și maximele atinse în această provocare (pentru telemetrie).
        int Level { get; }
        int MaxLevel { get; }
        int MaxSequenceLength { get; }

        void Begin(Transform head, List<Vector3> hidden, float seconds);
        void Feed(Ray ray, bool click);
        void SetVisible(bool visible);
        void End();
    }

    public enum MemoryTaskKind { Constellation, Bakery }

    public static class MemoryTaskUtil
    {
        /// Direcția orizontală opusă comorilor ascunse (ca jucătorul să se întoarcă de la ele).
        public static Vector3 AwayFromHidden(Transform head, List<Vector3> hidden)
        {
            var mean = Vector3.zero;
            for (int i = 0; i < hidden.Count; i++)
            {
                var d = hidden[i] - head.position;
                d.y = 0f;
                mean += d;
            }
            if (mean.sqrMagnitude > 1e-4f) return -mean.normalized;
            var away = -head.forward;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.back;
            return away.normalized;
        }

        /// "e în dreapta / stânga / în spatele tău" sau "" dacă e în față.
        public static string DirectionHint(Transform head, Vector3 target, string what)
        {
            var f = head.forward;
            f.y = 0f;
            var to = target - head.position;
            to.y = 0f;
            if (f.sqrMagnitude < 0.01f || to.sqrMagnitude < 0.01f) return "";
            float ang = Vector3.Angle(f, to);
            if (ang < 40f) return "";
            if (ang > 120f) return what + " e în spatele tău";
            return Vector3.Dot(head.right, to) > 0f ? what + " e în dreapta" : what + " e în stânga";
        }

        /// Cel mai apropiat punct de rază (distanța perpendiculară) sub o rază de selecție.
        public static int Pick(Ray ray, IList<Vector3> points, float radius)
        {
            int best = -1;
            float bestD = radius;
            for (int i = 0; i < points.Count; i++)
            {
                var oc = points[i] - ray.origin;
                float t = Vector3.Dot(oc, ray.direction);
                if (t < 0.05f) continue;
                float d = (oc - ray.direction * t).magnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }
    }
}
