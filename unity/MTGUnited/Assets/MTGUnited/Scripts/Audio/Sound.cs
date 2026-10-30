using System;
using System.Collections.Generic;
using UnityEngine;

namespace MTGUnited
{
    public enum Sfx
    {
        UiClick, Draw, LandPlay, Cast, Resolve, Ability, Tap, Untap, Die, Exile, Discard, ToLibrary, Token,
        Counter, Attack, Impact, LifeLoss, LifeGain, Priority, TurnStart, Victory, Defeat, Popup, DrawMany,
    }

    /// <summary>
    /// Game sounds. Every effect is synthesised at startup (original, no audio files needed). To use your own
    /// sound instead, drop a file named after the effect (e.g. "Draw.wav", "Impact.ogg") into
    /// Assets/MTGUnited/Resources/Audio/. A file called "Music" there loops quietly in the background.
    /// Similar sounds are voice-limited (max 3 per 100 ms) so batched untaps or mills don't stack up.
    /// </summary>
    public class Sound : MonoBehaviour
    {
        static Sound instance;
        const int Voices = 10;

        readonly Dictionary<Sfx, AudioClip> clips = new Dictionary<Sfx, AudioClip>();
        readonly Dictionary<Sfx, List<float>> recent = new Dictionary<Sfx, List<float>>();
        readonly AudioClip[] castByColour = new AudioClip[6];
        AudioSource[] sources;
        AudioSource music;
        int next;

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt("sound", 1) == 1;
            set
            {
                PlayerPrefs.SetInt("sound", value ? 1 : 0);
                if (instance != null && instance.music != null) instance.music.mute = !value;
            }
        }

        public static float Volume
        {
            get => PlayerPrefs.GetFloat("soundVolume", 0.8f);
            set => PlayerPrefs.SetFloat("soundVolume", Mathf.Clamp01(value));
        }

        public static void Create(GameObject host)
        {
            if (instance != null) return;
            instance = host.AddComponent<Sound>();
            instance.Init();
        }

        void Init()
        {
            sources = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                sources[i] = gameObject.AddComponent<AudioSource>();
                sources[i].playOnAwake = false;
                sources[i].spatialBlend = 0f;
            }
            foreach (Sfx s in Enum.GetValues(typeof(Sfx)))
            {
                var custom = Resources.Load<AudioClip>("Audio/" + s);
                clips[s] = custom != null ? custom : Synth.Make(s);
            }
            // Colour-specific cast swells (W U B R G C), unless a custom Cast file replaces them all.
            bool customCast = Resources.Load<AudioClip>("Audio/Cast") != null;
            for (int i = 0; i < 6; i++) castByColour[i] = customCast ? clips[Sfx.Cast] : Synth.CastSwell(i);

            var musicClip = Resources.Load<AudioClip>("Audio/Music");
            if (musicClip != null)
            {
                music = gameObject.AddComponent<AudioSource>();
                music.clip = musicClip;
                music.loop = true;
                music.volume = 0.3f * Volume;
                music.mute = !Enabled;
                music.Play();
            }
        }

        /// <summary>Plays an effect. Pitch varies slightly so repeats don't sound mechanical.</summary>
        public static void Play(Sfx s, float volume = 1f, float pitch = 1f)
        {
            if (instance == null || !Enabled) return;
            instance.PlayClip(s, instance.clips[s], volume, pitch);
        }

        /// <summary>Cast swell tuned by the spell's colour (Forge colour string, e.g. "R" or "WU").</summary>
        public static void PlayCast(string colors)
        {
            if (instance == null || !Enabled) return;
            int idx = 5;
            if (!string.IsNullOrEmpty(colors))
            {
                string c = colors.ToUpperInvariant();
                for (int i = 0; i < 5; i++) if (c.IndexOf("WUBRG"[i]) >= 0) { idx = i; break; }
            }
            instance.PlayClip(Sfx.Cast, instance.castByColour[idx], 0.9f, 1f);
        }

        // ------------------------------------------------------------------ draws
        // Forge sends an opening hand (or "draw three") as several quick updates. Draws are gathered for a
        // moment, then either played one by one with a little gap, or, for 3+ cards, as a single riffle.
        int pendingDraws;
        float lastDrawAt;
        readonly Queue<float> drawTimes = new Queue<float>();
        const float GatherWindow = 0.12f, DrawGap = 0.09f, DrawVolume = 0.45f;

        /// <summary>Report cards drawn; the sound is decided once the burst is over.</summary>
        public static void QueueDraw(int count)
        {
            if (instance == null || !Enabled || count <= 0) return;
            instance.pendingDraws += count;
            instance.lastDrawAt = Time.unscaledTime;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (pendingDraws > 0 && now - lastDrawAt >= GatherWindow)
            {
                if (pendingDraws >= 3) PlayClip(Sfx.DrawMany, clips[Sfx.DrawMany], 0.6f, 1f);
                else for (int i = 0; i < pendingDraws; i++) drawTimes.Enqueue(now + i * DrawGap);
                pendingDraws = 0;
            }
            while (drawTimes.Count > 0 && drawTimes.Peek() <= now)
            {
                drawTimes.Dequeue();
                if (Enabled) PlayClip(Sfx.Draw, clips[Sfx.Draw], DrawVolume, 1f);
            }
        }

        void PlayClip(Sfx s, AudioClip clip, float volume, float pitch)
        {
            if (clip == null) return;
            // Voice limit: at most 3 of the same effect in any 100 ms window.
            float now = Time.unscaledTime;
            if (!recent.TryGetValue(s, out var times)) recent[s] = times = new List<float>(4);
            times.RemoveAll(t => now - t > 0.1f);
            if (times.Count >= 3) return;
            times.Add(now);

            var src = sources[next];
            next = (next + 1) % sources.Length;
            src.pitch = pitch * UnityEngine.Random.Range(0.96f, 1.04f);
            src.PlayOneShot(clip, volume * Volume);
        }
    }

    /// <summary>Small additive / noise synthesiser for the built-in effects. All original sounds.</summary>
    static class Synth
    {
        const int Rate = 44100;
        static readonly System.Random rng = new System.Random(1234);

        public static AudioClip Make(Sfx s)
        {
            switch (s)
            {
                case Sfx.UiClick: return Clip(s, 0.05f, (t, i) => Tone(t, 1150, 0.02f) * 0.35f + Noise() * Env(t, 0.002f, 0.01f) * 0.15f);
                case Sfx.Popup: return Clip(s, 0.12f, (t, i) => Tone(t, Lerp(600, 900, t / 0.12f), 0.08f) * 0.3f);
                case Sfx.Draw: return Whoosh(s, 0.22f, 900, 3000, 0.4f);
                case Sfx.DrawMany: return Riffle(s, 0.38f);
                case Sfx.ToLibrary: return Whoosh(s, 0.22f, 2600, 700, 0.35f);
                case Sfx.Discard: return Whoosh(s, 0.12f, 2500, 5000, 0.4f);
                case Sfx.LandPlay: return Thud(s, 0.24f, 85, 48, 0.9f, 0.25f);
                case Sfx.Resolve: return Thud(s, 0.32f, 70, 38, 1f, 0.35f);
                case Sfx.Tap: return Clip(s, 0.06f, (t, i) => (Noise() * Env(t, 0.001f, 0.012f) * 0.5f + Tone(t, 1900, 0.03f) * 0.25f));
                case Sfx.Untap: return Clip(s, 0.08f, (t, i) => (Noise() * Env(t, 0.001f, 0.02f) * 0.35f + Tone(t, 1400, 0.04f) * 0.2f));
                case Sfx.Counter: return Clip(s, 0.05f, (t, i) => Tone(t, 3000, 0.015f) * 0.3f);
                case Sfx.Ability: return Clip(s, 0.35f, (t, i) => Noise() * Env(t, 0.001f, 0.01f) * 0.3f
                                       + (Tone(t, 1320, 0.25f) + 0.6f * Tone(t, 1980, 0.18f)) * Gate(t, 0.04f) * 0.25f);
                case Sfx.Token: return Sparkle(s, 0.38f);
                case Sfx.Die: return Crumble(s, 0.4f);
                case Sfx.Exile: return Clip(s, 0.42f, (t, i) =>
                {
                    float rise = t / 0.42f;
                    return LowPass(Noise(), 0.05f + 0.6f * rise) * Env(t, 0.12f, 0.25f) * 0.5f
                         + Tone(t, Lerp(1800, 3600, rise), 0.4f) * 0.08f;
                });
                case Sfx.Attack: return Clip(s, 0.4f, (t, i) =>
                    Tone(t, Lerp(95, 50, t / 0.4f), 0.28f) * 0.9f + LowPass(Noise(), 0.15f) * Env(t, 0.002f, 0.06f) * 0.5f
                    + (t > 0.13f ? Tone(t - 0.13f, Lerp(90, 55, (t - 0.13f) / 0.27f), 0.2f) * 0.6f : 0f));
                case Sfx.Impact: return Clip(s, 0.24f, (t, i) =>
                    Noise() * Env(t, 0.001f, 0.05f) * 0.6f + Tone(t, Lerp(160, 70, t / 0.24f), 0.15f) * 0.8f);
                case Sfx.LifeLoss: return Clip(s, 0.28f, (t, i) => Tone(t, Lerp(220, 110, t / 0.28f), 0.2f) * 0.5f + LowPass(Noise(), 0.1f) * Env(t, 0.002f, 0.05f) * 0.3f);
                case Sfx.LifeGain: return Arp(s, new[] { 784f, 988f, 1175f }, 0.06f, 0.35f, 0.25f);
                case Sfx.Priority: return Arp(s, new[] { 660f, 990f }, 0.09f, 0.3f, 0.22f);
                case Sfx.TurnStart: return Arp(s, new[] { 523f, 784f }, 0.12f, 0.6f, 0.25f);
                case Sfx.Victory: return Arp(s, new[] { 523f, 659f, 784f, 1047f }, 0.12f, 1.2f, 0.3f);
                case Sfx.Defeat: return Arp(s, new[] { 392f, 311f, 262f }, 0.18f, 1.1f, 0.25f);
                case Sfx.Cast: return CastSwell(5);
                default: return Clip(s, 0.05f, (t, i) => 0f);
            }
        }

        /// <summary>Spell cast swell; each colour has its own chord and character.</summary>
        public static AudioClip CastSwell(int colour)
        {
            // W bright major, U airy, B low minor, R punchy, G warm, C metallic.
            float[][] chords =
            {
                new[] { 523f, 659f, 784f }, new[] { 440f, 659f, 988f }, new[] { 220f, 262f, 330f },
                new[] { 330f, 415f, 494f }, new[] { 262f, 330f, 392f }, new[] { 370f, 523f, 740f },
            };
            var ch = chords[Mathf.Clamp(colour, 0, 5)];
            float len = 0.42f;
            return Clip("Cast" + colour, len, (t, i) =>
            {
                float env = Mathf.Clamp01(t / 0.16f) * Mathf.Exp(-Mathf.Max(0, t - 0.16f) * 9f);
                float v = 0f;
                for (int k = 0; k < ch.Length; k++) v += Mathf.Sin(2 * Mathf.PI * ch[k] * t) / (k + 1.2f);
                if (colour == 3) v += LowPass(Noise(), 0.3f) * 0.4f * Mathf.Exp(-t * 12f);   // red crackle
                if (colour == 5) v *= 0.7f + 0.3f * Mathf.Sin(2 * Mathf.PI * 30f * t);       // colourless hum
                return v * env * 0.35f;
            });
        }

        // ------------------------------------------------------------------ building blocks

        static AudioClip Whoosh(Sfx s, float len, float f0, float f1, float vol)
        {
            return Clip(s, len, (t, i) =>
            {
                float p = t / len;
                float cutoff = Lerp(f0, f1, p) / Rate * 6f;
                return BandNoise(cutoff) * Mathf.Sin(Mathf.PI * p) * vol;
            });
        }

        static AudioClip Thud(Sfx s, float len, float f0, float f1, float vol, float dustVol)
        {
            return Clip(s, len, (t, i) =>
                Tone(t, Lerp(f0, f1, t / len), len * 0.6f) * vol
                + LowPass(Noise(), 0.08f) * Env(t, 0.002f, 0.08f) * dustVol);
        }

        static AudioClip Sparkle(Sfx s, float len)
        {
            var pips = new List<(float at, float f)>();
            for (int k = 0; k < 7; k++) pips.Add(((float)rng.NextDouble() * len * 0.7f, 1800f + (float)rng.NextDouble() * 2600f));
            return Clip(s, len, (t, i) =>
            {
                float v = 0;
                foreach (var (at, f) in pips) if (t >= at) v += Tone(t - at, f, 0.06f) * 0.15f;
                return v;
            });
        }

        /// <summary>Several cards dealt at once: a quick run of soft card flicks over a light whoosh.</summary>
        static AudioClip Riffle(Sfx s, float len)
        {
            const int flicks = 9;
            return Clip(s, len, (t, i) =>
            {
                float p = t / len;
                float slot = len * 0.8f / flicks;
                float local = t % slot;
                float flick = t < len * 0.8f ? Mathf.Exp(-local / 0.006f) : 0f;
                return BandNoise(0.35f) * flick * 0.5f
                     + LowPass(Noise(), 0.12f) * Mathf.Sin(Mathf.PI * p) * 0.18f;
            });
        }

        static AudioClip Crumble(Sfx s, float len)
        {
            return Clip(s, len, (t, i) =>
            {
                float grain = ((int)(t * 140f) % 3 == 0) ? 1f : 0.25f;
                return LowPass(Noise(), 0.07f) * grain * Mathf.Exp(-t * 5f) * 0.7f
                     + Tone(t, Lerp(120, 60, t / len), 0.3f) * 0.2f;
            });
        }

        static AudioClip Arp(Sfx s, float[] notes, float step, float len, float vol)
        {
            return Clip(s, len, (t, i) =>
            {
                float v = 0;
                for (int k = 0; k < notes.Length; k++)
                {
                    float st = k * step;
                    if (t < st) continue;
                    float tt = t - st;
                    v += (Mathf.Sin(2 * Mathf.PI * notes[k] * tt) + 0.3f * Mathf.Sin(4 * Mathf.PI * notes[k] * tt)) * Mathf.Exp(-tt * 5f);
                }
                return v * vol;
            });
        }

        static float lp;
        static float LowPass(float x, float a) { lp += a * (x - lp); return lp * 2f; }

        static float bp1, bp2;
        static float BandNoise(float a)
        {
            a = Mathf.Clamp(a, 0.01f, 0.9f);
            float n = Noise();
            bp1 += a * (n - bp1);
            bp2 += a * 0.5f * (bp1 - bp2);
            return (bp1 - bp2) * 3f;
        }

        static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);
        static float Tone(float t, float f, float decay) => Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t / Mathf.Max(0.001f, decay) * 3f);
        static float Env(float t, float attack, float decay) => t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);
        static float Gate(float t, float start) => t >= start ? 1f : 0f;
        static float Lerp(float a, float b, float t) => a + (b - a) * Mathf.Clamp01(t);

        static AudioClip Clip(Sfx s, float len, Func<float, int, float> f) => Clip(s.ToString(), len, f);

        static AudioClip Clip(string name, float len, Func<float, int, float> f)
        {
            int n = Mathf.CeilToInt(len * Rate);
            var data = new float[n];
            lp = 0; bp1 = 0; bp2 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                // 5 ms fade-out at the end so nothing clicks.
                float tail = Mathf.Clamp01((n - i) / (Rate * 0.005f));
                data[i] = Mathf.Clamp(f(t, i) * tail, -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
