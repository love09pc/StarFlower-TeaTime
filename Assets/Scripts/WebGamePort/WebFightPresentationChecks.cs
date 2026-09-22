#if UNITY_EDITOR
using System;
using UnityEngine;

public sealed partial class WebFightBootstrap
{
    public string RunPresentationChecks()
    {
        var oldMode = mode;
        var oldOne = playerOne.State; var oldTwo = playerTwo.State;
        bool oldMenu = menuOpen, oldSettings = settingsOpen, oldRemote = remotePaused, oldConfirm = confirmExit;
        bool oldFps = showFps, oldPing = showPing;
        int oldLanguage = language, oldBinding = editingBinding;
        float oldAnimation = animationTime;
        var oldSample = sampledOne;
        ushort oldPending = pendingOne;
        int oldCountdown = resumeTicks; bool oldResume = resumePending;
        int checks = 0;
        Action<bool, string> check = (ok, name) => { if (!ok) throw new InvalidOperationException("FAIL: " + name); checks++; };
        try
        {
            mode = SessionMode.Training; menuOpen = false; settingsOpen = false; remotePaused = false; confirmExit = false; editingBinding = -1;
            playerOne.State = FighterState.Create(220, 1); playerTwo.State = FighterState.Create(600, -1);
            sampledOne = new FighterInput { Right = true }; pendingOne = 0;
            playerOne.State.AttackTimer = 10;
            menuOpen = true;
            for (int i = 0; i < 120; i++) { TickSession(); AdvanceAnimation(1f / 120); }
            check(playerOne.State.X == 220 && playerOne.State.AttackTimer == 10 && animationTime == oldAnimation, "pause freezes movement, combat timers and animation");
            menuOpen = false; settingsOpen = true;
            TickSession(); AdvanceAnimation(1);
            check(playerOne.State.X == 220 && animationTime == oldAnimation, "settings alone pauses training");
            settingsOpen = false;
            TickSession(); AdvanceAnimation(1);
            check(resumeTicks == 360 && playerOne.State.AttackTimer == 10 && animationTime == oldAnimation, "resume waits three seconds");
            for (int i = 0; i < 359; i++) TickSession();
            check(playerOne.State.AttackTimer == 10, "countdown does not release early");
            TickSession(); AdvanceAnimation(1);
            check(playerOne.State.AttackTimer < 10 && animationTime > oldAnimation, "resume advances simulation and animation");
            showFps = showPing = true;
            UpdateHud(); UpdatePerformanceHud();
            check(fpsText.transform.parent == hudOne && fpsText.rectTransform.anchoredPosition.y == -80 && pingText.rectTransform.anchoredPosition.y == -102, "training FPS then ping below guard");
            check(!statusText.text.StartsWith("FPS"), "status and FPS are separate");
            check(pingText.text == "PING: --", "offline ping is unavailable, not zero");
            showFps = false; UpdatePerformanceHud();
            check(!fpsText.gameObject.activeSelf && pingText.rectTransform.anchoredPosition.y == -80, "ping-only occupies first metrics row");
            mode = SessionMode.Client; showFps = true; UpdateHud(); UpdatePerformanceHud();
            check(fpsText.transform.parent == hudTwo && pingText.rectTransform.anchoredPosition.y == -128, "client metrics follow own right-hand guard");
            language = 2;
            check(L("설정", "Settings") == "設定" && L("해상도", "Resolution") == "解像度", "Japanese translation selected");
            foreach (var entry in Japanese)
                foreach (char character in entry.Value)
                    check(uiFont.HasCharacter(character), "Japanese glyph " + character);
            check(!UnityEditor.PlayerSettings.resizableWindow && !UnityEditor.PlayerSettings.allowFullscreenSwitch, "standalone fullscreen switches disabled");
            return "PASS presentation checks (" + checks + " assertions, including Japanese glyph coverage).";
        }
        finally
        {
            mode = oldMode; playerOne.State = oldOne; playerTwo.State = oldTwo;
            menuOpen = oldMenu; settingsOpen = oldSettings; remotePaused = oldRemote; confirmExit = oldConfirm;
            showFps = oldFps; showPing = oldPing; language = oldLanguage; editingBinding = oldBinding;
            animationTime = oldAnimation; sampledOne = oldSample; pendingOne = oldPending;
            resumeTicks = oldCountdown; resumePending = oldResume;
            UpdateHud(); UpdatePerformanceHud();
        }
    }
}
#endif
