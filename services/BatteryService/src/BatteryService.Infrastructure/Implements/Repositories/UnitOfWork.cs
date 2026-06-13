using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;
using SharedInfrastructure.Persistence.Repositories;
using SharedKernels.Interfaces;

namespace BatteryService.Infrastructure.Implements.Repositories;

public class UnitOfWork : IBatteryUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private IDbContextTransaction? _currentTransaction;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public IGenericRepository<BatteryType> BatteryTypes => new GenericRepository<BatteryType>(_context);

    public IGenericRepository<BatteryAsset> BatteryAssets => new GenericRepository<BatteryAsset>(_context);

    public IGenericRepository<Site> Sites => new GenericRepository<Site>(_context);

    public IGenericRepository<CustomerAccount> CustomerAccounts => new GenericRepository<CustomerAccount>(_context);

    public IGenericRepository<ThresholdConfig> ThresholdConfigs => new GenericRepository<ThresholdConfig>(_context);

    public IGenericRepository<SensorReading> SensorReadings => new GenericRepository<SensorReading>(_context);

    public IGenericRepository<Alert> Alerts => new GenericRepository<Alert>(_context);

    public IGenericRepository<OutboxMessage> OutboxMessages => new GenericRepository<OutboxMessage>(_context);

    // Sprint 5B #89 — Ambient monitoring.
    public IGenericRepository<AmbientReading> AmbientReadings => new GenericRepository<AmbientReading>(_context);
    public IGenericRepository<AmbientThresholdConfig> AmbientThresholdConfigs => new GenericRepository<AmbientThresholdConfig>(_context);

    // Sprint 5B #100 — Environmental incident.
    public IGenericRepository<EnvironmentalIncident> EnvironmentalIncidents => new GenericRepository<EnvironmentalIncident>(_context);

    // Sprint 5B B1 (#152) — Noise breach hypertable.
    public IGenericRepository<NoiseBreachEvent> NoiseBreachEvents => new GenericRepository<NoiseBreachEvent>(_context);

    public async Task BeginTransactionAsync()
    {
        if (_currentTransaction is not null)
            return;

        _currentTransaction = await _context.Database.BeginTransactionAsync();
    }

    public async Task CommitTransactionAsync()
    {
        try
        {
            await _context.SaveChangesAsync();

            if (_currentTransaction is not null)
                await _currentTransaction.CommitAsync();
        }
        catch
        {
            await RollbackTransactionAsync();
            throw;
        }
        finally
        {
            if (_currentTransaction is not null)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }
    }

    public async Task RollbackTransactionAsync()
    {
        try
        {
            if (_currentTransaction is not null)
                await _currentTransaction.RollbackAsync();
        }
        finally
        {
            if (_currentTransaction is not null)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
