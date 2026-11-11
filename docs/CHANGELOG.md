# Changes

## 0.3.0 — optional quick replies and playtest logging

- Added opt-in adapters for SpeakUp and its installed Extension/Child/ChildSFW rules, Vanilla Social Interactions Expanded event text, Rumor Has It event text, and the known Callouts display interface. Callouts and Rumor Has It remain experimental on 1.7; their reviewed continued releases list support through 1.6.
- Added an adjustable quick-reply delay, exact-event fallback selection and source labels in history. Optional text cannot bypass player annotations, length, style, language or species checks.
- Added a follow-up toggle. Social fallbacks can receive fresh AI replies after reading time, with the already displayed line in context and a shared exchange ID. Total turn limits still apply; unseen or discarded text cannot trigger follow-ups.
- Prevented SpeakYourMind's own SpeakUp text reads from scheduling gameplay interactions. Native calls outside that scope retain their behavior.
- Added opt-in asynchronous JSONL and readable debug journals, recording prompts, context, model results, fallback decisions, successful draws, errors and memory work. Added playtest notes, a configurable session cap, explicit omitted-record counts, credential redaction and a folder-path button.
- Added regression coverage for adapter policy, child/extension grammar, scoped side-effect protection, continuation limits, unseen fallback expiry, debug privacy, file failures and disk caps. Automated total: 303 assertions.

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
