# Remote foundation security and protocol v1

## Desktop recovery scope — user clarification, 2026-10-20 UTC

The user explicitly requested full desktop mirroring and manual touch/keyboard control to open ordinary apps/files, operate XIVLauncher, click Log In and select a character. Once logged in, the same view combines with a small joystick/button panel for rescue and target/menu selection, not general gameplay. This supersedes the historical game-window-only and no-desktop-input scope below. Desktop access is a separate, default-off local per-phone permission; it does not depend on FFXIV, Dalamud or a character heartbeat.

The 0.3.0 candidate mirrors the primary monitor at up to 3 FPS with bounded in-memory JPEG frames (1280×720, 320 KiB). Taps, double-taps, long-press right-click and bounded Unicode text/special keys let the user operate ordinary windows. No filesystem/command-shell API is added; opening other apps/files happens through the visible Windows UI. Credentials intentionally typed by the user are sent once, held transiently and never logged/persisted. Desktop content is intentionally visible to the approved phone, including login screens; secure Windows/UAC/locked desktops remain inaccessible.

The original selected-game screenshot and character-bound gamepad paths remain distinct. Desktop viewing does not authorize unbound gamepad input. Existing private Serve, pairing, live control-device/session/revocation checks and local emergency Disable continue to apply. Desktop authorization uses bounded 40 ms gate acquisition so simultaneous desktop and gamepad readers do not deny each other; longer contention fails closed, and the independent native watchdogs are unchanged.

Desktop connections contain at most one pending capture and one input request; no input is automatically retried. Server request sequence, a recent unguessable displayed-frame ID, geometry/session checks and complete native down/up batches bound input. A few recent frames are accepted for at most two seconds to cover one capture in flight. View reconnect is read-only; it clears pending actions and never reacquires held gamepad input. Native capture/input, DPI, monitor-off and the actual launcher/login/menu handoff still require supervised field acceptance.


## Milestone A implementation update

The host now supports explicitly bound game clients through a default-off per-character local pipe server in the Relay plugin. The Android app exists in SentinelRemote. Earlier Phase 1 statements below are the historical foundation boundary; these additions apply to the 0.6.0.0 candidate:

- Local `bind INSTANCE_ID GAME_PID` verifies FFXIV executable, PID/start time and Windows-reported named-pipe server PID, then stores the expected character key. The phone cannot submit a PID/path/window handle. Rebinding rotates the host epoch and invalidates sessions. The desktop wrapper can automatically rebind only an unambiguous, opted-in process reporting the saved character identity from the saved executable path; a successful rebind rotates the host epoch. Changed/ambiguous identity requires local selection.
- The plugin owns a current-Windows-user-only named pipe, not an external network listener. The host is the client and verifies the server PID; this reverses the proposed pipe direction while preserving the same trusted-local-user boundary. No remote pipe name or target path is accepted. Same-user malicious processes remain outside the security boundary.
- Frames are length-prefixed and bounded (4 KiB request, about 10 MB maximum response); schema/operations/version are strict. Each connection carries one operation. A bounded queue submits only to the framework thread. Closing the connection or four-second expiry cancels work not yet dispatched. The host does not retry uncertain mutations.
- Every game request includes the expected character key and live plugin epoch. Logout, pause, disable, permission change and plugin reload stop the bridge or retire its epoch. Reset intent is additionally bound to the observed game epoch. The plugin rechecks current player identity and its independent per-character view/control/screenshot opt-ins on execution.
- Host grants/revocation/disable are rechecked outside the state-file transaction before sending and while awaiting the operation. A local disable/revoke cancels pending IPC; an operation already submitted to the game may finish and cannot be undone. Replies/frames are suppressed if authorization was revoked while waiting.
- Existing SRank IPC functions execute fixed status/skip/reset operations; screenshots reuse the verified current-process game-window capture, cooldown and in-memory PNG path. Capture runs off the framework thread and a second framework identity/permission check precedes delivery. No desktop fallback, generic input or new launcher operation exists.
- Status reports observed process/Relay health with clearly marked last-known game data after loss. Profiles remains unknown; a failed vnavmesh probe remains unknown. A normal process exit is not asserted to be a crash.
- Android uses OS-backed secure storage for device credentials, memory-only sessions/confirmations, no backups/cleartext/redirects, and the secure-window flag. Backgrounding closes network requests and invalidates queued work; refresh never reissues a mutation.

The [Milestone A checklist](MILESTONE_A.md) is the real-device acceptance gate. Manual game input remains unavailable, so the later exclusive-controller/watchdog/tray requirements remain gated.

Scope: private game-rescue companion for explicitly paired devices. The external host is optional and default disabled. Phase 1 does not execute OS input, launch processes, inspect files remotely, connect to the game, or collect incidents. The live Discord plugin's existing security policy remains independent.

## Trust boundaries

1. Phone to PC: Tailscale private connectivity and normal, certificate-validated HTTPS to Serve. Tailnet access rules constrain users/devices; Sentinel additionally requires its own paired device credential.
2. Serve to host: plaintext HTTP on **IPv4 loopback only**, same PC. Exact configured Host and verified `Tailscale-User-Login` header are required. Tailscale strips/replaces its identity headers. This is TLS termination, not end-to-end application encryption beyond the local proxy. Local processes running as the same Windows user are trusted; they can impersonate the local proxy, read process memory and modify local grants. This design does not defend against a compromised Windows account/host or malicious same-user software.
3. Local host state: owner-only directory, no plaintext credentials, exclusive state transactions and atomic replacement. Corruption/locking fails closed. Revocation and emergency-disable state are reread for every request. The current host has no remote administrative endpoints.
4. Local game bridge: current-user pipe, kernel-verified process identity, lifetime/character checks, independent plugin opt-in and framework dispatch. The historical Phase 1 wire descriptions below predate this implemented bridge; the Milestone A update above describes the current candidate.

No public Sentinel service, anonymous status route, cookies, query credentials, URL launch handler, arbitrary command endpoint or remote filesystem API exists. Origin checks reject other websites; content type is JSON; unknown members, missing/null required fields, excessive depth, oversize bodies and wrong protocol versions are rejected. Requests are limited globally to 120/minute, with pairing limited to 10/minute, no queues. Request reading times out after five seconds. Headers/connections/keepalive are bounded. No debug exception page or request/body/authorization logging is enabled.

Tailnet TLS/certificate correctness and actual Serve header forwarding require supervised deployment testing. CI emulates the trusted local proxy and tests the HTTP boundary; it cannot prove real Internet isolation. Do not expose the loopback origin through Funnel or another public proxy. A tailnet administrator or same-user process changing Serve/publication settings is outside the application's trust boundary.

## Local pairing and device lifecycle

The PC user registers opaque installation slots and creates a named invitation with exact slot grants and either view or control permission. Invitations are 256 random bits from .NET `RandomNumberGenerator`, expire after two minutes, are single-use, and are stored only as SHA-256 hashes. An invitation is a deliberate pairing credential, not an anonymous enrollment path. Never issue invites through an unauthenticated remote API.

Enrollment creates an independent random device token and random device ID; the phone receives the credential once through HTTPS. The host persists only the hash, label, grants and revocation state. The future mobile client must use OS-backed secure storage, disable credential backups where practical, and never include credentials in telemetry or debug exports. Credentials are bearer capabilities: extraction from a compromised paired phone permits impersonation until revoked. No hardware-backed device attestation is claimed in Phase 1.

Local `revoke` invalidates existing sessions and future reconnects. `disable` additionally clears invitations and rotates the host authorization epoch. `enable` rotates it again; old sessions remain invalid even if no request arrived while disabled. An initially missing state file creates a disabled authority, never an automatically enabled host. Host restart retains device pairing/grants and loses all sessions/reset intent. Losing a pairing response can consume the invite without delivering the credential; recover locally with a new invite rather than replaying it.

The local `status` command exposes device IDs/labels/grants/revocation, never invitation/token hashes or credentials. The bounded 128-event in-memory audit records only typed outcomes, local opaque device/instance IDs and timestamps. It does not record arbitrary operation text, exceptions, chat, frame data, credentials or command bodies. Audit is not persisted/exported/uploaded in this phase.

## Session and replay rules

Device credentials are accepted only to create a short-lived session; command requests require the separate random session token. Session tokens are held as hashes in memory. There is one current session per device; opening a new one invalidates the previous one and its pending reset. Sessions expire after five minutes absolute or 60 seconds idle, using monotonic time so a wall-clock adjustment cannot extend them. Reset intent also uses monotonic time; persisted invitation timestamps outside their two-minute validity range fail closed. Clients should ping every 15 seconds while active and open a new session after interruption.

Each request includes a strictly increasing positive 64-bit sequence. The authority serializes validation/consumption, so concurrent copies can be accepted only once. Sequence is consumed before dispatch, including denied or failed operations. No action queue survives disconnect, reconnect, restart or an uncertain reply. This provides at-most-once acceptance in a session, not guaranteed delivery or exactly-once external effects. A reconnect starts at sequence 1 with a different token. Never automatically resubmit a skip/reset after a lost reply; read status first.

Reset request produces a random confirmation value tied to the current session and exact instance, valid for 30 seconds. Confirm consumes pending intent before any future game dispatch, including failure; wrong/expired confirmations clear it. View-only devices cannot create/confirm reset or skip. Screenshots and SRank status require view access to the exact locally granted slot. Phase 1 returns `bridge_unavailable` for these operations, with no game action.

Session creation uses ordinary bearer authentication over TLS. A compromised device token can create fresh sessions until local revocation; sequence protection is not a substitute for TLS, secure credential storage or local revocation.

## Wire contract

All requests are JSON POSTs to the private HTTPS origin; no credentials in URLs. UTF-8 bodies are limited to 4096 bytes. Field names below are camelCase. Required fields must be present; unknown fields are not ignored. Path and body versions must both be v1. Replies have `{ "success": boolean, "code": string, "data": object|null }`; errors carry stable codes only. No response is cacheable.

| Route | Authorization | Body |
|---|---|---|
| `/remote/v1/pair` | Tailnet identity plus invitation | `{ "version": 1, "invitation": "64_HEX_CHAR_SECRET" }` |
| `/remote/v1/session` | `Authorization: Bearer DEVICE_TOKEN` | `{ "version": 1, "deviceId": "OPAQUE_ID" }` |
| `/remote/v1/request` | `Authorization: Bearer SESSION_TOKEN` | `{ "version": 1, "sequence": 1, "operation": "instances.list", "instanceId": null, "confirmation": null }` |

Enrollment data contains `deviceId`, `deviceToken`; session data contains `sessionToken`, `expires`, `protocolVersion`. Inventory data contains only locally authorized `instanceId`, `label`, and `relayStatus: "bridge_unavailable"`. No process observation/heartbeat/world/profile data is invented.

Allowlisted operations: `session.ping`, `session.close`, `instances.list`, `srank.status`, `screenshot`, `srank.skip`, `srank.reset.request`, `srank.reset.confirm`. Only the first three have a live host capability; reset request/confirmation tests intent semantics but cannot execute SRank yet. All other operations are rejected, including shell, keyboard, mouse, path, arbitrary chat/plugin commands, launch and administration.

HTTP outcomes: 200 accepted; 400 schema/version/sequence/confirmation/operation errors; 401 unrecognized/revoked/expired credentials or rejected invitation; 403 disabled/unauthorized client/control or failed proxy/origin validation; 404 unknown route; 413 excessive declared body size; 429 budget exhausted; 503 state temporarily unavailable or bridge absent. Unknown/corrupt local state fails closed; it never re-pairs or grants access automatically.

## Requirements before any input is enabled

The backend-independent `ControllerLease` now tests exclusive ownership, complete-state frames, sequence rejection, a monotonic 500 ms frame timeout checked by a 50 ms timer, explicit release/revoke, identity veto and latched emergency/fault handling. It is deliberately unused by the host and has no network input operation or installed backend. Timing is a coordinator policy, not a measured native release guarantee. A blocked/crashed host or adapter requires an independently enforced native-worker/process-loss release mechanism before live input.

Before enabling input, integrate and test live authorization, safe process/character-epoch rebinding, local visible active-control indication and tray emergency release. The first gamepad version must disable and release input whenever the host cannot positively establish exactly one running FFXIV client on the PC, including unbound/non-opted-in clients; observation failure is a veto. Revalidate before every frame and independently during silence. Do not enumerate only approved slots. Prove that another game process/physical controller is not affected. Revoke ownership before switching selected clients and require a new explicit acquire; old frames cannot act on a new owner. No device driver or global input hook is installed by this phase.

## Credentials and recovery exclusions

Sentinel will not read/export XIVLauncher credential storage, persist game passwords/OTP/TOTP secrets, log authentication input, forward it to Discord, or include it in incidents. Launcher launch paths/account choice must be locally configured and allowlisted. A sensitive manual authentication fallback needs verified screen identity, ephemeral handling, no recording and a separate review. No generic desktop, shell or file-management fallback is permitted.
