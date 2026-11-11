# Optional dialogue adapters — 0.3.0

These are optional integrations with separately installed mods. No third-party code or dialogue packs are redistributed. All adapters are awaiting an in-game compatibility playtest.

## SpeakUp and its content packs

The adapter reads the existing social log entry once, with the installed grammar and patches. It identifies SpeakUp-owned definitions and tagged SpeakUp grammar. SpeakUp Extension, SpeakUp Child and SpeakUp ChildSFW therefore contribute through the same adapter; there is no separate copy of their dialogue and no independent generator to keep synchronized. Choose the intended Child or ChildSFW pack in the mod list rather than combining alternate overhauls.

SpeakUp's grammar can call `DialogManager.Ensue`, which schedules actual follow-up interactions. During SpeakYourMind's own text read only, that call is blocked by a thread-local scope that always unwinds after exceptions. SpeakUp calls outside that scope retain their behavior. SpeakYourMind supplies its own display-only AI follow-ups, governed by the follow-up toggle and exchange turn limit. No `TryInteractWith`, worker `Interacted`, relationship or mood API is invoked to obtain fallback text.

If the known SpeakUp guard interface is missing, SpeakYourMind avoids resolving its grammar and reports the unavailable adapter. The current SpeakUp metadata lists 1.7. Extension/child compatibility still depends on the installed pack and game version. A SFW content pack controls that pack's authored text; use player instructions for corresponding AI behavior, which will exclude externally authored fallback text that cannot be checked against arbitrary instructions.

References: [SpeakUp text hook](https://github.com/sergiodinapoli/SpeakUp/blob/master/SpeakUp/HarmonyPatches/PlayLogEntry_Interaction_ToGameStringFromPOV_Worker.cs), [dialogue scheduling](https://github.com/sergiodinapoli/SpeakUp/blob/master/SpeakUp/DialogManager.cs), [Extension](https://steamcommunity.com/sharedfiles/filedetails/?id=3383734373), [ChildSFW](https://steamcommunity.com/sharedfiles/filedetails/?id=2786270605).

## Vanilla Social Interactions Expanded

This means **Vanilla Social Interactions Expanded**, package `VanillaExpanded.VanillaSocialInteractionsExpanded`. Existing interaction definitions provide descriptive text. The adapter can present that current event text in Hybrid/Narration modes after the fallback delay. It does not ask the mod to create another vent, lesson, date or relationship event. VSIE's own gameplay mechanics remain its responsibility. Its reviewed repository includes a 1.7 implementation.

References: [interaction definitions](https://github.com/Vanilla-Expanded/VanillaSocialInteractionsExpanded/blob/main/1.7/Defs/InteractionDefs/Interactions_Social.xml), [gameplay patches](https://github.com/Vanilla-Expanded/VanillaSocialInteractionsExpanded/blob/main/1.7/Source/HarmonyPatches/Interactions_Patches.cs).

## Rumor Has It

The adapter recognizes Rumor Has It-owned definitions or its `Rumor_Code` interaction workers and reads only the resulting social event text. It does not invoke the workers. Gossip and authored claims are labeled as unverified expressions in prompts, not accepted as evidence of a death, betrayal or other event. Its independently installed social effects are unchanged.

The reviewed continued release (`Mlie.RFRumorHasIt`) lists versions through 1.6. The event adapter does not make that mod itself compatible with RimWorld 1.7; use only a working version for your game. Runtime support remains experimental.

References: [maintained source](https://github.com/emipa606/RoumorHasIt), [metadata](https://github.com/emipa606/RoumorHasIt/blob/main/About/About.xml), [event definitions](https://github.com/emipa606/RoumorHasIt/blob/main/1.6/Defs/Interactions_Social.xml).

## Callouts

The adapter checks `CM_Callouts.CalloutTracker.RequestCallout` and its attached/floating text paths. It captures text that Callouts already resolved, inside a scoped request. It does not run Callouts' random selection or grammar a second time. Native output is suppressed only when SpeakYourMind accepts that exact event. Rejected, disabled, off-screen, conflicting or unsupported cases keep the native output. Wound damage-number motes and unrelated floating text are not intercepted.

Both attached and floating Callouts modes are covered by the known interface. A changed signature disables the optional hooks while preserving the base mod. The reviewed maintained Callouts release (`Mlie.Callouts`) lists versions through 1.6; this adapter is not a port of Callouts to 1.7. Other forks with the same interface can be detected, but require playtesting.

References: [Callouts tracker](https://github.com/emipa606/Callouts/blob/main/Source/CM_Callouts/CalloutTracker.cs), [metadata](https://github.com/emipa606/Callouts/blob/main/About/About.xml).

## Shared rules

- Sources are independently opt-in. SpeakUp extensions use the SpeakUp toggle.
- Text belongs to one event and its participants; there is no cross-pawn or cross-event random fallback cache.
- The original AI request is cancelled when a fallback is chosen. Follow-ups use a fresh request with the displayed text and remaining turn budget.
- Fallbacks respect hard length limits and repetition checks. Human dialogue is not routed to animals or mechanoids without verified species handling. Unknown/mismatched language or style is rejected.
- Colony or participant annotations exclude optional text because arbitrary creative instructions cannot be verified without another generation step.
- A vanished pawn, changed need/health state, stale event, pause, or higher-priority event can delay or discard a line. Selection never guarantees display.
- Main history records actual SpeakYourMind draws with source labels. Debug mode additionally records observations, emissions, prompts, completions and discarded work.
