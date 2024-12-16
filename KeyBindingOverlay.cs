using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace PerfectPlacement.UI
{
    [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
    static class AddKeyBindingOverlay
    {
        static void Postfix(Hud __instance)
        {
            __instance.gameObject.AddComponent<KeyBindingOverlay>();
        }
    }

    internal class KeyBindingOverlay : MonoBehaviour
    {
        private static bool showOverlay = false;
        private static Dictionary<string, List<string>> keyBindings = new();
        private static GameObject overlayObject;
        private static Transform contentArea;

        public static void ToggleOverlay(bool show)
        {
            if (overlayObject != null)
            {
                overlayObject.SetActive(show);
            }
        }

        public static void UpdateBindings(string mode, Dictionary<string, string> bindings)
        {
            if (overlayObject == null)
                CreateOverlay();

            if (keyBindings.TryGetValue(mode, out List<string>? existingBindings) && bindings.All(kv => existingBindings.Contains($"[<color=yellow><b>{kv.Value}</b></color>]: {kv.Key}")) && existingBindings.Count == bindings.Count)
            {
                return; // Skip update if no changes
            }

            // Update or add bindings for the specified mode
            if (!keyBindings.ContainsKey(mode))
                keyBindings[mode] = new List<string>();

            keyBindings[mode].Clear();
            foreach (KeyValuePair<string, string> binding in bindings)
            {
                keyBindings[mode].Add($"[<color=yellow><b>{binding.Value}</b></color>]: {binding.Key}");
            }

            // Refresh UI content
            RefreshOverlayContent();
            ShowMode(mode);
        }


        private static void CreateOverlay()
        {
            overlayObject = new GameObject("KeyBindingOverlay");
            Canvas? canvas = overlayObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000; // Ensure it's rendered on top

            overlayObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            overlayObject.AddComponent<GraphicRaycaster>();

            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(overlayObject.transform);

            RectTransform? rectTransform = panel.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(1f, 0f); // Bottom-right corner
            rectTransform.anchorMax = new Vector2(1f, 0f); // Bottom-right corner
            rectTransform.pivot = new Vector2(1f, 0f); // Align pivot to bottom-right
            rectTransform.anchoredPosition = new Vector2(-10f, 20f); // 10px from bottom-right

            Image? image = panel.AddComponent<Image>();
            image.color = new Color(0, 0, 0, 0.75f);

            VerticalLayoutGroup? layoutGroup = panel.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childAlignment = TextAnchor.UpperLeft;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.spacing = 5;

            ContentSizeFitter? contentSizeFitter = panel.AddComponent<ContentSizeFitter>();
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            contentArea = panel.transform;
        }


        private static void RefreshOverlayContent()
        {
            if (contentArea == null) return;

            // Clear existing content
            foreach (Transform child in contentArea)
            {
                GameObject.Destroy(child.gameObject);
            }

            foreach (KeyValuePair<string, List<string>> mode in keyBindings)
            {
                GameObject modeContainer = new GameObject($"{mode.Key}_Container");
                modeContainer.transform.SetParent(contentArea, false);

                VerticalLayoutGroup? modeContainerLayout = modeContainer.AddComponent<VerticalLayoutGroup>();
                modeContainerLayout.childAlignment = TextAnchor.UpperLeft;
                modeContainerLayout.childForceExpandHeight = false;
                modeContainerLayout.childForceExpandWidth = true;
                modeContainerLayout.spacing = 2;

                ContentSizeFitter? modeContainerFitter = modeContainer.AddComponent<ContentSizeFitter>();
                modeContainerFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                GameObject modeTitle = CreateTextElement(mode.Key, 16, FontStyle.Bold);
                modeTitle.transform.SetParent(modeContainer.transform, false);

                foreach (string? binding in mode.Value)
                {
                    GameObject bindingText = CreateTextElement(binding, 12, FontStyle.Normal);
                    bindingText.transform.SetParent(modeContainer.transform, false);
                }
            }
        }

        public static void ShowMode(string mode)
        {
            foreach (Transform child in contentArea)
            {
                child.gameObject.SetActive(child.name.StartsWith(mode));
            }
        }


        private static GameObject CreateTextElement(string text, int fontSize, FontStyle fontStyle)
        {
            GameObject textObject = new GameObject("Text");
            Text? textComponent = textObject.AddComponent<Text>();

            textComponent.text = text;

            // Try loading the "Norse" font or fallback to Arial
            Font? font = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(x => x.name.ToLower().Contains("norse")) ?? Font.CreateDynamicFontFromOSFont("Arial", fontSize);
            //var font = Font.CreateDynamicFontFromOSFont("Arial", fontSize);

            if (font == null)
            {
                Debug.LogError("Failed to load font. Ensure Norse font or Arial is available.");
            }

            textComponent.font = font;
            textComponent.fontSize = fontSize;
            textComponent.fontStyle = fontStyle;
            textComponent.color = Color.white;
            textComponent.alignment = TextAnchor.MiddleLeft;

            // Set RectTransform properties
            RectTransform? rectTransform = textObject.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(0, 0); // Let the layout control the size
            rectTransform.anchorMin = new Vector2(0, 1);
            rectTransform.anchorMax = new Vector2(1, 1);
            rectTransform.pivot = new Vector2(0.5f, 1);
            rectTransform.localScale = Vector3.one;

            return textObject;
        }
    }
}