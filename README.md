# SpeakYourMind

**Give your pawns a voice—and something worth saying.**

An early-development, display-only RimWorld **1.7** mod. Pawns express their needs, personalities, relationships and surroundings through AI-generated bubbles. It extends a compatible Interaction Bubbles display; Interaction Bubbles Expanded is the primary target.

## Status

Version **0.1.0 — initial test build**, compiled against installed **1.7.5005 rev1082** and the installed Expanded 1.7 interface. Automated core tests and reflection checks pass. **Not yet verified in a running colony or against a live paid AI account.** The original Interaction Bubbles integration is experimental and interface-checked at startup; it is not certified for 1.7. This is not a finished Workshop release.

## Install and set up

1. Extract the `SpeakYourMind` folder from the package into RimWorld's `Mods` folder.
2. Enable **Harmony**, **Interaction Bubbles Expanded**, then **SpeakYourMind**. Do not enable the original Interaction Bubbles alongside Expanded.
3. Restart the game. Open **Options → Mod settings → SpeakYourMind → Guided AI setup**.
4. Choose OpenAI, Claude, Gemini, a local server, OpenRouter, or another OpenAI-compatible service.
5. Follow the provider-specific help link. Add an API key where required and the exact model name available to your account. Keys are encrypted for your Windows account and kept out of saves and transcripts. This initial credential store targets Windows.
6. Start with a small, fast text model. OpenAI reasoning defaults to **Low**. If the selected model rejects that setting, turn off reasoning controls or choose a supported value. Claude and Gemini currently use their native default reasoning behaviour; model-specific reasoning controls remain future work.
7. Run the **connection test**. It makes one small API request and reports response time or an actionable error. Cloud API usage may be billed by your provider. The mod does not silently use a chat subscription or log in through a consumer app.
8. Enable generation. Default preferences are **Hybrid**, **Standard**, and **Lively (3/4)**. The character limit defaults to 240 and can be overridden; set it to zero for the length preset's limit.

### Local model setup

- **Ollama:** install Ollama, download a model suited to your hardware, start its server, choose the Ollama preset, and enter the exact installed model name. Default address: `http://localhost:11434/v1`.
- **LM Studio:** download/load a model and start its local server. Choose the LM Studio preset and use the model identifier shown by the server. Default address: `http://localhost:1234/v1`.
- Localhost does not require an API key unless your server does. Remote connections require HTTPS and a credential. Not every server implements every optional parameter: leave compatible-server reasoning controls off unless supported.

Official guides: [OpenAI](https://developers.openai.com/api/docs/quickstart), [Claude](https://platform.claude.com/docs/en/get-started), [Gemini](https://aistudio.google.com/apikey), [Ollama](https://docs.ollama.com/api/openai-compatibility), [LM Studio](https://lmstudio.ai/docs/developer), [OpenRouter](https://openrouter.ai/docs/quickstart).

## What the initial build does

- Replaces the bubble mod's default presentation while SpeakYourMind is enabled; the game's real social log is preserved.
- Uses the bubble mod's actual renderer, with animated dots during requests. Only SpeakYourMind's own bubbles get controlled lifetimes.
- Collects social events, damage, attacks, deaths of relatives, colony letters, research completions, low needs and ambient observations.
- Prioritizes selected and visible pawns. Off-screen significant events can enter memory without generating invisible bubbles.
- Includes current traits, skills, needs, health, relationships, apparel, equipment, background, ideology, work, local weather/terrain, visible nearby structures, and appropriate colony inventory/research context.
- Supports short exchanges of up to 2–4 turns. Each line enters history only after the bubble renderer reports a successful draw. Outdated, rejected, or never-rendered lines are not recorded as spoken.
- Pauses expression animation, expiry and release with the game. Pending requests may finish, but text waits until resume. Responses are rechecked and staggered.
- Uses generic combat fallbacks after a short wait. Cached fallbacks follow player rules; if no valid rule-compatible fallback is available, the bubble clears instead of ignoring the rules.
- Records a shared transcript with pawn and colony views, search and pagination. Retention defaults to unlimited; optional pawn/colony day limits are independent.
- Updates pawn personality, memory scores and fallback phrases automatically. Memory importance is continuous from 0 to 1; **1 never expires**. The retention curve is configurable. A shorter transcript retention limit does not erase core memories.
- Supports colony and pawn instructions in **Voices → Instructions**. Choose “Everyone” for colony instructions, or “Use selected pawn” for an individual annotation. They persist when generated profiles update.
- Allows all pawn groups, with species-appropriate animal expression and mechanical signals. Persistent profiles cover colony members, prisoners, slaves, player-owned creatures and optionally hosted guests. Other pawns use temporary profiles; their displayed transcripts remain readable.
- Keeps requests outside the game thread, bounds concurrent expressions and request rate, backs off after failures, and does not send credentials into prompts or diagnostics.

## Compatibility boundaries

SpeakYourMind has its own namespace, package ID, Harmony owner, save keys and settings. It adds no relationship, mood, work or decision effects. It does not overwrite other mods' files or edit their XML definitions. Its scheduling uses a private RNG, not the game simulation RNG.

This does **not** guarantee compatibility with every mod. SpeakUp, RimTalk, RimDialogue, or other text producers can compete over the same bubbles. A detected competing producer is reported in the settings screen. Disable competing generation for the initial test. Expanded itself includes its own social/frequency features; SpeakYourMind does not disable or change them.

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

Paths default to the standard Windows Steam library. Override `-RimWorldDir`, `-HarmonyDll`, and (for integration validation) `-BubblesDll` as needed. The test script builds to `artifacts/tests`, never to the runtime mod folder. The package contains only this mod's runtime files.

Alternatively, with the .NET 10 SDK:

```powershell
dotnet build Source/SpeakYourMind.csproj -c Release -p:RimWorldDir="C:/path/to/RimWorld" -p:HarmonyDll="C:/path/to/0Harmony.dll"
```

## Next validation

Follow [docs/PLAYTEST.md](docs/PLAYTEST.md) in a disposable test colony. Visual layout, bubble click navigation, pausing/resuming, save/load, provider quality and latency, and a representative mod list still require in-game checks. Keep an original save when trying an early build. Cloud and local request formats are covered with mocked transport; live provider acceptance is not established by those tests.

Design decisions and remaining work are tracked in [docs/DESIGN.md](docs/DESIGN.md).
