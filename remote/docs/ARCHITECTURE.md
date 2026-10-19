# Sentinel Remote architecture and feasibility audit

Audit date: 2026-10-19 UTC. Live source outranks the supplied project brief and older chat history. All implementations here are independent MIT code; no streaming, controller, or launcher project source was copied.

## Verified starting point

| Repository | Inspected main commit | Relevant finding |
|---|---|---|
| SentinelRelay | `64544ad9bcc386d6f92a05a963c07625f99604d8` | Published plugin version 0.5.0.7; direct Discord webhooks, optional REST polling, screenshots and SRank controls. No persistent PC backend. |
| Sentinel | `897dc23f11588ea8fa5a4f20eb4ec752f5c7f441` | Catalog, release policy and ecosystem documents confirm the direct Discord boundary. |
| SentinelCore | `e6527400680cddc39803321792b7c3b02fb0501d` | Latest Core release 0.4.1.0. Shared libraries, UI, IPC primitives and navigation; not an installed PC service. Relay currently pins Core/UI 0.3.1; no forced upgrade is needed. |
| SRankSentinel | `e2ea4cbec8f8a1355bafc5690b58a3f4588e2255` | Framework-thread-only, expected-character-bound remote v1 status/skip/reset IPC already exists. |
| SentinelHub | `d1396bfc1b31a6eb1f87b4925e76cea8ed37fc22` | Optional activity/health/diagnostics IPC. Not a remote transport or session authority. |
| SentinelProfiles | `f6288d82a612eac4d16d0963384bee12104d2f33` | Local last-applied profile/DTR presentation. No remote profile-status provider was found in Plugin/DTR source; expose unknown until a versioned read-only provider is added. |

Relay's `BACKEND_DEPLOYMENT.md` explicitly records removal of its earlier Node/TypeScript backend, WebSockets, SQLite routing, pairing codes and client tokens. Do not revive the retired protocol or assume a deployed service exists. Current `/srank status`, `/srank skip`, `/srank reset`, `/srank reset confirm` and `/screenshot` are **ordinary Discord messages**, not registered Discord application slash commands.

Current routing uses `IPlayerState.ContentId`, formatted as `cid:X16`, per-character configuration and exact Discord channel/user allowlists. `SRankRemoteClient` invokes only:

- `SRankSentinel.Remote.v1.Status`
- `SRankSentinel.Remote.v1.SkipCurrentKeepQueue`
- `SRankSentinel.Remote.v1.StopResetThroughUldah`

All take the expected character key. The SRank controller rereads character/readiness and rejects the wrong character or framework context. Reset confirmation in Relay is scoped to character/channel/user, expires after 30 seconds and is consumed before IPC. Existing screenshots resolve only the current process's FFXIV client-area window; minimized/blank capture fails closed. None survives plugin/game termination. Current Discord credentials are not mobile pairing credentials and must never be exported to a phone.

## Ownership and minimum repository structure

Keep PC code in `SentinelRelay/remote`, with a platform-neutral authority library, ASP.NET Core host and independent tests. Exclude `remote/**/*.cs` from the Dalamud project. The host is an optional, separately packaged Windows-user process; no dependency is introduced for existing Discord users. Its capture/input adapters will run in the interactive user session, not a privileged service or inside Dalamud. A later tray/startup wrapper will keep it available independently of game crashes.

Use `MarshalTitan/SentinelRemote` for the Android-first mobile application when Phase 2 begins. That repository was not found through the connected account during this audit, and connected tooling offers no repository-creation operation. The PC foundation does not depend on its creation. No additional hosted server repository is needed.

| Component | Owns | Prohibited responsibilities |
|---|---|---|
| Relay Dalamud plugin | Existing Discord path; future opt-in local bridge, character/world/territory and supported Sentinel snapshots; execute existing bounded IPC on framework thread | Video encoding, drivers, external listeners, launcher credentials, arbitrary game commands |
| External PC host | Pairing, grants, sessions, stable installation registry; future process/window tracking, scoped capture, input leases, launcher/recovery and bounded incidents | Generic shell, filesystem browser, desktop streaming, credential database |
| Sentinel Remote app | Device credential in OS secure storage, host/client selection, status UI, confirmations; future view/sticks/buttons and manual reconnect UX | Granting itself access, accepting invalid TLS certificates, storing game passwords/TOTP secrets |

The phone may connect to more than one PC host. Each host owns a random stable HostId; each locally registered installation owns a random stable InstanceId. The full remote target is `(HostId, InstanceId)`, independent of character names and window order. Tests use sample character labels only; runtime names are configurable.

## Stable game identity and bridge design — next stage

The host must bind a locally enrolled slot to a kernel-observed process ID **and start time**, selected HWND ownership and an ephemeral launch epoch. The plugin sends its current ContentId on an authenticated local channel. Use a current-user-only named pipe and kernel client-PID verification; match the expected executable and lifetime locally. The phone cannot supply a PID, HWND, executable path, content ID or pipe name. Validate the binding again before every action/capture. Reject duplicate/ambiguous bindings, PID reuse, logout/switch, HWND replacement, stale heartbeat and concurrent claims. A reconnect must mint a fresh game epoch and invalidate old intents.

Persist a slot's expected character/home-world locally, separately from last observed character. Retain that slot when the process exits so launcher recovery has an identity. The plugin's existing ContentId helps verify the correct character only **after** login; it cannot authenticate the launcher/lobby. An offline slot must not be reassigned to the first detected FFXIV process. On separate PCs, each host follows this same model. On one PC, shared input-device visibility is an additional isolation gate.

Bridge payloads will be versioned and bounded; no free-text execution field. Reuse the existing SRank IPC executor and screenshot policy with separate mobile authorization. Do not create fake Discord messages or poll Discord for the app. Plugin opt-in, pause/character checks and local emergency disable remain independent vetoes. No current plugin bridge is included in Phase 1.

## Technology choices and feasibility

| Area | Choice and alternatives | Evidence/remaining proof |
|---|---|---|
| PC host | .NET 10/ASP.NET Core, standard library security and JSON, no new NuGet dependency initially | Fits the ecosystem C# code and future Win32/WinRT adapters. Self-contained Windows artifact avoids requiring an SDK on users' PCs. |
| Mobile | Recommend Flutter: one touch UI/protocol implementation for Android/iOS. Kotlin + Swift offers strongest native integration but two UIs; React Native adds native modules; MAUI shares C# but still needs mobile media/input integration. | Flutter officially supports Android/iOS. Validate secure storage, lifecycle, TLS, touch latency and media package maintenance before pinning dependencies. Android native shell remains a fallback if media integration proves unsuitable. |
| Private networking | Tailscale Serve: private HTTPS termination on the same PC, loopback host, exact trusted tailnet login plus separate application pairing | No port forwarding or public Sentinel backend. Direct/private WireGuard plus managed certificates is an alternative with more setup. WebRTC ICE/TURN would add signaling/relay operations and possibly hosting cost. Never configure Funnel. Tailnet loss fails closed. |
| Virtual controller | No driver chosen or installed in Phase 1 | ViGEmBus/client libraries are retired. XInput describes shared controller slots, not secure process-exclusive routing. VHF means a kernel driver/signing/maintenance project and is not a shortcut. Evaluate a maintained, legally usable signed backend and game-device assignment first. Do not promise isolation by foreground focus alone. |
| Window capture | Windows.Graphics.Capture `CreateForWindow`, tied to locally verified HWND/PID/lifetime | System-supported window capture is preferable to desktop duplication/cropping. Occluded/background behavior and GPU/FFXIV modes need measurement; minimized windows cannot be promised. Existing screenshot GDI path is a fallback for stills only, with the same fail-closed checks. |
| First live view | Bounded in-memory JPEG frames over private HTTPS at roughly 5 FPS, maximum dimensions/bytes, one selected game window | Simplifies initial rescue view without codec invention. Measure bandwidth/latency before scaling to 15 FPS. Never substitute the desktop if capture fails. |
| Later video | Media Foundation hardware H.264 where supported, established WebRTC transport/library after license/maintenance audit | Hardware support and low-latency settings need capability checks; no chosen library or performance claim yet. Keep codec/network work out of Dalamud. |
| Launcher recovery | Fixed locally configured XIVLauncher installation/account flow, launched without passwords in arguments | Detailed installed-launcher capability/UI audit remains Phase 6. No password/OTP/TOTP database. No generic process-launch API. User handles required fresh authentication; no blind coordinate macros. |

For controller proof, begin with one remotely controlled instance per PC; other local FFXIV clients must demonstrably receive **no** input. If game/device routing cannot guarantee that, disable input whenever multiple clients are present and bring that limitation back as a product decision. Do not implement system-wide keyboard injection as a silent substitute. Capture/status/Discord functionality remains useful while this gate is unresolved.

## Recovery and incident design — planned

Separate host unreachable, process exited, process alive/plugin stale, character logged out/disconnected with positive evidence, and healthy. Missing heartbeat alone means plugin unavailable, not a proven disconnect/crash. Readiness and profile values may be unknown. Track monotonic heartbeat receipt and last-known timestamps; preserve world/territory/SRank as last known after loss.

A bounded reconnect machine will move through configured launcher, authentication, game process, lobby, character-selection and Relay-ready states using observed evidence and deadlines. It must not auto-select/delete/modify a character when identity is uncertain. Hand control to the phone on unknown screens. Authentication values must be ephemeral, intentionally entered only on a verified allowed login window, excluded from logs/screenshots/incident exports and never persisted. This sensitive-input fallback needs its own threat review before implementation.

Incident collection remains off initially: allowlisted process/heartbeat transitions and typed Sentinel state; later narrowly scoped crash-artifact existence and sanitized log/event excerpts with local consent. Bound retention and export size, never upload automatically. No crash-cause inference from disappearance alone.

## Sources

Repository paths above were inspected at the recorded commits, particularly Relay `ARCHITECTURE.md`, `BACKEND_DEPLOYMENT.md`, `Plugin.RemoteSRank.cs`, `Core/SRankRemoteControls.cs`, `Services/SRankRemoteClient.cs`, `Services/CharacterContextService.cs`, `Services/GameWindowCapture.cs`, SRank `RemoteRecoveryContract.cs`, Hub `Coordination/HubOperationsIpc.cs`, and Profiles `Plugin.cs`/`Services/ProfileDtrBar.cs`.

Official platform references reviewed 2026-10-19:

- [Tailscale Serve](https://tailscale.com/docs/features/tailscale-serve)
- [Flutter platform support](https://docs.flutter.dev/reference/supported-platforms)
- [Windows window capture](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)
- [Media Foundation H.264 encoder](https://learn.microsoft.com/en-us/windows/win32/medfound/h-264-video-encoder)
- [XInput controller access](https://learn.microsoft.com/en-us/windows/win32/xinput/getting-started-with-xinput)
- [ViGEm end of life](https://docs.nefarius.at/projects/ViGEm/End-of-Life/)
- [Virtual HID Framework](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/virtual-hid-framework--vhf-)
