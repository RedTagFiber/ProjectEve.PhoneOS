PROJECT EVE — PHASE 13
EVENT-DRIVEN WORLD ADVANCE + STRONGER NEXT EVENT

WHAT CHANGES

Before:
    +8 hours
      ↓
    GameClock jumps directly 8 hours
      ↓
    occupancy reconciles final state

Now:
    +8 hours
      ↓
    find next meaningful schedule boundary
      ↓
    jump to that exact boundary
      ↓
    reconcile world
      ↓
    continue
      ↓
    stop early only for a PLAYER-RELEVANT event

There is still NO minute-by-minute simulation.

EXAMPLE

Ryan is at Sinclair Coffee.

5:52 PM   Eve leaves home            mundane elsewhere
6:00 PM   Eve arrives coffee shop    Ryan can perceive her

Player presses:
    +2 hours

Phase 13 processes 5:52 silently, then stops at 6:00:

    Eve arrives.

The remaining requested time is NOT consumed until the player chooses to
advance again.

NEXT EVENT

"Next Event" now processes mundane NPC schedule boundaries without stopping for
all 200 NPCs.

It stops for:
- queued player GameEvent
- NPC arrival the requesting player actually perceives
- NPC departure the requesting player actually perceives
- phone contact through the existing PhoneOS Next Event controller

It does NOT stop because:
- Bob across town started work
- Lisa went home somewhere the player is not present
- a hidden NPC moved but the player could not perceive them
- a remote worker changed from home/off-shift to home/working

PLAYER-SAFE PERCEPTION

The coordinator checks the requesting player's own ScenePerception view before
deciding that an arrival/departure is player-relevant.

So:
    NPC physically present
does NOT automatically mean:
    player knows NPC is present

PHASE 12 NARRATION PRIVACY FIX

Phase 12 used shared scene-update narration for arrivals/departures.

Phase 13 tightens this:
A shared "Eve arrives" / "Eve leaves" line is only emitted when EVERY active
player in that shared scene can perceive that NPC.

If one player cannot perceive the NPC, no global narration is emitted. Each
player's PRESENT/perception state still stays correct.

This avoids hidden-NPC spoilers until we add observer-specific world narration.

WORLD BOUNDARY MODEL

WorldScheduleBoundary records:
- NPC
- exact game time
- depart / arrive / location_change / status_change
- from/to status
- from/to location
- activity/source

The schedule resolver determines this by comparing world state immediately
before and immediately after a known schedule boundary.

GAME CLOCK OWNERSHIP

ProjectEveGameTimeService is still the source of truth for persisted GameTime.
Phase 13 does NOT replace it.

IWorldAdvanceCoordinator is an orchestration layer:

    WorldAdvanceCoordinator
        ↓
    IGameTimeService
        ↓
    authoritative persisted clock

RIGHT TOOLS

The existing:
    +15 min
    +1 hour
    +2 hours
    Next day
    Sleep → 7 AM
    text wait commands
now use IWorldAdvanceCoordinator.

The visible GAME TIME display still reads IGameTimeService.Now.

PHONE NEXT EVENT

GameplayTimeControllerService now uses the world coordinator for its hidden
phone stepping.

That means:
    next phone retry due at 7:20
but:
    Eve visibly arrives at 7:12

Next Event stops at 7:12 for Eve instead of silently jumping past her to the
phone check.

LIMITS / SAFETY

- Next Event searches up to 14 game days at a time.
- Hard safety cap: 4096 world boundaries in one call.
- Neither limit minute-ticks the NPC population.
- If no player-relevant event is known, time is not intentionally thrown weeks
  forward just to manufacture one.

NEXT PHASE

Phase 14 should fix another server-state issue:
player physical location/presence should survive switching PhoneOS apps.

Right now the InPerson page still owns join/leave lifecycle. If the player opens
Messages, the scene page can dispose and remove the player from scene presence.

Phase 14 should make PlayerWorldPresence server-owned, so:
    In Person
    → Messages
    → Contacts
    → Calendar

does NOT mean the player physically left Sinclair Coffee.
