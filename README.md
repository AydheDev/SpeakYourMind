# SpeakYourMind

**Give your pawns a voice—and something worth saying.**

An early-development, display-only RimWorld **1.7** mod. Pawns express their needs, personalities, relationships and surroundings through AI-generated bubbles. It extends a compatible Interaction Bubbles display; Interaction Bubbles Expanded is the primary target.

## Status

Version **0.3.3 — OpenAI JSON input fix**, compiled against installed **1.7.5005 rev1082** and the installed Expanded 1.7 interface. **328 automated assertions** and **8 bubble interface checks** pass. **Not yet verified in a running colony or against a live AI service.** The original Interaction Bubbles integration is experimental and interface-checked at startup; it is not certified for 1.7. This is not a finished Workshop release. Changes are listed in [docs/CHANGELOG.md](docs/CHANGELOG.md).

## Install and set up

1. Extract the `SpeakYourMind` folder from the package into RimWorld's `Mods` folder.
2. Enable **Harmony**, **Interaction Bubbles Expanded**, then **SpeakYourMind**. Do not enable the original Interaction Bubbles alongside Expanded.
3. Restart the game. Open **Options → Mod settings → SpeakYourMind → Guided AI setup**.
4. Choose OpenAI, Claude, Gemini, a local server, OpenRouter, or another OpenAI-compatible service.
5. Follow the provider-specific help link. Add an API key where required. On the next screen, enter the exact model name or use **Choose from server's model list** for OpenAI and compatible servers. Choose a text model; a model appearing in the list does not guarantee support for every setting. Keys are encrypted for your Windows account and kept out of saves and transcripts. This credential store targets Windows.
6. Start with a small, fast text model. OpenAI reasoning defaults to **Low**. If the selected model rejects that setting, turn off reasoning controls or choose a supported value. Claude and Gemini currently use their native default reasoning behaviour; model-specific reasoning controls remain future work.
7. Run **Test connection**. It generates a fictional pawn's sample expression and checks the response format, then shows the text and response time. It sends no real colony history. Cloud API usage may be billed by your provider. The mod does not silently use a chat subscription or log in through a consumer app.
8. Continue, then choose **Save and enable SpeakYourMind**. Cancelling setup preserves your current connection; changes to the model, key, or request settings require another successful test before saving.
9. Default preferences are **Hybrid**, **Standard**, and **Lively (3/4)**. The character override starts at zero, using the length preset: Brief 100, Standard 240, Descriptive 420. You can enter an exact override from 1 to 2,000 visible characters, including punctuation and annotation additions.

If upgrading from 0.1.0 or 0.2.0, replace the installed SpeakYourMind folder while the game is closed. Existing history and player instructions use the same save format. Existing explicit character overrides remain unchanged; set the override to zero to follow length presets.

### Local model setup

- **Ollama:** install Ollama, download a model suited to your hardware, start its server, choose the Ollama preset, and enter the exact installed model name. Default address: `http://localhost:11434/v1`.
- **LM Studio:** download/load a model and start its local server. Choose the LM Studio preset and use the model identifier shown by the server. Default address: `http://localhost:1234/v1`.
- Localhost does not require an API key unless your server does. Remote connections require HTTPS and a credential. Not every server implements every optional parameter: leave compatible-server reasoning controls off unless supported.

Official guides: [OpenAI](https://developers.openai.com/api/docs/quickstart), [Claude](https://platform.claude.com/docs/en/get-started), [Gemini](https://aistudio.google.com/apikey), [Ollama](https://docs.ollama.com/api/openai-compatibility), [LM Studio](https://lmstudio.ai/docs/developer), [OpenRouter](https://openrouter.ai/docs/quickstart).

## What the build does

- Replaces the bubble mod's default presentation while SpeakYourMind is enabled; the game's real social log is preserved.
- Uses the bubble mod's actual renderer, with animated dots during requests. Only SpeakYourMind's own bubbles get controlled lifetimes.
- Collects social events, damage, attacks, deaths of relatives, colony letters, research completions, low needs and ambient observations.
- Prioritizes selected and visible pawns. Off-screen significant events can enter memory without generating invisible bubbles.
- Includes current traits, skills, needs, health, relationships, apparel, equipment, background, ideology, work, local weather/terrain, visible nearby structures, and appropriate colony inventory/research context.
- Supports short exchanges of up to 2–4 turns, with reading time for each line. Both participants are reserved for the exchange, preventing overlapping conversations. Each line enters history exactly once after the bubble renderer reports a successful draw. Outdated, rejected, or never-rendered lines are not recorded as spoken.
- Pauses expression animation, expiry and release with the game. Pending requests may finish, but text waits until resume. Responses are rechecked and staggered.
- Uses generic combat fallbacks after a short wait. Cached fallbacks follow player rules; if no valid rule-compatible fallback is available, the bubble clears instead of ignoring the rules.
- Records a shared transcript with pawn and colony views, search, expression-type filters and pagination. Retention defaults to unlimited; optional pawn/colony day limits are independent. The history browser caches filtered entries and draws the visible page.
- Updates pawn personality, memory scores and fallback phrases automatically. Memory importance is continuous from 0 to 1; **1 never expires**. The retention curve is configurable. A shorter transcript retention limit does not erase core memories.
- Supports colony and pawn instructions in **Voices → Instructions**. Choose “Everyone” for colony instructions, or “Use selected pawn” for an individual annotation. They persist when generated profiles update.
- Allows all pawn groups, with species-appropriate animal expression and mechanical signals. Persistent profiles cover colony members, prisoners, slaves, player-owned creatures and optionally hosted guests. Former residents retain their history. Other pawns use current character information without background personality requests; their displayed transcripts remain readable.
- Keeps requests outside the game thread, bounds concurrent expressions and request rate, and backs off after failures. Memory-service failures do not pause dialogue requests. Request timeouts and output allowances are configurable, including for slower local models.
- Shows connection status, retry timing and session counters in settings. **Copy diagnostic summary** omits keys, server addresses, pawn names, player instructions and conversation text. Invalid or unsupported saved history is preserved and generation pauses for that colony instead of overwriting the original data.

### If something does not appear

- Check **Bubble integration** in mod settings, enable generation, and ensure the bubble mod itself is allowed to render.
- Resume the game and keep a pawn on screen. Completed replies wait during pause, and stale/off-screen requests are discarded.
- Read **Status** and **Memory** separately. A memory problem does not prevent otherwise working expressions. **Retry connection now** clears pending requests and the retry delay without erasing history.
- For a timeout, raise **Request timeout** in guided setup. For incomplete output, increase **Output allowance** or lower reasoning. Retest before saving.
- For a rejected request, note the **Reported field**, **Server code** and **HTTP** status in the error. These identify recognized provider error metadata without exposing the raw response or credentials. Do not assume every rejection concerns reasoning. For GPT-6.1 Sol, the model ID is `gpt-6.1-sol` and `low` is a supported reasoning effort ([official model documentation](https://developers.openai.com/api/docs/models/gpt-6.1-sol)). Version 0.3.3 adds an explicit JSON instruction to OpenAI input to satisfy the [JSON-mode guard](https://developers.openai.com/api/docs/guides/structured-outputs?api-mode=responses); live acceptance needs retesting.
- If a short hard limit or a strict creative rule rejects a response, the bubble may clear. The mod does not fall back to text that ignores player instructions.

## Optional quick replies and follow-ups

Enable individual sources under **Optional quick replies when AI is slow**. They are off by default and do not become required dependencies:

| Source | How it is used |
| --- | --- |
| SpeakUp | Reuses the already resolved text for that social interaction. |
| SpeakUp Extension, SpeakUp Child, SpeakUp ChildSFW | Uses the final installed SpeakUp rules, including these packs. Choose Child or ChildSFW in your mod list; they are alternatives. |
| Vanilla Social Interactions Expanded | Can display the current interaction's own event text, which may be narration rather than direct speech. |
| Rumor Has It | Can display the current gossip/interaction text. Rumors remain allegations in AI context. The reviewed continued release lists support through 1.6; 1.7 is unverified. |
| Callouts | Captures resolved text from its known display interface. Routes it through SpeakYourMind only when accepted for the current event, avoiding a duplicate native callout. The reviewed continued release lists support through 1.6; 1.7 is unverified. |

The default quick-reply delay is **6 unpaused seconds**, configurable from 2–30. Combat retains its 3-second fallback. An available optional reply can also be used while requests are queued or the AI service is backing off. No dialogue library is copied into this mod, and no social interaction is triggered just to obtain a line.

**Allow follow-up lines** is on by default, with or without optional mods. A displayed social fallback may receive a fresh AI reply after its reading time. The reply receives the actual displayed line as context and shares its exchange ID in history. The fallback consumes one turn of the 2–4-turn exchange limit. Late results from the original request cannot replace it. Follow-ups respect visibility, pause, request budgets and higher-priority events.

Fallback text must fit the character limit, species, selected style and language. Arbitrary player instructions cannot be verified for externally authored text, so those rules exclude optional-mod fallbacks; existing rule-specific generic caches remain available. Mod-generated summaries are excluded in Dialogue mode. Optional mods retain their own gameplay effects and content restrictions; ChildSFW does not by itself set an AI-wide profanity rule.

See [the adapter notes and version boundaries](docs/COMPATIBILITY.md).

## Debugging the first playtest

Enable **Debug: record dialogue and full prompt context to local files** in mod settings. This is separate from RimWorld developer mode and from the ordinary save transcript.

Each session produces a readable `.txt` report and a `.jsonl` file. They contain observed social lines, supported Callouts emissions, AI prompts and context, returned model text, validation failures, fallback choices, displayed lines, memory updates and timing. Add a **Playtest note** when a response feels wrong. Use **Copy debug folder path** to find the files.

Debugging is off by default. It records colony details, names and player instructions; known API credentials are redacted, request headers/endpoints are not logged, and nothing is uploaded. The default session cap is 50 MB across both files. A reached limit or write failure is reported explicitly. Existing debug sessions are kept for review. See [docs/DEBUGGING.md](docs/DEBUGGING.md) for the workflow and event meanings.

## Compatibility boundaries

SpeakYourMind has its own namespace, package ID, Harmony owner, save keys and settings. It adds no relationship, mood, work or decision effects. It does not overwrite other mods' files or edit their XML definitions. Its scheduling uses a private RNG, not the game simulation RNG.

This does **not** guarantee compatibility with every mod. SpeakUp has a scoped text adapter; RimTalk, RimDialogue, or other independent AI text producers can still compete over the same bubbles. A detected competing producer is reported in settings. Expanded itself includes its own social/frequency features; SpeakYourMind does not disable or change them. Optional adapters check known interfaces at runtime and preserve native behavior if they cannot take responsibility for a line. None has yet been playtested with a running colony.

Unknown or changed bubble interfaces leave SpeakYourMind inactive and preserve the existing bubble behaviour. Only one bubble backend should be enabled. Mod-added needs are read generically; specialized concepts such as “no usable toilet” are not inferred without a dedicated integration. A low need alone is not proof of a missing facility.

## Build and test

This repository does not redistribute game, Harmony, Expanded, or third-party runtime DLLs.

With PowerShell 7.6+ (.NET 10 and its bundled Roslyn compiler), no SDK/NuGet download is required:

```powershell
./build.ps1
./build.ps1 -Tests
./validate-integration.ps1
./package.ps1
```

Paths default to the standard Windows Steam library. Override `-RimWorldDir`, `-HarmonyDll`, and (for integration validation) `-BubblesDll` as needed. The test script builds to `artifacts/tests`, never to the runtime mod folder. Packaging checks a build manifest and rejects changed or uncompiled source. A fresh staging folder prevents old files from entering a new package. The package contains this mod's assembly, manifest, metadata and player documentation.

For compilation with the .NET 10 SDK:

```powershell
dotnet build Source/SpeakYourMind.csproj -c Release -p:RimWorldDir="C:/path/to/RimWorld" -p:HarmonyDll="C:/path/to/0Harmony.dll"
```

The packaging workflow currently requires `build.ps1` to generate its matching manifest; an SDK build alone does not create that manifest.

## Next validation

Follow [docs/PLAYTEST.md](docs/PLAYTEST.md) in a disposable test colony. Visual layout, bubble click navigation, pausing/resuming, save/load, provider quality and latency, and a representative mod list still require in-game checks. Keep an original save when trying an early build. Cloud and local request formats are covered with mocked transport; live provider acceptance is not established by those tests.

Design decisions and remaining work are tracked in [docs/DESIGN.md](docs/DESIGN.md).
