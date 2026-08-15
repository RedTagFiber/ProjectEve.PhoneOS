using ProjectEve.Core.Time;

namespace ProjectEve.PhoneOS.Services;

/// <summary>
/// PhoneOS-facing orchestration for "Next Event". The authoritative clock and
/// event queue remain ProjectEve-owned. Pending phone scheduler checks are
/// treated as hidden stepping stones: the player stops only for an actual
/// player event/contact, not every internal phone reconsideration.
/// </summary>
public sealed class GameplayTimeControllerService
{
    private readonly IWorldAdvanceCoordinator _worldTime;
    private readonly PhoneMessagingService _phone;

    public GameplayTimeControllerService(
        IWorldAdvanceCoordinator worldTime,
        PhoneMessagingService phone)
    {
        _worldTime = worldTime;
        _phone = phone;
    }

    public async Task<GameTimeAdvanceResult> NextEventAsync(
        string playerId,
        CancellationToken cancellationToken = default)
    {
        playerId = string.IsNullOrWhiteSpace(playerId)
            ? "legacy-player"
            : playerId.Trim();

        // Twelve hidden phone reconsiderations is already a long chain. If an
        // NPC still has not replied, preserve silence instead of hot-looping.
        for (var stepIndex = 0; stepIndex < 12; stepIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var worldEvent = await _worldTime.PeekNextPlayerEventAsync(
                playerId,
                cancellationToken);

            var phoneDue = _phone.GetNextPendingPhoneGameTime(playerId);

            if (worldEvent is not null &&
                (!phoneDue.HasValue || worldEvent.GameTime <= phoneDue.Value))
            {
                return await _worldTime.AdvanceToNextPlayerEventAsync(
                    playerId,
                    cancellationToken);
            }

            if (!phoneDue.HasValue)
            {
                // The hosted phone worker may have claimed the exact row a
                // millisecond before this controller looked. Do not tell the
                // player "nothing happened" while an NPC reply is already
                // being generated. Briefly wait for that claimed work to
                // either produce a real NPC message or become pending again.
                if (_phone.HasProcessingPhoneWork(playerId))
                {
                    var claimedResult = await WaitForClaimedPhoneWorkAsync(
                        playerId,
                        cancellationToken);

                    if (claimedResult is not null)
                        return claimedResult;

                    // Scheduler may have moved the claimed message back to a
                    // future in-game opportunity. Re-enter the outer loop so
                    // that opportunity remains a hidden stepping stone.
                    if (_phone.GetNextPendingPhoneGameTime(playerId).HasValue)
                        continue;
                }

                return await _worldTime.AdvanceToNextPlayerEventAsync(
                    playerId,
                    cancellationToken);
            }

            var advance = await _worldTime.AdvanceUntilAsync(
                playerId,
                phoneDue.Value,
                "next_event_phone_step",
                cancellationToken);

            // A real scheduled world event appeared before the hidden phone
            // check. Stop there and let the player react.
            if (advance.InterruptedByEvent)
                return advance;

            _phone.WakePhoneWorkForGameTime(playerId, _worldTime.Now);

            var beforeNpcMessage = _phone.GetLatestNpcMessageId(playerId);
            var processed = await _phone.ProcessOneDuePhoneBurstAsync(
                playerId,
                cancellationToken);
            var afterNpcMessage = _phone.GetLatestNpcMessageId(playerId);

            if (processed && afterNpcMessage > beforeNpcMessage)
            {
                // Background replies also queue a normal npc_contact event.
                // If this thread was actively open, there may be no duplicate
                // event; returning this result still stops Next Event correctly.
                var contactEvent = await _worldTime.PeekNextPlayerEventAsync(
                    playerId,
                    cancellationToken);

                if (contactEvent is not null &&
                    contactEvent.GameTime <= _worldTime.Now &&
                    contactEvent.EventType.Equals("npc_contact", StringComparison.OrdinalIgnoreCase))
                {
                    return await _worldTime.AdvanceToNextPlayerEventAsync(
                        playerId,
                        cancellationToken);
                }

                return new GameTimeAdvanceResult
                {
                    FromGameTime = advance.FromGameTime,
                    ToGameTime = _worldTime.Now,
                    InterruptedByEvent = true,
                    Message = "A new message arrived."
                };
            }

            // If the scheduler chose retry_later, it has written a new
            // ReplyDueGameTime. Loop to that hidden opportunity while checking
            // the world-event queue again first.
        }

        return new GameTimeAdvanceResult
        {
            FromGameTime = _worldTime.Now,
            ToGameTime = _worldTime.Now,
            Message = "No player-relevant event happened yet."
        };
    }

    private async Task<GameTimeAdvanceResult?> WaitForClaimedPhoneWorkAsync(
        string playerId,
        CancellationToken cancellationToken)
    {
        var beforeNpcMessage = _phone.GetLatestNpcMessageId(playerId);

        // This is not simulated world waiting. It is only a short real-time
        // race guard while the local worker already owns the row. Ten seconds
        // covers the normal local-model response/fallback window without
        // turning Next Event into a long blocking action.
        for (var i = 0; i < 40; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken);

            var afterNpcMessage = _phone.GetLatestNpcMessageId(playerId);
            if (afterNpcMessage > beforeNpcMessage)
            {
                var queuedContact = await _worldTime.PeekNextPlayerEventAsync(
                    playerId,
                    cancellationToken);

                if (queuedContact is not null &&
                    queuedContact.GameTime <= _worldTime.Now &&
                    queuedContact.EventType.Equals(
                        "npc_contact",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return await _worldTime.AdvanceToNextPlayerEventAsync(
                        playerId,
                        cancellationToken);
                }

                return new GameTimeAdvanceResult
                {
                    FromGameTime = _worldTime.Now,
                    ToGameTime = _worldTime.Now,
                    InterruptedByEvent = true,
                    Message = "A new message arrived."
                };
            }

            if (!_phone.HasProcessingPhoneWork(playerId))
                return null;
        }

        return null;
    }
}
