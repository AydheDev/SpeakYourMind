# 0.2.0 home playtest checklist

Use a disposable colony first. This checklist is not a record of completed in-game validation.

1. **Startup:** Harmony → Expanded → SpeakYourMind. Verify no red startup errors. Without either bubble mod, verify the settings explain the missing dependency and no patches suppress existing behaviour.
2. **Guided setup:** choose a provider/local server; enter a key; fetch a model list or enter a model; test; inspect the fictional preview and latency; then save. Cancel an edited connection and confirm the old one still works. Change the key/model after a test and confirm another test is required. Try wrong model/key and stopped local server. Confirm messages contain no credentials.
3. **Social:** observe two selected/visible pawns interacting. Dots replace original bubble text, then all accepted turns appear sequentially with time to read. Pawns should not participate in two exchanges simultaneously. The native log remains intact. Verify clicks open SpeakYourMind history.
4. **Needs:** observe a hungry pawn, then let them eat before a slow reply returns. The old hunger reply should be discarded. Mod-added needs should be named without inventing missing buildings.
5. **Animals/mechs:** enable normal activity near them. Check species-appropriate nonverbal output and no invented human speech.
6. **Combat:** observe shots/misses and damage. Callouts must not invent successful hits. Simulate a slow service and check generic fallback, no late overwrite, no repeated identical fallback.
7. **Pause:** pause during a request. No answer should appear, dot animation and display expiry should stop. Resume and verify staggered release, no flood of completed answers.
8. **Camera:** move away or change maps during a request. Stale unseen text must not enter the transcript. Selected visible pawns should get priority.
9. **History:** check colony and both participant views for every displayed line, exact wording, source and timestamp, with no duplicates. Search, filter by expression type, paginate, select a pawn and inspect memories. Check the window at your normal UI scale and a smaller resolution.
10. **Annotations:** add a colony instruction and a conflicting individual instruction. Verify priority and preservation after background updates/save-load. Change a rule during a request; the old result should be discarded.
11. **Persistence:** save, exit to menu, load. Verify transcripts, annotations, core memory scores and profiles persist; keys do not appear in save XML. Switching saves must not deliver old pending replies into the new colony.
12. **Retention:** test pawn and colony limits separately on a disposable save. A retained view must keep the shared record; core memory survives transcript expiry.
13. **No gameplay effects:** inspect opinions, mood and jobs before/after generated expressions without real interactions. AI output must not add thoughts, relationship changes or jobs.
14. **Stress:** fast-forward a large colony, watch the real-time request cap and bubble cap. Verify network errors back off and the game continues smoothly.
15. **Disable/re-enable:** disable generation during a request; dots clear and existing bubble behaviour returns. Re-enable; no old response is injected.
16. **Compatibility:** repeat with the actual mod list. Test other dialogue producers separately; do not assume simultaneous generation works.
17. **Health and identity:** observe an injury and recovery, then a death affecting a related pawn. Verify current conditions replace stale reactions, relationship labels remain correct after death, and former colony members retain their history if they return.
18. **Length presets:** with character override zero, switch Brief/Standard/Descriptive. Enter a small exact override, then restore zero. Confirm the displayed limit and accepted text follow the chosen setting.
19. **Separate memory service:** configure an unavailable memory server while dialogue works. Confirm memory retries without silencing expressions. Use a slow local model to check timeout controls.
20. **Diagnostics:** use **Copy diagnostic summary** in settings. Check counters/status are useful and no keys, server addresses, pawn names, annotations or dialogue appear.

Start with startup, setup, one social exchange, pause/resume, and save/load. Once those pass, work through the remaining scenarios. Record the mod version, model, reasoning setting, approximate response delay and any visible error. The copied diagnostic summary helps report problems without sharing colony text.
