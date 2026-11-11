# 0.2.0 development validation — 11 November 2026

- Compiled successfully with no compiler warnings against local RimWorld **1.7.5005 rev1082**, .NET 10, and Harmony 1.7.
- **258 automated assertions passed**: continuous memory retention, permanent core memories, independent transcript retention, annotation persistence, validated save round trips, corrupt/future-save rejection, Unicode character limits, duplicate suppression, provider payloads and parsing, credential-safe errors, request budgets, mocked HTTP handling, and game hook signatures.
- Exchange regression checks cover sequential turns, exactly-once recording at successful draw, pause/resume, staggered release, unseen expiry, cancellation, and late responses after fallback.
- Setup checks cover draft isolation, successful-test requirements, and invalidation after key, model or request-setting changes. Mocked tests exercise every provider through response parsing, display lifecycle, transcript recording and save restoration; model listing and server retry delays are also checked.
- History query checks use **100,000 transcript entries**. Repeated unchanged redraw queries reuse the existing result list; participant/category filters, pagination, search, appends and retention expiry are covered. This measures the data/query behavior, not the running game's frame rate.
- **8 Expanded bubble integration interfaces verified**, including inspected method bodies for creation, rendering and fade. The rendering method's success return is used to record displayed text.
- Installed Expanded 1.7 DLL SHA-256: `E6258956862ABA113B7C8023B860867CF29F7670863F77FBA4AB75A9D5F0B48D`.
- Runtime package excludes game assemblies, Harmony, Expanded, test assemblies, source tooling and credentials.
- Packaging verifies the compiled input hashes and assembly hash, rejects newly added uncompiled source, and uses a fresh staging directory. The archive includes the build manifest for traceability.

Not performed: live API requests, in-game loading/visual checks, save/load in a running colony, original Interaction Bubbles on 1.7, large-colony performance measurement, cross-mod playtests. These require the checks in PLAYTEST.md. Mocked HTTP tests prove the request/response handling locally, not current provider acceptance for every account/model.
