using System.Collections.Generic;
using UnityEngine;

namespace MindThePaws
{
    /// <summary>
    /// Audio 3D, adăugat automat de MindThePawsGame.
    /// Pentru fiecare cheie (calm_music, chaos_music, poof, place, bark, purr, chime, grow, breath, oops)
    /// folosește fișierul din Resources/MindThePaws/Audio dacă există, altfel îl sintetizează în memorie la pornire.
    /// Muzica face crossfade între Calm / Chaos / Pătura Moale (merge și când jocul e pe pauză).
    /// </summary>
    public class AudioDirector : MonoBehaviour
    {
        public static AudioDirector Instance { get; private set; }

        [Range(0f, 1f)] public float musicVolume = 0.32f;
        [Range(0f, 1f)] public float sfxVolume = 0.9f;
        public float crossfadeSeconds = 2.2f;
        public bool playMusic = true;
        [Tooltip("Folosește plugin-ul de spațializare (Meta XR Audio). Dacă nu se aude nimic pe cască, lasă-l oprit: " +
                 "sunetele rămân 3D (panoramare + atenuare cu distanța), doar fără HRTF.")]
        public bool useSpatializer = false;

        public static readonly string[] Keys = { "calm_music", "chaos_music", "poof", "place", "bark", "purr", "chime", "grow", "breath", "oops" };

        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, bool> fromFile = new Dictionary<string, bool>();
        AudioSource musicA, musicB;
        bool aIsCurrent = true;
        string currentMusicKey;
        float fade = 1f;
        float oldStartVolume;
        bool spatializerAvailable;

        public string Summary { get; private set; } = "";

        void Awake()
        {
            Instance = this;
            spatializerAvailable = useSpatializer && !string.IsNullOrEmpty(AudioSettings.GetSpatializerPluginName());
            // pe Quest, după un dialog de sistem (permisiuni Space Setup etc.) ascultătorul poate rămâne pe pauză / mut
            AudioListener.pause = false;
            AudioListener.volume = 1f;
            musicA = MakeMusicSource("MusicA");
            musicB = MakeMusicSource("MusicB");
            LoadAll();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        AudioSource MakeMusicSource(string n)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.volume = 0f;
            s.ignoreListenerPause = true;
            return s;
        }

        void LoadAll()
        {
            int files = 0;
            foreach (var k in Keys)
            {
                var c = AutoAssetLoader.TryAudio(k);
                fromFile[k] = c != null;
                if (c != null) files++;
                else c = Synth.Make(k);
                if (c != null) clips[k] = c;
            }
            Summary = files > 0 ? $"audio: {files} fișiere + {Keys.Length - files} sintetizate" : "audio: sintetizat";
            Debug.Log("[MindThePaws] " + Summary);
        }

        public AudioClip Get(string key)
        {
            if (key == null) return null;
            if (clips.TryGetValue(key, out var c)) return c;
            // cheie nouă, necunoscută: poate există în Resources
            c = AutoAssetLoader.TryAudio(key);
            if (c != null) clips[key] = c;
            return c;
        }

        /// Asigură un AudioListener pe camera jucătorului (OVRCameraRig îl are de obicei).
        public void EnsureListener(Transform head)
        {
            if (head == null) return;
            var all = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            AudioListener active = null;
            foreach (var l in all)
                if (l != null && l.isActiveAndEnabled) { active = l; break; }
            if (active == null)
            {
                // niciun ascultător ACTIV (unul dezactivat / pe un obiect inactiv nu se pune) → îl punem pe cameră
                active = head.GetComponent<AudioListener>();
                if (active == null) active = head.gameObject.AddComponent<AudioListener>();
                active.enabled = true;
            }
            int n = 0;
            foreach (var l in all) if (l != null && l.isActiveAndEnabled) n++;
            if (n > 1) Debug.LogWarning($"[MindThePaws] {n} AudioListener-e active în scenă: lasă doar unul (pe CenterEyeAnchor).");
            Summary += $" · ascultător: {active.name}";
            Debug.Log($"[MindThePaws] AudioListener: {active.name} · spatializer: {(spatializerAvailable ? AudioSettings.GetSpatializerPluginName() : "oprit")} · {AudioSettings.outputSampleRate} Hz");
        }

        void OnApplicationFocus(bool focus) { if (focus) ResumeAudio(); }
        void OnApplicationPause(bool paused) { if (!paused) ResumeAudio(); }

        /// După ce aplicația revine în prim-plan (meniul Quest, dialog de permisiuni), muzica poate rămâne oprită.
        void ResumeAudio()
        {
            AudioListener.pause = false;
            if (musicA == null || musicB == null || !playMusic || string.IsNullOrEmpty(currentMusicKey)) return;
            var cur = aIsCurrent ? musicA : musicB;
            if (cur.clip != null && !cur.isPlaying)
            {
                cur.Play();
                if (fade >= 1f) cur.volume = musicVolume * (currentMusicKey == "breath" ? 1.25f : 1f);
            }
        }

        // ------------------------------------------------------------ API static, sigur dacă lipsește directorul

        public static void Sfx(string key, Vector3 pos, float volume = 1f, float pitch = 1f)
        {
            if (Instance != null) Instance.PlayAt(key, pos, volume, pitch);
        }

        public static void Sfx2D(string key, float volume = 1f, float pitch = 1f)
        {
            if (Instance != null) Instance.Play2D(key, volume, pitch);
        }

        public static void Music(CozyMood mood)
        {
            if (Instance != null) Instance.SetMusic(mood);
        }

        public void PlayAt(string key, Vector3 pos, float volume, float pitch)
        {
            var clip = Get(key);
            if (clip == null) return;
            var go = new GameObject("Sfx_" + key);
            go.transform.position = pos;
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 0.6f;
            s.maxDistance = 12f;
            s.dopplerLevel = 0f;
            s.spread = 30f;
            s.spatialize = spatializerAvailable;
            s.volume = Mathf.Clamp01(volume * sfxVolume);
            s.pitch = Mathf.Clamp(pitch, 0.3f, 3f);
            s.ignoreListenerPause = true;
            s.Play();
            StartCoroutine(DestroyAfterUnscaled(go, clip.length / s.pitch + 0.2f));
        }

        // Destroy(go, t) numără timp SCALAT: cu Time.timeScale = 0 (Pătura Moale) sunetele nu se mai ștergeau
        System.Collections.IEnumerator DestroyAfterUnscaled(GameObject go, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (go != null) Destroy(go);
        }

        public void Play2D(string key, float volume, float pitch)
        {
            var clip = Get(key);
            if (clip == null) return;
            var go = new GameObject("Sfx2D_" + key);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.spatialBlend = 0f;
            s.volume = Mathf.Clamp01(volume * sfxVolume);
            s.pitch = Mathf.Clamp(pitch, 0.3f, 3f);
            s.ignoreListenerPause = true;
            s.Play();
            StartCoroutine(DestroyAfterUnscaled(go, clip.length / s.pitch + 0.2f));
        }

        public void SetMusic(CozyMood mood)
        {
            if (!playMusic) return;
            string key;
            switch (mood)
            {
                case CozyMood.Chaos: key = "chaos_music"; break;
                case CozyMood.Blanket: key = "breath"; break;
                default: key = "calm_music"; break;
            }
            if (key == currentMusicKey) return;
            var clip = Get(key);
            if (clip == null) return;
            currentMusicKey = key;
            var next = aIsCurrent ? musicB : musicA;
            var prev = aIsCurrent ? musicA : musicB;
            oldStartVolume = prev.isPlaying ? prev.volume : 0f;
            aIsCurrent = !aIsCurrent;
            next.clip = clip;
            next.time = 0f;
            next.volume = 0f;
            next.Play();
            fade = 0f;
        }

        float watchdog;
        bool diagLogged;

        /// Starea audio, pe scurt (în debug overlay și în log): dacă vreuna e greșită, de acolo vine liniștea.
        public string Diag
        {
            get
            {
                var cur = aIsCurrent ? musicA : musicB;
                AudioListener l = null;
                foreach (var x in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (x != null && x.isActiveAndEnabled) { l = x; break; }
                var cfg = AudioSettings.GetConfiguration();
                return $"audio: ascultător={(l != null ? l.name : "NICIUNUL ACTIV")} · vol={AudioListener.volume:0.##} pauză={AudioListener.pause} · " +
                       $"muzică={currentMusicKey ?? "-"} cântă={(cur != null && cur.isPlaying)} vol={(cur != null ? cur.volume : 0f):0.00} · " +
                       $"{AudioSettings.outputSampleRate} Hz, {cfg.speakerMode}, buffer {cfg.dspBufferSize} · clipuri={clips.Count}";
            }
        }

        void Update()
        {
            // paznic: dacă sistemul a oprit muzica pe ascuns, o repornim (o dată pe secundă)
            watchdog += Time.unscaledDeltaTime;
            if (watchdog > 1f)
            {
                watchdog = 0f;
                ResumeAudio();
                if (!diagLogged && Time.realtimeSinceStartup > 4f)
                {
                    diagLogged = true;
                    Debug.Log("[MindThePaws] " + Diag);   // în logcat: adb logcat -s Unity | findstr MindThePaws
                }
            }
            if (fade >= 1f && !(musicA.isPlaying && musicB.isPlaying)) return;
            fade = Mathf.Min(1f, fade + Time.unscaledDeltaTime / Mathf.Max(0.05f, crossfadeSeconds));
            var cur = aIsCurrent ? musicA : musicB;
            var old = aIsCurrent ? musicB : musicA;
            // equal-power crossfade
            float k = fade * Mathf.PI * 0.5f;
            float target = musicVolume * (currentMusicKey == "breath" ? 1.25f : 1f);
            cur.volume = Mathf.Sin(k) * target;
            old.volume = Mathf.Cos(k) * oldStartVolume;
            if (fade >= 1f && old.isPlaying)
            {
                old.Stop();
                old.volume = 0f;
            }
        }

        // ================================================================ sinteză procedurală

        static class Synth
        {
            const int SR = 44100;   // efecte
            const int MSR = 22050;  // muzică / bucle lungi (economie de memorie)

            public static AudioClip Make(string key)
            {
                float[] d;
                int sr = SR;
                switch (key)
                {
                    case "calm_music": d = CalmMusic(); sr = MSR; break;
                    case "chaos_music": d = ChaosMusic(); sr = MSR; break;
                    case "breath": d = Breath(); sr = MSR; break;
                    case "poof": d = Poof(); break;
                    case "place": d = Place(); break;
                    case "bark": d = Bark(); break;
                    case "purr": d = Purr(); break;
                    case "chime": d = Chime(); break;
                    case "grow": d = Grow(); break;
                    case "oops": d = Oops(); break;
                    default: return null;
                }
                Normalize(d, key.EndsWith("music") || key == "breath" ? 0.7f : 0.85f);
                var clip = AudioClip.Create("synth_" + key, d.Length, 1, sr, false);
                clip.SetData(d, 0);
                return clip;
            }

            static float Midi(float n) => 440f * Mathf.Pow(2f, (n - 69f) / 12f);
            const float TAU = Mathf.PI * 2f;

            static void Normalize(float[] d, float peak)
            {
                float m = 0f;
                for (int i = 0; i < d.Length; i++) m = Mathf.Max(m, Mathf.Abs(d[i]));
                if (m < 1e-6f) return;
                float g = peak / m;
                for (int i = 0; i < d.Length; i++) d[i] *= g;
            }

            /// Notă cu parțiale și decădere exponențială; "wrap" = se continuă la începutul buclei (fără cusătură).
            static void Note(float[] buf, int sr, float start, float freq, float amp, float decay, float attack,
                float[] partials, float[] pAmps, float maxLen, bool wrap)
            {
                int s0 = Mathf.RoundToInt(start * sr);
                int len = Mathf.RoundToInt(maxLen * sr);
                for (int i = 0; i < len; i++)
                {
                    int idx = s0 + i;
                    if (idx >= buf.Length)
                    {
                        if (!wrap) break;
                        idx %= buf.Length;
                    }
                    float t = i / (float)sr;
                    float env = (t < attack ? t / attack : 1f) * Mathf.Exp(-(t) / decay);
                    if (env < 1e-4f && t > attack) break;
                    float v = 0f;
                    for (int p = 0; p < partials.Length; p++)
                        v += Mathf.Sin(TAU * freq * partials[p] * t) * pAmps[p] * Mathf.Exp(-t * p * 1.5f);
                    buf[idx] += v * env * amp;
                }
            }

            static void LowPass(float[] d, float cutoff, int sr, bool loop)
            {
                float rc = 1f / (TAU * cutoff);
                float a = (1f / sr) / (rc + 1f / sr);
                float y = 0f;
                int passes = loop ? 2 : 1;   // la bucle: a doua trecere pornește cu starea de la final → fără clic
                var src = (float[])d.Clone();
                for (int p = 0; p < passes; p++)
                    for (int i = 0; i < d.Length; i++)
                    {
                        y += a * (src[i] - y);
                        d[i] = y;
                    }
            }

            static System.Random rng;
            static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

            // ---------------- muzică calmă: music-box lo-fi pe acorduri majore calde (72 BPM, buclă de 4 acorduri)
            static float[] CalmMusic()
            {
                rng = new System.Random(7);
                const float bpm = 72f;
                float beat = 60f / bpm;
                int[][] chords =
                {
                    new[] { 48, 60, 64, 67, 71 },   // Cmaj7
                    new[] { 45, 57, 60, 64, 67 },   // Am7
                    new[] { 41, 53, 57, 60, 64 },   // Fmaj7
                    new[] { 43, 55, 59, 62, 67 }    // G
                };
                float chordLen = beat * 4f;
                float total = chordLen * chords.Length;
                var buf = new float[Mathf.RoundToInt(total * MSR)];
                float[] pad = { 1f, 2f };
                float[] padA = { 1f, 0.08f };
                float[] box = { 1f, 2f, 3.98f };
                float[] boxA = { 1f, 0.32f, 0.12f };
                float[] bass = { 1f, 2f };
                float[] bassA = { 1f, 0.25f };
                int[] arp = { 1, 2, 3, 4, 3, 2, 3, 4 };

                for (int c = 0; c < chords.Length; c++)
                {
                    float t0 = c * chordLen;
                    var ch = chords[c];
                    // pad cald (sinusuri ușor dezacordate, atac lent)
                    for (int n = 1; n < ch.Length; n++)
                    {
                        Note(buf, MSR, t0, Midi(ch[n]), 0.05f, 3.2f, 0.9f, pad, padA, chordLen + 1.2f, true);
                        Note(buf, MSR, t0, Midi(ch[n]) * 1.003f, 0.035f, 3.2f, 1.1f, pad, padA, chordLen + 1.2f, true);
                    }
                    // bas moale
                    Note(buf, MSR, t0, Midi(ch[0]), 0.16f, 1.4f, 0.02f, bass, bassA, chordLen, true);
                    Note(buf, MSR, t0 + beat * 2.5f, Midi(ch[0] + 7), 0.08f, 0.9f, 0.02f, bass, bassA, chordLen, true);
                    // cutie muzicală: arpegiu în optimi, cu câteva note "uitate" (lo-fi)
                    for (int i = 0; i < 8; i++)
                    {
                        if (i > 0 && rng.NextDouble() < 0.18) continue;
                        float swing = (i % 2 == 1) ? beat * 0.06f : 0f;
                        float tt = t0 + i * beat * 0.5f + swing + (float)(rng.NextDouble() * 0.01);
                        int note = ch[arp[i]] + 12;
                        float vel = 0.08f + (float)rng.NextDouble() * 0.03f;
                        Note(buf, MSR, tt, Midi(note), vel, 0.7f, 0.003f, box, boxA, 2.2f, true);
                    }
                    // o sclipire sus, la final de acord
                    Note(buf, MSR, t0 + beat * 3.5f, Midi(ch[2] + 24), 0.035f, 0.9f, 0.003f, box, boxA, 2f, true);
                }
                // praf de vinil foarte discret
                for (int i = 0; i < buf.Length; i++)
                    if (rng.NextDouble() < 0.0006) buf[i] += Noise() * 0.03f;
                LowPass(buf, 4200f, MSR, true);
                return buf;
            }

            // ---------------- muzică de haos: jucăușă, rapidă (120 BPM), dar tot drăguță
            static float[] ChaosMusic()
            {
                rng = new System.Random(11);
                const float bpm = 124f;
                float beat = 60f / bpm;
                int[][] chords =
                {
                    new[] { 48, 60, 64, 67 },  // C
                    new[] { 43, 59, 62, 67 },  // G
                    new[] { 45, 60, 64, 69 },  // Am
                    new[] { 41, 60, 65, 69 }   // F
                };
                float chordLen = beat * 4f;
                float total = chordLen * chords.Length;
                var buf = new float[Mathf.RoundToInt(total * MSR)];
                float[] pluck = { 1f, 3f, 5f };
                float[] pluckA = { 1f, 0.3f, 0.12f };
                float[] tri = { 1f, 3f };
                float[] triA = { 1f, 0.11f };
                float[] bass = { 1f, 2f };
                float[] bassA = { 1f, 0.4f };
                int[] penta = { 72, 74, 76, 79, 81, 84 };

                for (int c = 0; c < chords.Length; c++)
                {
                    float t0 = c * chordLen;
                    var ch = chords[c];
                    for (int b = 0; b < 4; b++)
                    {
                        float tb = t0 + b * beat;
                        // bas săltăreț: rădăcină / cvintă
                        Note(buf, MSR, tb, Midi(ch[0] + (b % 2 == 0 ? 0 : 7)), 0.14f, 0.22f, 0.005f, bass, bassA, 0.5f, true);
                        // acorduri staccato pe contratimp
                        for (int n = 1; n < ch.Length; n++)
                            Note(buf, MSR, tb + beat * 0.5f, Midi(ch[n]), 0.035f, 0.12f, 0.003f, pluck, pluckA, 0.4f, true);
                        // tobă moale (kick) pe 1 și 3
                        if (b % 2 == 0) Kick(buf, MSR, tb, 0.35f);
                        // hi-hat ușor pe optimi
                        Tick(buf, MSR, tb, 0.05f);
                        Tick(buf, MSR, tb + beat * 0.5f, 0.03f);
                    }
                    // melodie pentatonică jucăușă
                    for (int i = 0; i < 8; i++)
                    {
                        if (rng.NextDouble() < 0.3) continue;
                        float tt = t0 + i * beat * 0.5f;
                        int note = penta[rng.Next(penta.Length)];
                        Note(buf, MSR, tt, Midi(note), 0.07f, 0.18f, 0.004f, tri, triA, 0.6f, true);
                    }
                    // "boop" de jucărie la final de frază
                    Glide(buf, MSR, t0 + beat * 3.5f, 0.16f, Midi(79), Midi(86), 0.05f);
                }
                LowPass(buf, 5200f, MSR, true);
                return buf;
            }

            static void Kick(float[] buf, int sr, float start, float amp)
            {
                int s0 = Mathf.RoundToInt(start * sr);
                int len = Mathf.RoundToInt(0.16f * sr);
                float ph = 0f;
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)sr;
                    float f = Mathf.Lerp(140f, 50f, Mathf.Clamp01(t / 0.1f));
                    ph += TAU * f / sr;
                    buf[(s0 + i) % buf.Length] += Mathf.Sin(ph) * Mathf.Exp(-t / 0.05f) * amp;
                }
            }

            static void Tick(float[] buf, int sr, float start, float amp)
            {
                int s0 = Mathf.RoundToInt(start * sr);
                int len = Mathf.RoundToInt(0.04f * sr);
                float prev = 0f;
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)sr;
                    float n = Noise();
                    float hp = n - prev;   // high-pass simplu
                    prev = n;
                    buf[(s0 + i) % buf.Length] += hp * Mathf.Exp(-t / 0.01f) * amp;
                }
            }

            static void Glide(float[] buf, int sr, float start, float dur, float f0, float f1, float amp)
            {
                int s0 = Mathf.RoundToInt(start * sr);
                int len = Mathf.RoundToInt(dur * sr);
                float ph = 0f;
                for (int i = 0; i < len; i++)
                {
                    float k = i / (float)len;
                    float f = Mathf.Lerp(f0, f1, k);
                    ph += TAU * f / sr;
                    float env = Mathf.Sin(k * Mathf.PI);
                    buf[(s0 + i) % buf.Length] += Mathf.Sin(ph) * env * amp;
                }
            }

            // ---------------- respirație: pad cald care crește 4 s (inspiră) și scade 4 s (expiră)
            static float[] Breath()
            {
                rng = new System.Random(3);
                const float cycle = 8f;
                var buf = new float[Mathf.RoundToInt(cycle * MSR)];
                float[] notes = { Midi(48), Midi(55), Midi(60), Midi(64), Midi(67) };
                // frecvențe cu număr întreg de perioade pe buclă → fără clic la reluare
                for (int n = 0; n < notes.Length; n++) notes[n] = Mathf.Round(notes[n] * cycle) / cycle;
                float[] amps = { 0.22f, 0.14f, 0.12f, 0.08f, 0.05f };
                float noiseLP = 0f;
                for (int i = 0; i < buf.Length; i++)
                {
                    float t = i / (float)MSR;
                    float env = 0.5f - 0.5f * Mathf.Cos(TAU * t / cycle);   // 0 → 1 → 0
                    float v = 0f;
                    for (int n = 0; n < notes.Length; n++)
                        v += Mathf.Sin(TAU * notes[n] * t) * amps[n];
                    // aer: zgomot filtrat, mai deschis la vârful inspirației
                    float cutoff = Mathf.Lerp(0.02f, 0.12f, env);
                    noiseLP += cutoff * (Noise() - noiseLP);
                    buf[i] = v * (0.3f + 0.7f * env) + noiseLP * env * env * 0.9f;
                }
                return buf;
            }

            // ---------------- efecte scurte
            static float[] Poof()
            {
                rng = new System.Random(5);
                var buf = new float[Mathf.RoundToInt(0.5f * SR)];
                float lp = 0f, ph = 0f;
                for (int i = 0; i < buf.Length; i++)
                {
                    float t = i / (float)SR;
                    float k = t / 0.5f;
                    float env = Mathf.Clamp01(t / 0.012f) * Mathf.Exp(-t / 0.12f);
                    float cut = Mathf.Lerp(0.35f, 0.02f, Mathf.Sqrt(k));
                    lp += cut * (Noise() - lp);
                    float f = Mathf.Lerp(520f, 140f, Mathf.Sqrt(k));
                    ph += TAU * f / SR;
                    buf[i] = (lp * 1.4f + Mathf.Sin(ph) * 0.35f) * env;
                }
                // mică sclipire la final
                Note(buf, SR, 0.06f, Midi(96), 0.12f, 0.12f, 0.002f, new[] { 1f }, new[] { 1f }, 0.4f, false);
                return buf;
            }

            static float[] Place()
            {
                var buf = new float[Mathf.RoundToInt(0.5f * SR)];
                Glide(buf, SR, 0f, 0.05f, 260f, 620f, 0.5f);
                float[] bell = { 1f, 2.76f, 5.4f };
                float[] bellA = { 1f, 0.35f, 0.12f };
                Note(buf, SR, 0.03f, Midi(84), 0.35f, 0.22f, 0.002f, bell, bellA, 0.45f, false);
                Note(buf, SR, 0.09f, Midi(91), 0.22f, 0.2f, 0.002f, bell, bellA, 0.4f, false);
                return buf;
            }

            static float[] Bark()
            {
                rng = new System.Random(9);
                var buf = new float[Mathf.RoundToInt(0.48f * SR)];
                OneBark(buf, 0f, 1f, 1f);
                OneBark(buf, 0.2f, 0.93f, 0.8f);
                LowPass(buf, 3800f, SR, false);
                return buf;
            }

            static void OneBark(float[] buf, float start, float pitch, float amp)
            {
                int s0 = Mathf.RoundToInt(start * SR);
                const float dur = 0.14f;
                int len = Mathf.RoundToInt(dur * SR);
                float ph = 0f;
                for (int i = 0; i < len && s0 + i < buf.Length; i++)
                {
                    float t = i / (float)SR;
                    float k = t / dur;
                    float f0 = Mathf.Lerp(640f, 400f, k) * pitch;
                    ph += TAU * f0 / SR;
                    float v = 0f;
                    for (int h = 1; h <= 8; h++)
                    {
                        float fh = f0 * h;
                        float formant = Mathf.Exp(-Mathf.Pow((fh - 1150f) / 650f, 2f)) + 0.35f / h;
                        v += Mathf.Sin(ph * h) * formant;
                    }
                    float env = Mathf.Clamp01(t / 0.008f) * Mathf.Pow(1f - k, 1.6f);
                    float breathNoise = Noise() * 0.25f * Mathf.Exp(-t / 0.03f);
                    buf[s0 + i] += (v * 0.35f + breathNoise) * env * amp;
                }
            }

            static float[] Purr()
            {
                rng = new System.Random(13);
                const float dur = 1.8f;
                var buf = new float[Mathf.RoundToInt(dur * SR)];
                float lp = 0f, lp2 = 0f;
                for (int i = 0; i < buf.Length; i++)
                {
                    float t = i / (float)SR;
                    float breath = Mathf.Sin(Mathf.PI * Mathf.Repeat(t / 0.9f, 1f));     // două respirații
                    float rate = 24f + 3f * Mathf.Sin(TAU * 0.5f * t);
                    float pulse = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * rate * t)), 6f);
                    lp += 0.06f * (Noise() - lp);
                    lp2 += 0.02f * (lp - lp2);
                    float body = Mathf.Sin(TAU * 2f * rate * t) * 0.5f + Mathf.Sin(TAU * 4f * rate * t) * 0.25f;
                    float fadeOut = Mathf.Clamp01((dur - t) / 0.2f);
                    buf[i] = (lp * 2.2f + lp2 * 3f + body * 0.25f) * pulse * breath * fadeOut;
                }
                return buf;
            }

            static float[] Chime()
            {
                var buf = new float[Mathf.RoundToInt(1.1f * SR)];
                float[] bell = { 1f, 2.76f, 5.4f };
                float[] bellA = { 1f, 0.3f, 0.1f };
                Note(buf, SR, 0f, Midi(88), 0.3f, 0.45f, 0.002f, bell, bellA, 1.05f, false);     // E6
                Note(buf, SR, 0.06f, Midi(95), 0.22f, 0.4f, 0.002f, bell, bellA, 1.0f, false);   // B6
                Note(buf, SR, 0.12f, Midi(100), 0.12f, 0.35f, 0.002f, bell, bellA, 0.95f, false); // E7
                return buf;
            }

            static float[] Grow()
            {
                rng = new System.Random(17);
                const float dur = 0.9f;
                var buf = new float[Mathf.RoundToInt(dur * SR)];
                float ph = 0f;
                for (int i = 0; i < buf.Length; i++)
                {
                    float t = i / (float)SR;
                    float k = t / dur;
                    float f = Mathf.Lerp(Midi(67), Midi(84), k * k * (3f - 2f * k)) * (1f + 0.012f * Mathf.Sin(TAU * 6f * t));
                    ph += TAU * f / SR;
                    float env = Mathf.Clamp01(t / 0.05f) * Mathf.Pow(1f - k, 1.2f);
                    buf[i] = (Mathf.Sin(ph) + 0.25f * Mathf.Sin(2f * ph)) * env * 0.35f;
                }
                for (int s = 0; s < 6; s++)
                    Note(buf, SR, 0.1f + s * 0.11f, Midi(91 + rng.Next(0, 9)), 0.1f, 0.12f, 0.002f, new[] { 1f }, new[] { 1f }, 0.3f, false);
                return buf;
            }

            static float[] Oops()
            {
                var buf = new float[Mathf.RoundToInt(0.42f * SR)];
                float[] tri = { 1f, 3f };
                float[] triA = { 1f, 0.11f };
                Note(buf, SR, 0f, Midi(72), 0.3f, 0.12f, 0.005f, tri, triA, 0.2f, false);
                Note(buf, SR, 0.13f, Midi(67), 0.3f, 0.18f, 0.005f, tri, triA, 0.28f, false);
                return buf;
            }
        }
    }
}
