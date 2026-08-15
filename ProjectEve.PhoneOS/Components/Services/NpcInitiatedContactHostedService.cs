using Microsoft.Extensions.Hosting;
using ProjectEve.Core.Phone;
using ProjectEve.Core.Time;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.PhoneOS.Services;

/// <summary>
/// Bridges ProjectEve's initiated-contact truth into the existing phone DB.
///
/// Game time drives this service. Wall time does not advance the world.
/// </summary>
public sealed class NpcInitiatedContactHostedService : IHostedService, IDisposable
{
    private readonly INpcInitiatedContactService _initiated;
    private readonly NpcInitiatedPhoneDeliveryService _delivery;
    private readonly PhoneMessagingService _messaging;
    private readonly PlayerProfileService _players;
    private readonly IGameTimeService _clock;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();

    public NpcInitiatedContactHostedService(
        INpcInitiatedContactService initiated,
        NpcInitiatedPhoneDeliveryService delivery,
        PhoneMessagingService messaging,
        PlayerProfileService players,
        IGameTimeService clock)
    {
        _initiated = initiated;
        _delivery = delivery;
        _messaging = messaging;
        _players = players;
        _clock = clock;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _clock.Changed += OnClockChanged;

        await ProcessAsync(
            _clock.Now,
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _clock.Changed -= OnClockChanged;
        _lifetime.Cancel();
        return Task.CompletedTask;
    }

    private void OnClockChanged(GameTimeSnapshot snapshot)
    {
        _ = Task.Run(
            () => ProcessAsync(
                snapshot.GameTime,
                _lifetime.Token),
            _lifetime.Token);
    }

    private async Task ProcessAsync(
        DateTimeOffset gameTime,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (!_players.HasPlayer || _players.Current == null)
                return;

            var player = _players.Current;

            string playerName =
                !string.IsNullOrWhiteSpace(player.FullName)
                    ? player.FullName
                    : player.DisplayName;

            // Existing contacts can occasionally decide to initiate a simple
            // check-in. Blocked contacts are never candidates.
            var contacts = _messaging
                .GetContacts(player.Id)
                .Select(x => new NpcSpontaneousContactCandidate
                {
                    NpcId = x.NpcId,
                    NpcName = x.DisplayName,
                    ContactTier = x.ContactTier,
                    IsBlocked = x.IsBlocked
                })
                .ToArray();

            await _initiated.EnsureSpontaneousCheckInsAsync(
                new NpcSpontaneousContactDayRequest
                {
                    PlayerId = player.Id,
                    PlayerName = playerName,
                    GameTime = gameTime,
                    MaxSpontaneousContactsPerDay = 2,
                    Contacts = contacts
                },
                cancellationToken);

            var due = await _initiated.ProcessDueAsync(
                gameTime,
                cancellationToken);

            foreach (var outbound in due)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var delivered = await _delivery.DeliverAsync(
                        outbound,
                        cancellationToken);

                    if (delivered.Delivered)
                    {
                        await _initiated.MarkDeliveredAsync(
                            outbound.TriggerId,
                            delivered.PhoneMessageId,
                            cancellationToken);
                    }
                    else
                    {
                        await _initiated.MarkSkippedAsync(
                            outbound.TriggerId,
                            delivered.Reason,
                            cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        "[NPC initiated phone delivery] " + ex.Message);

                    // Leave status='generated'. The exact text is already staged
                    // and will be retried without another AI generation.
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[NPC initiated contact] " + ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _clock.Changed -= OnClockChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _gate.Dispose();
    }
}
