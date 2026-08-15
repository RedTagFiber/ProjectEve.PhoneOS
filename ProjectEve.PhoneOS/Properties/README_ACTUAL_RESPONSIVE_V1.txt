PROJECT EVE PHONEOS — ACTUAL RESPONSIVE PATCH v1
================================================

THIS PATCH WAS BUILT AGAINST THE PHONEOS ZIP YOU UPLOADED.

FILES REPLACED
--------------
ProjectEve.PhoneOS/Components/Layout/MainLayout.razor
ProjectEve.PhoneOS/wwwroot/app.css
ProjectEve.PhoneOS/Program.cs

WHAT CHANGED
------------

1. REAL PHONE MODE
On screens below 820px:
- PhoneOS fills 100% of the real phone screen.
- The old fake phone bezel is removed.
- The fake notch is removed.
- The old rounded inner phone screen is removed.
- Safe-area top/bottom insets are respected.
- Existing Home, Messages, GroupChat and other page logic is preserved.

This means:
REAL PHONE -> THE WEB APP IS THE PHONE SCREEN.
No phone-inside-a-phone.

2. LAPTOP / DESKTOP MODE
At 820px and wider:
- A permanent Project Eve navigation rail appears on the left.
- The old fake phone body becomes a wide desktop companion panel.
- Maximum workspace grows to 1180px.
- Home app grid expands to four columns.
- Conversation bubbles stay centered/readable instead of stretching across the monitor.
- Existing message/chat code remains intact.

This is intentionally a "phone + laptop" style rather than simply making
the mobile phone 3x wider.

3. LAN / SERVER-READY HOOK
Program.cs now supports:

    EVE_PHONEOS_URLS

Example on the laptop:

    set EVE_PHONEOS_URLS=http://0.0.0.0:5055

Then start PhoneOS.

A phone on the same LAN can later browse to:

    http://<LAPTOP-LAN-IP>:5055

Windows Firewall may need to allow the port.

If EVE_PHONEOS_URLS is NOT set, the existing Visual Studio / ASP.NET
launch profile behavior is unchanged.

IMPORTANT
---------
This patch changes presentation + adds the LAN binding hook.

It does NOT yet replace the existing immediate:

    Chat.GetReplyAsync("phone", text)

flow in Messages.razor.

That is the NEXT backend phase, where we connect:
- PlayerId
- PlayerPhoneContact
- MessageThread
- ConversationManager
- delayed NPC responses
- availability/willingness scheduler
- cross-channel memory
- server-owned state

The current working chat behavior was intentionally preserved in this
visual patch so we can change one layer at a time.

INSTALL
-------
Copy the ProjectEve.PhoneOS folder from this patch over the matching
ProjectEve.PhoneOS project folder and allow these three files to replace
the current versions.

Then rebuild.

TEST
----
Laptop:
- Home should use the wide companion layout.
- Left navigation rail should be visible.
- No giant fake narrow phone should waste the monitor.

Browser DevTools / actual phone-width:
- Fake bezel/notch should disappear.
- App should fill the full viewport.
- Message composer should stay at the bottom with safe-area spacing.

NEXT DEVELOPMENT STEP
---------------------
Next patch should rebuild the actual Messages data flow around the
server-first conversation/message architecture, while preserving this
responsive layout.
