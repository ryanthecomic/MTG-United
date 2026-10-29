using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>Start screen: paste a decklist, pick the pod size and AI decks, then play.</summary>
    public class MainMenu : MonoBehaviour
    {
        GameController gc;
        InputField deckInput, commanderInput, nameInput;
        Text connText, errorText, oppLabel, aiLabel, launcherText;
        Button playButton, launchButton;
        int opponents;
        string aiDecks;

        public bool IsOpen => gameObject.activeSelf;

        public static MainMenu Create(RectTransform parent, GameController gc)
        {
            var shade = UIKit.Panel("MainMenu", parent, new Color(0.02f, 0.03f, 0.05f, 0.96f), false);
            shade.rectTransform.Stretch();
            var m = shade.gameObject.AddComponent<MainMenu>();
            m.gc = gc;
            m.opponents = PlayerPrefs.GetInt("opponents", 3);
            m.aiDecks = PlayerPrefs.GetString("aiDecks", "precon");
            m.Build(shade.rectTransform);
            return m;
        }

        void Build(RectTransform root)
        {
            var frame = UIKit.Rect("Frame", root);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(1500, 960);

            var title = UIKit.Label("Title", frame, "MTG UNITED", 72, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Outline(2);
            title.rectTransform.Place(0, 860, 900, 100);
            var subtitle = UIKit.Label("Subtitle", frame, "Commander on the Forge rules engine", 22, Theme.TextDim, TextAnchor.UpperLeft);
            subtitle.rectTransform.Place(4, 820, 900, 40);

            // Deck column
            var deckLabel = UIKit.Label("DeckLabel", frame, "Your decklist  (paste an Arena / Moxfield / MTGO export, or a Forge .dck)", 18, Theme.Text, TextAnchor.LowerLeft, FontStyle.Bold);
            deckLabel.rectTransform.Place(0, 770, 940, 36);
            deckInput = UIKit.Input("Deck", frame, "Commander\n1 Atraxa, Praetors' Voice\n\nDeck\n1 Sol Ring\n1 Command Tower\n...\n\n(Leave empty to play a random Commander precon.)", true, 15);
            deckInput.GetComponent<RectTransform>().Place(0, 120, 940, 645);
            deckInput.text = PlayerPrefs.GetString("deck", "");

            var cmdLabel = UIKit.Label("CmdLabel", frame, "Commander (optional; overrides the list. Partners: separate with ';')", 16, Theme.TextDim, TextAnchor.LowerLeft);
            cmdLabel.rectTransform.Place(0, 72, 940, 32);
            commanderInput = UIKit.Input("Commander", frame, "e.g. Atraxa, Praetors' Voice", false, 18);
            commanderInput.GetComponent<RectTransform>().Place(0, 20, 940, 50);
            commanderInput.text = PlayerPrefs.GetString("commander", "");

            // Settings column
            float x = 1000, w = 500;
            var nameLabel = UIKit.Label("NameLabel", frame, "Your name", 18, Theme.Text, TextAnchor.LowerLeft, FontStyle.Bold);
            nameLabel.rectTransform.Place(x, 770, w, 36);
            nameInput = UIKit.Input("Name", frame, "Player", false, 20);
            nameInput.GetComponent<RectTransform>().Place(x, 715, w, 52);
            nameInput.text = PlayerPrefs.GetString("playerName", "Player");

            oppLabel = UIKit.Label("OppLabel", frame, "", 18, Theme.Text, TextAnchor.LowerLeft, FontStyle.Bold);
            oppLabel.rectTransform.Place(x, 650, w, 36);
            for (int i = 1; i <= 5; i++)
            {
                int n = i;
                var b = UIKit.Button("Opp" + i, frame, i.ToString(), Theme.Secondary, () => { opponents = n; RefreshChoices(); }, 22);
                ((RectTransform)b.transform).Place(x + (i - 1) * 100, 590, 90, 56);
                b.name = "Opp" + i;
            }

            aiLabel = UIKit.Label("AiLabel", frame, "", 18, Theme.Text, TextAnchor.LowerLeft, FontStyle.Bold);
            aiLabel.rectTransform.Place(x, 525, w, 36);
            var precon = UIKit.Button("AiPrecon", frame, "Precons", Theme.Secondary, () => { aiDecks = "precon"; RefreshChoices(); }, 20);
            ((RectTransform)precon.transform).Place(x, 465, 245, 56);
            var random = UIKit.Button("AiRandom", frame, "Random decks", Theme.Secondary, () => { aiDecks = "random"; RefreshChoices(); }, 20);
            ((RectTransform)random.transform).Place(x + 255, 465, 245, 56);

            // Bridge status + launcher
            var connBg = UIKit.Panel("Conn", frame, Theme.Panel);
            connBg.rectTransform.Place(x, 250, w, 195);
            connText = UIKit.Label("Status", connBg.transform, "", 18, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            connText.rectTransform.Stretch(16, 0, 16, 12);
            launcherText = UIKit.Label("Launcher", connBg.transform, "", 13, Theme.TextDim, TextAnchor.UpperLeft);
            launcherText.rectTransform.Stretch(16, 70, 16, 44);
            launchButton = UIKit.Button("Launch", connBg.transform, "Start Forge bridge", Theme.Secondary, () => gc.Launcher.Launch(), 18);
            var lr = (RectTransform)launchButton.transform;
            lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0); lr.sizeDelta = new Vector2(w - 32, 48); lr.anchoredPosition = new Vector2(0, 36);

            errorText = UIKit.Label("Errors", frame, "", 15, Theme.Danger, TextAnchor.UpperLeft);
            errorText.rectTransform.Place(x, 120, w, 120);

            playButton = UIKit.Button("Play", frame, "PLAY", Theme.Accent, OnPlay, 34);
            ((RectTransform)playButton.transform).Place(x, 20, w, 90);

            RefreshChoices();
            SetConnection(false);
        }

        void RefreshChoices()
        {
            oppLabel.text = "AI opponents: " + opponents;
            aiLabel.text = "AI decks: " + (aiDecks == "random" ? "randomly generated" : "Commander precons");
            foreach (Transform t in playButton.transform.parent)
            {
                if (t.name.StartsWith("Opp") && t.name.Length == 4)
                    t.GetComponent<Image>().color = t.name == "Opp" + opponents ? Theme.Accent : Theme.Secondary;
                if (t.name == "AiPrecon") t.GetComponent<Image>().color = aiDecks == "precon" ? Theme.Accent : Theme.Secondary;
                if (t.name == "AiRandom") t.GetComponent<Image>().color = aiDecks == "random" ? Theme.Accent : Theme.Secondary;
            }
        }

        public void SetConnection(bool connected)
        {
            if (connText == null) return;
            connText.text = connected ? "<color=#5fd977>●</color> Connected to Forge" : "<color=#ff6b6b>●</color> Waiting for the Forge bridge on port " + gc.Bridge.Port + "...";
            playButton.interactable = connected;
            launchButton.SetVisible(!connected);
        }

        public void SetBusy(string text)
        {
            errorText.color = Theme.TextDim;
            errorText.text = text;
        }

        public void ShowErrors(List<string> errors)
        {
            errorText.color = Theme.Danger;
            errorText.text = string.Join("\n", errors.Count > 8 ? errors.GetRange(0, 8) : errors) + (errors.Count > 8 ? "\n(+" + (errors.Count - 8) + " more)" : "");
        }

        void Update()
        {
            if (launcherText != null) launcherText.text = gc.Launcher.StatusLine;
        }

        void OnPlay()
        {
            PlayerPrefs.SetString("deck", deckInput.text);
            PlayerPrefs.SetString("commander", commanderInput.text);
            PlayerPrefs.SetString("playerName", nameInput.text);
            PlayerPrefs.SetInt("opponents", opponents);
            PlayerPrefs.SetString("aiDecks", aiDecks);
            PlayerPrefs.Save();

            var commanders = new List<object>();
            foreach (var c in commanderInput.text.Split(';'))
                if (c.Trim().Length > 0) commanders.Add(c.Trim());

            gc.BoardView.Clear();
            gc.StartGame(new Dictionary<string, object>
            {
                { "t", "start" },
                { "deck", deckInput.text },
                { "commanders", commanders },
                { "name", string.IsNullOrWhiteSpace(nameInput.text) ? "Player" : nameInput.text.Trim() },
                { "opponents", opponents },
                { "aiDecks", aiDecks },
            });
        }

        public void Show()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            errorText.text = "";
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
