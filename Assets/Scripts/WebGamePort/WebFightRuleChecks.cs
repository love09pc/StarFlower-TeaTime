#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed partial class WebFightBootstrap
{
    public string RunRuleChecks()
    {
        var oldOne = playerOne.State; var oldTwo = playerTwo.State; var oldMode = mode;
        bool oldMenu = menuOpen, oldRemote = remotePaused, oldAuth = authenticated, oldMatch = matchStarted, oldResume = resumePending;
        int oldCountdown = resumeTicks; byte oldVotes = rematchVotes; uint oldRound = round;
        var passed = new List<string>();
        Action<bool, string> check = (ok, message) => { if (!ok) throw new InvalidOperationException("FAIL: " + message); passed.Add(message); };
        Action setup = () => {
            playerOne.State = FighterState.Create(220, 1); playerTwo.State = FighterState.Create(280, -1);
            mode = SessionMode.Local; menuOpen = remotePaused = resumePending = false; resumeTicks = 0;
        };
        Func<Fighter, int, FighterInput> prepare = (fighter, kind) => {
            if (kind == 2) return new FighterInput { Grab = true, JustGrab = true };
            StartAttack(fighter.State, kind == 0 ? AttackType.Normal : AttackType.Heavy, false);
            fighter.State.AttackTimer = kind == 0 ? 11 : 16;
            return default;
        };
        try
        {
            foreach (bool alreadyGuarding in new[] { false, true })
            for (int action = 0; action < 5; action++)
            {
                setup();
                var state = playerOne.State;
                state.IsGuarding = alreadyGuarding; state.GuardHoldTimer = 600; state.UltimateReadyTicks = 180;
                var input = new FighterInput {
                    Guard = true, JustGuard = !alreadyGuarding,
                    JustNormal = action == 0, JustHeavy = action == 1, JustDash = action == 2,
                    Grab = action == 3, JustGrab = action == 3, JustUltimate = action == 4
                };
                Simulate(input, default);
                check(state.IsGuarding && state.AttackTimer == 0 && !state.IsDashing && !state.IsGrabbing && !state.GrabRequested &&
                    state.NormalCooldown == 0 && state.HeavyCooldown == 0 && state.DashCooldown == 0 && state.GrabCooldown == 0 &&
                    state.SpeedBoostTicks == 0 && state.UltimateReadyTicks == 179 && playerTwo.State.Hp == 100,
                    "guard blocks action " + action + " existing=" + alreadyGuarding);
            }
            setup(); Simulate(new FighterInput { Guard = true, JustGuard = true }, default);
            Simulate(new FighterInput { JustNormal = true }, default);
            check(!playerOne.State.IsGuarding && playerOne.State.AttackTimer > 0, "releasing guard permits new attack");
            foreach (bool secondOwns in new[] { false, true })
            {
                setup();
                var grab = new FighterInput { Grab = true, JustGrab = true };
                Simulate(secondOwns ? default : grab, secondOwns ? grab : default);
                var owner = secondOwns ? playerTwo.State : playerOne.State;
                var target = secondOwns ? playerOne.State : playerTwo.State;
                var holdGuard = new FighterInput { Grab = true, Guard = true, JustGuard = true };
                Simulate(holdGuard, holdGuard);
                check(owner.IsGrabbing && target.IsGrabbed && !owner.IsGuarding && !target.IsGuarding &&
                    owner.PerfectGuardWindow == 0 && target.PerfectGuardWindow == 0, "neither grab participant can guard owner2=" + secondOwns);
                owner.IsGuarding = target.IsGuarding = true;
                owner.PerfectGuardWindow = target.PerfectGuardWindow = 24;
                owner.GuardHoldTimer = target.GuardHoldTimer = 600;
                owner.HitStop = target.HitStop = 5;
                Simulate(holdGuard, holdGuard);
                check(!owner.IsGuarding && !target.IsGuarding && owner.GuardHoldTimer == 0 && target.GuardHoldTimer == 0 &&
                    owner.PerfectGuardWindow == 0 && target.PerfectGuardWindow == 0, "hitstop cannot preserve grabbed guard owner2=" + secondOwns);
            }
            for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++)
            {
                setup(); var first = prepare(playerOne, a); var second = prepare(playerTwo, b);
                Simulate(first, second);
                if (a == b)
                {
                    check(playerOne.State.Hp == 100 && playerTwo.State.Hp == 100 && !playerOne.State.IsGrabbing && !playerTwo.State.IsGrabbing, "equal clash " + a);
                    float push = a == 0 ? 0 : a == 1 ? 18 : 9;
                    check(playerOne.State.PushVx == -push && playerTwo.State.PushVx == push, "equal knockback " + a);
                }
                else
                {
                    bool firstWins = (a == 1 && b == 0) || (a == 2 && b == 1) || (a == 0 && b == 2);
                    var winner = firstWins ? playerOne : playerTwo;
                    var loser = firstWins ? playerTwo : playerOne;
                    int kind = firstWins ? a : b;
                    check(winner.State.Hp == 100 && (kind == 2 ? winner.State.IsGrabbing && loser.State.IsGrabbed : loser.State.Hp == (kind == 1 ? 92 : 96)), "direction-independent RPS " + a + "/" + b);
                }
            }
            setup(); playerTwo.State.X = 800;
            Simulate(prepare(playerOne, 1), prepare(playerTwo, 1));
            check(playerOne.State.AttackTimer > 0 && playerTwo.State.AttackTimer > 0 && playerOne.State.PushVx == 0, "no clash without hitbox contact");
            setup(); var guard = playerTwo.State;
            UpdateGuardState(guard, new FighterInput { Guard = true, JustGuard = true }, false);
            for (int i = 0; i < 23; i++) UpdateGuardState(guard, new FighterInput { Guard = true }, false);
            ApplyHitToTarget(guard, playerOne.State, AttackType.Heavy, AttackDirection.Forward, 1);
            check(guard.Hp == 100 && guard.GuardHp == 20 && guard.PerfectGuardStreak == 1, "perfect guard within 0.2 seconds blocks all damage");
            check(guard.UltimateComboTicks == 240, "combo deadline starts on first perfect guard");
            for (int i = 0; i < 40; i++) TickUltimateTimers(guard);
            for (int i = 0; i < 3; i++) RegisterPerfectGuard(guard);
            check(guard.PerfectGuardStreak == 4 && guard.UltimateComboTicks == 200, "later perfect guards do not extend two-second deadline");
            guard.IsGuarding = true; guard.PerfectGuardWindow = 0;
            ApplyHitToTarget(guard, playerOne.State, AttackType.Normal, AttackDirection.Forward, 1);
            check(guard.PerfectGuardStreak == 0 && guard.GuardHp == 16, "ordinary block breaks perfect streak");
            setup(); RegisterPerfectGuard(playerOne.State);
            for (int i = 0; i < 240; i++) TickUltimateTimers(playerOne.State);
            check(playerOne.State.PerfectGuardStreak == 0 && playerOne.State.UltimateComboTicks == 0, "two-second deadline expires");

            setup(); for (int i = 0; i < 4; i++) RegisterPerfectGuard(playerOne.State);
            Simulate(new FighterInput { Grab = true, JustGrab = true }, default);
            check(playerOne.State.UltimateStage == 1, "qualified grab advances combo");
            Simulate(new FighterInput { Grab = true, JustHeavy = true, Up = true }, default);
            check(playerOne.State.UltimateStage == 2 && playerTwo.State.Vy > 0, "upward throw advances combo");
            playerOne.State.Y = playerTwo.State.Y = 180; playerOne.State.HitStop = playerTwo.State.HitStop = 0;
            playerOne.State.X = 220; playerTwo.State.X = 280;
            Simulate(prepare(playerOne, 1), default);
            check(playerOne.State.UltimateReadyTicks == 180 && playerOne.State.UltimateComboTicks == 0, "airborne heavy unlocks manual 1.5-second window");
            check(!playerOne.State.UltimateAttack && playerOne.State.SpeedBoostTicks == 0, "unlock does not auto-fire");
            playerOne.State.AttackTimer = 0; playerOne.State.HitStop = 0;
            Simulate(new FighterInput { JustUltimate = true }, default);
            check(playerOne.State.UltimateAttack && playerOne.State.UltimateReadyTicks == 0 && playerOne.State.SpeedBoostTicks == 600, "Q consumes readiness and grants five-second speed boost");
            playerTwo.State.Hp = 100; playerTwo.State.IsGuarding = false;
            playerOne.State.Hp = 10;
            ApplyHitToTarget(playerTwo.State, playerOne.State, AttackType.Heavy, AttackDirection.Forward, 1);
            check(playerTwo.State.Hp == 80, "ultimate deals exactly 20 without low-HP multiplier");
            var normalBoxes = new List<HitBox>(); var ultimateBoxes = new List<HitBox>();
            foreach (AttackDirection direction in Enum.GetValues(typeof(AttackDirection)))
            {
                normalBoxes.Clear(); ultimateBoxes.Clear();
                playerOne.State.UltimateAttack = false; BuildAttackBoxes(playerOne.State, AttackType.Heavy, direction, normalBoxes);
                playerOne.State.UltimateAttack = true; BuildAttackBoxes(playerOne.State, AttackType.Heavy, direction, ultimateBoxes);
                check(normalBoxes.Count == ultimateBoxes.Count && normalBoxes.TrueForAll(box => ultimateBoxes.Exists(other => other.X == box.X && other.Y == box.Y && other.Width == box.Width && other.Height == box.Height)), "ultimate uses heavy geometry " + direction);
            }
            setup(); playerOne.State.UltimateReadyTicks = 1; Simulate(new FighterInput { JustUltimate = true }, default);
            check(!playerOne.State.UltimateAttack, "expired readiness rejects Q");
            setup(); Simulate(new FighterInput { Grab = true, JustGrab = true }, default);
            Simulate(new FighterInput { Grab = true }, new FighterInput { JustUp = true });
            Simulate(new FighterInput { Grab = true }, new FighterInput { JustRight = true });
            check(playerTwo.State.EscapeStep == 0, "wrong escape direction resets sequence");
            check(AllowedBinding((int)Key.Q) && AllowedBinding((int)Key.Space) && AllowedBinding((int)Key.LeftShift) && AllowedBinding(-1) && AllowedBinding((int)Key.Digit1) && !AllowedBinding((int)Key.Enter) && !AllowedBinding((int)Key.LeftCtrl) && !AllowedBinding((int)Key.RightShift), "restricted binding whitelist");
            setup(); mode = SessionMode.Host; authenticated = matchStarted = true; playerTwo.State.Hp = 0;
            uint before = round; ApplyRematchVote(1, true);
            check(round == before && RoundOver && rematchVotes == 1, "one player cannot force restart");
            ApplyRematchVote(2, false);
            check(RoundOver && rematchVotes == 0, "decline cancels rematch request");
            ApplyRematchVote(2, true); ApplyRematchVote(1, true);
            check(round == before + 1 && !RoundOver && resumeTicks == 360, "both accept resets round with countdown");
            return passed.Count + " new rule checks passed: " + string.Join(", ", passed) + "\n" + RunBindingChecks();
        }
        finally
        {
            playerOne.State = oldOne; playerTwo.State = oldTwo; mode = oldMode;
            menuOpen = oldMenu; remotePaused = oldRemote; authenticated = oldAuth; matchStarted = oldMatch;
            resumePending = oldResume; resumeTicks = oldCountdown; rematchVotes = oldVotes; round = oldRound;
        }
    }

    private string RunBindingChecks()
    {
        var savedBindings = (int[])bindings.Clone();
        var prefValues = new int[bindings.Length]; var hadPrefs = new bool[bindings.Length];
        for (int i = 0; i < bindings.Length; i++)
        {
            hadPrefs[i] = PlayerPrefs.HasKey("WebFight.Key" + i);
            prefValues[i] = PlayerPrefs.GetInt("WebFight.Key" + i);
        }
        int oldEdit = editingBinding, oldPending = pendingBindingKey, oldLanguage = language;
        bool oldInvalid = invalidBindingKey;
        int oldBindFrame = bindFrame;
        float oldVolume = AudioListener.volume;
        int count = 0;
        Action<bool, string> check = (ok, message) => { if (!ok) throw new InvalidOperationException("FAIL: " + message); count++; };
        try
        {
            CancelBindingEdit();
            Array.Copy(DefaultBindings, bindings, bindings.Length);
            for (int i = 0; i < bindings.Length; i++) PlayerPrefs.SetInt("WebFight.Key" + i, bindings[i]);
            editingBinding = 0; SelectBindingKey((int)Key.S);
            check(pendingBindingKey == (int)Key.S && bindings[0] == (int)Key.W && bindings[1] == (int)Key.S, "duplicate waits for confirmation without mutation");
            CancelBindingEdit();
            check(pendingBindingKey == 0 && editingBinding == -1 && bindings[0] == (int)Key.W && bindings[1] == (int)Key.S, "cancel retains both assignments");
            editingBinding = 0; SelectBindingKey((int)Key.S); ApplyBinding(pendingBindingKey);
            check(bindings[0] == (int)Key.S && bindings[1] == UnassignedBinding && !Bound(1), "confirm unassigns displaced action and disables input");
            LoadBindings();
            check(bindings[0] == (int)Key.S && bindings[1] == UnassignedBinding, "unassigned survives preference reload");
            language = 0; check(BindingLabel(1) == "지정되지 않음", "unassigned Korean label");
            language = 1; check(BindingLabel(1) == "Unassigned", "unassigned English label");
            language = 2; check(BindingLabel(1) == "未設定", "unassigned Japanese label");
            editingBinding = 1; SelectBindingKey((int)Key.W);
            check(bindings[1] == (int)Key.W && editingBinding == -1, "unassigned action accepts released key");
            editingBinding = 1; SelectBindingKey((int)Key.W);
            check(pendingBindingKey == 0 && editingBinding == -1 && bindings[1] == (int)Key.W, "same action key is not a conflict");
            editingBinding = 6; SelectBindingKey(-2);
            check(pendingBindingKey == -2 && bindings[6] == -1 && bindings[7] == -2, "mouse duplicate also prompts");
            ApplyBinding(pendingBindingKey);
            check(bindings[6] == -2 && bindings[7] == 0 && !Bound(7), "mouse conflict unassigns previous action");
            editingBinding = 7; SelectBindingKey((int)Key.Enter);
            check(editingBinding == 7 && bindings[7] == 0 && pendingBindingKey == 0 && invalidBindingKey, "forbidden key warns without rebinding unassigned action");
            ResumeBindingCapture();
            SelectBindingKey(-1);
            check(bindings[7] == -1 && editingBinding == -1, "unassigned action accepts mouse input");
            PlayerPrefs.SetInt("WebFight.Key0", (int)Key.Q);
            LoadBindings();
            check(bindings[0] == (int)Key.Q && bindings[10] == 0 && !Bound(10), "legacy duplicate becomes unassigned rather than arbitrary key");
            editingBinding = 10; SelectBindingKey(0);
            check(!invalidBindingKey && editingBinding == 10, "idle input does not trigger warning");
            foreach (Key key in new[] { Key.A, Key.Z, Key.Digit1, Key.Digit0, Key.Space, Key.LeftShift, Key.Numpad0, Key.Numpad9 })
                check(AllowedBinding((int)key), "allowed printable key " + key);
            var forbidden = new[] {
                Key.RightShift, Key.Enter, Key.NumpadEnter, Key.Tab, Key.Backspace, Key.Delete, Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.LeftMeta, Key.UpArrow, Key.CapsLock,
                Key.LeftBracket, Key.RightBracket, Key.Quote, Key.Semicolon, Key.Backquote, Key.Minus, Key.Equals, Key.Slash, Key.Backslash, Key.Comma, Key.Period,
                Key.NumpadPlus, Key.NumpadMinus, Key.NumpadDivide, Key.NumpadMultiply, Key.NumpadEquals, Key.NumpadPeriod,
                Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12
            };
            foreach (Key key in forbidden)
            {
                editingBinding = 10; SelectBindingKey((int)key);
                check(!AllowedBinding((int)key) && invalidBindingKey && bindings[10] == 0 && pendingBindingKey == 0, "warning preserves binding for " + key);
                ResumeBindingCapture();
            }
            editingBinding = 10; SelectBindingKey((int)Key.Digit1);
            check(bindings[10] == (int)Key.Digit1 && BindingLabel(10) == "1", "number assigned after dismissing warning");
            foreach (Key key in forbidden)
            {
                PlayerPrefs.SetInt("WebFight.Key2", (int)key); LoadBindings();
                check(bindings[2] == UnassignedBinding && PlayerPrefs.GetInt("WebFight.Key2") == UnassignedBinding && !Bound(2), "saved forbidden key cleared " + key);
            }
            editingBinding = 0; SelectBindingKey((int)Key.Digit1);
            check(pendingBindingKey == (int)Key.Digit1 && !invalidBindingKey, "allowed duplicate number prompts replacement");
            CancelBindingEdit();
            editingBinding = 10; SelectBindingKey((int)Key.F2); CancelBindingEdit();
            check(!invalidBindingKey && editingBinding == -1 && bindings[10] == (int)Key.Digit1, "cancel invalid warning retains previous assignment");
            for (int key = (int)Key.Digit1; key <= (int)Key.Digit0; key++)
            {
                check(AllowedBinding(key) && !IsShiftedDigit(key, false) && IsShiftedDigit(key, true), "shifted digit rejected while plain digit remains valid " + key);
                editingBinding = 10; SelectBindingKey(IsShiftedDigit(key, true) ? -3 : key);
                check(invalidBindingKey && bindings[10] == (int)Key.Digit1, "shifted symbol shows warning without reassignment " + key);
                ResumeBindingCapture();
            }
            return count + " binding checks passed";
        }
        finally
        {
            Array.Copy(savedBindings, bindings, bindings.Length);
            for (int i = 0; i < bindings.Length; i++)
                if (hadPrefs[i]) PlayerPrefs.SetInt("WebFight.Key" + i, prefValues[i]); else PlayerPrefs.DeleteKey("WebFight.Key" + i);
            PlayerPrefs.Save(); editingBinding = oldEdit; pendingBindingKey = oldPending;
            language = oldLanguage; AudioListener.volume = oldVolume; uiSignature = "";
            invalidBindingKey = oldInvalid; bindFrame = oldBindFrame;
        }
    }
}
#endif
