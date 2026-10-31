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

        /// <summary>
        /// Image for a card on screen: tokens are looked up on Scryfall by name, colour and power/toughness
        /// (Forge tokens have no printing of their own); everything else by name and set.
        /// </summary>
        public void GetFor(CardData c, Action<Texture2D> callback)
        {
            if (c == null || c.Hidden) return;
            if (c.Token) GetToken(c.Name, c.Colors, c.HasPT ? c.Power.ToString() : null, c.HasPT ? c.Toughness.ToString() : null, c.TypeLine, callback);
            else Get(c.Name, c.Set, callback);
        }

        const string TokenMark = "\u0001token:";

        public void GetToken(string name, string colors, string power, string toughness, string typeLine, Action<Texture2D> callback)
        {
            if (string.IsNullOrEmpty(name)) return;
            string n = name.EndsWith(" Token") ? name.Substring(0, name.Length - 6) : name;
            string c = string.IsNullOrEmpty(colors) ? "c" : colors.ToLowerInvariant();
            // Search: exact token name, colour, and P/T when it has them. Encoded into the "set" slot of the queue.
            string q = "t:token !\"" + n + "\" c=" + c + (power != null ? " pow=" + power + " tou=" + toughness : "");
            Get(TokenMark + n, q, callback);
        }

        /// <summary>Calls back with the texture (now or later), or never if the card has no image.</summary>
        public void Get(string name, string set, Action<Texture2D> callback)
        {
            if (string.IsNullOrEmpty(name)) return;
            // Custom cards (imported sets) use their own image file, or their picture URL.
            if (!name.StartsWith(TokenMark))
            {
                var custom = CustomSets.Get(name);
                if (custom != null)
                {
                    string ckey = "custom|" + name.ToLowerInvariant();
                    if (memory.TryGetValue(ckey, out var ct)) { callback(ct); return; }
                    string path = CustomSets.ImageFor(name);
                    if (path != null)
                    {
                        var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                        try
                        {
                            if (t.LoadImage(File.ReadAllBytes(path))) { Prepare(t); memory[ckey] = t; callback(t); }
                        }
                        catch (Exception ex) { Debug.LogWarning("[MTGUnited] Custom image failed: " + ex.Message); }
                        return;
                    }
                    if (!string.IsNullOrEmpty(custom.PicUrl) && custom.PicUrl.StartsWith("http")) { StartCoroutine(DownloadCustom(custom.PicUrl, ckey, callback)); return; }
                    return;   // custom card without art: shows as a text card
                }
            }
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
                byte[] bytes = null;
                if (name.StartsWith(TokenMark))
                {
                    // Token: search for a matching token card, then download its image (relax P/T if nothing matches).
                    string url = null;
                    yield return FindTokenImage(set, u => url = u);
                    yield return new WaitForSecondsRealtime(MinSecondsBetweenRequests);
                    if (url == null && set.Contains(" pow="))
                    {
                        yield return FindTokenImage(set.Substring(0, set.IndexOf(" pow=")), u => url = u);
                        yield return new WaitForSecondsRealtime(MinSecondsBetweenRequests);
                    }
                    if (url != null)
                    {
                        yield return Download(url, (t, b) => { tex = t; bytes = b; });
                        yield return new WaitForSecondsRealtime(MinSecondsBetweenRequests);
                    }
                    set = null; name = null;   // skip the normal lookups below
                }
                // Try the exact printing first, then any printing of the card.
                if (!string.IsNullOrEmpty(set))
                {
                    yield return Download(Url(name, set), (t, b) => { tex = t; bytes = b; });
                    yield return new WaitForSecondsRealtime(MinSecondsBetweenRequests);
                }
                if (tex == null && name != null)
                {
                    yield return Download(Url(name, null), (t, b) => { tex = t; bytes = b; });
                    yield return new WaitForSecondsRealtime(MinSecondsBetweenRequests);
                }

                if (tex != null)
                {
                    memory[key] = tex;
                    // Save the downloaded file as-is (no re-encode).
                    try { if (bytes != null) File.WriteAllBytes(Path.Combine(diskDir, SafeFileName(key) + ".jpg"), bytes); }
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

        IEnumerator DownloadCustom(string url, string key, Action<Texture2D> callback)
        {
            Texture2D tex = null;
            yield return Download(url, (t, b) => tex = t);
            if (tex != null) { memory[key] = tex; callback(tex); }
        }

        static IEnumerator FindTokenImage(string query, Action<string> done)
        {
            string url = "https://api.scryfall.com/cards/search?unique=art&include_extras=true&q=" + UnityWebRequest.EscapeURL(query);
            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("Accept", "application/json");
                req.SetRequestHeader("User-Agent", "MTGUnited/0.1 (local Forge client)");
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) yield break;
                try
                {
                    var root = Json.ParseObject(req.downloadHandler.text);
                    foreach (var o in root.Arr("data"))
                    {
                        var card = o as Dictionary<string, object>;
                        if (card == null) continue;
                        var uris = card.Obj("image_uris");
                        if (uris == null)
                            foreach (var f in card.Arr("card_faces")) { uris = (f as Dictionary<string, object>)?.Obj("image_uris"); if (uris != null) break; }
                        string img = uris?.Str("normal");
                        if (!string.IsNullOrEmpty(img)) { done(img); yield break; }
                    }
                }
                catch (Exception ex) { Debug.LogWarning("[MTGUnited] Token search failed: " + ex.Message); }
            }
        }

        static string Url(string name, string set)
        {
            string url = "https://api.scryfall.com/cards/named?format=image&version=normal&exact=" + UnityWebRequest.EscapeURL(name);
            if (!string.IsNullOrEmpty(set)) url += "&set=" + UnityWebRequest.EscapeURL(set.ToLowerInvariant());
            return url;
        }

        static IEnumerator Download(string url, Action<Texture2D, byte[]> done)
        {
            using (var req = UnityWebRequestTexture.GetTexture(url, false))
            {
                req.SetRequestHeader("Accept", "image/*");
                req.SetRequestHeader("User-Agent", "MTGUnited/0.1 (local Forge client)");
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    var t = DownloadHandlerTexture.GetContent(req);
                    var raw = req.downloadHandler.data;
                    if (t != null) { Prepare(t); done(t, raw); }
                }
            }
        }

        /// <summary>
        /// Card images are compressed on the GPU (about 1/6 of the memory) and their CPU copy is dropped, so even a
        /// deck builder full of cards stays light.
        /// </summary>
        static void Prepare(Texture2D t)
        {
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 4;
            try
            {
                if (t.isReadable)
                {
                    // Block compression needs sizes that are multiples of 4 (Scryfall's are; custom exports may not be).
                    if (t.width % 4 == 0 && t.height % 4 == 0) t.Compress(true);
                    t.Apply(false, true);   // either way, drop the CPU copy
                }
            }
            catch (Exception ex) { Debug.LogWarning("[MTGUnited] Could not compress card image: " + ex.Message); }
        }

        static string SafeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_').Replace('|', '@');
        }
    }
}
