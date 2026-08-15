using Microsoft.Extensions.Hosting;
using ProjectEve.Core.Time;
using ProjectEve.Core.World;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.PhoneOS.Services;

/// <summary>
/// Keeps world occupancy synchronized whenever authoritative game time changes.
/// This runs on the server even when the In Person page is not open.
/// </summary>
public sealed class WorldOccupancyHostedService : IHostedService, IDisposable
{
    private readonly IGameTimeService _clock;
    private readonly IWorldOccupancyService _occupancy;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();

    public WorldOccupancyHostedService(
        IGameTimeService clock,
        IWorldOccupancyService occupancy)
    {
        _clock = clock;
        _occupancy = occupancy;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _clock.Changed += OnClockChanged;

        try
        {
            await _occupancy.SynchronizeAsync(_clock.Now, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[WorldOccupancy startup] " + ex.Message);
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
            () => SyncAsync(snapshot.GameTime, _lifetime.Token),
            _lifetime.Token);
    }

    private async Task SyncAsync(
        DateTimeOffset gameTime,
        CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken);

        try
        {
            await _occupancy.SynchronizeAsync(gameTime, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Console.WriteLine("[WorldOccupancy clock] " + ex.Message);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    public void Dispose()
    {
        _clock.Changed -= OnClockChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _syncGate.Dispose();
    }
}
