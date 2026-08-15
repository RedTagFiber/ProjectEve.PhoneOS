PROJECT EVE — PHASE 16
NPC-INITIATED COMMUNICATION + SOCIAL FOLLOW-UP

CORE CHANGE

Before:
    player texts Eve
        ↓
    Eve may answer

Now:
    Eve can decide she wants to contact the player
        ↓
    ProjectEve schedules that intent in GAME TIME
        ↓
    time reaches the contact
        ↓
    Eve writes/sends the message
        ↓
    Messages list updates

Examples:
    check in
    follow up
    keep a promise
    remind the player
    apologize
    invite them somewhere
    warn them
    share gossip she ACTUALLY knows
    work/family contact
    emergency contact

WHO OWNS WHAT

ProjectEve:
    contact motive
    commitment
    game-time trigger
    knowledge boundary
    character state
    exact conversation transcript

NpcInitiatedTextEngine:
    wording only

PhoneOS:
    delivery / contact list / message thread

This preserves the architecture:

    ProjectEve knows the world.
    ProjectEve.Core translates the world.
    PhoneOS shows the world.

NO FAKE PLAYER MESSAGE

Phase 16 does NOT fake:

    Player: "please text me"

inside the Brain just to get an outbound sentence.

NpcInitiatedTextEngine has its own outbound prompt and DOES NOT call Brain.Think().

That matters because an internal system directive must not mutate Fast traits
as though the player had actually said those words.

EXACT CONVERSATION MEMORY

When Eve initiates:

    Eve: "hey, you make it home okay?"

that exact line is appended to the real ConversationSession as an NPC message
before PhoneOS delivery.

The next player reply therefore continues the SAME text section.

If delivery crashes after generation:
    generated text remains staged
    retry uses the exact same text
    Qwen is NOT called again

KNOWLEDGE / TELEPHONE GAME SAFETY

A trigger may reference ClaimId.

Before generation:
    Phase 16 loads THIS NPC's knowledge ledger
    verifies THIS NPC owns the claim
    exposes only that held claim text

It does NOT use:
    relationship closeness
    twin/spouse status
    family connection
    hidden source transcript

as automatic knowledge.

A gossip-generation NPC cannot magically recover the original wording from
the first person in the telephone game.

CONVERSATION PLAN SAFETY

A trigger may reference ConversationPlanId.

Phase 16 verifies:
    PlayerId matches
    NpcId matches
    plan is still planned

Current ConversationPlan TimeText is free text.

Therefore Phase 16 does NOT guess:

    "later" = 2 hours
    "tomorrow" = 9 AM

The calendar/schedule system must resolve exact DueGameTime first.

SPONTANEOUS CHECK-INS

Existing phone contacts are evaluated once per GAME DAY.

The chance uses:
    NpcPhoneBehaviorProfile.InitiatesContact
    affection
    trust
    playfulness
    guard
    anger
    resentment
    internal contact tier

Max default:
    2 spontaneous contacts per player per game day

This is intentionally conservative so the player's phone does not become spam.

Spontaneous prompts are hard-locked to:

    no external event is being asserted

So the NPC can simply:
    say hey
    ask how the player is doing
    send a joke
    send a small affectionate/cold/awkward check-in

without hallucinating a crime, secret, appointment, pregnancy, argument, etc.

GAME TIME / NEXT EVENT

Every committed contact gets a GameEvent:

    npc_initiated_contact_due
    title = Phone notification

So:

    +2 hours
    Sleep
    Next Event
    Travel

can stop when a real NPC contact is due.

This also plugs directly into Phase 15 travel.

Example:

    Ryan leaves at 6:00
    arrival 6:25
    Eve's initiated text due 6:11

Travel stops at 6:11 for the phone event.
The player can read it, then Continue Travel.

PHONE DELIVERY

Phase 16 does NOT replace current PhoneMessagingService.

NpcInitiatedPhoneDeliveryService uses the existing phone database and:
    sender = npc
    SentGameTime = authoritative game time
    ConversationSessionId = real conversation session

Messages.razor already polls the contact list/thread, so an initiated text can
appear without a special Phase 16 page.

UNKNOWN NUMBER

Normally spontaneous contact uses existing PlayerPhoneContact rows only.

For an authored/simulation trigger:
    AllowUnknownNumber = true

permits first contact.

When delivered, PhoneOS creates:
    ContactSource = received_text

That matches the rule that receiving a text can create a phone contact.

BLOCK / MUTE

Blocked:
    message is not delivered
    trigger is marked skipped

Muted:
    message is still delivered
    notification behavior can be handled later

CALLS

Phase 16 implements initiated TEXT.

The contract recognizes channel="call" but deliberately does not fake a call
without the call/voicemail UI and phone-call state machine.

A later phone-call phase should build:
    ringing
    answer / decline
    missed call
    voicemail
    call duration
    active call conversation channel

DATABASE TABLES

Project Eve DB:
    NpcInitiatedContactTrigger
    NpcSpontaneousContactDay

Phone DB:
    adds InitiatedTriggerId to PhoneMessage
    unique index prevents duplicate delivery

NEXT GOOD PHASE

Phase 17 should be PHONE CALLS + VOICEMAIL + MISSED CALLS.

After that, Phase 18 can expand NPC-initiated plans:
    invitations
    meetups
    ride requests
    work shift calls
    appointment reminders
    real Calendar integration
    NPC accepts/declines/cancels/reschedules
