# Sentinel Remote — remembered setup candidate 0.4.0

Keep Tailscale installed and connected on the PC and phone. Sentinel uses that existing account/network; no separate Sentinel account or hosted login service is introduced. The normal workflow is pair/trust once, then open Desktop and use the gamepad after the approved character connects.

## Update an existing installation

1. Exit the PC app from its tray and install Windows **0.4.0** over it. Existing pairing, character bindings, launcher selection and Tailscale preferences remain. Keep the working Relay DLL and controller driver.
2. Select your existing control phone and click **Trust selected phone** once. This visibly grants and remembers desktop, gamepad and launcher access. Upgrading never silently upgrades old pairings to full-PC trust.
3. Install Android Preview **0.4.0**. CI debug signing is still temporary: if Android refuses the update, uninstall only **Sentinel Remote Preview**, install the new APK and pair again.
4. Open **Desktop** on the phone. The PC prepares the pinned signed controller component automatically; no separate Prepare/Allow clicks are needed. An initial download may take time. **More options > Retry controller setup** handles a failed download without disabling Desktop.
5. Optional: check **Start with Windows**. Closing the PC window keeps it running in the tray. Reopening the PC app automatically resumes the saved, enabled connection only when its Tailscale host/account match.

## First setup on another PC

1. Install/sign in to official Tailscale on both devices. Install Sentinel Remote on the PC and Sentinel Remote Preview on Android. iPhone distribution is not implemented yet.
2. On the PC click **Connect securely**, completing Tailscale's initial private HTTPS consent if needed. This remains private Serve, never Funnel or port forwarding.
3. Click **Pair a phone** with the visible trust option checked. On the phone choose **Pair a PC** and paste the single setup code. Pairing works before FFXIV opens. The code expires in two minutes and is a bearer invitation: keep it private.
4. Desktop is ready without a game. For game controls, enable all mobile features once in Relay, log in, then **Connect selected character** once on the PC. Trusted phones receive characters explicitly approved on that PC; view/rescue-only phones keep their original grants.
5. Install the official signed USBip x64 controller driver once if this PC has not passed Gamepad Check. A PC that already passed needs no driver reinstall. The separately managed worker/backend starts under Sentinel; it has no additional application window to operate.

## Everyday use

The phone remembers the last selected PC and reconnects status automatically. Tap **Desktop** to see/control its primary monitor. Tap selects/clicks, double-tap opens, long-press right-clicks; pinch to zoom/pan. Use the keyboard icon for transient text, or Start/Tab/Enter/Escape/Backspace/scroll keys.

Open XIVLauncher (or another ordinary app/file) through the visible desktop; operate login and character selection yourself. The optional **Open XIVLauncher** shortcut requires selecting the installed launcher once in PC More options.

Desktop checks for approved connected characters every five seconds. Its gamepad icon becomes available after Relay connects, without leaving Desktop or refreshing Home. **Start gamepad** remains explicit. Portrait places the view above controls; landscape places it beside them. Gamepad still requires exactly one verified FFXIV process, matching character/session and Relay mobile/control opt-ins. Enable FFXIV's inactive-client gamepad setting for minimized input.

View reconnects automatically with backoff while open. Returning from phone background resumes viewing only; gamepad input never reacquires automatically, and no mouse/key/text/rescue action is replayed. Explicit **Stop desktop** stays stopped after background/resume. **Back** closes the view.

## Stop and revoke

**Pause desktop + gamepad** clears saved full-PC trust, cancels outstanding full-PC invitations and stops desktop/gamepad/launcher access. That pause survives PC restarts; locally trust the phone again to resume. Game status and existing rescue grants are separate. **Revoke selected phone** removes all access for that credential. The always-reachable **Disable all remote access** stops the host and prevents automatic startup from enabling it again.

Full-PC trust is stored in the same owner-only atomic authority state as paired device hashes, never in phone-supplied fields. Network pairing cannot request broader rights than its locally created invitation. View-only devices cannot be trusted for control. Native controller/frame/watchdog/revocation checks remain unchanged.

## Bounds and acceptance

Windows must stay awake and unlocked. The primary monitor is mirrored at up to three FPS, 1280×720/320 KiB per in-memory JPEG; one view per PC. Input has bounded messages, increasing socket sequences, recent frame IDs and complete down/up batches. No disk recording, clipboard sync, remote shell/filesystem API or stored typed credentials. Secure/elevated/locked desktops remain unavailable.

Real-device evidence: the user confirmed primary desktop viewing and touch-to-mouse positioning in 0.3.0, plus earlier actual gamepad movement/camera and mobile-data screenshots. New 0.4.0 restart/trust/pause and automatic game handoff still require testing. Actual launcher typing/login/character selection, full combined gamepad session, monitor standby/off and native controller failure/coexistence checks remain open. A powered-off/disconnected monitor may behave differently from standby; no virtual display is installed. Production catalog and releases remain held.
