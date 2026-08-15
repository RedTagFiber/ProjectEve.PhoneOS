PROJECT EVE — PHASE 10
SHARED IN-PERSON SCENES + APPROVED UI INTEGRATION

WHAT THIS PHASE DOES

1. WIRES PHASE 9 INTO THE APPROVED IN-PERSON PAGE
   ACTION and SAY now go through IGroupSceneConversationOrchestrator.
   Up to 10 NPCs can be cheaply considered; only the strongest few receive
   full Brain calls.

2. ONE LOCATION = ONE SHARED SCENE
   Old Phase 6 prototype:
       inperson:<playerId>:coffee-shop

   Phase 10:
       inperson:coffee-shop

   Two players at Sinclair Coffee therefore share one physical scene.

3. TWO PLAYERS ARE SAFE
   The shared presence coordinator allows two active player slots.
   Player 1 leaving removes ONLY Player 1.
   It does not remove Eve, other NPCs, or Player 2.

4. PLAYER-SAFE PERCEPTION
   The left PRESENT panel is refreshed from the real perception service.
   Hidden NPCs stay hidden.
   Distance is calculated from scene coordinates.
   Player 2 appears only if Player 1 can actually perceive Player 2.

5. SHARED CHRONOLOGICAL TEXT STREAM
   Player 1 action/speech, Player 2 action/speech, NPC body language,
   NPC action, NPC speech, scene opening, and scene updates all use the same
   plain-text chronological stream.

   No chat bubbles are added.

6. COLORS
   Every NPC keeps a stable actor hue.
   Each NPC's speech/action/body language uses variants of that hue.
   Local player keeps the familiar blue.
   A second player receives a separate stable color.

7. PARTIAL HEARING IS VISIBLE
   If Phase 6 gives a player only a fragment/partial perception, Phase 10 shows
   only that observer-safe text and marks the meta line with fragment/partial.

8. NO UI LOCK-UP
   Group AI work stays off the Blazor UI circuit.
   A fast poll paints player actions almost immediately while NPC Brains continue.
   The player can still scroll, use side drawers, and type while the turn runs.\n   Razor UI state is not mutated from the background AI worker.

9. SCENE SESSION SAFETY
   EnsureSceneAsync starts a fresh open scene session before the first turn.
   This prevents an old closed visit from being replayed as if it were the new visit.

10. SAME SCENE DESCRIPTION STREAM
   AppendWorldEntryAsync stores the opening description as a world/scene entry.
   It appears in the same text stream instead of a separate scene-description box.

CURRENT COMPATIBILITY BOOTSTRAP

The current InPerson page still knows that Eve (NPC 1) is the authored companion
for the prototype scene. Phase 10 asks the server to place her at 4 ft.

The coordinator will never yank Eve out of another location while another player
is actively with her. If that old scene has no players left, the compatibility
anchor may move her.

This is temporary compatibility behavior. Future schedule/world occupancy code
should call ISharedScenePresenceCoordinator.UpsertNpcAsync directly, so the world
decides who is actually at each location.

WHAT THIS PHASE DOES NOT DO

- It does not invent schedules for 10 NPCs.
- It does not make every visible NPC answer every turn.
- It does not expose hidden NPCs.
- It does not use one AI call for several minds.
- It does not change the approved app.css.
- It does not add SignalR yet. The InteractiveServer page uses short server-side
  polling, which works across two connected browser/phone circuits on one server.

NEXT NATURAL PHASE

Phase 11 should move NPC occupancy from the compatibility Eve anchor into the
real schedule/world-location layer:
schedule -> travel -> active location -> shared scene presence.

That will let Adam, Lisa, Edward, coworkers, customers, etc. enter and leave
scenes because the WORLD says they are there, not because the page hard-coded them.
