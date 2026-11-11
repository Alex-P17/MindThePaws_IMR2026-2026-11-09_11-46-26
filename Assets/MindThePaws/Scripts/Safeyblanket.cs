using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MindThePaws
{
    public class SafetyBlanket : MonoBehaviour
    {
        public OVRCameraRig cameraRig;
        public Transform head;
        public bool armed = true;
        public float breatheSeconds = 4f;
        public int breatheCycles = 2;
        public float palmHoldSeconds = 1.2f;
        public Vector3 palmLocalNormal = Vector3.down;
        public bool flipPalmNormal;
        public float palmFacingAngle = 50f;

        public System.Action onStart;
        public System.Action<bool> onFinish;

        public bool Running { get; private set; }
        public bool Choosing { get; private set; }
        public bool Hovering { get; private set; }
        public Vector3 HoverPoint { get; private set; }

        static readonly Color Gold = new Color(1f, 0.82f, 0.4f);

        OVRHand leftHand;
        float nextSearch;
        float holdTimer;
        GameObject wristOrb;
        GameObject hudRoot;
        Transform hudOrb;
        GameObject breath;
        TextMesh breathText;
        readonly GameObject[] buttons = new GameObject[2];
        readonly TextMesh[] buttonLabels = new TextMesh[2];
        TextMesh promptText;
        bool skip;
        int choice;

        Vector3 PalmNormalLocal()
        {
            return flipPalmNormal ? -palmLocalNormal : palmLocalNormal;
        }

        void Update()
        {
            if (head == null) return;
            if (Time.unscaledTime >= nextSearch)
            {
                FindLeftHand();
                nextSearch = Time.unscaledTime + 2f;
            }
            UpdateWrist();
            if (!armed || Running)
            {
                if (hudRoot != null) hudRoot.SetActive(false);
                holdTimer = 0f;
                return;
            }
            if (Triggered())
            {
                Begin();
                return;
            }
            bool facing = PalmFacing();
            holdTimer = facing ? holdTimer + Time.unscaledDeltaTime : Mathf.Max(0f, holdTimer - Time.unscaledDeltaTime * 2f);
            UpdateHud(facing);
            if (holdTimer >= palmHoldSeconds)
            {
                holdTimer = 0f;
                Begin();
            }
        }

        void FindLeftHand()
        {
            leftHand = null;
            var hs = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
            for (int i = 0; i < hs.Length; i++)
            {
                var h = hs[i];
                bool left = cameraRig != null && cameraRig.leftHandAnchor != null && h.transform.IsChildOf(cameraRig.leftHandAnchor);
                if (!left) left = h.name.ToLowerInvariant().Contains("left");
                if (left)
                {
                    leftHand = h;
                    break;
                }
            }
        }

        void UpdateWrist()
        {
            if (wristOrb == null)
            {
                wristOrb = new GameObject("BlanketWristOrb");
                Graybox.Prim(PrimitiveType.Sphere, wristOrb.transform, Vector3.zero, Vector3.one * 0.022f, Graybox.MakeMat(Gold));
            }
            bool show = armed && !Running && leftHand != null && leftHand.IsTracked;
            wristOrb.SetActive(show);
            if (show) wristOrb.transform.position = leftHand.transform.TransformPoint(-PalmNormalLocal() * 0.05f);
        }

        bool Triggered()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) return true;
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Space)) return true;
#endif
            return OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch) ||
                   OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch);
        }

        bool PalmFacing()
        {
            if (leftHand == null || !leftHand.IsTracked) return false;
            var n = leftHand.transform.TransformDirection(PalmNormalLocal()).normalized;
            var toHead = head.position - leftHand.transform.position;
            if (toHead.magnitude > 0.8f) return false;
            if (Vector3.Angle(n, toHead) > palmFacingAngle) return false;
            return Vector3.Angle(head.forward, -toHead) < 60f;
        }

        void UpdateHud(bool facing)
        {
            if (hudRoot == null) BuildHud();
            hudRoot.SetActive(facing);
            if (facing) hudOrb.localScale = Vector3.one * (0.035f + 0.03f * Mathf.Clamp01(holdTimer / palmHoldSeconds));
        }

        void BuildHud()
        {
            hudRoot = new GameObject("BlanketHud");
            hudRoot.transform.SetParent(head, false);
            hudRoot.transform.localPosition = new Vector3(-0.2f, -0.17f, 0.65f);
            hudOrb = Graybox.Prim(PrimitiveType.Sphere, hudRoot.transform, Vector3.zero, Vector3.one * 0.035f, Graybox.MakeMat(Gold)).transform;
            var tm = Graybox.Text("BlanketHudText", 0.0022f);
            tm.richText = true;
            tm.text = "Pătura Moale\n<size=40>ține palma spre tine</size>";
            tm.transform.SetParent(hudRoot.transform, false);
            tm.transform.localPosition = new Vector3(0f, -0.055f, 0f);
        }

        public void Begin()
        {
            if (Running || head == null) return;
            Running = true;
            Choosing = false;
            Hovering = false;
            skip = false;
            choice = 0;
            if (hudRoot != null) hudRoot.SetActive(false);
            if (onStart != null) onStart();
            StartCoroutine(Sequence());
        }

        Vector3 FlatForward()
        {
            var f = head.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            return f.normalized;
        }

        Vector3 BreathTarget()
        {
            return head.position + FlatForward() * 1f + Vector3.down * 0.1f;
        }

        IEnumerator Sequence()
        {
            breath = new GameObject("BreathBubble");
            Graybox.Prim(PrimitiveType.Sphere, breath.transform, Vector3.zero, Vector3.one, Graybox.MakeMat(Gold));
            breathText = Graybox.Text("BreathText", 0.0045f);
            breathText.richText = true;
            breath.transform.position = BreathTarget();
            breath.transform.localScale = Vector3.one * 0.12f;
            for (int c = 0; c < breatheCycles && !skip; c++)
            {
                yield return Breathe(true, c);
                if (skip) break;
                yield return Breathe(false, c);
            }
            Destroy(breath);
            Destroy(breathText.gameObject);
            BuildChoice();
            Choosing = true;
            while (choice == 0) yield return null;
            Choosing = false;
            Hovering = false;
            DestroyChoice();
            Running = false;
            if (onFinish != null) onFinish(choice == 1);
        }

        IEnumerator Breathe(bool inhale, int cycle)
        {
            for (float e = 0f; e < breatheSeconds && !skip; e += Time.unscaledDeltaTime)
            {
                float k = Mathf.Clamp01(e / breatheSeconds);
                k = k * k * (3f - 2f * k);
                float s = inhale ? Mathf.Lerp(0.12f, 0.3f, k) : Mathf.Lerp(0.3f, 0.12f, k);
                breath.transform.localScale = Vector3.one * s;
                breath.transform.position = Vector3.Lerp(breath.transform.position, BreathTarget(), Mathf.Clamp01(5f * Time.unscaledDeltaTime));
                breathText.transform.position = breath.transform.position + Vector3.up * (s * 0.5f + 0.1f);
                breathText.transform.rotation = Quaternion.LookRotation(breathText.transform.position - head.position, Vector3.up);
                breathText.text = (inhale ? "Inspiră..." : "Expiră...") + "\n<size=40>" + (cycle + 1) + "/" + breatheCycles + "  ·  pinch = sari peste</size>";
                yield return null;
            }
        }

        void BuildChoice()
        {
            var f = FlatForward();
            var r = Vector3.Cross(Vector3.up, f);
            var basePos = head.position + f * 0.9f + Vector3.down * 0.08f;
            string[] names = { "Continuă\n<size=40>(haos la jumătate)</size>", "Mergi la\n<size=40>Debriefing</size>" };
            Color[] cols = { new Color(0.55f, 0.9f, 0.6f), new Color(0.6f, 0.75f, 1f) };
            for (int i = 0; i < 2; i++)
            {
                var b = new GameObject("BlanketChoice" + i);
                Graybox.Prim(PrimitiveType.Sphere, b.transform, Vector3.zero, Vector3.one, Graybox.MakeMat(cols[i]));
                b.transform.position = basePos + r * (i == 0 ? -0.3f : 0.3f);
                b.transform.localScale = Vector3.one * 0.12f;
                buttons[i] = b;
                var tm = Graybox.Text("BlanketChoiceText" + i, 0.0035f);
                tm.richText = true;
                tm.text = names[i];
                tm.transform.position = b.transform.position + Vector3.up * 0.13f;
                tm.transform.rotation = Quaternion.LookRotation(tm.transform.position - head.position, Vector3.up);
                buttonLabels[i] = tm;
            }
            promptText = Graybox.Text("BlanketPrompt", 0.0035f);
            promptText.richText = true;
            promptText.text = "Respirăm împreună.\nCe vrei să facem acum?";
            promptText.transform.position = basePos + Vector3.up * 0.3f;
            promptText.transform.rotation = Quaternion.LookRotation(promptText.transform.position - head.position, Vector3.up);
        }

        void DestroyChoice()
        {
            for (int i = 0; i < 2; i++)
            {
                if (buttons[i] != null) Destroy(buttons[i]);
                if (buttonLabels[i] != null) Destroy(buttonLabels[i].gameObject);
                buttons[i] = null;
                buttonLabels[i] = null;
            }
            if (promptText != null) Destroy(promptText.gameObject);
            promptText = null;
        }

        public void Feed(Ray ray, bool click)
        {
            Hovering = false;
            if (!Running) return;
            if (!Choosing)
            {
                if (click) skip = true;
                return;
            }
            int best = -1;
            float bestD = 0.1f;
            for (int i = 0; i < 2; i++)
            {
                if (buttons[i] == null) continue;
                var oc = buttons[i].transform.position - ray.origin;
                float t = Vector3.Dot(oc, ray.direction);
                if (t < 0.05f) continue;
                float d = (oc - ray.direction * t).magnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            for (int i = 0; i < 2; i++)
                if (buttons[i] != null) buttons[i].transform.localScale = Vector3.one * (i == best ? 0.14f : 0.12f);
            if (best >= 0)
            {
                Hovering = true;
                HoverPoint = buttons[best].transform.position;
                if (click) choice = best + 1;
            }
        }

        void OnDestroy()
        {
            if (breath != null) Destroy(breath);
            if (breathText != null) Destroy(breathText.gameObject);
            DestroyChoice();
            if (wristOrb != null) Destroy(wristOrb);
            if (hudRoot != null) Destroy(hudRoot);
        }
    }
}