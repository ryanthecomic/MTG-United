using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// A small centred question with a few buttons ("Save changes?" Save / Discard / Keep editing).
    /// Esc or a click outside picks the last button (the safe "cancel" choice).
    /// </summary>
    public class ConfirmDialog : MonoBehaviour
    {
        public static ConfirmDialog Instance { get; private set; }
        RectTransform panel, buttons;
        Text title, message;
        Action cancel;

        public bool IsOpen => gameObject.activeSelf;

        public static ConfirmDialog Create(RectTransform parent)
        {
            var shade = UIKit.Panel("Confirm", parent, new Color(0, 0, 0, 0.6f), false);
            shade.rectTransform.anchorMin = shade.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            shade.rectTransform.sizeDelta = new Vector2(4000, 3000);   // whole screen at any aspect
            var d = shade.gameObject.AddComponent<ConfirmDialog>();
            Instance = d;
            shade.gameObject.AddComponent<PointerEvents>().OnClick = d.Cancel;
            var p = UIKit.Panel("Panel", shade.transform, new Color(0.07f, 0.07f, 0.08f, 0.98f));
            d.panel = p.rectTransform;
            d.panel.anchorMin = d.panel.anchorMax = new Vector2(0.5f, 0.5f);
            d.panel.sizeDelta = new Vector2(640, 280);
            p.gameObject.AddComponent<PointerEvents>();
            var rim = p.gameObject.AddComponent<UnityEngine.UI.Outline>();
            rim.effectColor = new Color(1f, 0.75f, 0.4f, 0.3f); rim.effectDistance = new Vector2(1, -1);
            d.title = UIKit.Label("Title", p.transform, "", 26, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            d.title.rectTransform.Stretch(36, 0, 36, 28);
            d.title.verticalOverflow = VerticalWrapMode.Overflow;
            d.message = UIKit.Label("Message", p.transform, "", 18, Theme.Text, TextAnchor.UpperLeft);
            d.message.rectTransform.Stretch(36, 100, 36, 78);
            d.buttons = UIKit.Rect("Buttons", p.transform);
            d.buttons.anchorMin = new Vector2(0, 0); d.buttons.anchorMax = new Vector2(1, 0); d.buttons.pivot = new Vector2(0.5f, 0);
            d.buttons.sizeDelta = new Vector2(-72, 56); d.buttons.anchoredPosition = new Vector2(0, 28);
            var hl = d.buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 12; hl.childAlignment = TextAnchor.MiddleRight; hl.childControlWidth = false; hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            UiScaled.Add(p, Vector2.zero).MaxScale = 1.2f;
            MenuKit.Bandify(p);   // Arena look: a full-width band, dim shadow behind
            shade.gameObject.SetActive(false);
            return d;
        }

        /// <summary>Opens the question. Buttons left to right; the last one is also what Esc / clicking outside does.</summary>
        public void Show(string heading, string text, params (string label, Color color, Action action)[] choices)
        {
            foreach (Transform c in buttons) Destroy(c.gameObject);
            title.text = heading;
            message.text = text;
            foreach (var (label, color, action) in choices)
            {
                var a = action;
                var b = UIKit.Button(label, buttons, label.ToUpperInvariant(), color, () => { Close(); a?.Invoke(); }, 18);
                ((RectTransform)b.transform).sizeDelta = new Vector2(Mathf.Max(150, label.Length * 13 + 50), 56);
            }
            cancel = choices.Length > 0 ? choices[choices.Length - 1].action : null;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            FadeIn.Play(panel.gameObject, Motion.Short, 0.94f);
            Sound.Play(Sfx.Popup, 0.5f);
        }

        /// <summary>Esc / outside click: the last (safe) choice.</summary>
        public void Cancel()
        {
            var c = cancel;
            Close();
            c?.Invoke();
        }

        public void Close()
        {
            cancel = null;
            gameObject.SetActive(false);
        }

        public static readonly Color Danger = new Color(0.62f, 0.16f, 0.14f);
    }
}
