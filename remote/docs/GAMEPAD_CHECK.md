# Sentinel Remote Gamepad Check

This is a separate Windows support check for the next phone-control feature. Your current Sentinel Remote PC/Android apps keep their existing pairing and settings.

1. Extract the download and run **SentinelRemote-Gamepad-Check-Setup.exe** normally.
2. Open **Sentinel Remote Gamepad Check** from Start. Close FFXIV and other games before running the check.
3. If this PC does not have the controller driver, click **Open official driver download**. From that official release, install **USBip-0.9.8.1-x64.exe**. Its installer requests administrator approval and a Windows restart; complete that restart before continuing. Do not choose ARM64, test-signed packages or change Windows security settings.
4. Click **Run gamepad check**. On its first run it downloads and verifies the signed VIIPER 0.9.0 component from the official project. It then checks Windows controller input, neutral states and automatic release. Keep games closed throughout.
5. Click **Copy result** and paste the result into the setup conversation, whether it passed or was not verified.

You do not need PowerShell, a .NET installation, new phone pairing, or a Relay update for this check. A physical controller can stay connected if a free Windows controller slot remains. The check uses only a newly created virtual controller and verifies existing slots stay connected; this does not yet prove in-game coexistence.

A pass is Windows backend evidence. FFXIV mapping, one-client enforcement in the real host, phone touch controls, game-specific permissions and full field release checks are still required before a phone-control candidate.

The helper has its own uninstaller. It does not uninstall the separately installed USB-IP driver, which other software may use.

VIIPER runs as a separate program; its binary is downloaded directly from the author's pinned release, not included in this installer. Its GPL-3.0 license/source are available at https://github.com/Alia5/VIIPER/tree/v0.9.0. The support-check installer is an unsigned private candidate. The downloaded VIIPER binary is checked by SHA-256 and Windows Authenticode.
