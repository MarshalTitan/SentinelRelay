# Navigation polish - 0.5.0.12

Mouse activation now clears rounded focus/hover decoration after pointer exit; keyboard/controller focus keeps its visible cue. Native focus and button callbacks are unchanged.

Uses compatible SentinelCore UI 0.4.1.3, source `0d59460cc592f1589600e11e4e9e04478667b629`. Existing Modern sidebar icons have borderless idle states, persistent rounded selection, soft animated hover highlights and padded translucent tooltip bubbles. Custom minimize/restore controls use drawn down/up chevrons; close uses a more visible X. Native hit areas, keyboard/controller focus, window dragging, saved placement and callbacks remain intact. Headers follow the selected page. Classic and plugin functionality are preserved.

Stable 0.5 maintenance is published separately from the future 0.6 candidate. Character Content-ID profiles, protected credentials, webhooks, channel routing, filters, keywords, screenshots and remote controls are untouched.

Dependency packages are restored from the immutable release and SHA-256 checked before locked restore. Locks record the release SHA-512 values. Automated builds/native UI/persistence checks are recorded in the coordinated Sentinel report. Actual FFXIV visual acceptance remains separate from native renderer checks.
