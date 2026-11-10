using UnityEngine;

namespace MindThePaws
{
    /// Corgi-cub care țopăie aleatoriu pe podea, în jurul jucătorului.
    public class CorgiWander : MonoBehaviour
    {
        public Vector3 center;
        public float radius = 1.5f;
        public float floorY;
        public float speed = 0.6f;

        Vector3 target;
        float phase;

        void Start()
        {
            phase = Random.value * 10f;
            PickTarget();
        }

        void PickTarget()
        {
            var c = Random.insideUnitCircle * radius;
            target = new Vector3(center.x + c.x, floorY, center.z + c.y);
        }

        void Update()
        {
            var p = transform.position;
            var flat = new Vector3(p.x, floorY, p.z);
            var dir = target - flat;
            dir.y = 0f;
            if (dir.magnitude < 0.1f) { PickTarget(); return; }

            flat += dir.normalized * speed * Time.deltaTime;
            float hop = Mathf.Abs(Mathf.Sin((Time.time + phase) * 8f)) * 0.04f;
            transform.position = flat + Vector3.up * hop;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir.normalized, Vector3.up), 8f * Time.deltaTime);
        }
    }
}
