using UnityEngine;
using UnityEngine.UI;

public sealed partial class WebFightBootstrap
{
    private Text fpsText, pingText;
    private int fpsFrames, sampledFps;
    private float fpsElapsed;
    private uint pingSequence;
    private bool pingPending, hasPing;
    private double pingSentAt, nextPingAt, lastPongAt;

    private void AdvanceAnimation(float delta)
    {
        if (!GameplayPaused && !RoundOver) animationTime += delta;
    }

    private void UpdatePerformanceSample(float delta)
    {
        fpsFrames++;
        fpsElapsed += delta;
        if (fpsElapsed < 0.25f) return;
        sampledFps = Mathf.RoundToInt(fpsFrames / fpsElapsed);
        fpsFrames = 0; fpsElapsed = 0;
    }

    private void UpdatePerformanceHud()
    {
        bool right = mode == SessionMode.Client;
        bool training = mode == SessionMode.Training;
        var parent = right ? hudTwo : hudOne;
        if (fpsText.transform.parent != parent)
        {
            fpsText.transform.SetParent(parent, false);
            pingText.transform.SetParent(parent, false);
        }
        float width = parent.sizeDelta.x;
        float top = training ? 80 : 106;
        fpsText.alignment = pingText.alignment = right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
        fpsText.gameObject.SetActive(showFps);
        pingText.gameObject.SetActive(showPing);
        Place(fpsText.rectTransform, 0, top, width, 20);
        Place(pingText.rectTransform, 0, top + (showFps ? 22 : 0), width, 20);
        fpsText.text = sampledFps > 0 ? "FPS: " + sampledFps : "FPS: --";
        bool fresh = Online && authenticated && hasPing && Time.realtimeSinceStartupAsDouble - lastPongAt < 3;
        pingText.text = fresh ? "PING: " + Mathf.RoundToInt(latencyMs) + " ms" : "PING: --";
    }

    private void ResetPing()
    {
        pingPending = hasPing = false;
        latencyMs = 0;
        nextPingAt = lastPongAt = 0;
    }

    private void UpdatePing()
    {
        if (!Online || !authenticated || transport == null || !transport.Connected) return;
        double now = Time.realtimeSinceStartupAsDouble;
        if (now < nextPingAt) return;
        nextPingAt = now + 1;
        pingSentAt = now;
        pingPending = true;
        pingSequence++;
        transport.Send(MakePacket(6, writer => writer.Write(pingSequence)));
    }

    private void AcceptPong(uint sequence)
    {
        if (!pingPending || sequence != pingSequence) return;
        double now = Time.realtimeSinceStartupAsDouble;
        float sample = (float)((now - pingSentAt) * 1000);
        if (sample < 0 || sample > 3000) return;
        latencyMs = hasPing ? Mathf.Lerp(latencyMs, sample, 0.25f) : sample;
        lastPongAt = now;
        hasPing = true; pingPending = false;
    }
}
