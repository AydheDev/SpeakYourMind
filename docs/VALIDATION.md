# 0.5.0 validation — 12 November 2026

- Full game-dependent build/tests: **436 assertions**, compiled against installed RimWorld **1.7.5005 rev1082** and Harmony. Includes 66 new review regressions, mocked provider requests, note save compatibility, bounded memory-score retries, fallback policy, need crossings, frozen prompt examples and separated usage outcomes.
- The 100,000-line history regression verifies that profile/note edits do not rebuild the conversation index, appends update existing groups without full regrouping, and filters/retention still invalidate correctly. This is not an in-game frame-rate benchmark.
- Installation runs the Expanded interface checks, a fresh optimized runtime build, package integrity checks and installed-file hash verification. It backs up the previous mod and does not modify credentials, preferences, enabled mods or saves.
- No live provider calls were made for this development validation. The UI compiles against the installed game, but visual layout, actual save/load in a running colony, long sessions and optional mod combinations remain on the playtest checklist.
- Token metrics report only provider-supplied usage and do not estimate billing. Memory facts with repeatedly missing importance scores are retained and marked for explicit retry rather than silently assigned invented importance.
- The prompt lab keeps at most 24 input snapshots, also bounded to roughly 2 MB of UTF-16 string storage, only while debug capture is enabled. Replay and feedback do not change production prompts, pawn profiles, history or game state.
- Reasoning capability entries were checked against the official [Sol](https://developers.openai.com/api/docs/models/gpt-6.1-sol), [Terra](https://developers.openai.com/api/docs/models/gpt-6.1-terra) and [Luna](https://developers.openai.com/api/docs/models/gpt-6-luna) pages on 12 November 2026. These are local compatibility hints, not live model availability checks; custom endpoints are not constrained by this catalog.

## Historical 0.4.0 workspace validation — 11 November 2026

- **355 core assertions passed** using the standalone .NET 10 runner, including previous non-game tests and new timing, topic novelty, bounded relevant memory retrieval, legacy-save defaults, grouped/searchable exchanges, token parsing and concurrent metrics checks.
- **9 installer transaction checks passed** in PowerShell 7.6 on Linux with temporary fixture directories: replacement, backups, rejected staged hashes, unrelated-target refusal, rollback after an injected verification failure and first install.
- All repository C# files and PowerShell scripts are syntax-checked separately.
- **Not run for 0.4.0:** full runtime compilation against RimWorld/Harmony, Windows Steam discovery, installation on the user's PC, live provider requests, or in-game UI/scheduling/adapter checks. The installer will require the full game-dependent tests and Expanded interface checks before installation. Pure core/transaction tests are not evidence of those checks passing.

The following is the historical validation record for the previous build, not a claim about 0.4.0:

# 0.3.3 development validation — 11 November 2026

- Compiled successfully with no compiler warnings against local RimWorld **1.7.5005 rev1082**, .NET 10, and Harmony 1.7.
- **328 automated assertions passed**: continuous memory retention, permanent core memories, independent transcript retention, annotation persistence, validated save round trips, corrupt/future-save rejection, Unicode character limits, duplicate suppression, provider payloads and parsing, credential-safe errors, request budgets, mocked HTTP handling, and game hook signatures.
- A mocked JSON-mode input guard rejects the old preview payload and accepts corrected dialogue and memory requests. This checks the documented guard condition, input preservation, low reasoning and token/privacy settings; it does not prove successful live API acceptance.
- Exchange regression checks cover sequential turns, exactly-once recording at successful draw, pause/resume, staggered release, unseen expiry, cancellation, and late responses after fallback.
- Setup checks cover draft isolation, successful-test requirements, and invalidation after key, model or request-setting changes. Mocked tests exercise every provider through response parsing, display lifecycle, transcript recording and save restoration; model listing and server retry delays are also checked.
- History query checks use **100,000 transcript entries**. Repeated unchanged redraw queries reuse the existing result list; participant/category filters, pagination, search, appends and retention expiry are covered. This measures the data/query behavior, not the running game's frame rate.
- **8 Expanded bubble integration interfaces verified**, including inspected method bodies for creation, rendering and fade. The rendering method's success return is used to record displayed text.
- Installed Expanded 1.7 DLL SHA-256: `E6258956862ABA113B7C8023B860867CF29F7670863F77FBA4AB75A9D5F0B48D`.
- Runtime package excludes game assemblies, Harmony, Expanded, test assemblies, source tooling and credentials.
- Packaging verifies the compiled input hashes and assembly hash, rejects newly added uncompiled source, and uses a fresh staging directory. The archive includes the build manifest for traceability.
- Optional-source tests cover independent opt-in, style/language/species/annotation rejection, patched child/extension grammar recognition, and restoring the SpeakUp read guard after nested calls or exceptions. These use synthetic definitions and the local game types; they do not load the actual third-party assemblies.
- Follow-up tests cover the remaining turn budget, disable setting, prevention of repeated continuations, missing listeners, cancelled exchanges, unseen fallback expiry, and late-response rejection.
- Debug tests write real temporary files: readable and JSONL records, prompt/context preservation, source labels, notes, key redaction, shutdown draining, rejection of late writes, explicit disk limits and isolated file failures.

Not performed: live API requests, in-game loading/visual checks, save/load in a running colony, original Interaction Bubbles on 1.7, large-colony performance measurement, or optional-mod playtests. Third-party adapter behavior was checked against published source interfaces, not a running SpeakUp, VSIE, Rumor Has It or Callouts installation. Callouts and Rumor Has It's reviewed maintained metadata lists only through 1.6. These require the checks in PLAYTEST.md. Mocked HTTP tests prove the request/response handling locally, not current provider acceptance for every account/model.
