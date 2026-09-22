using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public sealed partial class WebFightBootstrap
{
    private Vector2Int selectedResolution;
    private Coroutine resolutionChange;
    private float nextFullscreenCheck;
    private Vector2Int desktopResolution;
    private string ResolutionLabel => selectedResolution.x + " x " + selectedResolution.y;

    private void InitializeDisplay()
    {
        var current = Screen.currentResolution;
        desktopResolution = Application.isEditor ? new Vector2Int(current.width, current.height) : new Vector2Int(Display.main.systemWidth, Display.main.systemHeight);
        Vector2Int native = NativeResolution();
        selectedResolution = new Vector2Int(PlayerPrefs.GetInt("WebFight.Width", native.x), PlayerPrefs.GetInt("WebFight.Height", native.y));
        // Recover old builds that persisted the low render size as the monitor's maximum.
        if (!Application.isEditor && PlayerPrefs.GetInt("WebFight.DisplayDefaultsVersion", 0) < 1)
        {
            selectedResolution = native;
            PlayerPrefs.SetInt("WebFight.DisplayDefaultsVersion", 1);
        }
        if (selectedResolution.x < 640 || selectedResolution.y < 360 || selectedResolution.x > 8192 || selectedResolution.y > 8192)
            selectedResolution = native;
        if (!Application.isEditor) SelectResolution(selectedResolution);
    }

    private Vector2Int NativeResolution()
    {
        return new Vector2Int(Mathf.Max(1280, desktopResolution.x), Mathf.Max(720, desktopResolution.y));
    }

    private void SelectResolution(Vector2Int size)
    {
        selectedResolution = size;
        if (resolutionChange != null) StopCoroutine(resolutionChange);
        resolutionChange = StartCoroutine(ApplyResolution(size));
    }

    private IEnumerator ApplyResolution(Vector2Int size)
    {
        if (!Application.isEditor)
        {
            Screen.SetResolution(size.x, size.y, FullScreenMode.FullScreenWindow);
            // SetResolution is deferred by Unity; do not read the old size in the click callback.
            yield return new WaitForSecondsRealtime(0.75f);
            selectedResolution = new Vector2Int(Screen.width, Screen.height);
        }
        PlayerPrefs.SetInt("WebFight.Width", selectedResolution.x);
        PlayerPrefs.SetInt("WebFight.Height", selectedResolution.y);
        PlayerPrefs.Save();
        resolutionChange = null;
        uiSignature = "";
    }

    private void MaintainFullscreen()
    {
        if (Application.isEditor || !Application.isFocused || resolutionChange != null || Time.unscaledTime < nextFullscreenCheck) return;
        nextFullscreenCheck = Time.unscaledTime + 1f;
        if (Screen.fullScreenMode != FullScreenMode.FullScreenWindow) SelectResolution(selectedResolution);
    }

    private void ShowResolutionOptions()
    {
        var native = NativeResolution();
        var sizes = new List<Vector2Int>();
        Action<int, int> add = (w, h) => {
            var size = new Vector2Int(w, h);
            if (w >= 640 && h >= 360 && w <= 8192 && h <= 8192 && !sizes.Contains(size)) sizes.Add(size);
        };
        add(1280, 720); add(1600, 900); add(1920, 1080); add(2560, 1440); add(3840, 2160);
        foreach (var size in Screen.resolutions) add(size.width, size.height);
        add(native.x, native.y); add(selectedResolution.x, selectedResolution.y);
        sizes.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        var labels = sizes.ConvertAll(size => size.x + " x " + size.y).ToArray();
        ShowOptions(L("해상도", "Resolution"), labels, sizes.IndexOf(selectedResolution), index => SelectResolution(sizes[index]));
    }

    private void ShowOptions(string title, string[] options, int selected, Action<int> select)
    {
        var overlay = UiPanel(screenRoot, "Options overlay", 0, 0, UiWidth, UiHeight, new Color(0, 0, 0, 0.75f));
        var dismiss = overlay.gameObject.AddComponent<Button>();
        dismiss.transition = Selectable.Transition.None;
        dismiss.onClick.AddListener(() => Destroy(overlay.gameObject));
        float height = Mathf.Min(UiHeight - 100, 110 + options.Length * 58);
        float width = Mathf.Min(500, UiWidth - 80);
        var box = UiPanel(overlay, "Options", (UiWidth - width) / 2, (UiHeight - height) / 2, width, height, Hex("222222"), true, Orange);
        UiText(box, title, 24, Orange, 24, 20, width - 48, 36);
        var viewport = UiRect(box, "Options viewport", 24, 78, width - 48, height - 100);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = UiRect(viewport, "Options list", 0, 0, width - 48, options.Length * 58);
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
        scroll.scrollSensitivity = 30; scroll.movementType = ScrollRect.MovementType.Clamped;
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            var button = UiButton(content, options[i], 0, i * 58, width - 48, 50, () => { Destroy(overlay.gameObject); select(index); });
            if (i == selected) SetButtonColor(button, Orange, Hex("111111"));
        }
    }

    private void ToggleSetting(Transform parent, float width, float y, string title, bool value, Action<bool> change)
    {
        var row = UiPanel(parent, title, 0, y, width, 86, Hex("292929"), true);
        UiText(row, title, 18, Color.white, 24, 16, width - 110, 54);
        var box = UiPanel(row, "Toggle", width - 68, 23, 40, 40, Hex("151515"), true, Hex("777777"));
        var mark = UiPanel(box, "Checked", 9, 9, 22, 22, Orange);
        var toggle = box.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = box.GetComponent<Image>(); toggle.graphic = mark.GetComponent<Image>();
        toggle.isOn = value;
        toggle.onValueChanged.AddListener(on => { change(on); PlayerPrefs.Save(); });
    }

    private void ConfirmResetSettings()
    {
        ShowOptions(L("설정을 초기화할까요?", "Reset all settings?"), new[] { L("취소", "Cancel"), L("초기화", "Reset") }, 0, index => {
            if (index != 1) return;
            for (int i = 0; i < bindings.Length; i++) { bindings[i] = DefaultBindings[i]; PlayerPrefs.DeleteKey("WebFight.Key" + i); }
            for (int i = 0; i < 3; i++) PlayerPrefs.DeleteKey("WebFight.Volume" + i);
            foreach (string key in new[] { "VSync", "Fps", "Ping", "Language", "English", "Hitboxes", "Width", "Height" }) PlayerPrefs.DeleteKey("WebFight." + key);
            language = 0; showFps = showPing = showHitboxes = false;
            AudioListener.volume = 1; QualitySettings.vSyncCount = 1;
            PlayerPrefs.Save(); SelectResolution(NativeResolution()); uiSignature = "";
        });
    }

    private static bool AllowedBinding(int value)
    {
        if (value == -1 || value == -2) return true;
        var key = (Key)value;
        return key == Key.Space || key == Key.LeftShift ||
            (value >= (int)Key.A && value <= (int)Key.Z) ||
            (value >= (int)Key.Digit1 && value <= (int)Key.Digit0) ||
            (value >= (int)Key.Numpad0 && value <= (int)Key.Numpad9);
    }
}
