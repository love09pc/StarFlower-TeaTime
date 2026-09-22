using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed partial class WebFightBootstrap : MonoBehaviour
{
    private const float UnitScale = 0.01f;
    private const float PlayerWidth = 50f;
    private const float PlayerHeight = 100f;
    private const float WorldWidth = 4096f;
    private const float WorldHeight = 1200f;
    private const float GroundHeight = 80f;
    private const int NormalDamage = 4;
    private const int HeavyDamage = 8;
    private const float DashStraightSpeed = 28f;
    private const float DashDiagonalSpeed = 20f;
    private const int DashDuration = 15;
    private const float GrabOffset = 40f;

    private static Sprite pixelSprite;

    private Fighter playerOne;
    private Fighter playerTwo;
    private Camera mainCamera;
    private Font uiFont;
    private Text statusText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntime()
    {
        if (FindFirstObjectByType<WebFightBootstrap>() != null)
        {
            return;
        }

        var root = new GameObject("WebGame Unity Port");
        root.AddComponent<WebFightBootstrap>();
    }

    private void Awake()
    {
        Time.fixedDeltaTime = 1f / 120f;
        uiFont = Resources.Load<Font>("WebGamePort/PFStardust");
        if (uiFont == null)
        {
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        ConfigureCamera();
        CreateArena();
        CreateFighters();
        CreateHud();
        InitializeSession();
    }

    private void Update()
    {
        if (playerOne == null || playerTwo == null) return;

        UpdateSession();
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame && mode == SessionMode.Training && !GameplayPaused && Array.IndexOf(bindings, (int)Key.R) < 0)
        {
            round++;
            ResetRound();
        }
    }

    private void FixedUpdate()
    {
        if (playerOne == null || playerTwo == null)
        {
            return;
        }

        TickSession();
    }

    private void LateUpdate()
    {
        if (playerOne == null) return;
        bool visibleGame = presentationReady && mode != SessionMode.Menu && (!Online || matchStarted);
        mainCamera.cullingMask = visibleGame ? -1 : 0;
        if (!visibleGame) { RefreshPresentation(); return; }
        UpdateFighterVisual(playerOne);
        UpdateFighterVisual(playerTwo);
        UpdateCamera();
        UpdateHud();
        RefreshPresentation();
        snapVisuals = false;
    }

    private void ConfigureCamera()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            mainCamera = cameraObject.AddComponent<Camera>();
        }

        mainCamera.orthographic = true;
        mainCamera.allowHDR = false;
        mainCamera.allowMSAA = false;
        mainCamera.cullingMask = 0;
        mainCamera.orthographicSize = 4.2f;
        mainCamera.clearFlags = CameraClearFlags.SolidColor;
        mainCamera.backgroundColor = new Color(0.102f, 0.102f, 0.102f);
        mainCamera.transform.position = new Vector3(5.2f, 2.15f, -10f);

        if (mainCamera.GetComponent<AudioListener>() == null)
        {
            mainCamera.gameObject.AddComponent<AudioListener>();
        }
    }

    private void CreateArena()
    {
        CreateRect("Web Arena Floor", new Color(0.2f, 0.2f, 0.2f), WorldWidth, GroundHeight, WorldWidth * 0.5f, -GroundHeight * 0.5f, 0);
        CreateRect("Floor Border", new Color(0.333f, 0.333f, 0.333f), WorldWidth, 2, WorldWidth * 0.5f, -1, 1);
        CreateRect("Left Wall", new Color(0.32f, 0.34f, 0.4f), 18f, 460f, -9f, 230f, 0);
        CreateRect("Right Wall", new Color(0.32f, 0.34f, 0.4f), 18f, 460f, WorldWidth + 9f, 230f, 0);
        CreateRect("Ceiling Guide", new Color(0.13f, 0.15f, 0.2f), WorldWidth, 12f, WorldWidth * 0.5f, WorldHeight - GroundHeight - PlayerHeight + 6f, 0);
    }

    private void CreateFighters()
    {
        playerOne = CreateFighter(
            "Player 1",
            new Color(0.16f, 0.57f, 1f),
            new FighterInputMap
            {
                Left = Key.A,
                Right = Key.D,
                Up = Key.W,
                Down = Key.S,
                Jump = Key.Space,
                Dash = Key.LeftShift,
                Guard = Key.F,
                Grab = Key.E,
                NormalMouseButton = MouseButton.Left,
                HeavyMouseButton = MouseButton.Right
            },
            220f,
            1f
        );

        playerTwo = CreateFighter(
            "Player 2",
            new Color(1f, 0.38f, 0.28f),
            new FighterInputMap
            {
                Left = Key.LeftArrow,
                Right = Key.RightArrow,
                Up = Key.UpArrow,
                Down = Key.DownArrow,
                Jump = Key.Enter,
                Dash = Key.RightShift,
                Guard = Key.RightCtrl,
                Grab = Key.RightAlt,
                Normal = Key.Numpad1,
                Heavy = Key.Numpad2
            },
            820f,
            -1f
        );
    }

    private Fighter CreateFighter(string fighterName, Color bodyColor, FighterInputMap inputMap, float x, float facing)
    {
        var fighterObject = new GameObject(fighterName);
        fighterObject.transform.SetParent(transform);
        var body = fighterObject.AddComponent<SpriteRenderer>();
        body.sprite = GetPixelSprite();
        body.color = bodyColor;
        body.sortingOrder = 10;

        var fighter = new Fighter
        {
            Name = fighterName,
            Root = fighterObject.transform,
            Body = body,
            Controls = inputMap,
            BaseColor = bodyColor,
            State = FighterState.Create(x, facing)
        };

        fighter.GrabVisual = CreateBoxVisual(fighterName + " Grab Box", new Color(0.25f, 0.95f, 1f, 0.32f), 6);
        for (var i = 0; i < 3; i += 1)
        {
            fighter.AttackVisuals.Add(CreateBoxVisual(fighterName + " Attack Box " + (i + 1), new Color(1f, 0.86f, 0.1f, 0.42f), 7));
        }

        UpdateFighterVisual(fighter);
        return fighter;
    }

    private void CreateHud()
    {
        CreateNativeInterface();
    }

    private void StepFighter(Fighter fighter, Fighter opponent, FighterInput input)
    {
        var state = fighter.State;
        var wasGrabbingAtStart = state.IsGrabbing;
        if (state.IsGrabbing || state.IsGrabbed) ClearGuard(state);

        if (state.HitStop > 0)
        {
            state.HitStop--;
            return;
        }

        state.IsLookingUp = input.Up;
        state.IsLookingDown = input.Down;
        state.IsWindup = false;
        state.AttackBoxes.Clear();
        state.GrabBox = BuildGrabBox(state);

        state.HitStun = Math.Max(0, state.HitStun - 1);
        if (state.HitStun > 0)
        {
            state.DashTimer = 0;
            state.IsDashing = false;
            state.AttackTimer = 0;
            state.AttackType = AttackType.None;
        }

        var isStunned = state.HitStun > 0;
        state.NormalCooldown = Math.Max(0, state.NormalCooldown - 1);
        state.HeavyCooldown = Math.Max(0, state.HeavyCooldown - 1);
        state.DashCooldown = Math.Max(0, state.DashCooldown - 1);
        state.GrabCooldown = Math.Max(0, state.GrabCooldown - 1);
        state.NormalComboTimer = Math.Max(0, state.NormalComboTimer - 1);

        if (state.NormalComboTimer <= 0)
        {
            state.NormalCombo = 0;
        }

        if (state.BuffTimer > 0)
        {
            state.BuffTimer = Math.Max(0, state.BuffTimer - 1);
            if (state.BuffTimer <= 0)
            {
                state.BuffStacks = 0;
            }
        }

        var wasDashing = state.DashTimer > 0 || state.IsDashing;
        state.DashTimer = Math.Max(0, state.DashTimer - 1);
        state.IsDashing = state.DashTimer > 0;

        if (wasDashing && !state.IsDashing)
        {
            if (state.Vy > 0f)
            {
                state.Vy = 0f;
            }

            state.PushVx *= 0.3f;
        }

        UpdateGuardState(state, input, isStunned);

        var moveSide = (input.Right ? 1 : 0) - (input.Left ? 1 : 0);
        var isMovingSide = moveSide != 0;

        if (!isStunned && !state.IsDashing && !state.IsGrabbed && !state.IsGuarding && isMovingSide)
        {
            state.FacingDirection = moveSide > 0 ? 1f : -1f;
        }

        if (state.IsGrabbed)
        {
            StepGrabbedFighter(fighter, input);
            ClampState(state);
            state.IsMoving = false;
            return;
        }

        var isAttacking = state.AttackTimer > 0;
        var canAct = !isStunned &&
                     state.HitStop <= 0 &&
                     !state.IsGrabbed &&
                     !state.IsDashing &&
                     !state.IsGrabbing &&
                     !state.IsGuarding &&
                     !isAttacking;

        if (canAct && input.JustDash && state.DashCooldown == 0)
        {
            StartDash(state, input, moveSide);
        }

        else if (canAct && input.JustGrab && state.GrabCooldown == 0)
        {
            TryStartGrab(fighter, opponent);
        }

        if (state.IsGrabbing)
        {
            StepGrabOwner(fighter, opponent, input);
        }

        canAct = canAct && !state.IsDashing && !state.IsGrabbing && !wasGrabbingAtStart && !state.GrabRequested;
        if (canAct && input.JustUltimate && state.UltimateReadyTicks > 0)
        {
            StartAttack(state, AttackType.Heavy, isMovingSide);
            state.UltimateAttack = true; state.UltimateReadyTicks = 0; state.SpeedBoostTicks = 600;
        }
        else if (canAct && input.JustNormal && state.NormalCooldown == 0)
        {
            StartAttack(state, AttackType.Normal, isMovingSide);
        }

        else if (canAct && input.JustHeavy && state.HeavyCooldown == 0)
        {
            StartAttack(state, AttackType.Heavy, isMovingSide);
        }

        StepAttack(fighter, opponent);
        StepMovement(state, input, moveSide, isStunned, state.AttackTimer > 0);
        ClampState(state);
        state.IsMoving = (!isStunned && isMovingSide) ||
                         state.Y > 0f ||
                         Mathf.Abs(state.Vy) > 0.5f ||
                         Mathf.Abs(state.PushVx) > 0.5f;
    }

    private void UpdateGuardState(FighterState state, FighterInput input, bool isStunned)
    {
        if (state.GuardCooldownTimer > 0)
        {
            state.GuardCooldownTimer = Math.Max(0, state.GuardCooldownTimer - 1);
            if (state.GuardCooldownTimer <= 0)
            {
                state.GuardHp = state.MaxGuardHp;
                state.GuardRegenTimer = 180;
            }
        }
        else if (!state.IsGuarding && state.GuardHp < state.MaxGuardHp)
        {
            state.GuardRegenTimer = Math.Max(0, state.GuardRegenTimer - 1);
            if (state.GuardRegenTimer <= 0)
            {
                state.GuardHp = Math.Min(state.MaxGuardHp, state.GuardHp + 2);
                state.GuardRegenTimer = 180;
            }
        }
        else
        {
            state.GuardRegenTimer = 180;
        }

        if (!isStunned &&
            !state.IsDashing &&
            !state.IsGrabbing &&
            !state.IsGrabbed &&
            state.AttackTimer <= 0 &&
            state.GuardCooldownTimer <= 0)
        {
            if (input.Guard && !state.IsGuarding && state.GuardHp > 0)
            {
                state.IsGuarding = true;
                state.PerfectGuardWindow = input.JustGuard ? 24 : 0;
                state.GuardHoldTimer = 600;
            }
            else if (input.Guard && state.IsGuarding)
            {
                state.GuardHoldTimer = Math.Max(0, state.GuardHoldTimer - 1);
                state.PerfectGuardWindow = Math.Max(0, state.PerfectGuardWindow - 1);
                if (state.GuardHoldTimer <= 0)
                {
                    state.IsGuarding = false;
                    state.GuardCooldownTimer = 600;
                }
            }
            else
            {
                ClearGuard(state);
            }
        }
        else
        {
            ClearGuard(state);
        }
    }

    private static void ClearGuard(FighterState state)
    {
        state.IsGuarding = false;
        state.PerfectGuardWindow = 0;
        state.GuardHoldTimer = 0;
    }

    private void StepGrabbedFighter(Fighter fighter, FighterInput input)
    {
        var state = fighter.State;
        state.ConsumedEscapeInput = true;
        var owner = state.GrabOwner;
        if (owner != null && owner.State.IsGrabbing && owner.State.GrabTarget == fighter)
        {
            state.X = owner.State.FacingDirection > 0f ? owner.State.X + GrabOffset : owner.State.X - GrabOffset;
            state.Y = owner.State.Y;
        }
        else
        {
            ClearGrabbedState(state);
            return;
        }

        state.Vy = 0f;
        state.PushVx = 0f;

        if (IsCorrectEscapeInput(state.EscapeStep, input))
        {
            state.EscapeStep = Math.Min(8, state.EscapeStep + 1);
        }
        else if (input.JustUp || input.JustLeft || input.JustDown || input.JustRight)
            state.EscapeStep = input.JustUp && !input.JustLeft && !input.JustDown && !input.JustRight ? 1 : 0;

        if (state.GrabbedTimer <= 0 || state.EscapeStep >= 8)
        {
            var pushDirection = owner != null ? owner.State.FacingDirection : 0f;
            ClearGrabbedState(state);
            state.PushVx = pushDirection * 15f;

            if (owner != null)
            {
                owner.State.IsGrabbing = false;
                owner.State.GrabTarget = null;
                owner.State.GrabCooldown = 60;
            }
        }
    }

    private void StartDash(FighterState state, FighterInput input, int moveSide)
    {
        var dirX = moveSide;
        var dirY = 0;

        if (input.Up)
        {
            dirY = 1;
        }

        if (input.Down)
        {
            dirY = -1;
        }

        if (dirX == 0 && dirY == 0)
        {
            dirX = state.FacingDirection > 0f ? 1 : -1;
        }

        var diagonal = dirX != 0 && dirY != 0;
        state.PushVx = dirX * (diagonal ? DashDiagonalSpeed : DashStraightSpeed);
        state.Vy = dirY * (diagonal ? DashDiagonalSpeed : DashStraightSpeed);
        state.IsDashing = true;
        state.DashTimer = DashDuration;
        state.DashCooldown = 120;
    }

    private void TryStartGrab(Fighter fighter, Fighter opponent)
    {
        var state = fighter.State;
        if (state.IsGuarding) return;
        if (deferCombatContacts) { state.GrabRequested = true; state.GrabCooldown = 30; return; }
        var opponentState = opponent.State;
        var grabBox = BuildGrabBox(state);
        var opponentFacingAttacker =
            (state.X < opponentState.X && opponentState.FacingDirection < 0f) ||
            (state.X > opponentState.X && opponentState.FacingDirection > 0f);
        var canBeGrabbed = !opponentState.IsGuarding || !opponentFacingAttacker;

        state.GrabCooldown = 30;
        if (opponentState.Hp <= 0 || state.Hp <= 0 || opponentState.IsGrabbed ||
            opponentState.IsGrabbing || !CheckCollision(grabBox, BuildPlayerBox(opponentState)) || !canBeGrabbed)
        {
            return;
        }

        state.IsGrabbing = true;
        ClearGuard(state);
        if (state.PerfectGuardStreak >= 4 && state.UltimateComboTicks > 0) state.UltimateStage = 1;
        ResetUltimateChain(opponentState);
        state.GrabTarget = opponent;
        state.GrabTimer = 180;
        opponentState.EscapeStep = 0;
        opponentState.AttackTimer = 0;
        opponentState.AttackType = AttackType.None;
        opponentState.AttackBoxes.Clear();
        opponentState.DashTimer = 0;
        opponentState.IsDashing = false;
        ClearGuard(opponentState);
        state.GrabBox = grabBox;
        HoldTarget(fighter, opponent, 180);
    }

    private void StepGrabOwner(Fighter fighter, Fighter opponent, FighterInput input)
    {
        var state = fighter.State;
        var target = state.GrabTarget;
        if (target == null || target != opponent || target.State.GrabOwner != fighter)
        {
            EndOwnerGrab(state);
            return;
        }

        state.GrabTimer = Math.Max(0, state.GrabTimer - 1);
        var heldTimer = Math.Max(0, Math.Min(state.GrabTimer, target.State.GrabbedTimer));
        HoldTarget(fighter, target, heldTimer);

        if (!input.Grab || state.GrabTimer <= 0 || target.State.EscapeStep >= 8)
        {
            ReleaseGrab(fighter, target, -5f, 0f);
            return;
        }

        if (input.JustNormal && state.NormalCooldown == 0)
        {
            target.State.Hp = Math.Max(0, target.State.Hp - GetScaledDamage(state, NormalDamage, 1));
            target.State.HitStop = Math.Max(target.State.HitStop, 5);
            state.NormalCooldown = 20;
            state.NormalCombo += 1;
            state.NormalComboTimer = 60;

            if (state.NormalCombo >= 4)
            {
                ReleaseGrab(fighter, target, 15f, 5f);
                state.NormalCooldown = 120;
                state.NormalCombo = 0;
            }
        }

        if (state.IsGrabbing && input.JustHeavy && !input.JustNormal && state.HeavyCooldown == 0)
        {
            target.State.Hp = Math.Max(0, target.State.Hp - GetScaledDamage(state, HeavyDamage, 1));
            target.State.HitStop = Math.Max(target.State.HitStop, 10);

            var dirX = 0;
            var dirY = 0;
            if (input.Left)
            {
                dirX = -1;
            }

            if (input.Right)
            {
                dirX = 1;
            }

            if (input.Up)
            {
                dirY = 1;
            }

            if (input.Down)
            {
                dirY = -1;
            }

            if (dirX == 0 && dirY == 0)
            {
                dirX = state.FacingDirection > 0f ? 1 : -1;
            }

            ClearGrabbedState(target.State);
            if (dirY == 1 && state.UltimateStage == 1 && state.UltimateComboTicks > 0) state.UltimateStage = 2;
            else ResetUltimateChain(state);
            target.State.PushVx = dirX * (dirY == 1 && dirX != 0 ? 13f : 22f);
            if (dirY == 1)
            {
                target.State.Vy = dirX != 0 ? 12f : 18f;
            }
            else if (dirY == -1)
            {
                target.State.Vy = -25f;
                state.Vy = 10f;
            }
            else
            {
                target.State.Vy = 6f;
            }

            EndOwnerGrab(state);
            state.GrabCooldown = 60;
            state.HeavyCooldown = 60;
        }
    }

    private void HoldTarget(Fighter owner, Fighter target, int grabbedTimer)
    {
        var ownerState = owner.State;
        var targetState = target.State;
        targetState.IsGrabbed = true;
        targetState.GrabOwner = owner;
        targetState.GrabbedTimer = grabbedTimer;
        targetState.X = ownerState.FacingDirection > 0f ? ownerState.X + GrabOffset : ownerState.X - GrabOffset;
        targetState.Y = ownerState.Y;
        targetState.Vy = 0f;
        targetState.PushVx = 0f;
    }

    private void ReleaseGrab(Fighter owner, Fighter target, float pushScale, float vy)
    {
        ClearGrabbedState(target.State);
        target.State.PushVx = owner.State.FacingDirection * pushScale;
        target.State.Vy = vy;
        EndOwnerGrab(owner.State);
        owner.State.GrabCooldown = 60;
    }

    private void EndOwnerGrab(FighterState state)
    {
        state.IsGrabbing = false;
        state.GrabTarget = null;
        state.GrabTimer = 0;
    }

    private void StartAttack(FighterState state, AttackType attackType, bool isMovingSide)
    {
        state.UltimateAttack = false;
        state.AttackType = attackType;
        state.AttackDirection = GetAttackDirection(state, isMovingSide);
        state.AttackTimer = attackType == AttackType.Normal ? 15 : 35;
        state.HasHit = false;

        if (attackType == AttackType.Normal)
        {
            state.NormalCombo += 1;
            state.NormalComboTimer = 60;
            if (state.NormalCombo >= 4)
            {
                state.NormalCooldown = 120;
                state.NormalCombo = 0;
            }
            else
            {
                state.NormalCooldown = 30;
            }
        }
        else
        {
            state.HeavyCooldown = 60;
        }
    }

    private void StepAttack(Fighter fighter, Fighter opponent)
    {
        var state = fighter.State;
        if (state.AttackTimer > 0 && state.HitStun <= 0 && !state.IsGrabbing && !state.IsGrabbed)
        {
            state.AttackTimer = Math.Max(0, state.AttackTimer - 1);
            var isActive =
                (state.AttackType == AttackType.Normal && state.AttackTimer <= 10) ||
                (state.AttackType == AttackType.Heavy && state.AttackTimer <= 15);

            if (isActive)
            {
                BuildAttackBoxes(state, state.AttackType, state.AttackDirection, state.AttackBoxes);
            }
            else
            {
                state.IsWindup = true;
            }
        }
        else if (state.AttackTimer <= 0)
        {
            state.AttackType = AttackType.None;
            state.HasHit = false;
        }

        if (deferCombatContacts) { state.ContactReady = true; return; }
        ResolveAttackHit(fighter, opponent);
    }

    private void ResolveAttackHit(Fighter fighter, Fighter opponent)
    {
        var state = fighter.State;
        if (state.AttackBoxes.Count == 0 || state.HasHit)
        {
            return;
        }

        var targetBox = BuildPlayerBox(opponent.State);
        var hit = false;
        for (var i = 0; i < state.AttackBoxes.Count; i += 1)
        {
            if (CheckCollision(state.AttackBoxes[i], targetBox))
            {
                hit = true;
                break;
            }
        }

        if (!hit)
        {
            return;
        }

        var target = opponent.State;
        var wasAirborne = target.Y > 0f;
        var stopFrames = state.AttackType == AttackType.Heavy ? 14 : 8;
        if (wasAirborne)
        {
            stopFrames = state.AttackType == AttackType.Normal ? 30 : 12;
        }

        int previousHp = target.Hp;
        ApplyHitToTarget(target, state, state.AttackType, state.AttackDirection, 1);
        if (!state.UltimateAttack && state.AttackType == AttackType.Heavy && state.Y > 0 && wasAirborne && target.Hp < previousHp && state.UltimateStage == 2 && state.UltimateComboTicks > 0)
        { ResetUltimateChain(state); state.UltimateReadyTicks = 180; }

        if (state.Y > 0f)
        {
            state.Vy = 0f;
        }

        if (state.AttackDirection == AttackDirection.Down)
        {
            state.Vy = state.AttackType == AttackType.Heavy ? 7f : 6f;
        }

        if (state.AttackDirection == AttackDirection.DownDiagonal)
        {
            state.Vy = 8f;
        }

        if (wasAirborne && target.Vy > 0f)
        {
            target.Vy = 0f;
        }

        state.HitStop = stopFrames;
        target.HitStop = stopFrames;
        state.HasHit = true;
    }

    private void StepMovement(FighterState state, FighterInput input, int moveSide, bool isStunned, bool isAttacking)
    {
        var maxX = WorldWidth - PlayerWidth;
        var isWallSliding = false;

        if (state.HitStop > 0)
        {
            state.HitStop = Math.Max(0, state.HitStop - 1);
            return;
        }

        if (state.IsDashing)
        {
            state.X += state.PushVx;
            state.Y += state.Vy;
            return;
        }

        var isWallLeft = state.X <= 0f;
        var isWallRight = state.X >= maxX;
        if (!isStunned &&
            state.Y > 0f &&
            state.Vy < 0f &&
            !state.IsGrabbing &&
            !state.IsGuarding &&
            ((isWallLeft && input.Left) || (isWallRight && input.Right)))
        {
            isWallSliding = true;
        }

        if (!isStunned && !state.IsGrabbing && !state.IsGuarding && Mathf.Approximately(state.Y, 0f))
        {
            state.HasWallJumped = false;
            state.HasDoubleJumped = false;
            if (input.Jump)
            {
                state.Vy = 12f;
            }
        }
        else if (!isStunned &&
                 !state.IsGrabbing &&
                 !state.IsGuarding &&
                 (isWallLeft || isWallRight) &&
                 input.JustJump &&
                 !state.HasWallJumped)
        {
            state.HasWallJumped = true;
            state.HasDoubleJumped = false;
            state.PushVx = isWallLeft ? 20f : -20f;
            if (input.Up)
            {
                state.Vy = 12f;
            }
            else if (input.Down)
            {
                state.Vy = -12f;
            }
            else
            {
                state.Vy = 12f;
            }
        }
        else if (!isStunned &&
                 !state.IsGrabbing &&
                 !state.IsGuarding &&
                 state.Y > 0f &&
                 input.JustJump &&
                 !state.HasDoubleJumped)
        {
            state.HasDoubleJumped = true;
            if (input.Left)
            {
                state.PushVx = -20f;
            }
            else if (input.Right)
            {
                state.PushVx = 20f;
            }

            if (input.Up)
            {
                state.Vy = 12f;
            }
            else if (input.Down)
            {
                state.Vy = -12f;
            }
            else
            {
                state.Vy = 12f;
            }
        }

        var speed = state.IsGuarding || state.IsGrabbing ? 1.5f : isAttacking ? 1f : 5f;
        if (state.SpeedBoostTicks > 0)
        {
            speed *= 1.05f;
        }

        var inputMove = isStunned ? 0f : moveSide * speed;
        state.X += inputMove;
        state.X += state.PushVx;
        state.Y += state.Vy;

        state.PushVx *= 0.85f;
        if (Mathf.Abs(state.PushVx) < 0.5f)
        {
            state.PushVx = 0f;
        }

        if (state.Y > 0f)
        {
            state.Vy -= 0.35f;
            if (isWallSliding && state.Vy < -3f)
            {
                state.Vy = -3f;
            }
            else if (state.Vy < -12f)
            {
                state.Vy = -12f;
            }
        }
        else
        {
            state.Y = 0f;
            state.Vy = 0f;
            state.HasWallJumped = false;
            state.HasDoubleJumped = false;
        }

        state.IsWallSliding = isWallSliding;
    }

    private void ApplyHitToTarget(FighterState target, FighterState attacker, AttackType attackType, AttackDirection attackDirection, float awakenMultiplier)
    {
        var baseDamage = attacker.UltimateAttack ? 20 : GetScaledDamage(attacker, attackType == AttackType.Heavy ? HeavyDamage : NormalDamage, awakenMultiplier);
        var isFacingAttacker =
            (attacker.X < target.X && target.FacingDirection < 0f) ||
            (attacker.X > target.X && target.FacingDirection > 0f);

        var finalDamage = baseDamage;
        var didGuard = false;
        var didPerfectGuard = false;

        if (target.IsGuarding && target.GuardHp > 0 && isFacingAttacker)
        {
            didGuard = true;
            if (target.PerfectGuardWindow > 0)
            {
                didPerfectGuard = true;
                finalDamage = 0;
                target.HitStop = Math.Max(target.HitStop, 8);
                RegisterPerfectGuard(target);
                target.PerfectGuardWindow = 0;
            }
            else
            {
                ResetUltimateChain(target);
                target.GuardHp = Math.Max(0, target.GuardHp - baseDamage);
                finalDamage = Mathf.FloorToInt(baseDamage * 0.4f);
                if (target.GuardHp <= 0)
                {
                    target.IsGuarding = false;
                    target.GuardCooldownTimer = 600;
                }
            }
        }

        target.Hp = Math.Max(0, target.Hp - finalDamage);
        if (!didGuard) ResetUltimateChain(target);
        target.HitStop = Math.Max(target.HitStop, attackType == AttackType.Heavy ? 14 : 8);

        if (attackType == AttackType.Normal)
        {
            if (attackDirection == AttackDirection.Up)
            {
                target.PushVx = attacker.FacingDirection * 2f;
                target.Vy = 8f;
            }
            else if (attackDirection == AttackDirection.Down)
            {
                target.PushVx = attacker.FacingDirection * 2f;
                target.Vy = -10f;
            }
            else if (attackDirection == AttackDirection.UpDiagonal)
            {
                target.PushVx = attacker.FacingDirection * 5f;
                target.Vy = 6f;
            }
            else if (attackDirection == AttackDirection.DownDiagonal)
            {
                target.PushVx = -attacker.FacingDirection * 5f;
                target.Vy = -8f;
            }
            else
            {
                var isLastHit = attacker.NormalCombo == 0;
                target.PushVx = attacker.FacingDirection * (isLastHit ? 6f : 2f);
                target.Vy = 0f;
            }
        }
        else if (attackDirection == AttackDirection.Up)
        {
            target.PushVx = attacker.FacingDirection * 3f;
            target.Vy = 15f;
        }
        else if (attackDirection == AttackDirection.Down)
        {
            target.PushVx = 0f;
            target.Vy = -20f;
        }
        else if (attackDirection == AttackDirection.UpDiagonal)
        {
            target.PushVx = attacker.FacingDirection * 15f;
            target.Vy = 15f;
        }
        else if (attackDirection == AttackDirection.DownDiagonal)
        {
            target.PushVx = -attacker.FacingDirection * 12f;
            target.Vy = -15f;
        }
        else
        {
            target.PushVx = attacker.FacingDirection * 18f;
            target.Vy = 4f;
        }

        if (didPerfectGuard)
        {
            target.PushVx = 0f;
            target.Vy = 0f;
        }
        else if (didGuard)
        {
            target.PushVx *= 0.25f;
            target.Vy *= 0.25f;
            if (Mathf.Abs(target.PushVx) < 0.5f)
            {
                target.PushVx = 0f;
            }

            if (Mathf.Abs(target.Vy) < 0.5f)
            {
                target.Vy = 0f;
            }
        }

        ClearGrabbedState(target);
    }

    private AttackDirection GetAttackDirection(FighterState state, bool isMovingSide)
    {
        if (state.IsLookingUp && isMovingSide)
        {
            return AttackDirection.UpDiagonal;
        }

        if (state.IsLookingDown && isMovingSide)
        {
            return AttackDirection.DownDiagonal;
        }

        if (state.IsLookingUp)
        {
            return AttackDirection.Up;
        }

        if (state.IsLookingDown)
        {
            return AttackDirection.Down;
        }

        return AttackDirection.Forward;
    }

    private void BuildAttackBoxes(FighterState state, AttackType attackType, AttackDirection attackDirection, List<HitBox> boxes)
    {
        boxes.Clear();
        var isHeavy = attackType == AttackType.Heavy;

        if (attackDirection == AttackDirection.Up)
        {
            boxes.Add(new HitBox(state.X - 35f, state.Y + PlayerHeight, 120f, isHeavy ? 80f : 50f));
            return;
        }

        if (attackDirection == AttackDirection.Down)
        {
            var hitHeight = isHeavy ? 80f : 50f;
            boxes.Add(new HitBox(state.X - 35f, state.Y - hitHeight, 120f, hitHeight));
            return;
        }

        if (attackDirection == AttackDirection.UpDiagonal)
        {
            var thickness = isHeavy ? 60f : 40f;
            var length = isHeavy ? 110f : 90f;
            boxes.Add(new HitBox(state.X, state.Y + PlayerHeight, PlayerWidth, thickness));
            boxes.Add(new HitBox(state.FacingDirection > 0f ? state.X + PlayerWidth : state.X - thickness, state.Y + PlayerHeight + thickness - length, thickness, length));
            return;
        }

        if (attackDirection == AttackDirection.DownDiagonal)
        {
            var thickness = isHeavy ? 60f : 40f;
            var length = isHeavy ? 110f : 90f;
            boxes.Add(new HitBox(state.X, state.Y - thickness, PlayerWidth, thickness));
            boxes.Add(new HitBox(state.FacingDirection > 0f ? state.X + PlayerWidth : state.X - thickness, state.Y - thickness, thickness, length));
            return;
        }

        var hitWidth = isHeavy ? 90f : 50f;
        boxes.Add(new HitBox(state.FacingDirection > 0f ? state.X + PlayerWidth : state.X - hitWidth, state.Y, hitWidth, PlayerHeight));
    }

    private HitBox BuildGrabBox(FighterState state)
    {
        return new HitBox(state.FacingDirection > 0f ? state.X + PlayerWidth : state.X - 30f, state.Y, 30f, PlayerHeight);
    }

    private HitBox BuildPlayerBox(FighterState state)
    {
        return new HitBox(state.X, state.Y, PlayerWidth, PlayerHeight);
    }

    private static bool CheckCollision(HitBox boxA, HitBox boxB)
    {
        return boxA.X < boxB.X + boxB.Width &&
               boxA.X + boxA.Width > boxB.X &&
               boxA.Y < boxB.Y + boxB.Height &&
               boxA.Y + boxA.Height > boxB.Y;
    }

    private void ClearGrabbedState(FighterState state)
    {
        var owner = state.GrabOwner;
        if (owner != null && owner.State.GrabTarget != null && owner.State.GrabTarget.State == state)
        {
            EndOwnerGrab(owner.State);
            owner.State.GrabCooldown = 60;
        }
        state.IsGrabbed = false;
        state.GrabOwner = null;
        state.GrabbedTimer = 0;
        state.EscapeStep = 0;
    }

    private int GetScaledDamage(FighterState state, int damage, float awakenMultiplier)
    {
        var multiplier = state.Hp <= state.MaxHp * 0.3f ? 1.5f : 1f;
        return Mathf.FloorToInt(damage * multiplier * awakenMultiplier);
    }

    private bool IsCorrectEscapeInput(int escapeStep, FighterInput input)
    {
        int count = (input.JustUp ? 1 : 0) + (input.JustLeft ? 1 : 0) + (input.JustDown ? 1 : 0) + (input.JustRight ? 1 : 0);
        if (count != 1) return false;
        switch (escapeStep % 4)
        {
            case 0:
                return input.JustUp;
            case 1:
                return input.JustLeft;
            case 2:
                return input.JustDown;
            default:
                return input.JustRight;
        }
    }

    private FighterInput ReadInput(FighterInputMap map, FighterInput previous)
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        bool custom = playerOne != null && map == playerOne.Controls;

        var input = new FighterInput
        {
            Left = custom ? Bound(2) : IsKeyPressed(keyboard, map.Left),
            Right = custom ? Bound(3) : IsKeyPressed(keyboard, map.Right),
            Up = custom ? Bound(0) : IsKeyPressed(keyboard, map.Up),
            Down = custom ? Bound(1) : IsKeyPressed(keyboard, map.Down),
            Jump = custom ? Bound(4) : IsKeyPressed(keyboard, map.Jump),
            Dash = custom ? Bound(5) : IsKeyPressed(keyboard, map.Dash),
            Guard = custom ? Bound(8) : IsKeyPressed(keyboard, map.Guard),
            Grab = custom ? Bound(9) : IsKeyPressed(keyboard, map.Grab),
            Normal = custom ? Bound(6) : IsKeyPressed(keyboard, map.Normal) || IsMousePressed(mouse, map.NormalMouseButton),
            Heavy = custom ? Bound(7) : IsKeyPressed(keyboard, map.Heavy) || IsMousePressed(mouse, map.HeavyMouseButton),
            Ultimate = custom && Bound(10)
        };

        input.JustLeft = input.Left && !previous.Left;
        input.JustRight = input.Right && !previous.Right;
        input.JustUp = input.Up && !previous.Up;
        input.JustDown = input.Down && !previous.Down;
        input.JustJump = input.Jump && !previous.Jump;
        input.JustDash = input.Dash && !previous.Dash;
        input.JustGuard = input.Guard && !previous.Guard;
        input.JustGrab = input.Grab && !previous.Grab;
        input.JustNormal = input.Normal && !previous.Normal;
        input.JustHeavy = input.Heavy && !previous.Heavy;
        input.JustUltimate = input.Ultimate && !previous.Ultimate;
        var controlled = mode == SessionMode.Client ? playerTwo : playerOne;
        if (custom && controlled.State.IsGrabbed && keyboard != null)
        {
            input.JustUp = keyboard.wKey.wasPressedThisFrame;
            input.JustLeft = keyboard.aKey.wasPressedThisFrame;
            input.JustDown = keyboard.sKey.wasPressedThisFrame;
            input.JustRight = keyboard.dKey.wasPressedThisFrame;
        }
        return input;
    }

    private static bool IsKeyPressed(Keyboard keyboard, Key key)
    {
        return keyboard != null && key != Key.None && keyboard[key].isPressed;
    }

    private static bool IsMousePressed(Mouse mouse, MouseButton button)
    {
        if (mouse == null || button == MouseButton.None)
        {
            return false;
        }

        if (button == MouseButton.Left)
        {
            return mouse.leftButton.isPressed;
        }

        if (button == MouseButton.Right)
        {
            return mouse.rightButton.isPressed;
        }

        return false;
    }

    private void ClampState(FighterState state)
    {
        var maxX = WorldWidth - PlayerWidth;
        var maxY = WorldHeight - GroundHeight - PlayerHeight;
        state.X = Mathf.Clamp(state.X, 0f, maxX);
        if (Mathf.Approximately(state.X, 0f) && state.PushVx < 0f)
        {
            state.PushVx = 0f;
        }

        if (Mathf.Approximately(state.X, maxX) && state.PushVx > 0f)
        {
            state.PushVx = 0f;
        }

        if (state.Y < 0f)
        {
            state.Y = 0f;
            if (state.Vy < 0f)
            {
                state.Vy = 0f;
            }
        }

        if (state.Y > maxY)
        {
            state.Y = maxY;
            if (state.Vy > 0f)
            {
                state.Vy = 0f;
            }
        }
    }

    private void UpdateFighterVisual(Fighter fighter)
    {
        var state = fighter.State;
        var destination = ToUnityCenter(state.X, state.Y, PlayerWidth, PlayerHeight, -0.1f);
        fighter.Root.position = mode == SessionMode.Client && !state.IsGrabbed && !snapVisuals
            ? Vector3.Lerp(fighter.Root.position, destination, 1f - Mathf.Exp(-30f * Time.unscaledDeltaTime))
            : destination;
        fighter.Root.localScale = new Vector3(PlayerWidth * UnitScale, PlayerHeight * UnitScale, 1f);
        fighter.Body.color = new Color(fighter.BaseColor.r, fighter.BaseColor.g, fighter.BaseColor.b, showHitboxes ? 0.35f : 0f);
        if (mode == SessionMode.Training && fighter == playerTwo) fighter.Body.color = Hex("E74C3C");
        fighter.Body.flipX = state.FacingDirection < 0f;
        UpdateCharacterArt(fighter);

        UpdateBoxVisual(fighter.GrabVisual, BuildGrabBox(state), showHitboxes && (state.IsGrabbing || state.GrabCooldown <= 0));
        for (var i = 0; i < fighter.AttackVisuals.Count; i += 1)
        {
            var active = showHitboxes && i < state.AttackBoxes.Count;
            UpdateBoxVisual(fighter.AttackVisuals[i], active ? state.AttackBoxes[i] : default, active);
        }
    }

    private void UpdateBoxVisual(SpriteRenderer spriteRenderer, HitBox box, bool active)
    {
        spriteRenderer.enabled = active;
        if (!active)
        {
            return;
        }

        spriteRenderer.transform.position = ToUnityCenter(box.X, box.Y, box.Width, box.Height, -0.2f);
        spriteRenderer.transform.localScale = new Vector3(box.Width * UnitScale, box.Height * UnitScale, 1f);
    }

    private void UpdateHud()
    {
        UpdateFighterHud(playerOne);
        UpdateFighterHud(playerTwo);

        if (!RoundOver)
        {
            statusText.text = resumeTicks > 0 ? Mathf.CeilToInt(resumeTicks / 120f).ToString() : SessionStatus;
        }
        else statusText.text = playerOne.State.Hp == playerTwo.State.Hp ? L("무승부", "DRAW") : playerOne.State.Hp <= 0 ? L("플레이어 2 승리", "PLAYER 2 WINS") : L("플레이어 1 승리", "PLAYER 1 WINS");
    }

    private void UpdateFighterHud(Fighter fighter)
    {
        fighter.HpFill.fillAmount = fighter.State.MaxHp <= 0 ? 0f : (float)fighter.State.Hp / fighter.State.MaxHp;
        fighter.GuardFill.fillAmount = fighter.State.MaxGuardHp <= 0 ? 0f : (float)fighter.State.GuardHp / fighter.State.MaxGuardHp;
        UpdateNativeFighterHud(fighter);
    }

    private void UpdateCamera()
    {
        // Window shape changes the letterboxing, never the visible combat area.
        float aspect = GameViewWidth / GameViewHeight;
        float windowAspect = Mathf.Max(1, Screen.width) / (float)Mathf.Max(1, Screen.height);
        float viewportWidth = Mathf.Min(1f, aspect / windowAspect);
        float viewportHeight = Mathf.Min(1f, windowAspect / aspect);
        mainCamera.rect = new Rect((1f - viewportWidth) * 0.5f, (1f - viewportHeight) * 0.5f, viewportWidth, viewportHeight);
        mainCamera.aspect = aspect;
        mainCamera.orthographicSize = GameViewHeight * UnitScale * 0.5f;
        var followed = mode == SessionMode.Client ? playerTwo : playerOne;
        var midpoint = mode == SessionMode.Client ? followed.Root.position.x : (followed.State.X + PlayerWidth * 0.5f) * UnitScale;
        if (mode == SessionMode.Local)
        {
            var separation = Mathf.Abs(playerOne.State.X - playerTwo.State.X) * UnitScale;
            mainCamera.orthographicSize = Mathf.Max(mainCamera.orthographicSize, (separation + 3f) / (2f * mainCamera.aspect));
            midpoint = (playerOne.State.X + playerTwo.State.X + PlayerWidth) * 0.5f * UnitScale;
        }
        var minX = mainCamera.orthographicSize * mainCamera.aspect;
        var maxX = WorldWidth * UnitScale - minX;
        midpoint = minX <= maxX ? Mathf.Clamp(midpoint, minX, maxX) : WorldWidth * UnitScale * 0.5f;
        var followedY = mode == SessionMode.Client ? followed.Root.position.y : (followed.State.Y + PlayerHeight * 0.5f) * UnitScale;
        var centerY = Mathf.Max(mainCamera.orthographicSize - 0.8f, followedY);
        mainCamera.transform.position = new Vector3(midpoint, centerY, -10f);
    }

    private void ResetRound()
    {
        playerOne.State = FighterState.Create(mode == SessionMode.Training ? 200f : 220f, 1f);
        playerTwo.State = FighterState.Create(mode == SessionMode.Training ? 800f : 820f, -1f);
        playerOne.PreviousInput = default;
        playerTwo.PreviousInput = default;
        statusText.text = "";
    }

    private SpriteRenderer CreateBoxVisual(string objectName, Color color, int order)
    {
        var visual = new GameObject(objectName);
        visual.transform.SetParent(transform);
        var spriteRenderer = visual.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = GetPixelSprite();
        spriteRenderer.color = color;
        spriteRenderer.sortingOrder = order;
        spriteRenderer.enabled = false;
        return spriteRenderer;
    }

    private void CreateRect(string objectName, Color color, float width, float height, float centerX, float centerY, int order)
    {
        var rect = new GameObject(objectName);
        rect.transform.SetParent(transform);
        var spriteRenderer = rect.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = GetPixelSprite();
        spriteRenderer.color = color;
        spriteRenderer.sortingOrder = order;
        rect.transform.position = ToUnityCenter(centerX - width * 0.5f, centerY - height * 0.5f, width, height, 0.2f);
        rect.transform.localScale = new Vector3(width * UnitScale, height * UnitScale, 1f);
    }

    private Vector3 ToUnityCenter(float x, float y, float width, float height, float z)
    {
        return new Vector3((x + width * 0.5f) * UnitScale, (y + height * 0.5f) * UnitScale, z);
    }

    private static Sprite GetPixelSprite()
    {
        if (pixelSprite != null)
        {
            return pixelSprite;
        }

        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        pixelSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        return pixelSprite;
    }

    private enum AttackType
    {
        None,
        Normal,
        Heavy
    }

    private enum AttackDirection
    {
        Forward,
        Up,
        Down,
        UpDiagonal,
        DownDiagonal
    }

    private enum MouseButton
    {
        None,
        Left,
        Right
    }

    private struct HitBox
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;

        public HitBox(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    private struct FighterInput
    {
        public bool Ultimate, JustUltimate;
        public bool Left;
        public bool Right;
        public bool Up;
        public bool Down;
        public bool Jump;
        public bool Dash;
        public bool Guard;
        public bool Grab;
        public bool Normal;
        public bool Heavy;
        public bool JustLeft;
        public bool JustRight;
        public bool JustUp;
        public bool JustDown;
        public bool JustJump;
        public bool JustDash;
        public bool JustGuard;
        public bool JustGrab;
        public bool JustNormal;
        public bool JustHeavy;
    }

    private sealed class FighterInputMap
    {
        public Key Left;
        public Key Right;
        public Key Up;
        public Key Down;
        public Key Jump;
        public Key Dash;
        public Key Guard;
        public Key Grab;
        public Key Normal;
        public Key Heavy;
        public MouseButton NormalMouseButton;
        public MouseButton HeavyMouseButton;
    }

    private sealed class Fighter
    {
        public string Name;
        public Transform Root;
        public SpriteRenderer Body;
        public SpriteRenderer Art;
        public SpriteRenderer GrabVisual;
        public readonly List<SpriteRenderer> AttackVisuals = new List<SpriteRenderer>();
        public Image HpFill;
        public Image GuardFill;
        public Text NameText;
        public FighterInputMap Controls;
        public FighterInput PreviousInput;
        public FighterState State;
        public Color BaseColor;
    }

    private sealed class FighterState
    {
        public bool GrabRequested, ContactReady, UltimateAttack, ConsumedEscapeInput;
        public int PerfectGuardStreak, UltimateComboTicks, UltimateStage, UltimateReadyTicks, SpeedBoostTicks;
        public float X;
        public float Y;
        public float Vy;
        public float PushVx;
        public int Hp;
        public int MaxHp;
        public int GuardHp;
        public int MaxGuardHp;
        public int GuardHoldTimer;
        public int GuardCooldownTimer;
        public int GuardRegenTimer;
        public int PerfectGuardWindow;
        public float FacingDirection;
        public bool IsLookingUp;
        public bool IsLookingDown;
        public bool IsMoving;
        public bool IsDashing;
        public bool HasWallJumped;
        public bool HasDoubleJumped;
        public bool IsWallSliding;
        public bool IsGuarding;
        public bool IsGrabbing;
        public bool IsGrabbed;
        public Fighter GrabOwner;
        public Fighter GrabTarget;
        public bool IsWindup;
        public AttackType AttackType;
        public AttackDirection AttackDirection;
        public int AttackTimer;
        public readonly List<HitBox> AttackBoxes = new List<HitBox>(3);
        public HitBox GrabBox;
        public int GrabTimer;
        public int GrabbedTimer;
        public int GrabCooldown;
        public int EscapeStep;
        public int NormalCooldown;
        public int HeavyCooldown;
        public int DashCooldown;
        public int DashTimer;
        public int NormalCombo;
        public int NormalComboTimer;
        public int HitStun;
        public int HitStop;
        public bool HasHit;
        public int BuffStacks;
        public int BuffTimer;

        public static FighterState Create(float x, float facing)
        {
            return new FighterState
            {
                X = x,
                Y = 0f,
                Vy = 0f,
                PushVx = 0f,
                Hp = 100,
                MaxHp = 100,
                GuardHp = 20,
                MaxGuardHp = 20,
                GuardRegenTimer = 180,
                FacingDirection = facing,
                AttackType = AttackType.None,
                AttackDirection = AttackDirection.Forward
            };
        }
    }
}
