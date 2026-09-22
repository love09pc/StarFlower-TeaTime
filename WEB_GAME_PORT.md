# Web Game Unity Port

Source reference: `/Users/hyun_woo/Desktop/project/WebGame-React/Web-RTC-Game`.
The web project is unchanged. Runtime code and copied art live in `Assets/Scripts/WebGamePort` and `Assets/Resources/WebGamePort`.

## Run

Open `Assets/Scenes/SampleScene.unity` and press Play. `WebFightBootstrap` creates the game and native Unity UI automatically. The original `asd` menu, PF Stardust font, buttons, settings sidebar, room screens, HUD, floor and idle sprite are reproduced from the current web UI.

Mac and Windows are locked to borderless fullscreen, with proportional UI scaling using a 1600 x 900 reference canvas. Settings > General offers a saved rendering-resolution list; the native display resolution is the first-launch default. Unity applies resolution changes asynchronously, so the actual applied size is saved after the change. Editor Game View resolution is controlled by the Editor, not Screen.SetResolution. Combat keeps a centered 16:9 viewport and the same world-space field of view regardless of display shape. Extra width or height becomes black bars. Local two-player zoom still responds only to fighter separation.

## Distribution Targets

- Windows x64 is the planned Steam release target.
- macOS is a separate private build for the developer and friends; it is not intended for the Steam release. The Mac build contains both Intel x86_64 and Apple Silicon arm64.
- Both targets use the same LAN protocol, intended for cross-platform play; a physical Windows/Mac match still needs testing. Steam publishing, Steamworks, invitations and internet relay are not configured by this port.
- Build output directories are `Builds/Windows` and `Builds/macOS`. Keep the Windows executable together with its data directory and DLLs when sharing it.
- Repeatable Unity menu commands: `Web Fight > Build > Windows x64 - Steam Target` and `Web Fight > Build > macOS Universal - Private`. These create local builds only; they do not upload to Steam.

### Build Status (2026-09-17)

- macOS universal build includes the Korean IME fix, training HP/label/color changes, fullscreen resolution selection, paused animation, Japanese, and separate FPS/PING displays. Private sharing archive: `Builds/WebFight-macOS-private.zip`. Standalone Mac resolution changes at 1280x720, 1920x1080 and 3600x2260 were verified with fullscreen and fixed 16:9 camera bounds. Full cross-device gameplay still needs testing.
- Windows x64 Mono build completed with zero build errors after the Editor recognized Windows Build Support. Output: `Builds/Windows/WebFight.exe`. Sharing archive: `Builds/WebFight-Windows-x64.zip`, including runtime dependencies and Korean instructions in `Windows/README.txt`. Extract the full archive before running the EXE; Unity installation is not required. Actual Windows execution and a physical Mac/Windows match still need testing.
- Build menu validation checks the report's error count and output existence, not just its success enum.

## LAN Match

1. Connect both computers to the same Wi-Fi/LAN and run the game on each.
2. On the host choose **방 호스팅하기**, enter player/room names and an optional room password, then **방 만들기**.
3. On the guest choose **참여하기**, select the discovered room, enter the same password if needed, then **접속하기**.
4. The host chooses **게임 시작** after both players appear.
5. If broadcast discovery is unavailable, choose **IP 직접 입력** and use the host's IPv4 address shown in its lobby. The game port is UDP 7777; discovery uses UDP 47777. Allow the executable through the local firewall. Wi-Fi client isolation prevents direct LAN communication.

The web HTTP backend is not required. Room listing now uses local UDP discovery; internet relay/matchmaking is not included.

### Test on one Mac

1. Run the standalone Mac player and also enter Play mode in Unity.
2. Host a room in one window. In the other window choose **참여하기** and select the discovered room.
3. If the room is not listed, choose **IP 직접 입력** and enter `127.0.0.1` with port `7777`.
4. Start the match from the host. Only the focused window receives keyboard and mouse input, so switch focus when controlling the other player.

This confirms the complete room and game flow on one computer. A final release check should still use two physical computers on the same Wi-Fi/LAN.

## Controls

Both online players use their own keyboard/mouse with the original bindings:

| Action | Default |
| --- | --- |
| Move/look | W, A, S, D |
| Jump/double jump/wall jump | Space |
| Dash | Left Shift |
| Normal / heavy attack | Left / right mouse button |
| Guard | F |
| Grab / keep holding | E |
| Escape a grab | W, A, S, D twice |
| Pause/settings | Escape |
| Reset training round | R |

Bindings are editable in **설정 > 조작** and persist locally. Resolution, VSync, separate FPS/PING toggles, language (Korean/English/Japanese), and audio volume preferences are available. FPS is sampled over 250 ms and appears below the local player's guard. PING occupies the next line (or first line if FPS is off); both host and guest measure round-trip time with sequence-validated ping/pong packets every second. Offline or stale measurements display --. These texts never share the round-status label. Training pause and settings freeze both simulation and character animation, while networking and menus keep updating. The source contains no BGM/SFX assets; category volume values are retained for future assets. Training uses the original red dummy. Local two-keyboard-player simulation remains available internally for verification.

In training, lethal damage immediately refills the dummy to its maximum HP without ending the round or resetting positions, grabs, or launch velocity. Training names appear only in the top HUD, not over character bodies. Character sprites retain their original colors while guarding and during hit stop; combat timing and guard behavior are unchanged.

## Combat and Networking

### September 21 Update

- Binding whitelist includes only letters, top-row/numpad digits, Left Shift, Space and left/right mouse buttons. Punctuation/operator keys and F1-F12 are forbidden; Shift + top-row digits are rejected during capture rather than registered as plain numbers. Other captured inputs show a dismissible invalid-shortcut warning without changing assignments. Escape remains cancel. Previously saved forbidden keys are persisted as unassigned and produce no input. Updated verification passed 128 binding checks, 50 rule checks and 684 presentation assertions.
- Duplicate key selection requires confirmation. Accepting transfers the key and leaves the previous action unassigned; declining or Escape changes neither action. Unassigned actions return no input, persist across restarts, and can be rebound normally. Korean, English and Japanese labels are included.
- Guard prevents starting normal/heavy/ultimate attacks, dash and grab, including simultaneous guard/action presses. Grab owners and grabbed targets cannot guard, including during hit-stop; releasing guard permits a fresh action normally.
- Verification: 39 combat/protocol checks, 667 presentation assertions, 50 combat/rule checks and 14 binding checks passed. UDP loopback and the two-session packet-loss test passed. Conflict and unassigned UI screenshots are in `Captures/key-*-20260921.png`. Physical Mac-to-Windows play remains a separate user test.

### September 18 Update

- Protocol/discovery version 3: both players must update together. Old builds do not join new rooms.
- Mac camera follows the same smoothed position as the rendered local player instead of unsmoothed reconciled snapshots. Unused HDR/MSAA are disabled, hidden menu-world rendering and duplicate HUD text writes are removed, and completed hover transitions stop dirtying the canvas.
- Resolution choices no longer shrink to the current render resolution. Desktop dimensions are cached separately; the first launch of this update restores native resolution to recover incorrectly persisted low-resolution defaults.
- Initial UI stays hidden until fullscreen sizing and canvas scaling settle. The world camera renders no fighters before gameplay. Rounded textures clamp at their edges and inner fills stretch with their borders.
- LAN discovery queries each active subnet's directed broadcast as well as limited broadcast and loopback. Failed broadcasts are isolated and rate-limited; discovery receive polling runs at 30 Hz. Mac builds include NSLocalNetworkUsageDescription. System permission/firewall and real cross-device discovery still require testing on the user's LAN.
- Settings reset is confirmed and only clears this game's preferences. Rebinding permits A-Z, Space, either Shift, and left/right mouse buttons. Ultimate defaults to Q.
- Host-authoritative 360-tick resume countdown; new pauses cancel and restart it. Rematch requires both players' votes and starts with another countdown. Reliable requests carry the current round ID to reject stale messages. The R shortcut only resets training.
- Contact resolution is two-phase and independent of player order: normal/normal cancel; heavy/heavy cancel with opposite 18-unit push; grab/grab cancel with 9-unit push. Heavy beats normal, grab beats heavy, normal beats grab, only when both contacts would hit on the same simulation tick.
- Perfect guard lasts 24 simulation ticks (0.2 s), consumes its window on a successful block, and causes no HP/guard damage. Ordinary blocks, unguarded hits, or being grabbed break the streak.
- The random 0.03% proc is removed. The first perfect guard starts a fixed 240-tick deadline, never refreshed by later successes. Four consecutive perfect guards, a grab, an upward throw, then a heavy hit while both players are airborne must finish before it expires. This unlocks Q for 180 ticks. Q uses exactly the heavy attack's directional hitboxes, deals base damage 20 (no low-HP bonus), follows existing guard rules, and grants a 600-tick 5% movement-speed bonus on use. Pauses and resume countdowns do not advance these combat timers.
- W-A-S-D-W-A-S-D escape rejects out-of-order or simultaneous directions. Remote directional presses are queued in sequence so packet batching cannot merge their order; local/remote hit-stop buffering keeps queued escape steps until they can be processed.
- Added 35 combat/rule assertions and expanded full LAN tests for synchronized countdown and rematch consent under 33% input / 25% snapshot loss. Current presentation checks total 639 assertions including Japanese glyphs.
- Standalone Mac short training benchmark (240 frames per resolution, monotonic Stopwatch, 2026-09-18): 1280x720 mean 8.36 ms / p99 8.96 ms; 1920x1080 mean 8.37 ms / p99 9.05 ms; native 3600x2338 mean 8.39 ms / p99 9.30 ms. These are local rendering measurements, not proof of physical Mac/Windows network smoothness or a before/after speedup. Screenshots are in `Captures/standalone-*.png`.

- 120 Hz fixed simulation. The host alone owns HP, hits, grabs, escapes, throws and round state.
- Unity Transport 2.6.0 UDP: 120 Hz input messages, 60 Hz compact binary snapshots; separate reliable control messages.
- Input messages repeat the last 12 unacknowledged frames. Sequence numbers reject duplicate presses and stale snapshots.
- Guest movement prediction, host reconciliation, and visual position smoothing. Combat is authoritative rather than full rollback.
- Grab ownership is paired and cleared on release, timeout, escape, interruption, KO or disconnect. Held positions are resolved after both players move.
- Capturing cancels the victim's attack/dash. Simultaneous action inputs cannot start overlapping attacks or reciprocal grabs.
- Loss of input for 250 ms clears remote movement; loss of session traffic for 5 seconds closes the session.

## Validation

`WebFightVerification.cs` is editor-only. In Play mode, choose `Web Fight > Verify Combat and LAN` from the main menu, or call `BeginVerification()` on the runtime `WebFightBootstrap`; inspect `VerificationResult` or the Console. Run it from the game's main menu because the full-session test creates and tears down temporary sessions.

Verified in Unity 6000.3.24f1:

- 39 combat/protocol/presentation regression assertions, including training HP refills for normal hits, grabs and throws, continued damage after refill, unchanged versus KO, hidden training labels, original sprite colors, Korean IME input safety, both grab directions, timeout, command escape, guard, throw, wall limits, paired ownership, serialization, duplicate inputs and stale/malformed packets.
- Real Unity Transport loopback connection and unreliable/reliable message exchange.
- 578 presentation assertions, including paused movement/combat/animation, settings pause, resume, local-player FPS/PING placement, independent toggles, Japanese translations and complete translated glyph coverage.
- Two complete local game sessions, LAN room discovery, password/name handshake, remote grab, single-damage throw, pause and RTT measurement on both peers, with 33% of input packets and 25% of snapshot packets intentionally discarded.
- Main menu and settings visual checks against the running web build; screenshots in `Captures`.
- Training overlap and original-color visual check: `Captures/training-refill-original-colors.png`.
- Live Game View resizing checks at 1366 x 768, 1920 x 1080, 3840 x 2160, 3600 x 2260, 1280 x 960 and 2560 x 1080; menu/form button bounds checked, plus settings and training screenshots in `Captures/scale-*.png`. These validate shared rendering code in the Editor, not native Windows execution.
- Fixed combat viewport regression: at unchanged fighter positions, 1920 x 1080, an extreme 3600 x 320 window and an 800 x 1200 portrait window all render the same 16 x 9 world-unit area. Projection bounds and HUD/viewport alignment checked; host and client camera presentations also checked. Updated screenshots: `Captures/fixed-view-*.png`.

Testing across two physical computers remains a separate environment check. Loopback loss tests are not a measured guarantee of a particular Wi-Fi network's latency or reliability.

Standalone display regression can be run explicitly with `-webfight-display-check -webfight-capture-dir <absolute-directory>`. It cycles three resolutions, logs actual sizes/fullscreen/camera state, captures the training HUD and Japanese settings, restores the saved resolution preference, then exits. It is never activated during a normal launch.
