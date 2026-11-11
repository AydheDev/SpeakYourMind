# Reviewing the first playtest

1. Load a test colony and open SpeakYourMind's mod settings.
2. Turn on **Debug: record dialogue and full prompt context to local files**. The next record starts a new session. Debugging can capture native social events even with SpeakYourMind generation disabled, provided its bubble integration initialized.
3. Play normally. When an expression feels wrong, add a **Playtest note** in settings, describing the pawn, the line and the desired behavior. Notes receive the same UTC time and game-day information as the dialogue.
4. Turn debugging off when finished. Pending records drain to disk without blocking the game. Copy the debug folder path from settings and open it in Explorer.
5. Review the `.txt` report. Share the relevant session or excerpt when tuning prompts, after checking its contents. The `.jsonl` version is useful for grouping requests and comparing many examples.

Location: RimWorld's configuration folder, under `SpeakYourMind/Debug`. Every debug session uses its own timestamp and random ID; loading another colony starts a separate journal. Files are not embedded in saves or uploaded. Normal transcripts remain separate and contain only successful SpeakYourMind bubble draws.

## What the records mean

| Stage | Meaning |
| --- | --- |
| `social_observed` | An actual social log entry was read. It may never become a SpeakYourMind bubble. |
| `callout_emitted` | Callouts reached its text output method. Emission does not prove the native mote was visible. |
| `queued` | SpeakYourMind accepted an expression candidate. |
| `request` / `memory_request` | Exact system prompt and serialized context, model, reasoning choice, output allowance and timeout. |
| `provider_completed` | Model output or a safe error, including completion during pause or after cancellation. `ElapsedMs` here is wall-clock request time; `Day` is the request's starting day. Output is the model's returned text, not HTTP headers or private reasoning. |
| `response` / `memory_response` | The game resumed processing the returned text. |
| `response_rejected` / `line_rejected` | Parsing, length, speaker or repetition checks failed. The reason is included. |
| `fallback_selected` / `fallback_rejected` | Which optional source or generic preset was considered and why it was used or skipped. Selection is not proof of display. |
| `fallback_attached` | Callouts supplied text for a pending reaction from the same pawn, category and simulation tick. It has not been selected or shown yet. |
| `displayed` | A successful bubble draw, with exact text and speaker ID. This is what the player-facing transcript records. |
| `discarded` | Stale, hidden, interrupted or otherwise cancelled work. |
| `exchange_finished` | Display and reading time completed. A permitted fresh follow-up can be scheduled afterward. |
| `memory_applied` / `memory_rejected` / `memory_error` | Result of background profile work. |
| `player_note` | Your observation for later tuning. |

Request IDs join prompts, results and decisions. Exchange IDs in the details join an optional fallback to its fresh follow-up. `displayed.ElapsedMs` and the main-thread `response.ElapsedMs` measure unpaused time since dispatch, not server-only latency. All records also include UTC timestamps.

This is a complete trace of the supported capture paths while logging is active, not an interceptor for every possible third-party text renderer. Text generated before logging starts cannot be reconstructed. Native social/Callouts records are labeled separately from confirmed SpeakYourMind draws.

## Limits and privacy

The default 50 MB limit covers both files in one session and can be changed for the next session. On reaching it, the journal writes a limit marker and stops recording; omitted records are counted in settings. A bounded background queue avoids disk I/O on the game thread, and reports omitted records if overloaded. Write failures appear in debug status. Existing files are never silently rotated or deleted; remove reviewed sessions manually when no longer needed.

Prompts contain pawn names, biographies, colony events, memories and player instructions. These are deliberately included for tuning. API keys registered for the session and recognizable credential patterns are redacted; endpoints and HTTP headers are excluded. Avoid putting unrelated secrets in annotations. Review files before sharing. The separate **Copy diagnostic summary** button remains suitable when full colony text is unnecessary.

For a useful report, include the offending `displayed` line, its request/context, your note, and any nearby fallback or rejection records. Describe whether the problem was wording, incorrect facts, repetition, age/species voice, tone, timing, or too much chatter. Prompt changes are made in the source; debug mode does not rewrite them automatically.
