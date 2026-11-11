using System.Collections.Generic;
using UnityEngine;

namespace MindThePaws
{
    public class ChaosBehaviours : MonoBehaviour
    {
        public float lookAngle = 15f;
        public int bubbleCount = 10;
        public float zoomieSpeed = 1.1f;
        public int distractionLookCount;
        public float distractionLookSeconds;

        class GazeTarget
        {
            public Transform t;
            public float lift;
            public bool looking;
        }

        Transform head;
        float floorY;
        List<Vector3> positions = new List<Vector3>();
        List<string> types = new List<string>();
        float intensity = 1f;
        bool running;
        bool frozen;
        readonly List<GameObject> spawned = new List<GameObject>();
        readonly List<GameObject> puppies = new List<GameObject>();
        readonly List<GazeTarget> gaze = new List<GazeTarget>();

        public bool Running => running;
        public bool Frozen => frozen;

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

            var center = new Vector3(head.position.x, floorY, head.position.z);

            var zoomie = Graybox.Corgi("Mochi_Zoomie");
            zoomie.transform.position = center + new Vector3(0.55f, 0f, 0f);
            var zr = zoomie.AddComponent<ZoomieRunner>();
            zr.head = head;
            zr.floorY = floorY;
            zr.radius = 0.55f;
            zr.speed = zoomieSpeed * intensity;
            Track(zoomie, 0.15f, true);

            if (intensity > 0.75f && types.Count > 0)
            {
                var thief = Graybox.Corgi("ThiefPuppy_Gogoasa");
                var decoy = Graybox.TreasureByType(types[Random.Range(0, types.Count)], false, true);
                decoy.transform.SetParent(thief.transform, false);
                decoy.transform.localPosition = new Vector3(0f, 0.31f, 0.1f);
                decoy.transform.localScale = Vector3.one * 0.6f;
                thief.transform.position = center + new Vector3(-1f, 0f, 1f);
                var tw = thief.AddComponent<CorgiWander>();
                tw.center = center;
                tw.floorY = floorY;
                tw.radius = 1.7f;
                tw.speed = 0.95f;
                Track(thief, 0.15f, true);
            }

            if (positions.Count > 0)
            {
                var p = positions[Random.Range(0, positions.Count)];
                var toHead = head.position - p;
                toHead.y = 0f;
                var off = toHead.sqrMagnitude > 1e-4f ? toHead.normalized * 0.12f : Vector3.zero;
                var clingy = Graybox.Corgi("ClingyPuppy_Fasolica");
                clingy.transform.position = new Vector3(p.x + off.x, floorY, p.z + off.z);
                clingy.transform.localScale = new Vector3(1f, 0.85f, 1f);
                var cn = clingy.AddComponent<ClingyNod>();
                cn.head = head;
                Track(clingy, 0.15f, true);
            }

            var cat = Graybox.SpiritCat("Neon Spirit Cat");
            var cf = cat.AddComponent<CatFlight>();
            cf.head = head;
            cf.speed = intensity;
            Track(cat, 0.12f, false);

            /// Bule care plutesc în fața ochilor în timpul ghicirii (ocluzie vizuală).
            int n = Mathf.RoundToInt(bubbleCount * intensity);
            var bubbleMat = Graybox.MakeMat(new Color(0.8f, 0.92f, 1f));
            for (int i = 0; i < n; i++)
            {
                var b = new GameObject("Bubble");
                Graybox.Prim(PrimitiveType.Sphere, b.transform, Vector3.zero, Vector3.one * Random.Range(0.03f, 0.07f), bubbleMat);
                var f = b.AddComponent<BubbleFloater>();
                f.head = head;
                b.transform.position = f.Spot();
                spawned.Add(b);
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
                }
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
            Begin(head, floorY, positions, types, newIntensity);
        }

        public void Clear()
        {
            for (int i = 0; i < spawned.Count; i++)
                if (spawned[i] != null) Destroy(spawned[i]);
            spawned.Clear();
            puppies.Clear();
            gaze.Clear();
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

        void Update()
        {
            if (head == null) return;
            var d = head.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) return;
            var yaw = Quaternion.LookRotation(d.normalized, Vector3.up);
            var nod = Quaternion.Euler(Mathf.Sin(Time.time * 3.5f) * 7f, 0f, 0f);
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
}