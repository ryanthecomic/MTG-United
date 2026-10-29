using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace MTGUnited
{
    /// <summary>
    /// Downloads card images from Scryfall (https://scryfall.com/docs/api) and caches them on disk, so each card is
    /// fetched once. Requests are spaced out to stay well within Scryfall's rate limit. Images are for personal,
    /// non-commercial use under Wizards of the Coast's Fan Content Policy.
    /// </summary>
    public class CardImageCache : MonoBehaviour
    {
        public static CardImageCache Instance { get; private set; }

        const float MinSecondsBetweenRequests = 0.12f;
        readonly Dictionary<string, Texture2D> memory = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, List<Action<Texture2D>>> waiting = new Dictionary<string, List<Action<Texture2D>>>();
        readonly HashSet<string> failed = new HashSet<string>();
        readonly Queue<(string key, string name, string set)> queue = new Queue<(string, string, string)>();
        string diskDir;
        bool running;

        void Awake()
        {
            Instance = this;
            diskDir = Path.Combine(Application.persistentDataPath, "card-images");
            Directory.CreateDirectory(diskDir);
        }

        public static string CacheFolder => Instance != null ? Instance.diskDir : null;

        /// <summary>Calls back with the texture (now or later), or never if the card has no image.</summary>
        public void Get(string name, string set, Action<Texture2D> callback)
        {
            if (string.IsNullOrEmpty(name)) return;
            string key = (name + "|" + (set ?? "")).ToLowerInvariant();
            if (memory.TryGetValue(key, out var tex)) { callback(tex); return; }
            if (failed.Contains(key)) return;

            string file = Path.Combine(diskDir, SafeFileName(key) + ".jpg");
            if (File.Exists(file))
            {
                var t = new Texture2D(2, 2, TextureFormat.RGB24, true);
                if (t.LoadImage(File.ReadAllBytes(file)))
                {
                    Prepare(t);
                    memory[key] = t;
                    callback(t);
                    return;
                }
            }

            if (!waiting.TryGetValue(key, out var list))
            {
                list = new List<Action<Texture2D>>();
                waiting[key] = list;
                queue.Enqueue((key, name, set));
            }
            list.Add(callback);
            if (!running) StartCoroutine(Worker());
        }

        IEnumerator Worker()
        {
            running = true;
            while (queue.Count > 0)
            {
                var (key, name, set) = queue.Dequeue();
                Texture2D tex = null;
                // Try the exact printing first, then any printing of the card.
                if (!string.IsNullOrEmpty(set))
                {
                    yield return Download(Url(name, set), t => tex = t);
                    yield return new WaitForSecondsRealtime(MinSecondsBetweenRequests);
                }
                if (tex == null)
                {
                    yield return Download(Url(name, null), t => tex = t);
                    yield return new WaitForSecondsRealtime(MinSecondsBetweenRequests);
                }

                if (tex != null)
                {
                    memory[key] = tex;
                    try { File.WriteAllBytes(Path.Combine(diskDir, SafeFileName(key) + ".jpg"), tex.EncodeToJPG(90)); }
                    catch (Exception ex) { Debug.LogWarning("[MTGUnited] Could not cache image: " + ex.Message); }
                    if (waiting.TryGetValue(key, out var cbs)) foreach (var cb in cbs) cb(tex);
                }
                else
                {
                    failed.Add(key);
                }
                waiting.Remove(key);
            }
            running = false;
        }

        static string Url(string name, string set)
        {
            string url = "https://api.scryfall.com/cards/named?format=image&version=normal&exact=" + UnityWebRequest.EscapeURL(name);
            if (!string.IsNullOrEmpty(set)) url += "&set=" + UnityWebRequest.EscapeURL(set.ToLowerInvariant());
            return url;
        }

        static IEnumerator Download(string url, Action<Texture2D> done)
        {
            using (var req = UnityWebRequestTexture.GetTexture(url, false))
            {
                req.SetRequestHeader("Accept", "image/*");
                req.SetRequestHeader("User-Agent", "MTGUnited/0.1 (local Forge client)");
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    var t = DownloadHandlerTexture.GetContent(req);
                    if (t != null) { Prepare(t); done(t); }
                }
            }
        }

        static void Prepare(Texture2D t)
        {
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 4;
        }

        static string SafeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_').Replace('|', '@');
        }
    }
}
