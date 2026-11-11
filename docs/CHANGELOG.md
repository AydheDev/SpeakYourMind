# Changes

## 0.2.0 — prepared for first playtest

- Reworked exchange delivery so every accepted turn has reading time, records exactly once when drawn, waits through pauses, and cannot be overwritten by a late response after fallback.
- Prevented concurrent exchanges from sharing a participant. Urgent reactions can replace a pending ambient request; recent repeated lines are rejected.
- Separated memory retries from dialogue retries, honored service retry delays, and added configurable request timeouts and output allowances.
- Added observed health transitions and captured relationship names before death. Background profiles focus on established pawns; former residents retain their identities.
- Made guided setup transactional, with a fictional expression preview, test-before-save checks, and OpenAI/compatible-server model selection.
- Added exact character/history controls, visible Unicode character counting, adaptive window sizing, status counters and a diagnostic summary without colony text or names.
- Added expression-type history filters and cached, paginated queries tested against 100,000 entries.
- Added validation for saved data, preserving unreadable or unsupported data instead of replacing it. Kept existing version-1 history compatible.
- Added build manifests, stale-build rejection and clean package staging. Expanded automated coverage from 175 to 258 assertions.

In-game behavior, appearance, live provider acceptance and compatibility with a full mod list still need the home playtest.

## 0.1.0 — initial implementation

- Added contextual expression scheduling, the Expanded bubble adapter, four provider protocols, memory profiles, transcripts, instructions, settings and Windows credential storage.
- Compiled against RimWorld 1.7.5005 and verified the installed Expanded integration interfaces.
