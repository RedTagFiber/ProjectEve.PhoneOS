PROJECT EVE - PHASE 5 IN PERSON BACKGROUND + UI UNLOCK

REPLACE:
1) ProjectEve.PhoneOS/Components/Pages/InPerson.razor
2) ProjectEve.PhoneOS/wwwroot/app.css

WHAT CHANGED
- Uses the current location image as the full In Person background:
  /images/scenes/coffee-shop.png
  /images/scenes/eve-apartment.png
  /images/scenes/ryans-house.png
- Keeps scene description / scene updates in the same chronological text stream.
- No new scene card or bubble was added.
- Dark transparent wash + translucent top/composer keep text readable while the location stays visible.
- Fixes the "In Person locks up while Eve thinks" behavior:
  * NPC generation runs off the Blazor UI circuit.
  * The page remains scrollable/navigation drawers remain usable.
  * ACTION/SAY fields stay editable while the NPC response is being generated.
  * Send is still guarded so one player cannot start overlapping Eve turns.
  * TTS runs after text in the background and no longer holds the chat UI waiting for a WAV.
  * An old reply is not painted into a new location after travel.

BUILD
Clean Solution -> Rebuild Solution.

NOTE
This patch does NOT replace Brain.cs, GameClock, scheduler, or conversation-memory files.
