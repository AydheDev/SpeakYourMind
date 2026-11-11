# Initial development validation — 11 November 2026

- Compiled successfully with no compiler warnings against local RimWorld **1.7.5005 rev1082**, .NET 10, and Harmony 1.7.
- **175 automated assertions passed**: continuous memory retention, permanent core memories, invalid score handling, independent transcript visibility/deletion, annotation persistence, save-data round trips, speaker/length validation, provider payload and result extraction, credential redaction, request budget, mocked network success/rate limiting/cancellation, and game hook signatures.
- **8 Expanded bubble integration interfaces verified**, including inspected method bodies for creation, rendering and fade. The rendering method's success return is used to record displayed text.
- Installed Expanded 1.7 DLL SHA-256: `E6258956862ABA113B7C8023B860867CF29F7670863F77FBA4AB75A9D5F0B48D`.
- Runtime package excludes game assemblies, Harmony, Expanded, test assemblies, source tooling and credentials.

Not performed: live API requests, in-game loading/visual checks, save/load in a running colony, original Interaction Bubbles on 1.7, large-colony performance measurement, cross-mod playtests. These require the checks in PLAYTEST.md. Mocked HTTP tests prove the request/response handling locally, not current provider acceptance for every account/model.
