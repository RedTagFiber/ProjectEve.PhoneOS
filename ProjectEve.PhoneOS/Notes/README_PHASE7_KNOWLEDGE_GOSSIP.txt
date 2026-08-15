PROJECT EVE — PHASE 7
PERSONAL KNOWLEDGE + GOSSIP / TELEPHONE GAME
============================================================

PURPOSE
-------
Phase 6 answered: who physically heard/saw what?
Phase 7 answers: what does EACH NPC personally know/believe, where did it come from,
and what happens when one NPC tells another?

HARD RULES
----------
1. World truth is NOT the same thing as NPC knowledge.
2. Exact ConversationMessage transcript remains evidence only.
3. Relationship closeness is never telepathy.
4. A scene perception belongs only to the observer that actually perceived it.
5. Gossip creates a NEW claim owned by the recipient.
6. The recipient receives the wording it actually heard, not the source NPC's hidden evidence.
7. Every gossip handoff increments Generation.
8. Partial hearing stays partial. Missing words are never reconstructed.
9. Player-specific knowledge is scoped by PlayerId for the future two-player server.
10. No Brain.cs replacement in this phase.

WHAT THIS PHASE ADDS
--------------------
CORE CONTRACTS
  ProjectEve.Core/ProjectEve.Core/Knowledge/INpcKnowledgeService.cs
  ProjectEve.Core/ProjectEve.Core/Knowledge/INpcKnowledgeCommunicationService.cs

MAIN PROJECT
  ProjectEve/Knowledge/NpcKnowledgeService.cs
  ProjectEve/Knowledge/NpcKnowledgeCommunicationService.cs

REPLACED / INTEGRATION FILES
  ProjectEve/AI/ProjectEveConversationService.cs
  ProjectEve/Conversations/ConversationPromptContext.cs
  ProjectEve.PhoneOS/Program.cs

DATABASE TABLES CREATED AUTOMATICALLY
-------------------------------------
NpcKnowledgeClaim
  One row = one thing a specific NPC learned/perceived/was told.

KnowledgeTransmission
  One row = one NPC-to-NPC report event.
  Stores source claim, recipient claim, exact reported wording, generation,
  confidence, game time, channel, and scene.

AUTOMATIC FLOW
--------------
A) DIRECT CONVERSATION

Player -> Eve conversation
    ↓
exact transcript stored
    ↓
conversation closes
    ↓
Phase 6 perception-scoped summary/facts
    ↓
Phase 7 imports those facts into EVE'S personal ledger only

If the conversation changes from text -> in-person, the old section is imported too.

B) OVERHEARING / OBSERVATION

Ryan says something in a scene
    ↓
Phase 6 decides who actually heard/saw it
    ↓
ScenePerceptionEvidence
    ↓
when that NPC next thinks, Phase 7 lazily imports ONLY that NPC's evidence
    ↓
NPC personal knowledge context

Example:
Eve heard clearly -> Eve stores clear perceived speech.
Lisa heard a fragment -> Lisa stores the fragment only.
Adam heard nothing -> Adam receives nothing.

C) TELEPHONE GAME

NpcKnowledgeCommunicationService.SpeakKnownClaimAsync(...)

Eve knows claim #12
    ↓
Eve actually says her version aloud
    ↓
Phase 6 hearing resolves the room
    ↓
Adam hears clearly
Lisa hears only a fragment
Edward hears nothing
    ↓
Phase 7 creates:
  Adam claim generation 1 = what Adam actually heard
  Lisa claim generation 1 = Lisa's fragment
  Edward = no claim

Later Adam can tell Edward using ADAM'S claim as the source.
That creates generation 2. The hidden generation-0 wording is never copied to Edward.

WHY ReportedText IS REQUIRED
----------------------------
INpcKnowledgeService.TransmitAsync deliberately refuses a transmission without
ReportedText. That prevents code from doing this dangerous shortcut:

  sourceNpc.HasSecret -> recipientNpc.HasSecret

Instead the caller must provide what the source NPC actually communicated.
That wording may omit details, exaggerate, soften, lie, or simply be different.

PROMPT CONTEXT
--------------
Before Thought/Dialogue, ProjectEveConversationService now asks the knowledge
service for THIS NPC's personal ledger.

The prompt explicitly tells the model:
- this is belief/knowledge, not verified world truth
- gossip generation 1+ is reported information
- fragments cannot be repaired
- knowledge owned by another NPC is inaccessible
- facts scoped to another PlayerId cannot be used

INSTALL ORDER
-------------
1. ADD both Core files under:
   ProjectEve.Core/ProjectEve.Core/Knowledge/

2. BUILD ProjectEve.Core

3. ADD both main files under:
   ProjectEve/Knowledge/

4. REPLACE:
   ProjectEve/AI/ProjectEveConversationService.cs
   ProjectEve/Conversations/ConversationPromptContext.cs

5. BUILD ProjectEve

6. REPLACE:
   ProjectEve.PhoneOS/Program.cs

7. BUILD ProjectEve.PhoneOS

8. Clean Solution -> Rebuild Solution

DO NOT DELETE / REPLACE
-----------------------
- Brain.cs
- Phase 6 IScenePerceptionService.cs in ProjectEve.Core
- Phase 6 ProjectEve/Scene/ScenePerceptionService.cs
- your approved app.css
- InPerson.razor

IMPORTANT NAMESPACE RULE
------------------------
Core contracts exist ONLY under:
  namespace ProjectEve.Core.Knowledge

Implementations exist ONLY under:
  namespace ProjectEve.Knowledge

Do not copy the Core interface files into the main ProjectEve/Knowledge folder.
That would recreate the duplicate-type problem from Phase 6.

TESTING
-------
After rebuild:

1. Talk to Eve.
2. End/change the conversation section so it gets summarized.
3. Inspect NpcKnowledgeClaim using TEST_PHASE7_KNOWLEDGE_GOSSIP.sql.
4. For Phase 6 scene evidence, talk/act where another NPC can overhear.
5. Have that NPC enter cognition again; its own perception evidence should be
   imported into its ledger.
6. Future GroupConversationOrchestrator should call
   INpcKnowledgeCommunicationService.SpeakKnownClaimAsync whenever one NPC is
   actually telling a stored claim to others.

CURRENT PHASE LIMIT
-------------------
Phase 7 implements knowledge ownership, provenance, perception import, and the
telephone-game transfer engine. It does NOT yet make NPCs autonomously decide
"I feel like gossiping about this right now." That behavior decision belongs in
the group/social orchestrator layer so traits, motive, relationship, secrecy,
setting, and risk can decide whether the NPC tells anyone.

That separation is intentional:
  KNOWLEDGE SYSTEM = what they know / what was transferred
  SOCIAL BRAIN     = whether they choose to tell it
