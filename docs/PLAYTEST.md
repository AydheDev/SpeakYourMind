# Initial playtest checklist

Use a disposable colony first. This checklist is not a record of completed in-game validation.

1. **Startup:** Harmony → Expanded → SpeakYourMind. Verify no red startup errors. Without either bubble mod, verify the settings explain the missing dependency and no patches suppress existing behaviour.
2. **Guided setup:** choose a provider/local server; save a key; enter a valid model; test; verify success latency. Try wrong model/key and stopped local server. Confirm messages contain no credentials.
3. **Social:** observe two selected/visible pawns interacting. Dots replace original bubble text, then short sequential expressions appear. The native log remains intact. Verify clicks open SpeakYourMind history.
4. **Needs:** observe a hungry pawn, then let them eat before a slow reply returns. The old hunger reply should be discarded. Mod-added needs should be named without inventing missing buildings.
5. **Animals/mechs:** enable normal activity near them. Check species-appropriate nonverbal output and no invented human speech.
6. **Combat:** observe shots/misses and damage. Callouts must not invent successful hits. Simulate a slow service and check generic fallback, no late overwrite, no repeated identical fallback.
7. **Pause:** pause during a request. No answer should appear, dot animation and display expiry should stop. Resume and verify staggered release, no flood of completed answers.
8. **Camera:** move away or change maps during a request. Stale unseen text must not enter the transcript. Selected visible pawns should get priority.
9. **History:** check colony and both participant views for a shared exchange, exact wording, source and timestamp. Search, paginate, select a pawn and inspect memories.
10. **Annotations:** add a colony instruction and a conflicting individual instruction. Verify priority and preservation after background updates/save-load. Change a rule during a request; the old result should be discarded.
11. **Persistence:** save, exit to menu, load. Verify transcripts, annotations, core memory scores and profiles persist; keys do not appear in save XML. Switching saves must not deliver old pending replies into the new colony.
12. **Retention:** test pawn and colony limits separately on a disposable save. A retained view must keep the shared record; core memory survives transcript expiry.
13. **No gameplay effects:** inspect opinions, mood and jobs before/after generated expressions without real interactions. AI output must not add thoughts, relationship changes or jobs.
14. **Stress:** fast-forward a large colony, watch the real-time request cap and bubble cap. Verify network errors back off and the game continues smoothly.
15. **Disable/re-enable:** disable generation during a request; dots clear and existing bubble behaviour returns. Re-enable; no old response is injected.
16. **Compatibility:** repeat with the actual mod list. Test other dialogue producers separately; do not assume simultaneous generation works.
