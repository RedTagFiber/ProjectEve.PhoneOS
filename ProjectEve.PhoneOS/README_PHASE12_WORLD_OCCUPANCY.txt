PROJECT EVE — PHASE 12
WORLD OCCUPANCY + NPC SCHEDULE MOVEMENT

CORE RULE

    ProjectEve owns where every NPC actually is.
    PhoneOS only shows who the player can perceive there.

The In Person page no longer creates Eve because the player opened a page.

RESOLUTION ORDER

For each NPC:

    active override
        ↓
    assigned shift
        ↓
    normal JobProfile
        ↓
    home fallback

JOB PROFILE INTEGRATION

Phase 12 uses the JobProfile fields Project Eve already owns:

- StartHour
- EndHour
- WorkDays
- ShiftType
- WorkLocationMode
- CommuteMinutesOneWay

No second job system is created.

Example for Eve's authored work profile:

    home
      ↓ commute
    Sinclair Coffee
      ↓ shift
    Sinclair Coffee
      ↓ commute
    home

The exact shift hours/commute come from Eve's JobProfile, not this phase.

VARIABLE / ROTATING SHIFTS

A WorkDays value like "varies" is deliberately NOT guessed.

Those NPCs use:
    assigned_shift_only

Call:
    AssignShiftAsync(...)

when the roster/work system knows their actual shift.

This is important for firefighters, rotating factory shifts, nurses, etc.

CALL-OFF / SICK / VACATION / APPOINTMENT

Use NpcScheduleOverrideRequest.

Examples:

    Kind = "call_off"
    LocationId = ""
        → stay at bound home for the override window

    Kind = "sick"
        → home

    Kind = "vacation"
        → home unless a specific LocationId is supplied

    Kind = "appointment"
    LocationId = "clinic-west"
        → clinic during that window

    Kind = "manual_location"
    LocationId = "hospital"
        → explicit temporary world location

This gives the future job attendance system a clean place to apply call-ins,
late arrivals, overtime, vacation, emergencies, and appointments.

TRAVELING IS A REAL STATE

During commute:

    Status = traveling
    CurrentLocationId = ""
    OriginLocationId = ...
    DestinationLocationId = ...
    DepartGameTime = ...
    ExpectedArrivalGameTime = ...

So an NPC is not simultaneously at home and work while commuting.

SCENE ARRIVAL / DEPARTURE

If a player is in the location when an NPC leaves:

    SCENE UPDATE
    Eve heads out.

If a player is already there when an NPC arrives:

    SCENE UPDATE
    Eve arrives.

The left PRESENT panel then changes from the real ScenePresence table.

PHASE 11 CONTACT SAFETY

Leaving a scene breaks stale active physical-contact state for that scene.
An NPC cannot go to work, come back six hours later, and still display as
0 ft because an old hug/kiss/grapple record was left active.

ALL NPCS

On synchronization, Phase 12 creates a schedule binding for any Character row
that does not have one yet.

For general generated NPCs:
    home:npc:<id>
    work:<employer-slug>

These are INTERNAL world locations. They do not automatically become places the
player knows or can travel to.

Household/family generation can later replace multiple home:npc:<id> bindings
with one shared household location.

SINCLAIR ANCHOR BINDINGS

The existing authored family gets shared internal location IDs:

Eve + Adam
    home = adam-house

Lisa + Edward
    home = sinclair-family-home

Eve + Lisa
    work = coffee-shop

Adam + Edward
    work = fire-station

Their actual hours still come from their existing JobProfile.

Adam's authored WorkDays is variable, so Phase 12 does NOT invent which days
he is on duty. He needs actual assigned shifts.

GAME CLOCK

WorldOccupancyHostedService listens to the authoritative IGameTimeService.

When game time advances:
    +15m
    +1h
    sleep
    wait until
    Next Event

the server reconciles NPC occupancy to the new game time.

The laptop being shut down still does NOT advance game time.

LIMITATION IN PHASE 12

If the player jumps over an entire shift in one huge time jump and the NPC ends
at the same location they started, Phase 12 only needs to reconcile the final
world state.

Phase 13 should use GetNextBoundaryAsync to process every meaningful boundary
chronologically:

    leave home
    arrive work
    leave work
    arrive home

without minute-by-minute simulation.

That is also where Next Event can stop on a player-relevant arrival/contact
without mundane movements from 200 NPCs stopping fast-forward.
