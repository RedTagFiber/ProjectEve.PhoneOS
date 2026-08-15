PROJECT EVE — PHASE 6
SCENE PRESENCE + DISTANCE + OVERHEARING
=======================================

PURPOSE
-------
This phase replaces the old presentation-only "Eve is 4 ft away" prototype with a
ProjectEve-owned scene/perception engine.

World truth and observer perception are now separate:

  WORLD SCENE TRUTH
      who is physically present
      X/Y position in feet
      room/acoustic zone
      attention/activity
      concealment
      barriers
      ambient noise / visual clutter

              ↓ observer-specific perception

  PLAYER / NPC PERCEPTION
      who can be seen
      distance
      what speech was heard
      clear / partial / fragment
      what actions/body language were visible

A relationship NEVER grants knowledge. An NPC only receives perception evidence
when they physically hear/see something.

WHAT IS INCLUDED
----------------
1. ProjectEve.Core/Scene/IScenePerceptionService.cs
   - shared server contract and DTOs
   - supports the practical 10 NPC + 2 player active-scene target

2. ProjectEve.Core/Chat/IConversationChatService.cs
   - adds optional PerceivedPlayerMessage + PerceptionSourceKey
   - exact transcript and observer perception can coexist

3. ProjectEve/Scene/ScenePerceptionService.cs
   - SQLite-backed server-owned scene state
   - coordinate-based distance
   - ambient noise and visual clutter
   - room/acoustic zones
   - concealment
   - pairwise door/wall/barrier penalties
   - observer attention/activity
   - whisper / quiet / normal / raised / shout
   - deterministic chance for bystander overhearing
   - partial speech fragments WITHOUT inventing replacement words
   - visual gating for actions and body language
   - permanent ScenePerceptionEvidence provenance

4. ProjectEve/Conversations/ConversationPerceptionStore.cs
   - exact ConversationMessage rows remain untouched
   - persists what the NPC participant actually perceived for each player message
   - prevents a partial/missed line from becoming exact on the NEXT turn

5. ProjectEve/Conversations/ConversationPromptContext.cs
   - feeds the NPC's perception-view transcript to Brain when overlays exist
   - exact transcript remains evidence, not automatic knowledge

6. ProjectEve/Conversations/ConversationManager.cs
   - closed-section summaries/facts/plans are now built from the NPC perception transcript
     instead of omniscient exact player wording

7. ProjectEve/Conversations/ConversationSummaryEngine.cs
   - explicitly refuses to reconstruct [inaudible], fragments, or missed words
   - does not create facts/plans from partial hearing without clear support

8. ProjectEve/AI/ProjectEveConversationService.cs
   - records the current perception overlay before Brain context is built
   - DOES NOT replace Brain.cs

9. ProjectEve.PhoneOS/Program.cs
   - registers IScenePerceptionService as ProjectEve-owned singleton
   - SceneUiStateService becomes SCOPED so one browser/phone circuit cannot leak
     its presentation state into another player's circuit

10. ProjectEve.PhoneOS/Components/Pages/InPerson.razor
    - registers current scene with the perception engine
    - side-panel distance is calculated from X/Y scene coordinates
    - exact player ACTION/SAY is archived
    - Eve's Brain receives only what Eve physically saw/heard
    - other present NPCs independently get their own overhearing result
    - player only sees/hears Eve output that the player physically perceived
    - player voice level is inferred from whisper/yell/softly/etc.

11. ProjectEve/Data/World/Scene/scene_perception_v1.sql
12. ProjectEve/Data/World/Conversation/conversation_perception_v1.sql
    - human-readable schema/reference copies; services auto-create the tables

13. TEST_PHASE6_SCENE_PERCEPTION.sql
    - diagnostics + optional hidden-overhear test

IMPORTANT KNOWLEDGE RULE
------------------------
ScenePerceptionEvidence is NOT automatically copied into NPC memory/knowledge yet.
It is provenance: "this observer perceived this fragment at this game time."

That is deliberate. The next knowledge/gossip phase can consume this evidence and
create the correct personal memory/belief without telepathy.

CURRENT IN-PERSON LIMITATION
----------------------------
The present InPerson page is still the direct Eve prototype, so it creates a
player-scoped scene ID and places:

  Player: (0,0)
  Eve:    (4,0)

The ScenePerceptionService itself is NOT player-specific. The future group scene /
2-player orchestrator can give both players and all NPCs the same shared SceneId.
That is where the full 10 NPC + 2 player scene becomes live.

The current page does this conservatively so Phase 6 does not pretend Eve can exist
in two shared locations at once before authoritative NPC movement is wired.

DO NOT REPLACE
--------------
- Brain.cs
- app.css (keep the current dark-glass scene look you approved)
- GameClock services
- PhoneMessagingService

NOTE: ConversationManager.cs and ConversationSummaryEngine.cs ARE replaced in this phase
because closed-section memory must summarize the NPC's perceived transcript rather than
an omniscient exact transcript.

BUILD ORDER
-----------
1. Copy both Core files.
2. Build ProjectEve.Core.
3. Copy these ProjectEve files:
     Scene/ScenePerceptionService.cs
     Conversations/ConversationPerceptionStore.cs
     Conversations/ConversationPromptContext.cs
     Conversations/ConversationManager.cs
     Conversations/ConversationSummaryEngine.cs
     AI/ProjectEveConversationService.cs
   The SQL files are reference copies and can be copied with them.
4. Build ProjectEve.
5. Copy PhoneOS Program.cs + InPerson.razor.
6. KEEP your current app.css.
7. Clean Solution.
8. Rebuild Solution.
9. Run In Person.

BASIC TEST
----------
1. Open In Person.
2. Left panel should still show Eve at about 4 ft.
   Difference: that number is now calculated from X/Y scene coordinates.
3. Type SAY: Hello Eve
4. Eve should answer normally.
5. Run TEST_PHASE6_SCENE_PERCEPTION.sql and inspect ScenePerceptionEvidence.
   You should see:
      player speech -> Eve observer
      Eve speech -> player observer
      Eve action/body -> player observer when perceived

HIDDEN NPC / OVERHEARING TEST
-----------------------------
TEST_PHASE6_SCENE_PERCEPTION.sql contains an optional INSERT for Adam:
- 8 ft from the player
- high concealment so the player should NOT see him in PRESENT
- same acoustic zone so he still has a chance to overhear normal speech

Reload In Person after inserting him, say several normal sentences to Eve, then
inspect ScenePerceptionEvidence for ObserverCharacterKey='npc:2'.
Because overhearing is probabilistic per actual speech event, Adam may hear a clear,
partial, fragment, or no result. Only perceived results are written as evidence.

WHY PARTIAL HEARING DOES NOT INVENT WORDS
-----------------------------------------
If a bystander only catches part of:

  "Adam stole five thousand dollars from work"

Project Eve may record something like:

  "Adam ... thousand dollars ... work"

It does NOT rewrite the missing words into a new claim. Any later mistaken belief
must come from the NPC interpretation/gossip layer, where it belongs.
