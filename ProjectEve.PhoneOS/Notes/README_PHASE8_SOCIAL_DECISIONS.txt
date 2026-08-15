PROJECT EVE — PHASE 8
SOCIAL DISCLOSURE / GOSSIP DECISION LAYER

PURPOSE
Phase 6 answered: who physically heard/saw it?
Phase 7 answered: what does each NPC personally know/believe?
Phase 8 answers: what does an NPC choose to DO with that knowledge socially?

This phase adds decisions for:
- keep_private
- deflect
- hint
- share
- gossip
- warn
- confront
- distort

The decision uses real ProjectEve character state:
- Fast traits: openness, guard, anger, anxiety, fear, shame, guilt,
  jealousy, resentment, trust, affection, pride, patience, tension
- relationship to the proposed recipient: trust, respect, affection, tension
- source claim confidence
- telephone-game generation
- secrecy
- urgency
- consequence risk
- motive
- whether the NPC was asked directly
- how many OTHER people the source NPC currently perceives nearby

IMPORTANT ARCHITECTURE RULES
1. ProjectEve remains source of truth.
2. Phase 8 does NOT create a knowledge claim from nothing.
3. Relationship closeness never grants knowledge.
4. Phase 8 decides disclosure intent; it does not invent the actual words.
5. ActualText is supplied only when the decision is executed.
6. In-person execution goes through Phase 6 hearing.
   This means an unintended nearby NPC can overhear.
7. Full disclosure actions then go through Phase 7 knowledge transmission.
8. A hint/deflection does NOT silently copy the hidden claim metadata.
9. Distortion means the NPC is willing to frame/mislead from what THEY
   believe. It does not give them access to hidden world truth.
10. Decisions are persisted for audit/debug and the same pending decision
    is not rerolled every UI refresh.

NEW DATABASE TABLE
NpcSocialDecision

This stores:
- source NPC
- source claim
- intended target NPC
- action chosen
- share/privacy/distortion/confront scores
- audience count
- suggested voice level
- motive
- game time
- execution status
- exact ActualText after execution

NO BRAIN.CS REPLACEMENT
This phase does NOT replace Brain.cs.
It also does NOT replace your InPerson UI or app.css.

PHASE 8 DOES NOT YET AUTO-GENERATE RANDOM GOSSIP MOMENTS
It provides the authoritative decision layer and execution bridge.
A later social/group/world orchestrator can ask:
"Eve knows this. Is she inclined to tell anyone right now?"
Then RankRecipientsAsync can rank only NPCs Eve actually perceives in the scene.

WHY THIS SEPARATION MATTERS
Knowing a secret is not the same as wanting to tell it.
Wanting to tell it is not the same as being able to tell it privately.
Trying to tell one person does not mean a nearby person cannot overhear.
And being told something does not make it world truth.

BUILD NOTE
This environment does not have the .NET compiler installed, so Visual Studio
is still the compile authority. The package was structured against the current
ProjectEve SimCharacter/NpcTraits/Relationship APIs and the Phase 6/7 contracts.
