#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class WebFightBootstrap
{
    public string VerificationResult { get; private set; } = "Not run";

    public string RunCombatChecks()
    {
        var savedOne = playerOne.State;
        var savedTwo = playerTwo.State;
        var savedMode = mode;
        var savedRemote = remoteSequence;
        var savedHeld = remoteHeld;
        var savedEdges = remoteEdges;
        var savedReceived = receivedSnapshot;
        var savedRound = round;
        var savedMenu = menuOpen;
        var savedPaused = remotePaused;
        var savedAuthenticated = authenticated;
        var passed = new List<string>();
        Action<bool, string> check = (condition, name) => {
            if (!condition) throw new InvalidOperationException("FAIL: " + name);
            passed.Add(name);
        };
        Action setup = () => {
            playerOne.State = FighterState.Create(220, 1);
            playerTwo.State = FighterState.Create(280, -1);
        };
        var hold = new FighterInput { Grab = true };
        var start = new FighterInput { Grab = true, JustGrab = true };
        try
        {
            mode = SessionMode.Local;
            int imeCaret = 99, imeSelection = 2;
            WebFightInputField.NormalizeCaretForIme("한", 5, ref imeCaret, ref imeSelection);
            check(imeCaret == 2 && imeSelection == 2, "IME input clamps stale selection");

            setup(); Simulate(start, default);
            check(playerOne.State.IsGrabbing && playerTwo.State.GrabOwner == playerOne, "P1 grab links");
            check(playerOne.State.GrabTimer == 179 && playerTwo.State.GrabbedTimer == 179, "single grab timer tick");
            for (int i = 0; i < 20; i++) Simulate(new FighterInput { Grab = true, Right = true }, default);
            check(Mathf.Approximately(playerTwo.State.X, playerOne.State.X + GrabOffset), "moving grab stays attached");
            Simulate(default, default);
            check(!playerOne.State.IsGrabbing && !playerTwo.State.IsGrabbed && playerTwo.State.GrabOwner == null, "release clears both ends");

            setup(); Simulate(default, start);
            check(playerTwo.State.IsGrabbing && playerOne.State.GrabOwner == playerTwo, "P2 grab links");
            for (int i = 0; i < 180; i++) Simulate(default, hold);
            check(!playerOne.State.IsGrabbed && !playerTwo.State.IsGrabbing, "P2 timeout clears pair");

            setup(); Simulate(start, start);
            check(!playerOne.State.IsGrabbing && !playerTwo.State.IsGrabbing, "simultaneous grabs cancel without a cycle");
            check(!(playerOne.State.IsGrabbed && playerOne.State.IsGrabbing), "owner cannot also be target");

            setup(); Simulate(new FighterInput { Grab = true, JustGrab = true, JustNormal = true, JustHeavy = true }, default);
            check(playerOne.State.AttackTimer == 0 && playerTwo.State.Hp == 96, "grab attack damages exactly once");

            setup(); Simulate(start, default);
            for (int i = 0; i < 8; i++) Simulate(hold, DecodeInput(0, (ushort)(i % 4 == 0 ? 4 : i % 4 == 1 ? 1 : i % 4 == 2 ? 8 : 2)));
            check(!playerTwo.State.IsGrabbed && !playerOne.State.IsGrabbing, "eight-step escape clears pair");

            setup(); Simulate(default, start);
            for (int i = 0; i < 8; i++) Simulate(DecodeInput(0, (ushort)(i % 4 == 0 ? 4 : i % 4 == 1 ? 1 : i % 4 == 2 ? 8 : 2)), hold);
            check(!playerOne.State.IsGrabbed && !playerTwo.State.IsGrabbing, "P1 escape works against P2");

            setup(); Simulate(start, default);
            Simulate(new FighterInput { Grab = true, JustHeavy = true, Up = true }, default);
            check(!playerTwo.State.IsGrabbed && !playerOne.State.IsGrabbing && playerTwo.State.Hp == 92 && playerTwo.State.Vy > 0, "heavy throw releases with velocity");

            setup(); playerTwo.State.IsGuarding = true; TryStartGrab(playerOne, playerTwo);
            check(!playerOne.State.IsGrabbing, "front guard blocks grab");
            playerOne.State.GrabCooldown = 0; playerTwo.State.FacingDirection = 1; TryStartGrab(playerOne, playerTwo);
            check(playerTwo.State.IsGrabbed, "rear grab bypasses guard");

            setup(); playerTwo.State.AttackTimer = 10; playerTwo.State.IsDashing = true; playerTwo.State.DashTimer = 12;
            TryStartGrab(playerOne, playerTwo);
            check(playerTwo.State.AttackTimer == 0 && !playerTwo.State.IsDashing, "capture cancels target action");
            ClearGrabbedState(playerTwo.State);
            check(!playerOne.State.IsGrabbing && playerOne.State.GrabTarget == null, "interrupted hold clears owner");

            setup(); playerOne.State.X = WorldWidth - PlayerWidth - 60; playerTwo.State.X = WorldWidth - PlayerWidth;
            Simulate(start, default);
            for (int i = 0; i < 30; i++) Simulate(new FighterInput { Grab = true, Right = true }, default);
            check(playerTwo.State.X <= WorldWidth - PlayerWidth, "held target stays within wall");

            setup(); Simulate(start, default); playerTwo.State.Hp = 0; Simulate(hold, default);
            check(!playerOne.State.IsGrabbing && !playerTwo.State.IsGrabbed, "KO releases pair");
            check(RoundOver && playerTwo.State.Hp == 0, "versus KO is not refilled");

            mode = SessionMode.Training;
            setup(); playerTwo.State.Hp = 1;
            Simulate(new FighterInput { JustNormal = true }, default);
            for (int i = 0; i < 60; i++) Simulate(default, default);
            check(playerTwo.State.Hp == playerTwo.State.MaxHp && !RoundOver, "training normal KO refills dummy");

            setup(); playerTwo.State.Hp = 1;
            Simulate(new FighterInput { Grab = true, JustGrab = true, JustNormal = true }, default);
            check(playerTwo.State.Hp == playerTwo.State.MaxHp && !RoundOver, "training grab KO refills dummy");
            check(playerOne.State.IsGrabbing && playerTwo.State.GrabOwner == playerOne, "training refill preserves held grab");
            for (int i = 0; i < 25; i++) Simulate(hold, default);
            Simulate(new FighterInput { Grab = true, JustNormal = true }, default);
            check(playerTwo.State.Hp > 0 && playerTwo.State.Hp < playerTwo.State.MaxHp, "training continues taking damage after refill");
            playerTwo.State.Hp = 1;
            Simulate(new FighterInput { Grab = true, JustHeavy = true, Up = true }, default);
            check(playerTwo.State.Hp == playerTwo.State.MaxHp && playerTwo.State.Vy > 0 && !playerTwo.State.IsGrabbed, "training lethal throw preserves launch");
            UpdateHud();
            check(!labelOne.gameObject.activeSelf && !labelTwo.gameObject.activeSelf, "training hides overlapping world labels");
            check(statusText.text == SessionStatus && playerTwo.HpFill.fillAmount == 1f, "training HUD shows full HP without winner");

            foreach (var fighter in new[] { playerOne, playerTwo })
            {
                fighter.State.IsGuarding = true; fighter.State.HitStop = 0;
                UpdateCharacterArt(fighter);
                check(fighter.Art.color == Color.white, fighter.Name + " guard keeps original sprite color");
                fighter.State.IsGuarding = false; fighter.State.HitStop = 8;
                UpdateCharacterArt(fighter);
                check(fighter.Art.color == Color.white, fighter.Name + " hit stop keeps original sprite color");
            }
            mode = SessionMode.Local;

            setup(); Simulate(start, default);
            using (var stream = new System.IO.MemoryStream())
            {
                var writer = new System.IO.BinaryWriter(stream); WriteState(writer, playerTwo.State); writer.Flush(); stream.Position = 0;
                var restored = ReadState(new System.IO.BinaryReader(stream));
                check(restored.X == playerTwo.State.X && restored.IsGrabbed && restored.GrabbedTimer == playerTwo.State.GrabbedTimer, "binary state round trip");
                check(stream.Length < 128, "state packet stays compact");
            }
            mode = SessionMode.Host; authenticated = true; remoteSequence = 0; remoteEdges = remoteHeld = 0;
            var inputPacket = MakePacket(1, writer => { writer.Write(1.0); writer.Write((byte)2); writer.Write(1u); writer.Write((ushort)128); writer.Write((ushort)128); writer.Write(2u); writer.Write((ushort)128); writer.Write((ushort)0); });
            ReceivePacket(inputPacket);
            check(remoteSequence == 2 && remoteEdges == 128, "redundant packet recovers lost grab press");
            remoteEdges = 0; ReceivePacket(inputPacket);
            check(remoteEdges == 0, "duplicate input does not replay grab");
            ReceivePacket(new byte[] { 0x46, 0x57, ProtocolVersion, 1 });
            check(remoteSequence == 2, "truncated packet ignored atomically");

            var snapshot = CreateSnapshot();
            // Give the test a fresh monotonically increasing snapshot sequence.
            snapshot[4] = 10; snapshot[5] = snapshot[6] = snapshot[7] = 0;
            mode = SessionMode.Client; receivedSnapshot = 0; menuOpen = true;
            ReceivePacket(snapshot);
            check(playerOne.State.GrabTarget == playerTwo && playerTwo.State.GrabOwner == playerOne, "snapshot restores grab links");
            playerOne.State.Hp = 77; ReceivePacket(snapshot);
            check(playerOne.State.Hp == 77, "stale snapshot ignored");
            check(snapshot.Length < 1200, "snapshot fits one datagram");
            return passed.Count + " combat/protocol checks passed: " + string.Join(", ", passed);
        }
        finally
        {
            playerOne.State = savedOne; playerTwo.State = savedTwo; mode = savedMode;
            remoteSequence = savedRemote; remoteHeld = savedHeld; remoteEdges = savedEdges;
            receivedSnapshot = savedReceived; round = savedRound; menuOpen = savedMenu; remotePaused = savedPaused;
            authenticated = savedAuthenticated;
            UpdateFighterVisual(playerOne); UpdateFighterVisual(playerTwo); UpdateHud();
        }
    }

    public void BeginVerification()
    {
        VerificationResult = RunCombatChecks() + " | " + RunPresentationChecks() + " | " + RunRuleChecks();
        StartCoroutine(CheckLoopback());
    }

    private IEnumerator CheckLoopback()
    {
        using (var host = new LanFightTransport())
        using (var client = new LanFightTransport())
        {
            bool gotInput = false, gotSnapshot = false, gotControl = false;
            host.Received = bytes => { if (bytes.Length == 2 && bytes[0] == 42) gotInput = true; if (bytes.Length == 2 && bytes[0] == 99) gotControl = true; };
            client.Received = bytes => { if (bytes.Length == 2 && bytes[0] == 24) gotSnapshot = true; };
            host.Host(17777); client.Join("127.0.0.1", 17777);
            float deadline = Time.realtimeSinceStartup + 8;
            while (Time.realtimeSinceStartup < deadline)
            {
                host.Pump(); client.Pump();
                if (host.Connected && client.Connected)
                {
                    client.Send(new byte[] { 42, 1 });
                    client.Send(new byte[] { 99, 1 }, true);
                    host.Send(new byte[] { 24, 1 });
                }
                if (gotInput && gotSnapshot && gotControl)
                {
                    VerificationResult += " | UDP loopback connection, input, snapshot and reliable control passed.";
                    Debug.Log(VerificationResult);
                    StartCoroutine(CheckSessionLoopback());
                    yield break;
                }
                yield return null;
            }
            VerificationResult += " | FAIL: UDP loopback timed out";
            Debug.LogError(VerificationResult);
        }
    }

    private IEnumerator CheckSessionLoopback()
    {
        string oldPort = portText;
        string oldName = playerName;
        string oldRoom = roomName;
        string oldPassword = roomPassword;
        string oldAddressPreference = PlayerPrefs.GetString("WebFight.Address", "127.0.0.1");
        var guestObject = new GameObject("Verification Guest");
        var guest = guestObject.AddComponent<WebFightBootstrap>();
        guest.enabled = false;
        enabled = false;
        using (var search = new LanRoomDiscovery())
        try
        {
            portText = "17779"; playerName = "HostTest"; roomName = "LAN Test"; roomPassword = "test";
            StartSession(SessionMode.Host);
            search.StartSearch();
            float deadline = Time.realtimeSinceStartup + 4;
            while (search.Rooms.Count == 0 && Time.realtimeSinceStartup < deadline)
            {
                discovery.Pump(); search.Pump(); yield return null;
            }
            bool discovered = search.Rooms.Exists(room => room.port == 17779 && room.name == "LAN Test" && room.locked);
            guest.portText = "17779"; guest.address = "127.0.0.1"; guest.playerName = "GuestTest"; guest.roomPassword = "test";
            guest.StartSession(SessionMode.Client);
            deadline = Time.realtimeSinceStartup + 8;
            while ((!authenticated || !guest.authenticated) && Time.realtimeSinceStartup < deadline)
            {
                transport.Pump(); guest.transport.Pump(); TickSession(); guest.TickSession(); yield return null;
            }
            bool joined = authenticated && guest.authenticated && playerTwo.Name == "GuestTest";
            int inputPackets = 0, statePackets = 0;
            transport.Received = packet => { if (packet.Length > 3 && packet[3] == 1 && ++inputPackets % 3 == 1) return; ReceivePacket(packet); };
            guest.transport.Received = packet => { if (packet.Length > 3 && packet[3] == 2 && ++statePackets % 4 == 1) return; guest.ReceivePacket(packet); };
            matchStarted = true; uiPage = "game"; round++;
            playerOne.State = FighterState.Create(220, 1); playerTwo.State = FighterState.Create(280, -1);
            for (int i = 0; i < 12; i++)
            { transport.Pump(); guest.transport.Pump(); TickSession(); guest.TickSession(); yield return null; }
            guest.sampledOne = new FighterInput { Grab = true }; guest.pendingOne = 128;
            for (int i = 0; i < 35; i++)
            { transport.Pump(); guest.transport.Pump(); TickSession(); guest.TickSession(); yield return null; }
            bool grabbed = playerTwo.State.IsGrabbing && playerOne.State.GrabOwner == playerTwo && guest.playerTwo.State.IsGrabbing && guest.playerOne.State.GrabOwner == guest.playerTwo;
            guest.sampledOne = new FighterInput { Grab = true, Heavy = true, Up = true }; guest.pendingOne = 512;
            for (int i = 0; i < 35; i++)
            { transport.Pump(); guest.transport.Pump(); TickSession(); guest.TickSession(); yield return null; }
            bool threw = !playerTwo.State.IsGrabbing && !playerOne.State.IsGrabbed && playerOne.State.Hp == 92 && guest.playerOne.State.Hp == 92 && !guest.playerOne.State.IsGrabbed;
            guest.SendControl(1, true);
            for (int i = 0; i < 10; i++)
            { transport.Pump(); guest.transport.Pump(); TickSession(); guest.TickSession(); yield return null; }
            bool paused = remotePaused && guest.remotePaused;
            deadline = Time.realtimeSinceStartup + 4;
            while ((!hasPing || !guest.hasPing) && Time.realtimeSinceStartup < deadline)
            { transport.Pump(); guest.transport.Pump(); TickSession(); guest.TickSession(); yield return null; }
            bool ping = hasPing && guest.hasPing && latencyMs >= 0 && guest.latencyMs >= 0;
            guest.SetMenu(false);
            for (int i = 0; i < 20; i++)
            { transport.Pump(); guest.transport.Pump(); TickSession(); guest.TickSession(); yield return null; }
            bool countdown = resumeTicks > 0 && guest.resumeTicks > 0 && GameplayPaused && guest.GameplayPaused;
            for (int i = 0; i < 370; i++)
            { transport.Pump(); guest.transport.Pump(); TickSession(); guest.TickSession(); yield return null; }
            countdown &= resumeTicks == 0 && guest.resumeTicks == 0;
            playerTwo.State.Hp = 0;
            for (int i = 0; i < 10; i++)
            { transport.Pump(); guest.transport.Pump(); TickSession(); guest.TickSession(); yield return null; }
            uint previousRound = round;
            guest.RequestRematch(true);
            for (int i = 0; i < 12; i++)
            { transport.Pump(); guest.transport.Pump(); TickSession(); guest.TickSession(); yield return null; }
            bool consent = round == previousRound && rematchVotes == 2 && RoundOver && guest.RoundOver;
            RequestRematch(true);
            for (int i = 0; i < 12; i++)
            { transport.Pump(); guest.transport.Pump(); TickSession(); guest.TickSession(); yield return null; }
            consent &= round == previousRound + 1 && guest.round == round && !RoundOver && !guest.RoundOver && resumeTicks > 0 && guest.resumeTicks > 0;
            bool passed = discovered && joined && grabbed && threw && paused && ping && countdown && consent;
            VerificationResult += " | " + (passed ? "PASS" : "FAIL") + " full LAN session: discovery=" + discovered + ", password/name handshake=" + joined + ", remote grab=" + grabbed + ", throw once=" + threw + ", pause=" + paused + ", host/client ping=" + ping + ", countdown=" + countdown + ", rematch consent=" + consent + " (33% input / 25% snapshot loss injected).";
            if (passed) Debug.Log(VerificationResult); else Debug.LogError(VerificationResult);
        }
        finally
        {
            guest.transport?.Dispose();
            Destroy(guestObject);
            LeaveSession(); transport?.Dispose(); transport = null;
            portText = oldPort; playerName = oldName; roomName = oldRoom; roomPassword = oldPassword;
            PlayerPrefs.SetString("WebFight.Address", oldAddressPreference);
            ResetRound(); enabled = true;
        }
    }
}
#endif
