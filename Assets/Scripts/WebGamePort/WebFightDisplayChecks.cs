using System;
using System.Collections;
using System.IO;
using UnityEngine;

public sealed partial class WebFightBootstrap
{
    // Opt-in standalone regression run. Normal launches never enter this path.
    private IEnumerator CheckStandaloneDisplay()
    {
        yield return new WaitForSecondsRealtime(2);
        var original = selectedResolution;
        string[] args = Environment.GetCommandLineArgs();
        int captureIndex = Array.IndexOf(args, "-webfight-capture-dir");
        string captureDirectory = captureIndex >= 0 && captureIndex + 1 < args.Length ? args[captureIndex + 1] : Application.temporaryCachePath;
        Directory.CreateDirectory(captureDirectory);
        bool passed = true;
        var sizes = new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), NativeResolution() };
        StartSession(SessionMode.Training);
        showFps = showPing = true;
        foreach (var size in sizes)
        {
            SelectResolution(size);
            yield return new WaitForSecondsRealtime(2);
            bool correct = Screen.width == size.x && Screen.height == size.y && Screen.fullScreenMode == FullScreenMode.FullScreenWindow;
            passed &= correct;
            Debug.Log("DISPLAY CHECK " + size + " actual=" + Screen.width + "x" + Screen.height + " mode=" + Screen.fullScreenMode + " camera=" + mainCamera.aspect + "/" + mainCamera.orthographicSize + " pass=" + correct);
            var samples = new float[240];
            double sum = 0;
            long previousFrame = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int frame = 0; frame < samples.Length; frame++)
            {
                yield return null;
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                samples[frame] = (float)((now - previousFrame) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                previousFrame = now;
                sum += samples[frame];
            }
            Array.Sort(samples);
            Debug.Log("FRAME CHECK " + size + " avgMs=" + (sum / samples.Length).ToString("0.00") + " medianMs=" + samples[120].ToString("0.00") + " p95Ms=" + samples[228].ToString("0.00") + " p99Ms=" + samples[237].ToString("0.00"));
            ScreenCapture.CaptureScreenshot(Path.Combine(captureDirectory, "standalone-" + size.x + "x" + size.y + ".png"));
            yield return new WaitForSecondsRealtime(0.5f);
        }
        SetMenu(true); settingsOpen = true; settingsTab = "general"; language = 2;
        yield return new WaitForSecondsRealtime(0.5f);
        ScreenCapture.CaptureScreenshot(Path.Combine(captureDirectory, "standalone-japanese-settings.png"));
        yield return new WaitForSecondsRealtime(0.5f);
        ShowResolutionOptions();
        yield return new WaitForSecondsRealtime(0.5f);
        ScreenCapture.CaptureScreenshot(Path.Combine(captureDirectory, "standalone-resolution-options.png"));
        yield return new WaitForSecondsRealtime(0.5f);
        PlayerPrefs.SetInt("WebFight.Width", original.x); PlayerPrefs.SetInt("WebFight.Height", original.y); PlayerPrefs.Save();
        Debug.Log("DISPLAY CHECK COMPLETE pass=" + passed);
        Application.Quit(passed ? 0 : 1);
    }
}
