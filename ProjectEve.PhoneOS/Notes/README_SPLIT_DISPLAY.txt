PROJECT EVE - PHASE 5 IN PERSON SPLIT DISPLAY v1

Fixes the current mixed in-person output where Eve's visible movement and spoken
words are being painted as one SPEECH line, for example:

  smiles, leans in... **Hello Ryan. You're here early.**

REPLACE:
  ProjectEve.PhoneOS/Components/Pages/InPerson.razor
  ProjectEve.PhoneOS/wwwroot/app.css

WHAT CHANGED
- Existing **spoken words** are extracted into a separate SPEECH SceneEntry.
- Text outside **...** becomes BODY LANGUAGE / presentation instead of speech.
- Structured BODY:, ACTION:, SAY: output still works.
- Mixed PRESENTATION/BODY/ACTION lines containing **speech** are split safely.
- Leftover Markdown asterisks are removed from the visible scene.
- Prompt now asks for strict BODY/ACTION/SAY lines with no Markdown.
- Speech/action/body-language use more visibly different variants of the same
  NPC identity hue:
    speech = brightest/strongest
    action = darker italic
    body = softer/faded italic
- Still plain text. No bubbles added.

EXPECTED DISPLAY
  EVE SINCLAIR · BODY
  Smiles softly, leans in slightly, eyes flicker to her necklace then back.

  EVE SINCLAIR · SPEECH
  Hello Ryan. You're here earlier than I expected.

This patch does not replace Brain.cs, GameClock, messaging, or conversation memory.
