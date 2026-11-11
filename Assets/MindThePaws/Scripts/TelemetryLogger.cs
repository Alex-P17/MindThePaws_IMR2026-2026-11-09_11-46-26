using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace MindThePaws
{
    /// <summary>
    /// Un rând CSV per ghicire, în Application.persistentDataPath.
    /// Pe Quest: /sdcard/Android/data/&lt;package&gt;/files/mindthepaws_trials.csv
    /// Îl scoți cu Meta Quest Developer Hub (File Manager) sau:
    ///   adb pull /sdcard/Android/data/&lt;package&gt;/files/mindthepaws_trials.csv
    /// </summary>
    public class TelemetryLogger : MonoBehaviour
    {
        public string fileName = "mindthepaws_trials.csv";

        [Tooltip("Bifat: fiecare pornire a aplicației primește automat un ID nou (P001, P002...), care nu se repetă niciodată.")]
        public bool autoParticipantId = true;
        [Tooltip("Folosit doar dacă Auto Participant Id e debifat.")]
        public string participantId = "P001";

        const string PrefKey = "MindThePaws.LastParticipant";
        public string ParticipantId => participantId;

        const string Header =
            "timestamp_utc,session_id,participant,trial,object,condition,surface," +
            "hidden_x,hidden_y,hidden_z,guess_x,guess_y,guess_z," +
            "error_m,error_horizontal_m,angle_deg,hide_to_guess_s,guess_duration_s," +
            "item_index,distraction_count,distraction_seconds,safety_triggered";

        string path;
        string sessionId;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public string FilePath => path;

        void Awake()
        {
            path = Path.Combine(Application.persistentDataPath, fileName);
            sessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", Inv);
            try
            {
                if (!File.Exists(path)) File.WriteAllText(path, Header + "\n");
                else UpgradeHeader();
            }
            catch (Exception e) { Debug.LogError($"[MindThePaws] Nu pot crea CSV-ul: {e.Message}"); }

            if (autoParticipantId) participantId = NextParticipantId();
            Debug.Log($"[MindThePaws] CSV: {path} · participant: {participantId}");
        }

        void UpgradeHeader()
        {
            try
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length == 0)
                {
                    File.WriteAllText(path, Header + "\n");
                    return;
                }
                if (lines[0] == Header) return;
                int oldCols = lines[0].Split(',').Length;
                int newCols = Header.Split(',').Length;
                if (oldCols >= newCols) return;
                var outLines = new string[lines.Length];
                outLines[0] = Header;
                for (int i = 1; i < lines.Length; i++)
                {
                    bool oldRow = lines[i].Length > 0 && lines[i].Split(',').Length == oldCols;
                    outLines[i] = oldRow ? lines[i] + new string(',', newCols - oldCols) : lines[i];
                }
                File.WriteAllLines(path, outLines);
            }
            catch (Exception e) { Debug.LogWarning($"[MindThePaws] Nu pot actualiza header-ul CSV: {e.Message}"); }
        }

        /// Următorul ID = cel mai mare dintre (ultimul salvat în PlayerPrefs, cel mai mare P### din CSV) + 1.
        /// Două surse, ca să nu se repete nici dacă una dintre ele se pierde.
        string NextParticipantId()
        {
            int last = PlayerPrefs.GetInt(PrefKey, 0);
            try
            {
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadLines(path))
                    {
                        var cols = line.Split(',');
                        if (cols.Length < 3 || cols[2].Length < 2 || cols[2][0] != 'P') continue;
                        if (int.TryParse(cols[2].Substring(1), NumberStyles.Integer, Inv, out int n) && n > last) last = n;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning($"[MindThePaws] Nu pot citi CSV-ul pentru ID: {e.Message}"); }

            int next = last + 1;
            PlayerPrefs.SetInt(PrefKey, next);
            PlayerPrefs.Save();   // salvăm imediat, ca un crash să nu ducă la ID repetat
            return "P" + next.ToString("000", Inv);
        }

        public void LogTrial(int trial, int itemIndex, string obj, string condition, string surface,
                             Vector3 hidden, Vector3 guess,
                             float errorM, float errorHorizontalM, float angleDeg,
                             float hideToGuessS, float guessDurationS,
                             int distractionCount, float distractionSeconds, bool safetyTriggered)
        {
            string F(float v) => v.ToString("F4", Inv);
            string line = string.Join(",",
                DateTime.UtcNow.ToString("o", Inv), sessionId, participantId,
                trial.ToString(Inv), obj, condition, Clean(surface),
                F(hidden.x), F(hidden.y), F(hidden.z),
                F(guess.x), F(guess.y), F(guess.z),
                F(errorM), F(errorHorizontalM), F(angleDeg),
                F(hideToGuessS), F(guessDurationS),
                itemIndex.ToString(Inv), distractionCount.ToString(Inv), F(distractionSeconds), safetyTriggered ? "1" : "0");
            try { File.AppendAllText(path, line + "\n"); }
            catch (Exception e) { Debug.LogError($"[MindThePaws] Nu pot scrie în CSV: {e.Message}"); }
            Debug.Log($"[MindThePaws] trial {trial}.{itemIndex}: {errorM * 100f:0.0} cm");
        }

        static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(",", ";").Replace("\n", " ");
    }
}