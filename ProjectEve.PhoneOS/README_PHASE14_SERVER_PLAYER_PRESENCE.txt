PROJECT EVE — PHASE 14
SERVER-OWNED PLAYER WORLD PRESENCE

CORE RULE

    Closing an In Person PAGE
    is not the same thing as
    leaving an In Person PLACE.

Before Phase 14:

    Ryan at Sinclair Coffee
        ↓
    tap Messages
        ↓
    InPerson.razor disposes
        ↓
    Ryan removed from ScenePresence

That was wrong.

Now:

    Ryan at Sinclair Coffee
        ↓
    tap Messages
        ↓
    Ryan remains physically at Sinclair Coffee
        ↓
    MainLayout keeps server heartbeat alive
        ↓
    Eve / Adam / Player 2 can still perceive Ryan there

The player only changes physical location through:

    IPlayerWorldPresenceService.MoveToLocationAsync(...)

PHONE APP NAVIGATION DOES NOT MOVE THE BODY

These route changes preserve physical location:

    In Person → Messages
    Messages → Contacts
    Contacts → Calls
    Calls → Home
    Home → Calendar
    Calendar → In Person

MainLayout survives those route changes and owns the presence heartbeat.

LEFT PANEL STAYS LIVE

MainLayout also updates SceneUiStateService from the real observer-dependent
perception service.

So while Messages is open, the left panel can still say:

    Sinclair Coffee

    PRESENT
    Ryan     You
    Eve      3 ft
    Lisa     12 ft

If Eve walks out while Ryan is reading Messages, the server projection can
update without pretending Ryan left the coffee shop.

PHONE USE CAN REDUCE ATTENTION

Phase 14 does not move the player when opening Messages, but it can change
physical attention/activity:

    In Person
        activity = conversation
        attention = 0.90

    Messages / Contacts / Calls / Group Chat
        activity = using_phone
        attention = 0.58

This matters to Phase 6 perception.

A person staring at their phone may miss a subtle movement or quiet comment.
That is a cue/behavior effect — not a hard block.

SPATIAL MOVEMENT PERSISTS

Phase 14 also fixes a Phase 10/11 bug:

Old heartbeat:
    Ryan moves 4 ft → 2 ft
    heartbeat fires
    Ryan silently teleports back to slot position

New heartbeat:
    membership timestamp updates
    existing X/Y/Facing stay untouched

So:

    move closer
    move away
    hug/contact range
    fight positioning
    personal-space tells

remain real world state until something actually moves the person.

SHORT DISCONNECT / RECONNECT

PlayerWorldPresenceState persists:

    LocationId
    SceneId
    X / Y / Facing snapshot
    activity
    attention
    game-time update

A UI disconnect does NOT immediately erase physical world location.

If SharedScene membership later ages out, reattaching can restore the player's
last persisted world location and spatial coordinates.

PLAYER TRAVEL

InPerson.razor now uses PlayerWorldPresenceService for actual location changes.

It no longer does:

    SharedScenes.LeaveAsync(...)
    because the page disposed.

It only changes scene when the user actually travels to another known place.

PHASE 13 BENEFIT

WorldAdvanceCoordinator looks up the player's active shared scene to decide
whether an NPC arrival/departure is relevant.

Because Phase 14 keeps membership alive while Messages is open:

    Ryan is reading a text at Sinclair Coffee
        ↓
    Eve arrives
        ↓
    Next Event / perception still knows Ryan is physically there

DATABASE

New tables:

    PlayerWorldPresenceState
    PlayerWorldMovementEvent

The first is current truth.
The second is a movement history / debugging trail.

MULTIPLAYER NOTE

The new world-presence layer is keyed by PlayerId and is ready for separate
players.

However, the current PhoneOS PlayerProfileService is still a single local
profile service/file. A later player-session phase should separate client
identity/profile selection before calling the overall game fully two-player.

Phase 14 does NOT pretend that identity problem is solved.

NEXT PHASE

Phase 15 should build proper PLAYER TRAVEL:

    current location
        ↓
    choose known destination
        ↓
    travel method
        ↓
    route duration
        ↓
    player status = traveling
        ↓
    GameClock/world events process during trip
        ↓
    arrive

That will replace the remaining instant player-location jump.
