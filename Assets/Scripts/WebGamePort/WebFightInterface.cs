using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public sealed partial class WebFightBootstrap
{
    private const float GameViewWidth = 1600f;
    private const float GameViewHeight = 900f;
    private Canvas interfaceCanvas;
    private RectTransform hudRoot, screenRoot;
    private RectTransform gameBars, barLeft, barRight, barTop, barBottom;
    private string uiPage = "main", uiSignature = "", settingsTab = "controls";
    private bool confirmExit, showFps, showPing;
    private int language;
    private int editingBinding = -1;
    private int pendingBindingKey;
    private bool invalidBindingKey;
    private const int UnassignedBinding = 0;
    private int bindFrame;
    private string roomSearch = "", selectedRoomId = "";
    private bool directJoin;
    private Text guardOne, guardTwo, buffOne, buffTwo, labelOne, labelTwo;
    private RectTransform hudOne, hudTwo;
    private static readonly int[] DefaultBindings = { (int)Key.W, (int)Key.S, (int)Key.A, (int)Key.D, (int)Key.Space, (int)Key.LeftShift, -1, -2, (int)Key.F, (int)Key.E, (int)Key.Q };
    private readonly int[] bindings = (int[])DefaultBindings.Clone();
    private bool presentationReady;
    private int stableDisplayFrames, previousDisplayWidth, previousDisplayHeight;
    private CanvasGroup startupVisibility;
    private static Sprite roundedSprite;
    private static Color Orange => Hex("F39C12");
    private float UiWidth => interfaceCanvas.pixelRect.width / interfaceCanvas.scaleFactor;
    private float UiHeight => interfaceCanvas.pixelRect.height / interfaceCanvas.scaleFactor;
    private string L(string ko, string en) => language == 1 ? en : language == 2 && Japanese.TryGetValue(ko, out var ja) ? ja : ko;
    private static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }

    private void CreateNativeInterface()
    {
        var root = new GameObject("Web UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform);
        interfaceCanvas = root.GetComponent<Canvas>();
        interfaceCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        interfaceCanvas.sortingOrder = 10;
        startupVisibility = root.AddComponent<CanvasGroup>();
        startupVisibility.alpha = 0;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(GameViewWidth, GameViewHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        if (EventSystem.current == null)
        {
            var events = new GameObject("UI Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform);
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        language = Mathf.Clamp(PlayerPrefs.GetInt("WebFight.Language", PlayerPrefs.GetInt("WebFight.English", 0)), 0, 2);
        showFps = PlayerPrefs.GetInt("WebFight.Fps", 0) != 0;
        showPing = PlayerPrefs.GetInt("WebFight.Ping", 0) != 0;
        gameBars = UiRect(root.transform, "Game viewport bars", 0, 0, UiWidth, UiHeight);
        barLeft = UiPanel(gameBars, "Left bar", 0, 0, 0, 0, Color.black);
        barRight = UiPanel(gameBars, "Right bar", 0, 0, 0, 0, Color.black);
        barTop = UiPanel(gameBars, "Top bar", 0, 0, 0, 0, Color.black);
        barBottom = UiPanel(gameBars, "Bottom bar", 0, 0, 0, 0, Color.black);
        hudRoot = UiRect(root.transform, "HUD", 0, 0, Screen.width, Screen.height);
        hudRoot.gameObject.AddComponent<RectMask2D>();
        hudOne = UiRect(hudRoot, "Left HUD", 46, 18, 430, 140);
        hudTwo = UiRect(hudRoot, "Right HUD", Screen.width - 476, 18, 430, 140);
        CreateNativeFighterHud(playerOne, hudOne, out guardOne, out buffOne);
        CreateNativeFighterHud(playerTwo, hudTwo, out guardTwo, out buffTwo);
        fpsText = UiText(hudOne, "FPS: --", 16, Color.white, 0, 106, 430, 20);
        pingText = UiText(hudOne, "PING: --", 16, Color.white, 0, 128, 430, 20);
        statusText = UiText(hudRoot, "", 18, Color.white, 0, 110, Screen.width, 36, TextAnchor.MiddleCenter);
        labelOne = UiText(hudRoot, "Player 1", 16, Hex("2ECC71"), 0, 0, 160, 24, TextAnchor.MiddleCenter);
        labelTwo = UiText(hudRoot, "Player 2", 16, Orange, 0, 0, 160, 24, TextAnchor.MiddleCenter);
        screenRoot = UiRect(root.transform, "Screens", 0, 0, Screen.width, Screen.height);
        hudRoot.gameObject.SetActive(false);
    }

    private void CreateNativeFighterHud(Fighter fighter, RectTransform parent, out Text guard, out Text buff)
    {
        fighter.NameText = UiText(parent, fighter.Name, 22, Color.white, 0, 0, 430, 24);
        fighter.HpFill = UiBar(parent, "HP", 0, 30, 430, 22, 3, Hex("777777"), Hex("2ECC71"));
        guard = UiText(parent, "Guard", 18, Hex("3498DB"), 0, 60, 430, 20);
        fighter.GuardFill = UiBar(parent, "Guard bar", 0, 82, 430, 15, 2, Hex("777777"), Hex("3498DB"));
        buff = UiText(parent, "", 16, Hex("F1C40F"), 0, 106, 430, 20);
    }

    private void UpdateNativeFighterHud(Fighter fighter)
    {
        if (hudOne == null) return;
        bool left = fighter == playerOne;
        bool training = mode == SessionMode.Training;
        float margin = training ? 50 : 46;
        float width = training ? (left ? 300 : 450) : 430;
        var panel = left ? hudOne : hudTwo;
        Place(panel, left ? margin : GameViewWidth - margin - width, training ? 20 : 18, width, 140);
        Place(fighter.NameText.rectTransform, 0, 0, width, training ? 18 : 24);
        fighter.NameText.fontSize = training ? 16 : 22;
        fighter.NameText.alignment = left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
        fighter.NameText.text = fighter.Name + " HP: " + fighter.State.Hp + "/" + fighter.State.MaxHp;
        var hpBack = (RectTransform)fighter.HpFill.transform.parent;
        Place(hpBack, 0, training ? 21 : 30, width, training ? 20 : 22);
        fighter.HpFill.color = training && !left ? Hex("E67E22") : fighter.State.Hp <= 30 ? Hex("E74C3C") : Hex("2ECC71");
        var guard = left ? guardOne : guardTwo;
        guard.gameObject.SetActive(!training || left);
        fighter.GuardFill.transform.parent.gameObject.SetActive(!training || left);
        Place(guard.rectTransform, 0, training ? 46 : 60, width, training ? 16 : 20);
        guard.fontSize = training ? 14 : 18;
        guard.text = "Guard" + (training ? " (" + BindingLabel(8) + ")" : "") + "    " +
            (fighter.State.GuardCooldownTimer > 0 ? "BROKEN (" + (fighter.State.GuardCooldownTimer / 120f).ToString("0.0") + "s)" : fighter.State.GuardHp + "/20");
        Place((RectTransform)fighter.GuardFill.transform.parent, 0, training ? 62 : 82, width, training ? 10 : 15);
        var buff = left ? buffOne : buffTwo;
        var state = fighter.State;
        buff.text = state.UltimateReadyTicks > 0 ? L("필살기", "Ultimate") + " [" + BindingLabel(10) + "] " + (state.UltimateReadyTicks / 120f).ToString("0.0") + "s" :
            state.SpeedBoostTicks > 0 ? "SPEED +5%  " + (state.SpeedBoostTicks / 120f).ToString("0.0") + "s" :
            state.PerfectGuardStreak > 0 ? "PERFECT " + state.PerfectGuardStreak + "/4  " + (state.UltimateComboTicks / 120f).ToString("0.0") + "s" : "";
        bool local = left != (mode == SessionMode.Client);
        Place(buff.rectTransform, 0, (training ? 80 : 106) + (local ? ((showFps ? 1 : 0) + (showPing ? 1 : 0)) * 22 : 0), width, 20);
        var label = left ? labelOne : labelTwo;
        label.gameObject.SetActive(!training);
        label.text = fighter.Name;
        label.color = (mode == SessionMode.Client) != left ? Hex("2ECC71") : Orange;
        Vector3 viewport = mainCamera.WorldToViewportPoint(fighter.Root.position + new Vector3(0, 0.96f, 0));
        Place(label.rectTransform, viewport.x * GameViewWidth - 80, (1f - viewport.y) * GameViewHeight - 12, 160, 24);
    }

    private void RefreshPresentation()
    {
        if (previousDisplayWidth == Screen.width && previousDisplayHeight == Screen.height) stableDisplayFrames++;
        else { previousDisplayWidth = Screen.width; previousDisplayHeight = Screen.height; stableDisplayFrames = 0; }
        if (!presentationReady)
        {
            if (stableDisplayFrames < 3 || resolutionChange != null) return;
            Canvas.ForceUpdateCanvases();
            presentationReady = true;
            uiSignature = "";
        }
        float insetX = Mathf.Max(0, (UiWidth - GameViewWidth) * 0.5f);
        float insetY = Mathf.Max(0, (UiHeight - GameViewHeight) * 0.5f);
        Place(hudRoot, insetX, insetY, GameViewWidth, GameViewHeight);
        bool game = mode != SessionMode.Menu && (!Online || matchStarted);
        gameBars.gameObject.SetActive(game);
        Place(gameBars, 0, 0, UiWidth, UiHeight);
        Place(barLeft, 0, 0, insetX, UiHeight);
        Place(barRight, UiWidth - insetX, 0, insetX, UiHeight);
        Place(barTop, 0, 0, UiWidth, insetY);
        Place(barBottom, 0, UiHeight - insetY, UiWidth, insetY);
        hudRoot.gameObject.SetActive(game);
        statusText.gameObject.SetActive(game && (RoundOver || remotePaused || resumeTicks > 0));
        UpdatePerformanceHud();
        Place(statusText.rectTransform, 0, 112, GameViewWidth, 36);
        string signature = uiPage + menuOpen + settingsOpen + settingsTab + confirmExit + editingBinding + ":" + pendingBindingKey + ":" + invalidBindingKey + ":" +
            Screen.width + ":" + Screen.height + ":" + interfaceCanvas.scaleFactor + ":" + authenticated + matchStarted + language + RoundOver + rematchVotes + discovery.Revision + selectedRoomId + directJoin;
        if (uiSignature == signature) return;
        uiSignature = signature;
        foreach (Transform child in screenRoot) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        Place(screenRoot, 0, 0, UiWidth, UiHeight);
        if (settingsOpen) BuildSettings();
        else if (mode == SessionMode.Menu)
        {
            UiPanel(screenRoot, "Background", 0, 0, UiWidth, UiHeight, Hex(uiPage == "main" ? "111111" : "141414"));
            if (uiPage == "host") BuildHostForm();
            else if (uiPage == "join") BuildJoinForm();
            else BuildMainMenu();
        }
        else if (Online && !matchStarted) BuildLobby();
        else if (menuOpen || RoundOver) BuildPauseMenu();
        else if (mode != SessionMode.Training)
            UiButton(screenRoot, L("메뉴", "Menu"), UiWidth / 2 - 75, insetY + 18, 150, 44, () => SetMenu(true));
        if (confirmExit) BuildExitModal();
        if (editingBinding >= 0) BuildBindingModal();
        startupVisibility.alpha = 1;
    }

    private void BuildMainMenu()
    {
        float width = Mathf.Min(510, UiWidth - 40);
        float x = (UiWidth - width) / 2;
        float top = (UiHeight - 582.6f) / 2;
        var title = UiText(screenRoot, "asd", 80, Orange, x, top + 73.6f, width, 80.5f, TextAnchor.MiddleCenter);
        title.fontStyle = FontStyle.Bold;
        title.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -4);
        string[] labels = { L("방 호스팅하기", "Host Room"), L("참여하기", "Join Room"), L("훈련장", "Training"), L("설정", "Settings"), L("종료", "Exit") };
        Action[] actions = {
            () => { uiPage = "host"; playerName = "Player 1"; notice = ""; },
            () => { uiPage = "join"; playerName = "Player 2"; notice = ""; discovery.StartSearch(); },
            () => StartSession(SessionMode.Training), () => settingsOpen = true, () => confirmExit = true
        };
        for (int i = 0; i < labels.Length; i++) UiButton(screenRoot, labels[i], x, top + 204.1f + i * 74.5f, width, 60.5f, actions[i], true);
        if (notice.Length > 0) UiText(screenRoot, notice, 16, Hex("FF8F7D"), x, UiHeight - 35, width, 30, TextAnchor.MiddleCenter);
    }

    private RectTransform RoomPanel(string title, float width, float height)
    {
        width = Mathf.Min(width, UiWidth - 40);
        var panel = UiPanel(screenRoot, "Room", (UiWidth - width) / 2, (UiHeight - height) / 2, width, height, Hex("202020"), true, Hex("363636"));
        UiText(panel, title, 34, Orange, 32, 32, width - 64, 40);
        UiPanel(panel, "Divider", 32, 91, width - 64, 1, Hex("3A3A3A"));
        return panel;
    }

    private void BuildHostForm()
    {
        var panel = RoomPanel(L("방 호스팅하기", "Host Room"), 560, 528);
        float width = panel.sizeDelta.x - 64;
        UiField(panel, L("플레이어 이름", "Player Name"), playerName, 32, 118, width, 16, value => playerName = value);
        UiField(panel, L("방 이름", "Room Name"), roomName, 32, 210, width, 32, value => roomName = value);
        UiField(panel, L("방 비밀번호", "Room Password"), roomPassword, 32, 302, width, 32, value => roomPassword = value, true);
        UiText(panel, "LAN  " + localAddresses + " : " + portText, 14, Hex("9F9F9F"), 32, 393, width, 26);
        UiButton(panel, L("방 만들기", "Create Room"), 32, 445, (width - 12) / 2, 50, () => {
            if (string.IsNullOrWhiteSpace(roomName) || string.IsNullOrWhiteSpace(playerName)) { notice = L("방 이름과 플레이어 이름을 입력해 주세요.", "Enter room and player names."); uiSignature = ""; }
            else StartSession(SessionMode.Host);
        });
        UiButton(panel, L("돌아가기", "Back"), 38 + width / 2, 445, (width - 12) / 2, 50, () => uiPage = "main");
        if (notice.Length > 0) UiText(panel, notice, 14, Hex("FF8F7D"), 32, 416, width, 24);
    }

    private void BuildJoinForm()
    {
        float width = Mathf.Min(1120, UiWidth - 48);
        var panel = RoomPanel(L("방 참여하기", "Join Room"), width, 548);
        float leftWidth = (width - 88) * 0.59f;
        float rightX = 56 + leftWidth;
        float rightWidth = width - rightX - 32;
        UiField(panel, L("방 이름 또는 코드 검색", "Search room name or code"), roomSearch, 32, 110, leftWidth, 45, value => roomSearch = value);
        UiButton(panel, L("검색", "Search"), 32, 198, (leftWidth - 10) / 2, 46, () => { discovery.SearchNow(); uiSignature = ""; });
        UiButton(panel, L("새로고침", "Refresh"), 37 + leftWidth / 2, 198, (leftWidth - 10) / 2, 46, () => { discovery.SearchNow(); uiSignature = ""; });
        var viewport = UiRect(panel, "Room list viewport", 32, 258, leftWidth, 196);
        viewport.gameObject.AddComponent<RectMask2D>();
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        var content = UiRect(viewport, "Rooms", 0, 0, leftWidth, Mathf.Max(196, discovery.Rooms.Count * 82));
        scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.scrollSensitivity = 30;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        int row = 0;
        LanRoomDiscovery.Room selected = discovery.Rooms.Find(room => room.Id == selectedRoomId);
        foreach (var room in discovery.Rooms)
        {
            if (roomSearch.Length > 0 && room.name.IndexOf(roomSearch, StringComparison.OrdinalIgnoreCase) < 0 && room.Id.IndexOf(roomSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var found = room;
            var button = UiButton(content, room.name + "    " + room.players + "/2\n" + room.Id + "    " + (room.locked ? L("비밀번호 필요", "Password Required") : L("공개 방", "Open Room")), 0, row++ * 82, leftWidth, 72, () => {
                selectedRoomId = found.Id; address = found.address; portText = found.port.ToString(); roomPassword = ""; directJoin = false;
            });
            if (room.Id == selectedRoomId) SetButtonColor(button, Orange, Hex("111111"));
        }
        if (row == 0)
        {
            UiPanel(content, "Empty rooms", 0, 0, leftWidth, 78, Hex("171717"), true, Hex("3A3A3A"));
            UiText(content, L("참여 가능한 방이 없습니다.", "No rooms are available."), 16, Hex("AAAAAA"), 16, 16, leftWidth - 32, 46, TextAnchor.MiddleCenter);
        }
        UiButton(panel, directJoin ? L("방 목록", "Room List") : L("IP 직접 입력", "Enter IP"), 32, 472, leftWidth, 44, () => { directJoin = !directJoin; selectedRoomId = ""; });
        UiField(panel, L("플레이어 이름", "Player Name"), playerName, rightX, 110, rightWidth, 16, value => playerName = value);
        if (directJoin)
            UiField(panel, L("호스트 IP 주소", "Host IP Address"), address, rightX, 205, rightWidth, 45, value => address = value);
        else
        {
            UiPanel(panel, "Selected room", rightX, 208, rightWidth, 80, Hex("171717"), true, Hex("3A3A3A"));
            UiText(panel, L("선택한 방", "Selected Room"), 13, Hex("9F9F9F"), rightX + 16, 218, rightWidth - 32, 18);
            UiText(panel, selected != null ? selected.name : L("선택한 방 없음", "No Room Selected"), 19, Color.white, rightX + 16, 246, rightWidth - 32, 27);
        }
        UiField(panel, L("비밀번호 입력", "Password"), roomPassword, rightX, 307, rightWidth, 32, value => roomPassword = value, true);
        var join = UiButton(panel, L("접속하기", "Join"), rightX, 410, rightWidth, 46, () => {
            if (string.IsNullOrWhiteSpace(playerName)) { notice = L("플레이어 이름을 입력해 주세요.", "Enter a player name."); uiSignature = ""; }
            else StartSession(SessionMode.Client);
        });
        join.interactable = directJoin || selected != null;
        UiButton(panel, L("돌아가기", "Back"), rightX, 470, rightWidth, 46, () => { uiPage = "main"; discovery.Dispose(); });
        if (notice.Length > 0) UiText(panel, notice, 14, Hex("FF8F7D"), 32, 520, width - 64, 26);
    }

    private void BuildLobby()
    {
        UiPanel(screenRoot, "Room background", 0, 0, UiWidth, UiHeight, Hex("141414"));
        var panel = RoomPanel(mode == SessionMode.Host ? L("플레이어 대기 중", "Waiting for player") : L("호스트 대기 중", "Waiting for Host"), 560, 500);
        UiText(panel, L("준비되면 호스트가 게임을 시작합니다.", "The host starts the game when ready."), 16, Hex("CFCFCF"), 32, 108, 496, 26);
        UiText(panel, roomName, 24, Color.white, 32, 159, 496, 35);
        UiText(panel, "LAN  " + (mode == SessionMode.Host ? localAddresses : address) + " : " + portText, 16, Hex("3498DB"), 32, 202, 496, 24);
        UiText(panel, L("참여 인원", "Players") + "  " + (authenticated ? "2/2" : "1/2"), 18, Color.white, 32, 241, 496, 26);
        UiPanel(panel, "Host player", 32, 285, 496, 54, Hex("171717"), true, Hex("3A3A3A"));
        UiText(panel, playerOne.Name + "    " + L("호스트", "Host"), 18, Hex("2ECC71"), 48, 299, 464, 26);
        UiPanel(panel, "Guest player", 32, 349, 496, 54, Hex("171717"), true, Hex("3A3A3A"));
        UiText(panel, authenticated ? playerTwo.Name : L("상대 대기 중", "Waiting for opponent"), 18, Color.white, 48, 363, 464, 26);
        var start = UiButton(panel, L("게임 시작", "Start Game"), 32, 427, 242, 44, () => { matchStarted = true; uiPage = "game"; round++; ResetRound(); });
        start.interactable = mode == SessionMode.Host && authenticated;
        UiButton(panel, L("방 나가기", "Leave Room"), 286, 427, 242, 44, () => LeaveSession());
    }

    private void BuildPauseMenu()
    {
        UiPanel(screenRoot, "Pause overlay", 0, 0, UiWidth, UiHeight, new Color(0, 0, 0, 0.84f));
        float top = UiHeight / 2 - 165;
        UiText(screenRoot, L("메뉴", "Menu"), 40, Orange, UiWidth / 2 - 200, top, 400, 50, TextAnchor.MiddleCenter);
        string resume = Online ? L("재개 준비", "Ready to Resume") : L("계속하기", "Continue");
        if (!RoundOver) UiButton(screenRoot, resume, UiWidth / 2 - 125, top + 80, 250, 54, () => SetMenu(false));
        UiButton(screenRoot, L("설정", "Settings"), UiWidth / 2 - 125, top + 154, 250, 54, () => settingsOpen = true);
        UiButton(screenRoot, L("종료", "Exit"), UiWidth / 2 - 125, top + 228, 250, 54, () => confirmExit = true);
        if (RoundOver)
        {
            int localVote = mode == SessionMode.Client ? 2 : 1;
            bool waiting = Online && (rematchVotes & localVote) != 0;
            string label = waiting ? L("상대 수락 대기 중", "Waiting for acceptance") : rematchVotes != 0 ? L("재시작 수락", "Accept Rematch") : L("재시작 요청", "Request Rematch");
            var button = UiButton(screenRoot, label, UiWidth / 2 - 125, top + 80, 250, 54, () => RequestRematch(true));
            button.interactable = !waiting;
            if (Online && rematchVotes != 0) UiButton(screenRoot, L("재시작 취소", "Cancel Rematch"), UiWidth / 2 - 125, top + 302, 250, 50, () => RequestRematch(false));
        }
    }

    private void BuildExitModal()
    {
        var overlay = UiPanel(screenRoot, "Confirm overlay", 0, 0, UiWidth, UiHeight, new Color(0, 0, 0, 0.7f));
        var panel = UiPanel(overlay, "Confirm exit", UiWidth / 2 - 200, UiHeight / 2 - 125, 400, 250, Hex("1E1E1E"), true, Orange);
        UiText(panel, L("알림", "Notice"), 24, Orange, 30, 30, 340, 32, TextAnchor.MiddleCenter);
        UiText(panel, L("게임을 종료하시겠습니까?", "Exit the game?"), 18, Color.white, 30, 90, 340, 35, TextAnchor.MiddleCenter);
        UiButton(panel, L("예", "Yes"), 30, 163, 165, 52, () => {
            confirmExit = false;
            if (mode != SessionMode.Menu) LeaveSession();
            else {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        });
        UiButton(panel, L("아니오", "No"), 205, 163, 165, 52, () => confirmExit = false);
    }

    private void BuildSettings()
    {
        UiPanel(screenRoot, "Settings background", 0, 0, UiWidth, UiHeight, Hex("111111"));
        float sidebarWidth = UiWidth < 900 ? 220 : 280;
        var sidebar = UiPanel(screenRoot, "Settings sidebar", 0, 0, sidebarWidth, UiHeight, Hex("222222"));
        UiText(sidebar, L("설정", "Settings"), 32, Orange, 20, 30, sidebarWidth - 40, 38, TextAnchor.MiddleCenter);
        UiPanel(sidebar, "Divider", 20, 77, sidebarWidth - 40, 2, Hex("444444"));
        string[] tabs = { "controls", "audio", "general" };
        string[] labels = { L("조작", "Controls"), L("오디오", "Audio"), L("일반", "General") };
        for (int i = 0; i < 3; i++)
        {
            string tab = tabs[i];
            var button = UiButton(sidebar, labels[i], 20, 121 + i * 62, sidebarWidth - 40, 50, () => settingsTab = tab);
            if (settingsTab == tab) SetButtonColor(button, Orange, Hex("111111"));
        }
        var back = UiButton(sidebar, L("돌아가기", "Back"), 20, UiHeight - 80, sidebarWidth - 40, 50, () => settingsOpen = false);
        UiButton(sidebar, L("설정 초기화", "Reset Settings"), 20, UiHeight - 142, sidebarWidth - 40, 50, ConfirmResetSettings);
        SetButtonColor(back, Hex("A93226"), Color.white);
        float contentX = sidebarWidth + (UiWidth < 900 ? 30 : 60);
        float contentWidth = UiWidth - contentX - (UiWidth < 900 ? 30 : 60);
        UiText(screenRoot, labels[Array.IndexOf(tabs, settingsTab)], 28, Color.white, contentX, 40, contentWidth, 35);
        UiPanel(screenRoot, "Heading divider", contentX, 78, contentWidth, 1, Hex("333333"));
        var box = UiPanel(screenRoot, "Settings content", contentX, 104, contentWidth, UiHeight - 118, Hex("1E1E1E"), true, Hex("333333"));
        var viewport = UiRect(box, "Viewport", 30, 30, contentWidth - 60, UiHeight - 178);
        viewport.gameObject.AddComponent<RectMask2D>();
        var scroll = box.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 30;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        float innerWidth = contentWidth - 100;
        var content = UiRect(viewport, "Scrollable rows", 20, 10, innerWidth, settingsTab == "controls" ? 1600 : 840);
        scroll.content = content;
        if (settingsTab == "controls") BuildControlSettings(content, innerWidth);
        else if (settingsTab == "audio") BuildAudioSettings(content, innerWidth);
        else BuildGeneralSettings(content, innerWidth);
    }

    private void BuildControlSettings(RectTransform content, float width)
    {
        string[] ko = { "위보기", "아래보기", "왼쪽 이동", "오른쪽 이동", "점프", "대쉬", "일반 공격", "강공격", "가드 (방어)", "잡기", "필살기", "메뉴 열기 / 취소" };
        string[] en = { "Look Up", "Look Down", "Move Left", "Move Right", "Jump", "Dash", "Normal Attack", "Heavy Attack", "Guard", "Grab", "Ultimate", "Open Menu / Cancel" };
        float y = 30;
        for (int i = 0; i < ko.Length; i++)
        {
            if (i == 0 || i == 6 || i == 11)
            {
                UiText(content, i == 0 ? L("이동", "Movement") : i == 6 ? L("전투", "Combat") : L("시스템", "System"), 22, Orange, 0, y, width, 32);
                UiPanel(content, "Section divider", 0, y + 42, width, 2, Hex("444444"));
                y += 64;
            }
            var row = UiPanel(content, "Binding row", 0, y, width, 86, Hex("292929"), true);
            UiText(row, L(ko[i], en[i]), 18, Hex("EEEEEE"), 24, 16, width - 220, 54);
            int action = i;
            var button = UiButton(row, i == 11 ? "ESC" : BindingLabel(i), width - 174, 16, 150, 54, () => { editingBinding = action; bindFrame = Time.frameCount; });
            button.interactable = i != 11;
            y += 98;
        }
        content.sizeDelta = new Vector2(width, y + 30);
    }

    private void BuildAudioSettings(RectTransform content, float width)
    {
        UiText(content, L("사운드 볼륨", "Sound Volume"), 22, Orange, 0, 25, width, 32);
        string[] names = { L("마스터 볼륨", "Master Volume"), L("배경 음악 (BGM)", "BGM"), L("효과음 (SFX)", "SFX") };
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            var row = UiPanel(content, "Volume", 0, 90 + i * 98, width, 86, Hex("292929"), true);
            UiText(row, names[i], 18, Color.white, 24, 14, 180, 56);
            float value = PlayerPrefs.GetFloat("WebFight.Volume" + i, i == 0 ? 1 : 0.8f);
            var number = UiText(row, Mathf.RoundToInt(value * 100) + "%", 18, Orange, width - 65, 14, 60, 56);
            var track = UiPanel(row, "Slider", 200, 40, Mathf.Max(40, width - 285), 6, Hex("333333"));
            var slider = track.gameObject.AddComponent<Slider>();
            var handle = UiPanel(track, "Handle", 0, -6, 18, 18, Orange, true);
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.minValue = 0; slider.maxValue = 1; slider.value = value;
            slider.onValueChanged.AddListener(v => { PlayerPrefs.SetFloat("WebFight.Volume" + index, v); number.text = Mathf.RoundToInt(v * 100) + "%"; if (index == 0) AudioListener.volume = v; });
        }
    }

    private void BuildGeneralSettings(RectTransform content, float width)
    {
        UiText(content, L("디스플레이", "Display"), 22, Orange, 0, 25, width, 32);
        SettingRow(content, width, 90, L("해상도", "Resolution"), ResolutionLabel, ShowResolutionOptions);
        ToggleSetting(content, width, 188, L("수직동기화 (V-Sync)", "V-Sync"), QualitySettings.vSyncCount > 0, value => { QualitySettings.vSyncCount = value ? 1 : 0; PlayerPrefs.SetInt("WebFight.VSync", QualitySettings.vSyncCount); });
        UiText(content, L("게임 시스템", "Game System"), 22, Orange, 0, 316, width, 32);
        ToggleSetting(content, width, 372, L("프레임 (FPS) 표시", "Show FPS"), showFps, value => { showFps = value; PlayerPrefs.SetInt("WebFight.Fps", value ? 1 : 0); });
        ToggleSetting(content, width, 470, L("핑 (PING) 표시", "Show Ping"), showPing, value => { showPing = value; PlayerPrefs.SetInt("WebFight.Ping", value ? 1 : 0); });
        SettingRow(content, width, 568, L("언어 설정 (Language)", "Language"), new[] { "한국어", "English", "日本語" }[language], () => ShowOptions(L("언어 설정 (Language)", "Language"), new[] { "한국어", "English", "日本語" }, language, index => { language = index; PlayerPrefs.SetInt("WebFight.Language", index); PlayerPrefs.Save(); uiSignature = ""; }));
    }

    private void SettingRow(Transform parent, float width, float y, string title, string value, Action action)
    {
        var row = UiPanel(parent, title, 0, y, width, 86, Hex("292929"), true);
        UiText(row, title, 18, Color.white, 24, 16, width - 272, 54);
        UiButton(row, value, width - 224, 16, 200, 54, action);
    }

    private void BuildBindingModal()
    {
        var overlay = UiPanel(screenRoot, "Key wait overlay", 0, 0, UiWidth, UiHeight, new Color(0, 0, 0, 0.75f));
        var box = UiPanel(overlay, "Key wait", UiWidth / 2 - 260, UiHeight / 2 - 120, 520, 240, Hex("222222"), true, Orange);
        if (invalidBindingKey)
        {
            UiText(box, L("알림", "Notice"), 26, Orange, 40, 25, 440, 42, TextAnchor.MiddleCenter);
            UiText(box, L("지정할 수 없는 단축키입니다.", "This key cannot be assigned."), 18, Color.white, 40, 78, 440, 64, TextAnchor.MiddleCenter);
            UiButton(box, L("확인", "OK"), 157, 163, 206, 52, ResumeBindingCapture);
            return;
        }
        if (pendingBindingKey != UnassignedBinding)
        {
            UiText(box, L("알림", "Notice"), 26, Orange, 40, 25, 440, 42, TextAnchor.MiddleCenter);
            UiText(box, L("이 키는 이미 지정되어 있습니다. 바꾸시겠습니까?", "This key is already assigned. Replace it?"), 18, Color.white, 40, 78, 440, 64, TextAnchor.MiddleCenter);
            UiButton(box, L("예", "Yes"), 40, 163, 205, 52, () => ApplyBinding(pendingBindingKey));
            UiButton(box, L("아니오", "No"), 275, 163, 205, 52, CancelBindingEdit);
            return;
        }
        UiText(box, L("키 입력 대기 중", "Waiting for input"), 26, Orange, 40, 35, 440, 42, TextAnchor.MiddleCenter);
        UiText(box, L("변경할 키나 마우스 버튼을 누르세요.", "Press a key or mouse button."), 18, Color.white, 40, 103, 440, 36, TextAnchor.MiddleCenter);
        UiText(box, L("취소하려면 ESC를 누르세요.", "Press ESC to cancel."), 14, Hex("888888"), 40, 168, 440, 25, TextAnchor.MiddleCenter);
    }

    private void HandleInterfaceInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (editingBinding >= 0)
        {
            if (keyboard.escapeKey.wasPressedThisFrame) { CancelBindingEdit(); return; }
            if (pendingBindingKey != UnassignedBinding || invalidBindingKey) return;
            if (Time.frameCount <= bindFrame + 1) return;
            int key = 0;
            foreach (var control in keyboard.allKeys) if (control.wasPressedThisFrame) { key = (int)control.keyCode; break; }
            if (IsShiftedDigit(key, keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)) key = -3;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) key = -1;
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) key = -2;
            if (Mouse.current != null && (Mouse.current.middleButton.wasPressedThisFrame || Mouse.current.forwardButton.wasPressedThisFrame || Mouse.current.backButton.wasPressedThisFrame)) key = -3;
            SelectBindingKey(key);
            return;
        }
        if (!keyboard.escapeKey.wasPressedThisFrame) return;
        if (confirmExit) confirmExit = false;
        else if (settingsOpen) settingsOpen = false;
        else if (mode == SessionMode.Menu) uiPage = "main";
        else if (!Online || matchStarted) SetMenu(!menuOpen);
    }

    private void SelectBindingKey(int key)
    {
        if (editingBinding < 0 || key == UnassignedBinding || invalidBindingKey || pendingBindingKey != UnassignedBinding) return;
        if (!AllowedBinding(key)) { invalidBindingKey = true; return; }
        int owner = Array.IndexOf(bindings, key);
        if (owner >= 0 && owner != editingBinding) pendingBindingKey = key;
        else ApplyBinding(key);
    }

    private static bool IsShiftedDigit(int key, bool shiftHeld)
    {
        // Shift + top-row digit is a symbol, not a plain numeric binding.
        return shiftHeld && key >= (int)Key.Digit1 && key <= (int)Key.Digit0;
    }

    private void ApplyBinding(int key)
    {
        if (editingBinding < 0 || !AllowedBinding(key)) return;
        for (int i = 0; i < bindings.Length; i++)
        {
            if (i == editingBinding || bindings[i] != key) continue;
            bindings[i] = UnassignedBinding;
            PlayerPrefs.SetInt("WebFight.Key" + i, UnassignedBinding);
        }
        bindings[editingBinding] = key;
        PlayerPrefs.SetInt("WebFight.Key" + editingBinding, key);
        PlayerPrefs.Save();
        CancelBindingEdit();
    }

    private void CancelBindingEdit()
    {
        editingBinding = -1;
        pendingBindingKey = UnassignedBinding;
        invalidBindingKey = false;
        uiSignature = "";
    }

    private void ResumeBindingCapture()
    {
        invalidBindingKey = false;
        bindFrame = Time.frameCount;
        uiSignature = "";
    }

    private void LoadBindings()
    {
        bool changed = false;
        for (int i = 0; i < bindings.Length; i++)
        {
            int saved = PlayerPrefs.GetInt("WebFight.Key" + i, DefaultBindings[i]);
            bindings[i] = AllowedBinding(saved) ? saved : UnassignedBinding;
            if (bindings[i] != UnassignedBinding && Array.IndexOf(bindings, bindings[i], 0, i) >= 0)
                bindings[i] = UnassignedBinding;
            if (bindings[i] != saved)
            {
                PlayerPrefs.SetInt("WebFight.Key" + i, bindings[i]);
                changed = true;
            }
        }
        if (changed) PlayerPrefs.Save();
        AudioListener.volume = PlayerPrefs.GetFloat("WebFight.Volume0", 1);
    }

    private string BindingLabel(int action)
    {
        int value = bindings[action];
        if (value == UnassignedBinding) return L("지정되지 않음", "Unassigned");
        if (value >= (int)Key.Digit1 && value <= (int)Key.Digit0) return ((value - (int)Key.Digit1 + 1) % 10).ToString();
        if (value >= (int)Key.Numpad0 && value <= (int)Key.Numpad9) return "Num " + (value - (int)Key.Numpad0);
        return value == -1 ? "L-Click" : value == -2 ? "R-Click" : value == (int)Key.LeftShift ? "L-Shift" : value == (int)Key.RightShift ? "R-Shift" : ((Key)value).ToString();
    }

    private bool Bound(int action)
    {
        int value = bindings[action];
        if (!AllowedBinding(value)) return false;
        if (value < 0) return IsMousePressed(Mouse.current, value == -1 ? MouseButton.Left : MouseButton.Right);
        return IsKeyPressed(Keyboard.current, (Key)value);
    }

    private RectTransform UiRect(Transform parent, string name, float x, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); Place(rect, x, y, width, height); return rect;
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private RectTransform UiPanel(Transform parent, string name, float x, float y, float width, float height, Color color, bool rounded = false, Color? border = null)
    {
        var rect = UiRect(parent, name, x, y, width, height);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = border ?? color;
        image.sprite = rounded ? RoundedSprite() : GetPixelSprite();
        image.type = rounded ? Image.Type.Sliced : Image.Type.Simple;
        if (border.HasValue)
        {
            var inner = UiPanel(rect, "Fill", 1, 1, width - 2, height - 2, color, rounded);
            inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
            inner.offsetMin = Vector2.one; inner.offsetMax = -Vector2.one;
            inner.GetComponent<Image>().raycastTarget = false;
        }
        return rect;
    }

    private Text UiText(Transform parent, string text, int size, Color color, float x, float y, float width, float height, TextAnchor align = TextAnchor.MiddleLeft)
    {
        var label = UiRect(parent, text.Length > 0 ? text : "Text", x, y, width, height).gameObject.AddComponent<Text>();
        label.font = uiFont; label.fontSize = size; label.color = color; label.text = text; label.alignment = align;
        label.raycastTarget = false; label.horizontalOverflow = HorizontalWrapMode.Wrap;
        return label;
    }

    private Button UiButton(Transform parent, string text, float x, float y, float width, float height, Action action, bool main = false)
    {
        var rect = UiPanel(parent, text, x, y, width, height, Hex(main ? "2C2C2C" : "333333"), true, Hex(main ? "444444" : "555555"));
        var button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        var label = UiText(rect, text, main ? 20 : 18, Color.white, 10, 0, width - 20, height, TextAnchor.MiddleCenter);
        label.fontStyle = FontStyle.Normal;
        button.onClick.AddListener(() => action());
        var hover = rect.gameObject.AddComponent<WebFightButtonStyle>();
        hover.Configure(rect.GetComponent<Image>(), rect.GetChild(0).GetComponent<Image>(), label, main);
        return button;
    }

    private void SetButtonColor(Button button, Color background, Color text)
    {
        button.GetComponent<WebFightButtonStyle>().SetBase(background, text);
    }

    private Image UiBar(Transform parent, string name, float x, float y, float width, float height, int borderWidth, Color border, Color fill)
    {
        var rect = UiPanel(parent, name, x, y, width, height, border);
        var empty = UiPanel(rect, "Empty", borderWidth, borderWidth, width - 2 * borderWidth, height - 2 * borderWidth, Hex("151515"));
        empty.anchorMin = Vector2.zero; empty.anchorMax = Vector2.one;
        empty.offsetMin = new Vector2(borderWidth, borderWidth); empty.offsetMax = new Vector2(-borderWidth, -borderWidth);
        var inner = UiPanel(rect, "Value", borderWidth, borderWidth, width - 2 * borderWidth, height - 2 * borderWidth, fill);
        inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(borderWidth, borderWidth); inner.offsetMax = new Vector2(-borderWidth, -borderWidth);
        var image = inner.GetComponent<Image>(); image.type = Image.Type.Filled; image.fillMethod = Image.FillMethod.Horizontal; return image;
    }

    private void UiField(Transform parent, string title, string value, float x, float y, float width, int maxLength, Action<string> changed, bool password = false)
    {
        UiText(parent, title, 16, Hex("E8E8E8"), x, y, width, 24);
        var rect = UiPanel(parent, title + " Input", x, y + 32, width, 48, Hex("121212"), true, Hex("4A4A4A"));
        var field = rect.gameObject.AddComponent<WebFightInputField>();
        field.textComponent = UiText(rect, "", 18, Color.white, 14, 0, width - 28, 48);
        field.targetGraphic = rect.GetComponent<Image>(); field.characterLimit = maxLength;
        field.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
        field.text = value; field.onValueChanged.AddListener(v => changed(v));
    }

    private static Sprite RoundedSprite()
    {
        if (roundedSprite != null) return roundedSprite;
        var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            float dx = Mathf.Max(8 - (x + 0.5f), x + 0.5f - 24, 0);
            float dy = Mathf.Max(8 - (y + 0.5f), y + 0.5f - 24, 0);
            float alpha = Mathf.Clamp01(8.5f - Mathf.Sqrt(dx * dx + dy * dy));
            texture.SetPixel(x, y, new Color(1, 1, 1, alpha));
        }
        texture.Apply();
        roundedSprite = Sprite.Create(texture, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(9, 9, 9, 9));
        return roundedSprite;
    }
}

public sealed class WebFightInputField : InputField
{
    protected override void Append(string input)
    {
        NormalizeCaretForIme();
        base.Append(input);
    }

    protected override void Append(char input)
    {
        NormalizeCaretForIme();
        base.Append(input);
    }

    private void NormalizeCaretForIme()
    {
        int length = string.IsNullOrEmpty(m_Text) ? 0 : m_Text.Length;
        int caret = m_CaretPosition;
        int selection = m_CaretSelectPosition;
        NormalizeCaretForIme(UnityEngine.Input.compositionString, length, ref caret, ref selection);
        m_CaretPosition = caret;
        m_CaretSelectPosition = selection;
    }

    internal static void NormalizeCaretForIme(string composition, int length, ref int caret, ref int selection)
    {
        length = Mathf.Max(0, length);
        caret = Mathf.Clamp(caret, 0, length);
        selection = Mathf.Clamp(selection, 0, length);

        // Legacy uGUI adds the active IME composition length to both caret values.
        // A stale selection can then point past m_Text and make Append throw.
        if (!string.IsNullOrEmpty(composition))
        {
            caret = selection = Mathf.Min(caret, selection);
        }
    }
}

public sealed class WebFightButtonStyle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Image border, fill;
    private Text label;
    private Button button;
    private bool main, hovered;
    private Color baseFill, baseText, baseBorder;
    public void Configure(Image outer, Image inner, Text text, bool mainMenu)
    { border = outer; fill = inner; label = text; button = GetComponent<Button>(); main = mainMenu; baseFill = fill.color; baseText = label.color; baseBorder = border.color; }
    public void SetBase(Color background, Color text) { baseFill = background; baseText = text; }
    public void OnPointerEnter(PointerEventData data) { hovered = true; }
    public void OnPointerExit(PointerEventData data) { hovered = false; }
    private void Update()
    {
        bool enabled = button.interactable;
        Color orange = new Color(243f / 255, 156f / 255, 18f / 255);
        float t = 1 - Mathf.Exp(-18 * Time.unscaledDeltaTime);
        border.color = Approach(border.color, hovered && enabled ? orange : baseBorder, t);
        fill.color = Approach(fill.color, hovered && enabled ? main ? new Color(0.22f, 0.22f, 0.22f) : orange : baseFill, t);
        label.color = Approach(label.color, !enabled ? new Color(0.4f, 0.4f, 0.4f) : hovered ? main ? orange : new Color(0.067f, 0.067f, 0.067f) : baseText, t);
    }
    private static Color Approach(Color current, Color target, float t)
    {
        // Finish transitions exactly so idle buttons stop dirtying the canvas forever.
        return ((Vector4)(current - target)).sqrMagnitude < 0.00001f ? target : Color.Lerp(current, target, t);
    }
}
