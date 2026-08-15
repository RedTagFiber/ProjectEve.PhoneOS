PROJECT EVE — PHASE 15
REAL PLAYER TRAVEL

CORE FLOW

    known destination
        ↓
    choose travel method
        ↓
    find authored world route
        ↓
    leave current physical scene
        ↓
    status = traveling
        ↓
    advance authoritative GAME TIME
        ↓
    process world events / phone events
        ↓
    arrive
        ↓
    enter destination shared scene

NO TELEPORTING

Phase 14 made physical player location server-owned.
Phase 15 removes the remaining travel teleport.

The RightTools panel no longer does:

    NavigateTo("/in-person?location=coffee-shop")
        =
    physically appear at coffee shop

Instead it calls IPlayerTravelService.

LOCATION URLS ARE PRESENTATION ONLY

If Ryan is physically at home and somebody opens:

    /in-person?location=coffee-shop

InPerson now refuses to teleport him.

It will only open that destination after PlayerWorldPresence says Ryan has
actually arrived there.

TRAVEL STATE

During a trip:

    Status = traveling
    Current Location = none
    Origin = Ryan's House
    Destination = Sinclair Coffee
    Method = car
    DepartGameTime = ...
    ExpectedArrivalGameTime = ...
    ActiveTravelId = ...

The player is not simultaneously at home and at the destination.

CONTACT / FIGHT POSITION SAFETY

Starting travel immediately leaves the old shared scene.

That means old:
    hug
    kiss
    hand holding
    chest bump
    clinch
    restraint
    fight position

cannot remain active after the player physically drives away.

ROUTE TRUTH — IMPORTANT

Project Eve DOES NOT infer route time from a location name.

WorldTravelRoute contains explicit authored route links:

    FromLocationId
    ToLocationId
    Method
    Minutes
    Source

If no route is registered:

    "No car route/time is registered..."

The engine does not guess 12 minutes, 20 minutes, etc.

ROUTE GRAPH / MULTI-LEG

You do not need every location pair.

Example:

    home -> Main Street       4 min
    Main Street -> downtown   5 min
    downtown -> coffee shop   3 min

The planner can build:

    home -> Main Street -> downtown -> coffee shop

Total:
    12 authored game minutes

The route planner uses the shortest registered-time path for the selected method.

METHODS

Current vocabulary:

    car
    truck
    bike
    walk
    bus

The right panel defaults to the player's current Transport field.

INTERRUPTED TRAVEL

Suppose:

    6:00 depart
    6:20 expected arrival

But at 6:08:

    Eve sends an important text

Phase 13 can stop game-time advance at 6:08.

Player state remains:

    traveling
    12 game minutes remaining

The RightTools panel shows:

    Continue Travel

When the player continues, game time resumes toward 6:20.

ARRIVAL EVENT

Every trip schedules:

    EventType = player_travel_arrival

That means Next Event understands that arrival is a real player event.

At arrival:

    PlayerWorldPresenceService enters destination
    travel journey becomes arrived
    arrival GameEvent becomes handled

GLOBAL CLOCK SAFETY

PlayerTravelHostedService listens to IGameTimeService.

If some OTHER part of Project Eve advances game time beyond a travel arrival,
the hosted service finalizes the trip instead of leaving the player stuck
forever in "traveling".

LEFT PANEL

While physically traveling:

    LOCATION
    Traveling to Sinclair Coffee

and the local scene people list is empty.

That is intentional: the player is between scene endpoints.

A later road/vehicle scene phase could make travel itself a perceivable scene.

MULTIPLAYER / GLOBAL TIME NOTE

Phase 15 uses the existing global game clock.

The previously locked rule still applies:
one player must not fast-forward global time past another player's active scene.

That cross-player time-vote/lock coordinator is not implemented by this phase.

NEXT NATURAL PHASE

Phase 16 should be NPC-INITIATED COMMUNICATION + SOCIAL FOLLOW-UP:

    NPC decides to text/call
        ↓
    schedule contact in game time
        ↓
    phone delivery / seen / response system
        ↓
    Next Event sees it

That would make NPCs contact the player because of their own goals,
relationships, promises, arguments, gossip, reminders, invitations, etc.,
instead of communication mostly starting from the player.
