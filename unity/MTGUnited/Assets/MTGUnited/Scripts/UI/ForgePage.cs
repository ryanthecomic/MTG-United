using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// The Forge tab: what MTG United's rules engine is, whether it's installed and running, and every action to
    /// manage it: install, use a Forge you already have (browse or auto-detect), update, reinstall, uninstall,
    /// start/stop the bridge, open its folder and log. Setup output streams into the console at the bottom.
    /// </summary>
    public class ForgePage : MonoBehaviour
    {
        GameController gc;
        Text status, details, javaText, consoleText, hint;
        RectTransform actions, detected;
        ScrollRect consoleScroll;
        bool keepData = true;
        float uninstallArmedAt = -10f;
        string lastSignature;
        readonly List<string> found = new List<string>();
        bool scanned;

        public static RectTransform Create(RectTransform parent, GameController gc)
        {
            var page = UIKit.Rect("Forge", parent).Stretch();
            var f = page.gameObject.AddComponent<ForgePage>();
            f.gc = gc;
            var shade = UIKit.Panel("Shade", page, new Color(0.02f, 0.02f, 0.04f, 0.5f), false);
            shade.rectTransform.Stretch();
            shade.raycastTarget = false;

            var icon = UIKit.Rect("Icon", page); icon.Place(76, 868, 64, 64);
            var ii = icon.gameObject.AddComponent<Image>(); ii.sprite = Icons.Anvil; ii.color = new Color(1f, 0.7f, 0.35f); ii.preserveAspect = true; ii.raycastTarget = false;
            var title = UIKit.Label("Title", page, "Forge", 54, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold).Outline(1.5f).Titled();
            title.rectTransform.Place(156, 860, 600, 80);
            var sub = UIKit.Label("Sub", page, "MTG United plays Commander with the rules engine of Forge, the open-source Magic program. This is where you set it up.",
                18, Theme.TextDim, TextAnchor.UpperLeft);
            sub.rectTransform.Place(80, 812, 1100, 44);

            // Tutorial (top right): a step-by-step guide to why Forge is needed and how to set it up.
            UIKit.Pill("Tutorial", page, "How to set up Forge", new Color(0.62f, 0.33f, 0.12f), () => f.OpenTutorial(0), 1450, 836, 400, 68, 24);
            f.BuildTutorial(page);

            // ---- status card (left)
            var card = Card(page, 70, 360, 860, 440);
            f.status = UIKit.Label("Status", card, "", 30, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            f.status.rectTransform.Place(30, 360, 800, 50);
            f.status.supportRichText = true;
            f.details = UIKit.Label("Details", card, "", 17, new Color(0.85f, 0.87f, 0.92f), TextAnchor.UpperLeft);
            f.details.rectTransform.Place(32, 150, 800, 200);
            f.details.supportRichText = true;
            f.javaText = UIKit.Label("Java", card, "", 15, Theme.TextDim, TextAnchor.UpperLeft);
            f.javaText.rectTransform.Place(32, 110, 560, 30);
            // Java's official download page (Temurin JDK 21), for anyone who'd rather install it by hand.
            var dl = UIKit.Button("DownloadJava", card, "DOWNLOAD JAVA", new Color(0.62f, 0.33f, 0.12f), () =>
            {
                Application.OpenURL(JavaDownloadUrl);
                gc.Toasts.Show("Opening Java's download page. Install it, then restart MTG United.");
            }, 15);
            ((RectTransform)dl.transform).Place(600, 104, 230, 40);
            f.hint = UIKit.Label("Hint", card, "", 15, new Color(1f, 0.75f, 0.45f), TextAnchor.UpperLeft);
            f.hint.rectTransform.Place(32, 20, 800, 84);

            // ---- actions (right)
            var act = Card(page, 960, 360, 890, 440);
            var at = UIKit.Label("Title", act, "ACTIONS", 16, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            at.rectTransform.Stretch(28, 0, 28, 20);
            f.actions = UIKit.Rect("Buttons", act).Stretch(20, 20, 20, 56);

            // ---- console (bottom)
            var con = Card(page, 70, 30, 1780, 310);
            var ct = UIKit.Label("Title", con, "OUTPUT", 16, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            ct.rectTransform.Stretch(28, 0, 28, 16);
            // Copy everything in the output (to paste into a bug report or a message).
            var copy = UIKit.Button("Copy", con, "COPY OUTPUT", Theme.Secondary, () => f.CopyOutput(), 14);
            var cr = (RectTransform)copy.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(1, 1); cr.pivot = new Vector2(1, 1);
            cr.sizeDelta = new Vector2(180, 32); cr.anchoredPosition = new Vector2(-24, -8);
            var view = UIKit.Rect("View", con).Stretch(20, 16, 20, 46);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.35f);
            f.consoleScroll = view.gameObject.AddComponent<ScrollRect>();
            var content = UIKit.Rect("Content", view);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var vl = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(12, 12, 8, 8); vl.childControlHeight = true; vl.childControlWidth = true;
            f.consoleText = UIKit.Label("Log", content, "", 14, new Color(0.8f, 0.84f, 0.9f), TextAnchor.UpperLeft);
            f.consoleText.verticalOverflow = VerticalWrapMode.Overflow;
            f.consoleText.supportRichText = false;
            if (f.consoleText is SymbolText cst) { cst.Localize = false; cst.Symbols = false; }   // raw output, exactly as written
            // Right-click the output: copy it too.
            view.gameObject.AddComponent<PointerEvents>().OnRightClick = () => f.CopyOutput();
            f.consoleScroll.content = content; f.consoleScroll.horizontal = false; f.consoleScroll.scrollSensitivity = 40;
            f.consoleScroll.movementType = ScrollRect.MovementType.Clamped;
            UIKit.AddScrollbar(f.consoleScroll);

            ForgeSetup.Load();
            ForgeSetup.CheckJava();
            ForgeSetup.TaskFinished += ok =>
            {
                if (f == null) return;
                gc.Toasts.Show(ok ? ForgeSetup.TaskName + ": done." : ForgeSetup.TaskName + " failed. See the output below.", !ok);
                if (ok && ForgeSetup.Installed && !gc.Bridge.Connected) gc.Launcher.Launch();
                f.lastSignature = null;
            };
            return page;
        }

        static RectTransform Card(Transform parent, float x, float y, float w, float h)
        {
            var img = UIKit.Panel("Card", parent, new Color(0.06f, 0.07f, 0.11f, 0.9f));
            img.rectTransform.Place(x, y, w, h);
            img.pixelsPerUnitMultiplier = 3f;
            var edge = img.gameObject.AddComponent<UnityEngine.UI.Outline>();
            edge.effectColor = new Color(1, 1, 1, 0.07f); edge.effectDistance = new Vector2(1, -1);
            return img.rectTransform;
        }

        // =================================================================================================== refresh

        /// <summary>First visit with nothing installed: open the guide by itself.</summary>
        void OnEnable()
        {
            if (tutorial == null || ForgeSetup.Installed || PlayerPrefs.GetInt("forgeTutorialSeen", 0) == 1) return;
            PlayerPrefs.SetInt("forgeTutorialSeen", 1);
            OpenTutorial(0);
        }

        void Update()
        {
            ForgeSetup.Poll();
            ForgeSetup.PollBrowse();

            bool connected = gc.Bridge.Connected;
            bool running = gc.Launcher.Running;
            bool installed = ForgeSetup.Installed;
            bool busy = ForgeSetup.Busy;

            status.text = busy ? "<color=#f7a540>●</color>  " + ForgeSetup.TaskName + "..."
                : connected ? "<color=#5fd47a>●</color>  Running and connected"
                : running ? "<color=#f7a540>●</color>  Starting (loading cards)..."
                : installed ? "<color=#9aa0aa>●</color>  Installed, not running"
                : "<color=#e55>●</color>  Not installed";

            string mode = ForgeSetup.CurrentMode == ForgeSetup.Mode.Existing ? "Your own Forge"
                : ForgeSetup.CurrentMode == ForgeSetup.Mode.Bundled ? "MTG United's copy (built from Forge's source)" : "—";
            details.text =
                "<b>Setup:</b>  " + mode + "\n" +
                "<b>Forge folder:</b>  " + Short(ForgeSetup.ForgeDir) + "\n" +
                (ForgeSetup.CurrentMode == ForgeSetup.Mode.Existing ? "<b>Forge program:</b>  " + Short(ForgeSetup.ForgeJar) + "\n" : "") +
                "<b>Bridge:</b>  " + Short(ForgeSetup.BridgeJar) + "\n" +
                "<b>MTG United folder:</b>  " + Short(ForgeSetup.Root);
            javaText.text = "Java:  " + ForgeSetup.JavaVersion;
            hint.text = !installed && !busy
                ? "New here? Press \"How to set up Forge\" (top right) for a step-by-step guide. Already have Forge? Use \"Use my Forge...\" or \"Find Forge\" so nothing is downloaded twice. Otherwise \"Install Forge\" downloads about 500 MB and builds it (a few minutes)."
                : busy ? "Working... you can keep using the menus; output is shown below."
                : "";

            // Rebuild the action buttons only when the situation changes.
            string sig = installed + "|" + busy + "|" + running + "|" + connected + "|" + ForgeSetup.CurrentMode + "|" + found.Count + "|" + scanned + "|" + keepData + "|" + (Time.unscaledTime - uninstallArmedAt < 4f) + "|" + ForgeSetup.JavaMissing + "|" + ForgeSetup.GitFound;
            if (sig != lastSignature) { lastSignature = sig; BuildActions(installed, busy, running || connected); }

            string tail = ForgeSetup.LogTail(120);
            if (tail != consoleText.text)
            {
                bool atBottom = consoleScroll.verticalNormalizedPosition < 0.02f || consoleText.text.Length == 0;
                consoleText.text = tail.Length == 0 ? "Nothing yet." : tail;
                if (atBottom) { Canvas.ForceUpdateCanvases(); consoleScroll.verticalNormalizedPosition = 0f; }
            }
        }

        const string JavaDownloadUrl = "https://adoptium.net/temurin/releases/?version=21&os=windows&package=jdk";

        void CopyOutput()
        {
            string all = ForgeSetup.LogTail(100000);
            if (string.IsNullOrEmpty(all)) { gc.Toasts.Show("Nothing to copy yet."); return; }
            GUIUtility.systemCopyBuffer = all;
            gc.Toasts.Show("Output copied (" + all.Split('\n').Length + " lines).");
        }

        static string Short(string path)
        {
            if (string.IsNullOrEmpty(path)) return "—";
            return path.Length > 70 ? "…" + path.Substring(path.Length - 69) : path;
        }

        // =================================================================================================== actions

        float ax, ay;

        void BuildActions(bool installed, bool busy, bool bridgeUp)
        {
            foreach (Transform c in actions) Destroy(c.gameObject);
            ax = 0; ay = 0;

            if (busy) { Label("A setup task is running; actions are back when it finishes."); return; }

            if (!installed)
            {
                // Prerequisites first: Java (always) and Git (only to download Forge's source).
                if (ForgeSetup.JavaMissing) Btn("1. Install Java (JDK 21)", new Color(0.62f, 0.33f, 0.12f), () => ForgeSetup.InstallTool("Java", "EclipseAdoptium.Temurin.21.JDK"), true);
                if (!ForgeSetup.GitFound) Btn("Install Git (for Install Forge)", Theme.Secondary, () => ForgeSetup.InstallTool("Git", "Git.Git"), true);
                if (ForgeSetup.JavaMissing || !ForgeSetup.GitFound) NewRow();
                Btn("Install Forge", Theme.Accent, () => ForgeSetup.Install(false), true);
                Btn("Use my Forge...", Theme.Secondary, Browse);
                Btn("Find Forge", Theme.Secondary, Scan);
            }
            else
            {
                if (bridgeUp)
                {
                    Btn("Restart bridge", Theme.Accent, () => { gc.Launcher.Stop(); gc.Launcher.Launch(); });
                    Btn("Stop bridge", Theme.Secondary, () => gc.Launcher.Stop());
                }
                else Btn("Start bridge", Theme.Accent, () => gc.Launcher.Launch());
                Btn("Update", Theme.Secondary, ForgeSetup.Update);
                Btn("Reinstall", Theme.Secondary, () =>
                {
                    if (ForgeSetup.CurrentMode == ForgeSetup.Mode.Existing) ForgeSetup.UseExisting(ForgeSetup.ForgeDir);
                    else ForgeSetup.Install(true);
                });
                NewRow();
                if (ForgeSetup.CurrentMode == ForgeSetup.Mode.Existing)
                    Btn("Use MTG United's Forge", Theme.Secondary, () => ForgeSetup.Install(false));
                else
                {
                    Btn("Use my Forge...", Theme.Secondary, Browse);
                    Btn("Find Forge", Theme.Secondary, Scan);
                }
                Btn("Open folder", Theme.Secondary, () => ForgeSetup.OpenFolder(ForgeSetup.CurrentMode == ForgeSetup.Mode.Existing ? ForgeSetup.ForgeDir : ForgeSetup.Root));
                Btn("Open log", Theme.Secondary, () => ForgeSetup.OpenFolder(ForgeSetup.LogFile));
                NewRow();
                bool armed = Time.unscaledTime - uninstallArmedAt < 4f;
                Btn(armed ? "Click again to uninstall" : "Uninstall", new Color(0.5f, 0.15f, 0.15f), () =>
                {
                    if (Time.unscaledTime - uninstallArmedAt < 4f) { uninstallArmedAt = -10f; ForgeSetup.Uninstall(keepData); }
                    else { uninstallArmedAt = Time.unscaledTime; lastSignature = null; }
                });
                Btn(keepData ? "Keep settings & log: ON" : "Keep settings & log: OFF", Theme.Secondary, () => { keepData = !keepData; });
                if (ForgeSetup.CurrentMode == ForgeSetup.Mode.Existing)
                {
                    NewRow();
                    Label("Uninstalling only removes MTG United's bridge and links; your Forge folder is never touched.");
                }
            }

            // Auto-detect results.
            if (scanned)
            {
                NewRow();
                if (found.Count == 0) Label("No Forge found in the usual places. Use \"Use my Forge...\" to pick its folder.");
                foreach (var dir in found)
                {
                    string d = dir;
                    Btn("Use " + Short(d), new Color(0.2f, 0.35f, 0.25f), () => ForgeSetup.UseExisting(d), true);
                }
            }
        }

        // =================================================================================================== tutorial

        static readonly (string title, string body)[] Steps =
        {
            ("Why does MTG United need Forge?",
             "MTG United is the table: the cards, animations, sounds and menus.\n\n" +
             "The rules of Magic (tens of thousands of cards, the stack, combat, every Commander rule) and the AI opponents come from " +
             "<b>Forge</b>, a free, open-source Magic program. MTG United runs Forge in the background through a small helper called " +
             "the <b>bridge</b>, and draws what Forge decides.\n\nSo before your first game, Forge has to be on your computer once."),
            ("What you need",
             "•  <b>Java 17 or newer (the JDK)</b>. Forge is written in Java. If the Forge tab says Java is \"not found\", open a terminal and run:\n" +
             "      winget install EclipseAdoptium.Temurin.21.JDK\n   (or press <b>Install Java</b> below), then restart MTG United.\n\n" +
             "•  <b>Git</b>, only if MTG United installs Forge for you (it downloads Forge's source). <b>Install Git</b> below does it.\n\n" +
             "•  <b>Disk space</b>: about 1.5 GB if MTG United installs Forge for you; almost nothing if you already have Forge.\n\n" +
             "•  <b>Internet</b> for the first install, and for card images while you play."),
            ("Already have Forge? Use it",
             "If Forge is already installed (or unzipped) on your PC, you don't need to download it again.\n\n" +
             "•  <b>Find Forge</b> looks in the usual places (Program Files, Desktop, Downloads, Documents, Games...).\n" +
             "•  <b>Use my Forge...</b> lets you pick the folder yourself: the one that contains <b>forge.exe</b> (or Forge's .jar) and a <b>res</b> folder.\n\n" +
             "MTG United builds its bridge against your Forge and reads its card data through a link. Your Forge folder and your desktop Forge settings are never changed."),
            ("No Forge yet? Install it",
             "Press <b>Install Forge</b>. MTG United will:\n" +
             "   1.  download Forge's source at the version it was made for (about 500 MB, mostly card scripts);\n" +
             "   2.  build Forge's rules engine (a few minutes the first time);\n" +
             "   3.  build the bridge.\n\n" +
             "You can watch every step in the <b>Output</b> box at the bottom of this tab. Everything goes into MTG United's own folder."),
            ("Start playing",
             "When setup finishes, the bridge starts by itself. The dot at the top of this tab (and on Home) tells you where it is:\n\n" +
             "   <color=#5fd47a>●</color>  Running and connected: ready, press Play on Home.\n" +
             "   <color=#f7a540>●</color>  Starting: Forge is loading every card, about 30 to 60 seconds.\n" +
             "   <color=#9aa0aa>●</color>  Installed, not running: press Start bridge.\n" +
             "   <color=#e55>●</color>  Not installed: go back a step."),
            ("If something goes wrong",
             "•  <b>Java not found</b>: install the JDK (step 2) and restart MTG United.\n" +
             "•  <b>\"Doesn't compile against this Forge version\"</b>: your Forge is too different from the one MTG United was made for. Use <b>Install Forge</b> instead.\n" +
             "•  <b>Stuck or crashing</b>: <b>Open log</b> shows the bridge's log; <b>Restart bridge</b> often helps; <b>Reinstall</b> rebuilds everything.\n" +
             "•  <b>Updating MTG United</b>: press <b>Update</b> afterwards so the bridge is rebuilt with the new version.\n" +
             "•  <b>Uninstall</b> removes only MTG United's parts. With \"Keep settings & log\" on, your bridge settings survive."),
        };

        RectTransform tutorial, tutDots;
        Text tutTitle, tutBody, tutCount;
        Button tutBack, tutNext;
        RectTransform tutActions;
        int tutStep;

        void BuildTutorial(RectTransform page)
        {
            var shade = UIKit.Panel("Tutorial", page, new Color(0, 0, 0, 0.7f), false);
            shade.rectTransform.Stretch(0, 0, 0, -96);   // also darkens the nav bar area
            tutorial = shade.rectTransform;
            shade.gameObject.AddComponent<PointerEvents>().OnClick = CloseTutorial;
            var p = UIKit.Panel("Panel", shade.transform, new Color(0.07f, 0.07f, 0.08f, 0.98f));
            p.rectTransform.anchorMin = p.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            p.rectTransform.sizeDelta = new Vector2(1000, 640);
            p.rectTransform.anchoredPosition = new Vector2(0, -40);
            p.gameObject.AddComponent<PointerEvents>();
            var rim = p.gameObject.AddComponent<UnityEngine.UI.Outline>();
            rim.effectColor = new Color(0.95f, 0.65f, 0.3f, 0.45f); rim.effectDistance = new Vector2(1.5f, -1.5f);

            var icon = UIKit.Rect("Icon", p.transform); icon.Place(40, 548, 56, 56);
            var ii = icon.gameObject.AddComponent<Image>(); ii.sprite = Icons.Anvil; ii.color = new Color(1f, 0.7f, 0.35f); ii.preserveAspect = true; ii.raycastTarget = false;
            tutCount = UIKit.Label("Count", p.transform, "", 15, new Color(1f, 0.7f, 0.35f), TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            tutCount.rectTransform.Place(112, 580, 600, 26);
            tutTitle = UIKit.Label("Title", p.transform, "", 36, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            tutTitle.rectTransform.Place(110, 528, 840, 52);
            tutBody = UIKit.Label("Body", p.transform, "", 20, new Color(0.88f, 0.9f, 0.95f), TextAnchor.UpperLeft);
            tutBody.rectTransform.Place(50, 170, 900, 340);
            tutBody.supportRichText = true;
            tutBody.verticalOverflow = VerticalWrapMode.Overflow;
            tutActions = UIKit.Rect("Actions", p.transform);
            tutActions.Place(50, 104, 900, 52);

            tutBack = UIKit.Button("Back", p.transform, "BACK", Theme.Secondary, () => OpenTutorial(tutStep - 1), 18);
            ((RectTransform)tutBack.transform).Place(50, 28, 180, 54);
            tutNext = UIKit.Button("Next", p.transform, "NEXT", Theme.Accent, () => { if (tutStep >= Steps.Length - 1) CloseTutorial(); else OpenTutorial(tutStep + 1); }, 18);
            ((RectTransform)tutNext.transform).Place(770, 28, 180, 54);
            tutDots = UIKit.Rect("Dots", p.transform);
            tutDots.Place(250, 46, 500, 18);
            for (int i = 0; i < Steps.Length; i++)
            {
                int k = i;
                var d = UIKit.Panel("Dot", tutDots, Color.white);
                d.rectTransform.anchorMin = d.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                d.rectTransform.sizeDelta = new Vector2(14, 14);
                d.rectTransform.anchoredPosition = new Vector2((i - (Steps.Length - 1) / 2f) * 26f, 0);
                d.pixelsPerUnitMultiplier = 32f / 14f;
                d.gameObject.AddComponent<PointerEvents>().OnClick = () => OpenTutorial(k);
            }
            UIKit.CloseX(p.transform, CloseTutorial);
            shade.gameObject.SetActive(false);
        }

        void OpenTutorial(int step)
        {
            tutStep = Mathf.Clamp(step, 0, Steps.Length - 1);
            tutorial.gameObject.SetActive(true);
            tutorial.SetAsLastSibling();
            tutCount.text = "STEP " + (tutStep + 1) + " OF " + Steps.Length;
            tutTitle.text = Steps[tutStep].title;
            tutBody.text = Steps[tutStep].body;
            tutBack.SetVisible(tutStep > 0);
            UIKit.LabelOf(tutNext).text = tutStep >= Steps.Length - 1 ? "DONE" : "NEXT";
            for (int i = 0; i < tutDots.childCount; i++)
                tutDots.GetChild(i).GetComponent<Image>().color = i == tutStep ? new Color(1f, 0.72f, 0.35f) : new Color(1f, 1f, 1f, 0.3f);

            // Shortcuts to do the step right here.
            foreach (Transform c in tutActions) Destroy(c.gameObject);
            float x = 0;
            void Act(string label, UnityEngine.Events.UnityAction a)
            {
                var b = UIKit.Button(label, tutActions, label, new Color(0.62f, 0.33f, 0.12f), () => { CloseTutorial(); a(); }, 17);
                ((RectTransform)b.transform).Place(x, 0, 240, 50);
                x += 256;
            }
            if (tutStep == 1 && ForgeSetup.JavaMissing) Act("Install Java", () => ForgeSetup.InstallTool("Java", "EclipseAdoptium.Temurin.21.JDK"));
            if (tutStep == 1 && !ForgeSetup.GitFound) Act("Install Git", () => ForgeSetup.InstallTool("Git", "Git.Git"));
            if (tutStep == 2) { Act("Find Forge", Scan); Act("Use my Forge...", Browse); }
            if (tutStep == 3 && !ForgeSetup.Installed) Act("Install Forge", () => ForgeSetup.Install(false));
            if (tutStep == 4 && ForgeSetup.Installed && !gc.Bridge.Connected) Act("Start bridge", () => gc.Launcher.Launch());
            if (tutStep == 5 && ForgeSetup.Installed) Act("Open log", () => ForgeSetup.OpenFolder(ForgeSetup.LogFile));

            FadeIn.Play(tutBody.transform.parent.gameObject, Motion.Short, 0.98f);
        }

        void CloseTutorial() => tutorial.gameObject.SetActive(false);

        void Browse()
        {
            ForgeSetup.BrowseFolder(dir =>
            {
                if (string.IsNullOrEmpty(dir)) return;
                if (!ForgeSetup.LooksLikeForge(dir))
                {
                    gc.Toasts.Show("That folder doesn't look like Forge: it needs a 'res' folder and Forge's .jar file.", true);
                    return;
                }
                ForgeSetup.UseExisting(dir);
            });
        }

        void Scan()
        {
            found.Clear();
            found.AddRange(ForgeSetup.Detect());
            scanned = true;
            ForgeSetup.Log(found.Count == 0 ? "No Forge installs found." : "Found Forge at: " + string.Join(", ", found));
            lastSignature = null;
        }

        void Btn(string label, Color color, UnityEngine.Events.UnityAction a, bool wide = false)
        {
            float w = wide ? 560 : 270;
            if (ax + w > 850) NewRow();
            var b = UIKit.Button(label, actions, label, color, a, 17);
            var r = (RectTransform)b.transform;
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.sizeDelta = new Vector2(w - 10, 48); r.anchoredPosition = new Vector2(ax, -ay);
            ax += w;
        }

        void Label(string text)
        {
            if (ax > 0) NewRow();
            var t = UIKit.Label("Note", actions, text, 15, Theme.TextDim, TextAnchor.MiddleLeft);
            var r = t.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.sizeDelta = new Vector2(840, 48); r.anchoredPosition = new Vector2(0, -ay);
            NewRow();
        }

        void NewRow() { ax = 0; ay += 58; }
    }
}
