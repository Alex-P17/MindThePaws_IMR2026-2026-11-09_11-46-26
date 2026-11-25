using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace MindThePaws
{
    /// <summary>
    /// Pulsul real de la o centură / brățară Bluetooth (BLE Heart Rate, ex. Decathlon Kalenji / Domyos),
    /// prin plugin-ul Java din Assets/Plugins/Android/HeartRateBle.java. Se adaugă singur din MindThePawsGame.
    /// Dacă senzorul lipsește / nu trimite date, jocul folosește în continuare pulsul simulat (marcat „simulat”).
    /// Important: senzorul să NU fie conectat în același timp la telefon (Decathlon Coach, Strava...).
    /// </summary>
    public class HeartRateMonitor : MonoBehaviour
    {
        [Tooltip("Caută automat un senzor de puls Bluetooth la pornire (doar pe cască).")]
        public bool useBluetoothHrm = true;

        public static HeartRateMonitor Instance { get; private set; }
        public int Bpm { get; private set; } = -1;
        public string Status { get; private set; } = "oprit";
        public string Device { get; private set; } = "";
        public bool HasData => Bpm > 0;

        float nextPoll;
        bool started;

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaClass plugin;
        const string ScanPerm = "android.permission.BLUETOOTH_SCAN";
        const string ConnectPerm = "android.permission.BLUETOOTH_CONNECT";
#endif

        void Awake() { Instance = this; }

        void Start()
        {
            if (!useBluetoothHrm) { Status = "dezactivat"; return; }
#if UNITY_ANDROID && !UNITY_EDITOR
            try { plugin = new AndroidJavaClass("com.mindthepaws.ble.HeartRateBle"); }
            catch (System.Exception e) { Status = "plugin lipsă"; Debug.LogWarning("[MindThePaws] HRM: " + e.Message); return; }
            MindThePawsGame.ExternalHeartRate = () => Bpm;
            if (HasPerms()) StartBle();
            else
            {
                var cb = new PermissionCallbacks();
                cb.PermissionGranted += _ => { if (HasPerms()) StartBle(); };
                cb.PermissionDenied += _ => Status = "permisiune Bluetooth refuzată";
                Permission.RequestUserPermissions(new[] { ScanPerm, ConnectPerm }, cb);
                Status = "aștept permisiunea Bluetooth";
            }
#else
            Status = "doar pe cască (în Editor pulsul e simulat)";
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static bool HasPerms() =>
            Permission.HasUserAuthorizedPermission(ScanPerm) && Permission.HasUserAuthorizedPermission(ConnectPerm);

        void StartBle()
        {
            if (started) return;
            started = true;
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = up.GetStatic<AndroidJavaObject>("currentActivity"))
                    plugin.CallStatic("start", activity);
                Debug.Log("[MindThePaws] HRM: caut senzorul de puls Bluetooth...");
            }
            catch (System.Exception e) { Status = "eroare la pornire"; Debug.LogWarning("[MindThePaws] HRM: " + e.Message); }
        }

        void Update()
        {
            if (plugin == null || !started || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.25f;
            try
            {
                int b = plugin.CallStatic<int>("getBpm");
                string s = plugin.CallStatic<string>("getStatus");
                if (s != Status) Debug.Log("[MindThePaws] HRM: " + s);
                Bpm = b;
                Status = s;
                Device = plugin.CallStatic<string>("getDeviceName");
            }
            catch (System.Exception) { Bpm = -1; }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (plugin != null && started) { try { plugin.CallStatic("stop"); } catch { } }
            MindThePawsGame.ExternalHeartRate = null;
        }
#endif
    }
}
