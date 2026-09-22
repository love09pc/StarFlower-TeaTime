using System;
using System.IO;
using UnityEngine;

public sealed partial class WebFightBootstrap
{
    private byte[] MakePacket(byte kind, Action<BinaryWriter> write)
    {
        using (var stream = new MemoryStream(512))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0x5746);
            writer.Write(ProtocolVersion);
            writer.Write(kind);
            write(writer);
            return stream.ToArray();
        }
    }

    private void SendInputs()
    {
        transport.Send(MakePacket(1, writer => {
            writer.Write(Time.realtimeSinceStartupAsDouble);
            int count = Math.Min(12, inputHistory.Count);
            writer.Write((byte)count);
            // Repeat recent frames; sequence IDs prevent repeated attacks after retransmission.
            for (int i = inputHistory.Count - count; i < inputHistory.Count; i++)
            {
                writer.Write(inputHistory[i].Sequence);
                writer.Write(inputHistory[i].Held);
                writer.Write(inputHistory[i].Edges);
            }
        }));
    }

    private byte[] CreateSnapshot()
    {
        return MakePacket(2, writer => {
            writer.Write(snapshotSequence);
            writer.Write(remoteSequence);
            writer.Write(round);
            writer.Write(inputEchoTime);
            writer.Write(LocalPaused || remotePaused);
            writer.Write(authenticated);
            writer.Write(matchStarted);
            writer.Write((ushort)resumeTicks);
            writer.Write(rematchVotes);
            writer.Write(roomName);
            writer.Write(playerOne.Name);
            writer.Write(playerTwo.Name);
            WriteState(writer, playerOne.State);
            WriteState(writer, playerTwo.State);
        });
    }

    private void SendSnapshot() { transport.Send(CreateSnapshot()); }

    private void SendControl(byte action, bool value)
    {
        if (transport != null) transport.Send(MakePacket(3, writer => { writer.Write(action); writer.Write(value); writer.Write(round); }), true);
    }

    private void SendHello()
    {
        transport.Send(MakePacket(4, writer => { writer.Write(playerName); writer.Write(roomPassword); }), true);
    }

    private void ReceivePacket(byte[] bytes)
    {
        try
        {
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt16() != 0x5746 || reader.ReadByte() != ProtocolVersion) return;
                byte kind = reader.ReadByte();
                if (kind == 1 && mode == SessionMode.Host && authenticated)
                {
                    double echo = reader.ReadDouble();
                    int count = reader.ReadByte();
                    if (count > 12 || stream.Length - stream.Position != count * 8) return;
                    var frames = new InputFrame[count];
                    for (int i = 0; i < count; i++)
                    {
                        frames[i] = new InputFrame { Sequence = reader.ReadUInt32(), Held = reader.ReadUInt16(), Edges = reader.ReadUInt16() };
                        if ((frames[i].Held | frames[i].Edges) > 2047) return;
                        if (i > 0 && frames[i].Sequence <= frames[i - 1].Sequence) return;
                    }
                    foreach (var frame in frames)
                    {
                        if (frame.Sequence <= remoteSequence) continue;
                        remoteHeld = frame.Held;
                        remoteEdges |= frame.Edges;
                        if (playerTwo.State.IsGrabbed && (frame.Edges & 15) != 0 && remoteEscapeEdges.Count < 32)
                            remoteEscapeEdges.Enqueue((ushort)(frame.Edges & 15));
                        remoteSequence = frame.Sequence;
                    }
                    inputEchoTime = echo;
                    lastRemoteInputTime = Time.realtimeSinceStartup;
                }
                else if (kind == 2 && mode == SessionMode.Client)
                {
                    uint sequence = reader.ReadUInt32();
                    uint ack = reader.ReadUInt32();
                    uint incomingRound = reader.ReadUInt32();
                    double echo = reader.ReadDouble();
                    bool paused = reader.ReadBoolean();
                    bool accepted = reader.ReadBoolean();
                    bool started = reader.ReadBoolean();
                    int countdown = reader.ReadUInt16();
                    byte votes = reader.ReadByte();
                    if (countdown > 360 || votes > 3) return;
                    string incomingRoom = reader.ReadString();
                    string nameOne = reader.ReadString();
                    string nameTwo = reader.ReadString();
                    if (incomingRoom.Length > 32 || nameOne.Length > 16 || nameTwo.Length > 16) return;
                    var first = ReadState(reader);
                    var second = ReadState(reader);
                    if (stream.Position != stream.Length || sequence <= receivedSnapshot || !ValidState(first) || !ValidState(second)) return;
                    receivedSnapshot = sequence;
                    authenticated = accepted;
                    matchStarted = started;
                    resumeTicks = countdown; rematchVotes = votes;
                    roomName = incomingRoom;
                    playerOne.Name = nameOne;
                    playerTwo.Name = nameTwo;
                    if (started) uiPage = "game";
                    remotePaused = paused;
                    snapVisuals = round != incomingRound;
                    if (snapVisuals && started) { menuOpen = settingsOpen = false; resumePending = false; }
                    if (snapVisuals) inputHistory.Clear();
                    round = incomingRound;
                    playerOne.State = first;
                    playerTwo.State = second;
                    RestorePairLinks();
                    inputHistory.RemoveAll(frame => frame.Sequence <= ack);
                    if (started && !GameplayPaused && !RoundOver)
                        foreach (var frame in inputHistory) PredictMovement(playerTwo.State, DecodeInput(frame.Held, frame.Edges));
                }
                else if (kind == 3 && mode == SessionMode.Host && authenticated)
                {
                    byte action = reader.ReadByte();
                    bool value = reader.ReadBoolean();
                    uint requestedRound = reader.ReadUInt32();
                    if (stream.Position != stream.Length || requestedRound != round) return;
                    if (action == 1) { remotePaused = value; remoteEdges = remoteHeld = 0; if (value) { resumePending = true; resumeTicks = 0; } }
                    else if (action == 2) ApplyRematchVote(2, value);
                }
                else if (kind == 4 && mode == SessionMode.Host && !authenticated)
                {
                    string name = reader.ReadString();
                    string password = reader.ReadString();
                    if (stream.Position != stream.Length || name.Length == 0 || name.Length > 16 || password.Length > 32) return;
                    if (password != roomPassword)
                    {
                        transport.Send(MakePacket(5, writer => writer.Write("비밀번호가 일치하지 않습니다.")), true);
                    }
                    else { playerTwo.Name = name; authenticated = true; }
                }
                else if (kind == 5 && mode == SessionMode.Client)
                {
                    string reason = reader.ReadString();
                    if (reason.Length <= 100) LeaveSession(reason == "비밀번호가 일치하지 않습니다." ? L(reason, "Incorrect password.") : reason);
                }
                else if ((kind == 6 || kind == 7) && Online && authenticated && transport != null)
                {
                    uint sequence = reader.ReadUInt32();
                    if (stream.Position != stream.Length) return;
                    if (kind == 6) transport.Send(MakePacket(7, writer => writer.Write(sequence)));
                    else AcceptPong(sequence);
                }
                else return;
                lastReceiveTime = Time.realtimeSinceStartup;
            }
        }
        catch (EndOfStreamException) { }
        catch (IOException) { }
    }

    private void RestorePairLinks()
    {
        var one = playerOne.State;
        var two = playerTwo.State;
        if (one.IsGrabbing && two.IsGrabbed && !two.IsGrabbing && !one.IsGrabbed)
        { one.GrabTarget = playerTwo; two.GrabOwner = playerOne; }
        else if (two.IsGrabbing && one.IsGrabbed && !one.IsGrabbing && !two.IsGrabbed)
        { two.GrabTarget = playerOne; one.GrabOwner = playerTwo; }
        else
        {
            one.IsGrabbing = one.IsGrabbed = two.IsGrabbing = two.IsGrabbed = false;
        }
        RebuildBoxes(one);
        RebuildBoxes(two);
    }

    private void RebuildBoxes(FighterState state)
    {
        state.GrabBox = BuildGrabBox(state);
        if (!state.IsGrabbed && !state.IsGrabbing && state.AttackTimer > 0 &&
            ((state.AttackType == AttackType.Normal && state.AttackTimer <= 10) ||
             (state.AttackType == AttackType.Heavy && state.AttackTimer <= 15)))
            BuildAttackBoxes(state, state.AttackType, state.AttackDirection, state.AttackBoxes);
    }

    private static bool ValidState(FighterState state)
    {
        return !float.IsNaN(state.X) && !float.IsNaN(state.Y) && !float.IsNaN(state.Vy) && !float.IsNaN(state.PushVx) &&
            state.X >= 0 && state.X <= WorldWidth && state.Y >= 0 && state.Y <= WorldHeight &&
            Math.Abs(state.Vy) <= 100 && Math.Abs(state.PushVx) <= 100 &&
            (state.FacingDirection == 1 || state.FacingDirection == -1) &&
            state.Hp >= 0 && state.Hp <= 100 && state.MaxHp == 100 && state.MaxGuardHp == 20 &&
            state.GuardHp >= 0 && state.GuardHp <= 20 && state.EscapeStep >= 0 && state.EscapeStep <= 8 &&
            state.PerfectGuardStreak >= 0 && state.PerfectGuardStreak <= 4 && state.UltimateComboTicks >= 0 && state.UltimateComboTicks <= 240 &&
            state.UltimateStage >= 0 && state.UltimateStage <= 2 && state.UltimateReadyTicks >= 0 && state.UltimateReadyTicks <= 180 && state.SpeedBoostTicks >= 0 && state.SpeedBoostTicks <= 600 &&
            state.AttackType <= AttackType.Heavy && state.AttackDirection <= AttackDirection.DownDiagonal;
    }

    private static void WriteState(BinaryWriter writer, FighterState state)
    {
        writer.Write(state.X);
        writer.Write(state.Y);
        writer.Write(state.Vy);
        writer.Write(state.PushVx);
        writer.Write(state.FacingDirection);
        writer.Write((short)state.Hp);
        writer.Write((short)state.MaxHp);
        writer.Write((short)state.GuardHp);
        writer.Write((short)state.MaxGuardHp);
        writer.Write((short)state.GuardHoldTimer);
        writer.Write((short)state.GuardCooldownTimer);
        writer.Write((short)state.GuardRegenTimer);
        writer.Write((short)state.PerfectGuardWindow);
        writer.Write((short)state.AttackTimer);
        writer.Write((short)state.GrabTimer);
        writer.Write((short)state.GrabbedTimer);
        writer.Write((short)state.GrabCooldown);
        writer.Write((short)state.EscapeStep);
        writer.Write((short)state.NormalCooldown);
        writer.Write((short)state.HeavyCooldown);
        writer.Write((short)state.DashCooldown);
        writer.Write((short)state.DashTimer);
        writer.Write((short)state.NormalCombo);
        writer.Write((short)state.NormalComboTimer);
        writer.Write((short)state.HitStun);
        writer.Write((short)state.HitStop);
        writer.Write((short)state.BuffStacks);
        writer.Write((short)state.BuffTimer);
        writer.Write(state.IsLookingUp);
        writer.Write(state.IsLookingDown);
        writer.Write(state.IsMoving);
        writer.Write(state.IsDashing);
        writer.Write(state.HasWallJumped);
        writer.Write(state.HasDoubleJumped);
        writer.Write(state.IsWallSliding);
        writer.Write(state.IsGuarding);
        writer.Write(state.IsGrabbing);
        writer.Write(state.IsGrabbed);
        writer.Write(state.IsWindup);
        writer.Write(state.HasHit);
        writer.Write((byte)state.AttackType);
        writer.Write((byte)state.AttackDirection);
        writer.Write((byte)state.PerfectGuardStreak);
        writer.Write((short)state.UltimateComboTicks);
        writer.Write((byte)state.UltimateStage);
        writer.Write((short)state.UltimateReadyTicks);
        writer.Write((short)state.SpeedBoostTicks);
        writer.Write(state.UltimateAttack);
    }

    private static FighterState ReadState(BinaryReader reader)
    {
        return new FighterState
        {
            X = reader.ReadSingle(),
            Y = reader.ReadSingle(),
            Vy = reader.ReadSingle(),
            PushVx = reader.ReadSingle(),
            FacingDirection = reader.ReadSingle(),
            Hp = reader.ReadInt16(),
            MaxHp = reader.ReadInt16(),
            GuardHp = reader.ReadInt16(),
            MaxGuardHp = reader.ReadInt16(),
            GuardHoldTimer = reader.ReadInt16(),
            GuardCooldownTimer = reader.ReadInt16(),
            GuardRegenTimer = reader.ReadInt16(),
            PerfectGuardWindow = reader.ReadInt16(),
            AttackTimer = reader.ReadInt16(),
            GrabTimer = reader.ReadInt16(),
            GrabbedTimer = reader.ReadInt16(),
            GrabCooldown = reader.ReadInt16(),
            EscapeStep = reader.ReadInt16(),
            NormalCooldown = reader.ReadInt16(),
            HeavyCooldown = reader.ReadInt16(),
            DashCooldown = reader.ReadInt16(),
            DashTimer = reader.ReadInt16(),
            NormalCombo = reader.ReadInt16(),
            NormalComboTimer = reader.ReadInt16(),
            HitStun = reader.ReadInt16(),
            HitStop = reader.ReadInt16(),
            BuffStacks = reader.ReadInt16(),
            BuffTimer = reader.ReadInt16(),
            IsLookingUp = reader.ReadBoolean(),
            IsLookingDown = reader.ReadBoolean(),
            IsMoving = reader.ReadBoolean(),
            IsDashing = reader.ReadBoolean(),
            HasWallJumped = reader.ReadBoolean(),
            HasDoubleJumped = reader.ReadBoolean(),
            IsWallSliding = reader.ReadBoolean(),
            IsGuarding = reader.ReadBoolean(),
            IsGrabbing = reader.ReadBoolean(),
            IsGrabbed = reader.ReadBoolean(),
            IsWindup = reader.ReadBoolean(),
            HasHit = reader.ReadBoolean(),
            AttackType = (AttackType)reader.ReadByte(),
            AttackDirection = (AttackDirection)reader.ReadByte(),
            PerfectGuardStreak = reader.ReadByte(),
            UltimateComboTicks = reader.ReadInt16(),
            UltimateStage = reader.ReadByte(),
            UltimateReadyTicks = reader.ReadInt16(),
            SpeedBoostTicks = reader.ReadInt16(),
            UltimateAttack = reader.ReadBoolean(),
        };
    }
}
