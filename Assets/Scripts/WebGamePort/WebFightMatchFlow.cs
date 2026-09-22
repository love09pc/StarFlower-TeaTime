using UnityEngine;

public sealed partial class WebFightBootstrap
{
    private int resumeTicks;
    private bool resumePending;
    private byte rematchVotes;

    private void AdvanceMatchFlow()
    {
        if (mode == SessionMode.Client || mode == SessionMode.Menu || (Online && !matchStarted)) return;
        if (LocalPaused || remotePaused)
        {
            resumePending = true;
            resumeTicks = 0;
            return;
        }
        if (resumePending) { resumePending = false; resumeTicks = 360; }
        else if (resumeTicks > 0) resumeTicks--;
    }

    private void RequestRematch(bool accept)
    {
        if (!RoundOver) return;
        if (!Online) { BeginRematch(); return; }
        if (mode == SessionMode.Client) SendControl(2, accept);
        else ApplyRematchVote(1, accept);
    }

    private void ApplyRematchVote(byte player, bool accept)
    {
        if (!RoundOver || !authenticated || !matchStarted) return;
        if (!accept) rematchVotes = 0;
        else rematchVotes |= player;
        if (rematchVotes == 3) BeginRematch();
        uiSignature = "";
    }

    private void BeginRematch()
    {
        round++; ResetRound();
        rematchVotes = 0;
        menuOpen = settingsOpen = remotePaused = resumePending = false;
        resumeTicks = 360;
        pendingOne = pendingTwo = remoteHeld = remoteEdges = 0;
        inputHistory.Clear();
        localEscapeEdges.Clear(); remoteEscapeEdges.Clear();
        snapVisuals = true;
        uiSignature = "";
    }
}
