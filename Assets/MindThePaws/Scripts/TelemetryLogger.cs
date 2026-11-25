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
            "item_index,distraction_count,distraction_seconds,safety_triggered," +
            "baseline_bpm,baseline_source," +
            "memtask,memtask_max_level,memtask_max_sequence,memtask_correct,memtask_mistakes," +
            "safety_choices,chaos_intensity,hr_bpm_at_guess";

        const string EventsHeader = "timestamp_utc,session_id,participant,event,detail";

        string path;
        string sessionId;
        float baselineBpm = float.NaN;
        string baselineSource = "";

        public float BaselineBpm => baselineBpm;

        string safetyChoices = "";
        float chaosIntensity = 1f;
        string eventsPath;

        /// Alegerile din Pătura Moale (continue / gentler / debrief), cumulate pe sesiune, plus intensitatea curentă a haosului.
        public void AddSafetyChoice(string choice, float intensity)
        {
            safetyChoices = string.IsNullOrEmpty(safetyChoices) ? choice : safetyChoices + ";" + choice;
            chaosIntensity = intensity;
            LogEvent("safety_choice", choice + " (chaos_intensity=" + intensity.ToString("F2", Inv) + ")");
        }

        public void SetChaosIntensity(float intensity) { chaosIntensity = intensity; }

        /// Participant nou fără să repornești aplicația (butonul „Sesiune nouă”): ID nou, sesiune nouă, fără pulsul vechi.
        public void StartNewSession()
        {
            sessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", Inv);
            if (autoParticipantId) participantId = NextParticipantId();
            baselineBpm = float.NaN;
            baselineSource = "";
            SetMemoryTask("", 0, 0, 0, 0);
            ResetSession();
            LogEvent("session_start", "new session");
            Debug.Log($"[MindThePaws] Sesiune nouă · participant: {participantId}");
        }

        public void ResetSession()
        {
            safetyChoices = "";
            chaosIntensity = 1f;
        }

        /// Un rând într-un CSV separat de evenimente (mindthepaws_events.csv): pătură, promisiune, puls, bule sparte...
        public void LogEvent(string evt, string detail)
        {
            if (string.IsNullOrEmpty(eventsPath)) return;
            string line = string.Join(",", DateTime.UtcNow.ToString("o", Inv), sessionId, participantId, Clean(evt), Clean(detail));
            try
            {
                if (!File.Exists(eventsPath)) File.WriteAllText(eventsPath, EventsHeader + "\n");
                File.AppendAllText(eventsPath, line + "\n");
            }
            catch (Exception e) { Debug.LogWarning($"[MindThePaws] Nu pot scrie evenimentul: {e.Message}"); }
            Debug.Log($"[MindThePaws] eveniment: {evt} · {detail}");
        }

        string memTask = "";
        int memMaxLevel, memMaxSeq, memCorrect, memMistakes;

        /// Rezultatul provocării de memorie de lucru din runda curentă (se scrie pe rândurile de trial ale rundei).
        public void SetMemoryTask(string task, int maxLevel, int maxSequence, int correct, int mistakes)
        {
            memTask = Clean(task);
            memMaxLevel = maxLevel;
            memMaxSeq = maxSequence;
            memCorrect = correct;
            memMistakes = mistakes;
            if (!string.IsNullOrEmpty(task))
                Debug.Log($"[MindThePaws] {task}: nivel maxim {maxLevel}, secvență maximă {maxSequence}, {correct} corecte, {mistakes} greșeli");
        }

        /// Pulsul de calm măsurat în scena „Ceai cu Lumi” (sursa: "simulated" sau numele senzorului extern).
        /// Se scrie pe fiecare rând de trial din sesiunea curentă.
        public void SetBaseline(float bpm, string source)
        {
            baselineBpm = bpm;
            baselineSource = Clean(source);
            Debug.Log($"[MindThePaws] Puls de calm (baseline): {bpm:0} BPM · sursă: {source}");
            LogEvent("baseline_bpm", bpm.ToString("F1", Inv) + " (" + source + ")");
        }
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public string FilePath => path;

        void Awake()
        {
            path = Path.Combine(Application.persistentDataPath, fileName);
            eventsPath = Path.Combine(Application.persistentDataPath, "mindthepaws_events.csv");
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
                             int distractionCount, float distractionSeconds, bool safetyTriggered, int hrAtGuess = -1)
        {
            string F(float v) => v.ToString("F4", Inv);
            string line = string.Join(",",
                DateTime.UtcNow.ToString("o", Inv), sessionId, participantId,
                trial.ToString(Inv), obj, condition, Clean(surface),
                F(hidden.x), F(hidden.y), F(hidden.z),
                F(guess.x), F(guess.y), F(guess.z),
                F(errorM), F(errorHorizontalM), F(angleDeg),
                F(hideToGuessS), F(guessDurationS),
                itemIndex.ToString(Inv), distractionCount.ToString(Inv), F(distractionSeconds), safetyTriggered ? "1" : "0",
                float.IsNaN(baselineBpm) ? "" : baselineBpm.ToString("F1", Inv), baselineSource,
                memTask, memMaxLevel.ToString(Inv), memMaxSeq.ToString(Inv), memCorrect.ToString(Inv), memMistakes.ToString(Inv),
                safetyChoices, chaosIntensity.ToString("F2", Inv),
                hrAtGuess > 0 ? hrAtGuess.ToString(Inv) : "");
            try { File.AppendAllText(path, line + "\n"); }
            catch (Exception e) { Debug.LogError($"[MindThePaws] Nu pot scrie în CSV: {e.Message}"); }
            Debug.Log($"[MindThePaws] trial {trial}.{itemIndex}: {errorM * 100f:0.0} cm");
        }

        static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(",", ";").Replace("\n", " ");
    }
}