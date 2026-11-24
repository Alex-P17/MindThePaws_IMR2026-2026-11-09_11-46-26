using UnityEngine;
using UnityEngine.UI;

namespace MindThePaws
{
    /// <summary>
    /// Etichetă world-space (Canvas + panou rotunjit + UI.Text), înlocuiește vechiul TextMesh 3D.
    /// Poate să se întoarcă singură spre cameră (billboard) și să stea deasupra unui obiect (follow).
    /// </summary>
    public class CozyLabel : MonoBehaviour
    {
        public Text uiText;
        public CozyUI.Panel panel;
        public Transform faceTarget;       // dacă e setat, eticheta se întoarce spre el (de obicei capul)
        public Transform follow;           // dacă e setat, eticheta stă deasupra lui
        public Vector3 followOffset = new Vector3(0f, 0.35f, 0f);

        string raw = "";

        public string text
        {
            get => raw;
            set
            {
                raw = value ?? "";
                if (uiText != null) uiText.text = CozyUI.FixSize(raw, uiText.fontSize);
            }
        }

        public Color color
        {
            get => uiText != null ? uiText.color : Color.white;
            set { if (uiText != null) uiText.color = value; }
        }

        CanvasGroup group;

        /// Estompează toată eticheta (text + panou).
        public void SetAlpha(float a)
        {
            if (group == null) group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = Mathf.Clamp01(a);
        }

        public void SetPanelColors(Color fill, Color border)
        {
            if (panel != null) panel.SetColors(fill, border);
        }

        void LateUpdate()
        {
            if (follow != null) transform.position = follow.position + followOffset;
            if (faceTarget != null)
            {
                var d = transform.position - faceTarget.position;
                if (d.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            }
        }
    }
}
