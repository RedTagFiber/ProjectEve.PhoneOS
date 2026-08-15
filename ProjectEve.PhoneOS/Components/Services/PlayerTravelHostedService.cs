using Microsoft.Extensions.Hosting;
using ProjectEve.Core.Time;
using ProjectEve.Core.World;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.PhoneOS.Services;

/// <summary>
/// Completes due player trips even when game time was advanced by another
/// subsystem instead of the Travel panel.
/// </summary>
public sealed class PlayerTravelHostedService : IHostedService, IDisposable
{
    private readonly IGameTimeService _clock;
    private readonly IPlayerTravelService _travel;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();

    public PlayerTravelHostedService(
        IGameTimeService clock,
        IPlayerTravelService travel)
    {
        _clock = clock;
        _travel = travel;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _clock.Changed += OnClockChanged;

        try
        {
            await _travel.FinalizeDueTravelsAsync(
                _clock.Now,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[PlayerTravel startup] " + ex.Message);
        }
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
            () => FinalizeAsync(snapshot.GameTime, _lifetime.Token),
            _lifetime.Token);
    }

    private async Task FinalizeAsync(
        DateTimeOffset gameTime,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _travel.FinalizeDueTravelsAsync(
                gameTime,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Console.WriteLine("[PlayerTravel clock] " + ex.Message);
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
