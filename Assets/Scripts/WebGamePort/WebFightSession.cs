using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed partial class WebFightBootstrap
{
    private enum SessionMode { Menu, Training, Local, Host, Client }
    private SessionMode mode;
    private LanFightTransport transport;
    private readonly LanRoomDiscovery discovery = new LanRoomDiscovery();
    private bool menuOpen = true;
    private bool settingsOpen;
    private bool remotePaused;
    private bool showHitboxes;
    private bool snapVisuals;
    private string address = "127.0.0.1";
    private string portText = "7777";
    private string notice = "";
    private string localAddresses = "";
    private Sprite[] idleFrames;
    private FighterInput sampledOne, sampledTwo;
    private ushort pendingOne, pendingTwo, remoteHeld, remoteEdges;
    private uint inputSequence, remoteSequence, snapshotSequence, receivedSnapshot, round;
    private float lastReceiveTime, lastRemoteInputTime;
    private double inputEchoTime;
    private float latencyMs;
    private readonly List<InputFrame> inputHistory = new List<InputFrame>();
    private readonly Queue<ushort> remoteEscapeEdges = new Queue<ushort>();
    private readonly Queue<ushort> localEscapeEdges = new Queue<ushort>();
    private const byte ProtocolVersion = 3;
    private bool matchStarted;
    private bool authenticated;
    private string playerName = "Player 1", roomName = "", roomPassword = "";
    private struct InputFrame
    {
        public uint Sequence;
        public ushort Held, Edges;
    }
    private bool Online => mode == SessionMode.Host || mode == SessionMode.Client;
    private bool LocalPaused => menuOpen || settingsOpen || confirmExit || editingBinding >= 0;
    private bool GameplayPaused => mode == SessionMode.Menu || LocalPaused || remotePaused || resumeTicks > 0 || (Online && !matchStarted);
    private float animationTime;
    private bool RoundOver => mode != SessionMode.Training && (playerOne.State.Hp <= 0 || playerTwo.State.Hp <= 0);
    private string SessionStatus => notice.Length > 0 ? notice :
        mode == SessionMode.Menu ? "" : Online && (transport == null || !transport.Connected) ? "Waiting for opponent" :
        remotePaused ? L("상대방이 일시정지했습니다.", "Opponent paused") : Online ? (mode == SessionMode.Host ? "LAN HOST" : "LAN  " + Mathf.RoundToInt(latencyMs) + " ms") :
        mode == SessionMode.Training ? "TRAINING" : "LOCAL VERSUS";

    private void InitializeSession()
    {
        Application.runInBackground = true;
        InitializeDisplay();
        if (!Application.isEditor && Array.IndexOf(Environment.GetCommandLineArgs(), "-webfight-display-check") >= 0)
            StartCoroutine(CheckStandaloneDisplay());
        QualitySettings.vSyncCount = PlayerPrefs.GetInt("WebFight.VSync", 1);
        Application.targetFrameRate = 120;
        showHitboxes = PlayerPrefs.GetInt("WebFight.Hitboxes", 0) != 0;
        LoadBindings();
        address = PlayerPrefs.GetString("WebFight.Address", "127.0.0.1");
        var sheet = Resources.Load<Texture2D>("WebGamePort/idle-breathing");
        if (sheet != null)
        {
            sheet.filterMode = FilterMode.Point;
            idleFrames = new Sprite[8];
            float width = sheet.width / 8f;
            for (int i = 0; i < 8; i++)
                idleFrames[i] = Sprite.Create(sheet, new Rect(i * width, 0, width, sheet.height), new Vector2(0.5f, 0f), width / 1.5f);
        }
        AttachCharacterArt(playerOne);
        AttachCharacterArt(playerTwo);
        try
        {
            var addresses = new List<string>();
            foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) addresses.Add(ip.ToString());
            localAddresses = string.Join(" / ", addresses);
        }
        catch (SocketException) { localAddresses = ""; }
    }

    private void AttachCharacterArt(Fighter fighter)
    {
        var artObject = new GameObject(fighter.Name + " Character");
        artObject.transform.SetParent(transform);
        fighter.Art = artObject.AddComponent<SpriteRenderer>();
        fighter.Art.sortingOrder = 12;
    }

    private void UpdateCharacterArt(Fighter fighter)
    {
        if (fighter.Art == null || idleFrames == null) return;
        fighter.Art.enabled = !(mode == SessionMode.Training && fighter == playerTwo);
        var state = fighter.State;
        bool idle = state.Y == 0 && !state.IsMoving && !state.IsGuarding && state.AttackTimer == 0;
        fighter.Art.sprite = idleFrames[idle ? (int)(animationTime / 0.85f * 8f) % 8 : 0];
        fighter.Art.flipX = state.FacingDirection < 0;
        fighter.Art.transform.position = fighter.Root.position + new Vector3(0, -0.67f, -0.05f);
        fighter.Art.color = Color.white;
    }

    private void UpdateSession()
    {
        transport?.Pump();
        discovery.Pump(authenticated ? 2 : 1, matchStarted);
        if (mode == SessionMode.Menu && transport != null) { transport.Dispose(); transport = null; }
        if (mode == SessionMode.Client && transport != null && !transport.Connected && Time.realtimeSinceStartup - lastReceiveTime > 12f)
            LeaveSession(L("호스트에 연결할 수 없습니다.", "Host not reachable"));
        if (Online && transport != null && transport.Connected && Time.realtimeSinceStartup - lastReceiveTime > 5f)
        {
            LeaveSession(L("연결 시간이 초과되었습니다.", "Connection timed out"));
        }
        HandleInterfaceInput();
        UpdatePerformanceSample(Time.unscaledDeltaTime);
        AdvanceAnimation(Time.unscaledDeltaTime);
        MaintainFullscreen();
        var first = ReadInput(playerOne.Controls, sampledOne);
        var second = ReadInput(playerTwo.Controls, sampledTwo);
        if (!GameplayPaused)
        {
            pendingOne |= EdgeMask(first);
            pendingTwo |= EdgeMask(second);
            ushort escape = (ushort)(EdgeMask(first) & 15);
            if (mode != SessionMode.Client && playerOne.State.IsGrabbed && escape != 0 && localEscapeEdges.Count < 32) localEscapeEdges.Enqueue(escape);
        }
        sampledOne = first;
        sampledTwo = second;
    }

    private void TickSession()
    {
        UpdatePing();
        if (mode == SessionMode.Menu) return;
        if (Online && (transport == null || !transport.Connected)) return;
        AdvanceMatchFlow();
        if (mode == SessionMode.Client)
        {
            if (!authenticated) return;
            var frame = new InputFrame { Sequence = ++inputSequence, Held = GameplayPaused ? (ushort)0 : HeldMask(sampledOne), Edges = GameplayPaused ? (ushort)0 : pendingOne };
            pendingOne = 0;
            inputHistory.Add(frame);
            if (inputHistory.Count > 128) inputHistory.RemoveAt(0);
            SendInputs();
            if (!GameplayPaused && !RoundOver) PredictMovement(playerTwo.State, DecodeInput(frame.Held, frame.Edges));
            return;
        }

        if (!GameplayPaused && !RoundOver)
        {
            var first = DecodeInput(HeldMask(sampledOne), pendingOne);
            bool localEscape = playerOne.State.IsGrabbed && localEscapeEdges.Count > 0;
            if (playerOne.State.IsGrabbed)
            {
                var escape = DecodeInput(0, localEscape ? localEscapeEdges.Peek() : (ushort)0);
                first.JustUp = escape.JustUp; first.JustLeft = escape.JustLeft;
                first.JustDown = escape.JustDown; first.JustRight = escape.JustRight;
            }
            var second = mode == SessionMode.Host ? DecodeInput(remoteHeld, remoteEdges) :
                mode == SessionMode.Training ? default : DecodeInput(HeldMask(sampledTwo), pendingTwo);
            bool queuedEscape = mode == SessionMode.Host && playerTwo.State.IsGrabbed && remoteEscapeEdges.Count > 0;
            if (mode == SessionMode.Host && playerTwo.State.IsGrabbed)
            {
                var escape = DecodeInput(0, queuedEscape ? remoteEscapeEdges.Peek() : (ushort)0);
                second.JustUp = escape.JustUp; second.JustLeft = escape.JustLeft;
                second.JustDown = escape.JustDown; second.JustRight = escape.JustRight;
            }
            pendingOne = pendingTwo = remoteEdges = 0;
            if (mode == SessionMode.Host && Time.realtimeSinceStartup - lastRemoteInputTime > 0.25f) second = default;
            Simulate(first, second);
            if (localEscape && playerOne.State.ConsumedEscapeInput) localEscapeEdges.Dequeue();
            if (!playerOne.State.IsGrabbed) localEscapeEdges.Clear();
            if (queuedEscape && playerTwo.State.ConsumedEscapeInput) remoteEscapeEdges.Dequeue();
            if (!playerTwo.State.IsGrabbed) remoteEscapeEdges.Clear();
        }
        else pendingOne = pendingTwo = remoteEdges = 0;
        if (mode == SessionMode.Host && ++snapshotSequence % 2 == 0) SendSnapshot();
    }

    private void Simulate(FighterInput first, FighterInput second)
    {
        TickUltimateTimers(playerOne.State); TickUltimateTimers(playerTwo.State);
        playerOne.State.GrabRequested = playerTwo.State.GrabRequested = false;
        playerOne.State.ContactReady = playerTwo.State.ContactReady = false;
        playerOne.State.ConsumedEscapeInput = playerTwo.State.ConsumedEscapeInput = false;
        // Collect both players' contacts before applying either hit; player order cannot win a clash.
        deferCombatContacts = true;
        StepFighter(playerOne, playerTwo, first);
        StepFighter(playerTwo, playerOne, second);
        deferCombatContacts = false;
        ResolvePairContacts(first, second);
        SyncGrabPair(playerOne);
        SyncGrabPair(playerTwo);
        // Refill only HP so training combos, throws and held grabs remain uninterrupted.
        if (mode == SessionMode.Training && playerTwo.State.Hp <= 0)
            playerTwo.State.Hp = playerTwo.State.MaxHp;
        if (RoundOver)
        {
            ClearGrabbedState(playerOne.State);
            ClearGrabbedState(playerTwo.State);
        }
    }

    private void SyncGrabPair(Fighter owner)
    {
        var state = owner.State;
        if (!state.IsGrabbing) return;
        var target = state.GrabTarget;
        if (target == null || !target.State.IsGrabbed || target.State.GrabOwner != owner)
        {
            EndOwnerGrab(state);
            state.GrabCooldown = 60;
            return;
        }
        HoldTarget(owner, target, state.GrabTimer);
        ClampState(target.State);
    }

    private void PredictMovement(FighterState state, FighterInput input)
    {
        if (state.IsGrabbed || state.IsGrabbing) return;
        if (state.HitStop > 0) { state.HitStop--; return; }
        state.HitStun = Math.Max(0, state.HitStun - 1);
        state.DashCooldown = Math.Max(0, state.DashCooldown - 1);
        bool wasDash = state.IsDashing;
        state.DashTimer = Math.Max(0, state.DashTimer - 1);
        state.IsDashing = state.DashTimer > 0;
        if (wasDash && !state.IsDashing) { if (state.Vy > 0) state.Vy = 0; state.PushVx *= 0.3f; }
        int side = (input.Right ? 1 : 0) - (input.Left ? 1 : 0);
        if (state.HitStun == 0 && !state.IsDashing && !state.IsGuarding && side != 0) state.FacingDirection = side;
        if (state.HitStun == 0 && state.AttackTimer == 0 && !state.IsDashing && input.JustDash && state.DashCooldown == 0)
            StartDash(state, input, side);
        StepMovement(state, input, side, state.HitStun > 0, state.AttackTimer > 0);
        ClampState(state);
    }

    private static ushort HeldMask(FighterInput input)
    {
        return (ushort)((input.Left ? 1 : 0) | (input.Right ? 2 : 0) | (input.Up ? 4 : 0) | (input.Down ? 8 : 0) |
            (input.Jump ? 16 : 0) | (input.Dash ? 32 : 0) | (input.Guard ? 64 : 0) | (input.Grab ? 128 : 0) |
            (input.Normal ? 256 : 0) | (input.Heavy ? 512 : 0) | (input.Ultimate ? 1024 : 0));
    }

    private static ushort EdgeMask(FighterInput input)
    {
        return (ushort)((input.JustLeft ? 1 : 0) | (input.JustRight ? 2 : 0) | (input.JustUp ? 4 : 0) | (input.JustDown ? 8 : 0) |
            (input.JustJump ? 16 : 0) | (input.JustDash ? 32 : 0) | (input.JustGuard ? 64 : 0) | (input.JustGrab ? 128 : 0) |
            (input.JustNormal ? 256 : 0) | (input.JustHeavy ? 512 : 0) | (input.JustUltimate ? 1024 : 0));
    }

    private static FighterInput DecodeInput(ushort held, ushort edges)
    {
        return new FighterInput {
            Left = (held & 1) != 0, Right = (held & 2) != 0, Up = (held & 4) != 0, Down = (held & 8) != 0,
            Jump = (held & 16) != 0, Dash = (held & 32) != 0, Guard = (held & 64) != 0, Grab = (held & 128) != 0,
            Normal = (held & 256) != 0, Heavy = (held & 512) != 0, Ultimate = (held & 1024) != 0,
            JustLeft = (edges & 1) != 0, JustRight = (edges & 2) != 0, JustUp = (edges & 4) != 0, JustDown = (edges & 8) != 0,
            JustJump = (edges & 16) != 0, JustDash = (edges & 32) != 0, JustGuard = (edges & 64) != 0, JustGrab = (edges & 128) != 0,
            JustNormal = (edges & 256) != 0, JustHeavy = (edges & 512) != 0, JustUltimate = (edges & 1024) != 0
        };
    }

    private void StartSession(SessionMode next)
    {
        transport?.Dispose();
        discovery.Dispose();
        transport = null;
        mode = next;
        resumeTicks = 0; resumePending = false; rematchVotes = 0;
        ResetPing();
        animationTime = 0;
        settingsOpen = false;
        menuOpen = false;
        remotePaused = false;
        notice = "";
        round = inputSequence = remoteSequence = snapshotSequence = receivedSnapshot = 0;
        remoteHeld = remoteEdges = pendingOne = pendingTwo = 0;
        inputHistory.Clear();
        remoteEscapeEdges.Clear();
        localEscapeEdges.Clear();
        authenticated = !Online;
        matchStarted = !Online;
        uiPage = Online ? "lobby" : "game";
        ResetRound();
        playerOne.Name = next == SessionMode.Host ? playerName : "Player 1";
        playerTwo.Name = next == SessionMode.Client ? playerName : next == SessionMode.Training ? "Dummy" : "Player 2";
        if (!Online) return;
        if (!ushort.TryParse(portText, out var port) || port == 0) { LeaveSession(L("포트 번호가 올바르지 않습니다.", "Invalid port")); return; }
        transport = new LanFightTransport();
        transport.Received = ReceivePacket;
        transport.Left = () => {
            if (mode != SessionMode.Host) { LeaveSession(L("방장이 나가 방이 종료되었습니다.", "The host left the room.")); return; }
            authenticated = matchStarted = remotePaused = false;
            ResetPing();
            remoteSequence = 0; remoteHeld = remoteEdges = 0;
            menuOpen = false; uiPage = "lobby"; ResetRound();
        };
        transport.Joined = () => {
            lastReceiveTime = lastRemoteInputTime = Time.realtimeSinceStartup;
            if (mode == SessionMode.Host) ResetRound();
            else SendHello();
        };
        lastReceiveTime = Time.realtimeSinceStartup;
        try
        {
            if (next == SessionMode.Host) { transport.Host(port); discovery.StartHost(roomName, port, roomPassword.Length > 0); }
            else { transport.Join(address.Trim(), port); PlayerPrefs.SetString("WebFight.Address", address.Trim()); }
        }
        catch (Exception error) { LeaveSession(error.Message); }
    }

    private void LeaveSession(string reason = "")
    {
        // Disposal is deferred until after Pump returns, including disconnect callbacks.
        mode = SessionMode.Menu;
        resumeTicks = 0; resumePending = false; rematchVotes = 0;
        ResetPing();
        settingsOpen = false;
        confirmExit = false;
        uiPage = "main";
        menuOpen = true;
        remotePaused = false;
        notice = reason;
        discovery.Dispose();
        pendingOne = pendingTwo = remoteEdges = remoteHeld = 0;
        remoteEscapeEdges.Clear();
        authenticated = matchStarted = false;
        ClearGrabbedState(playerOne.State);
        ClearGrabbedState(playerTwo.State);
    }

    private void SetMenu(bool open)
    {
        localEscapeEdges.Clear(); remoteEscapeEdges.Clear();
        if (open) { resumePending = true; resumeTicks = 0; }
        menuOpen = open;
        settingsOpen = false;
        pendingOne = pendingTwo = remoteEdges = 0;
        if (mode == SessionMode.Client) SendControl(1, open);
    }

    private void OnDestroy() { transport?.Dispose(); discovery.Dispose(); }

}
