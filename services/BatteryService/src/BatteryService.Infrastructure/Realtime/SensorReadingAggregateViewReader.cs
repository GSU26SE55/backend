using System.Data;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BatteryService.Infrastructure.Realtime;

/// <summary>
/// Sprint Bonus NS-06 (#650, PA-4) — đọc continuous aggregate <c>sensor_readings_agg_1h</c> bằng
/// ADO.NET thô (view không map EF entity). Real-time aggregation bật → trả cả bucket chưa materialize.
/// </summary>
public class SensorReadingAggregateViewReader : ISensorReadingAggregateViewReader
{
    private const string Sql = @"
SELECT bucket,
       avg_voltage, avg_current, avg_temperature, avg_soc_percent, avg_soh_percent,
       min_voltage, max_voltage, min_temperature, max_temperature,
       max_charge_current, min_charge_current, avg_charge_current,
       max_discharge_current, min_discharge_current, avg_discharge_current,
       charge_sample_count, discharge_sample_count
FROM sensor_readings_agg_1h
WHERE battery_asset_id = @assetId
  AND (@from IS NULL OR bucket >= @from)
  AND (@to   IS NULL OR bucket <= @to)
ORDER BY bucket;";

    private readonly ApplicationDbContext _db;

    public SensorReadingAggregateViewReader(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<SensorReadingAggregateDto>> ReadHourlyAsync(
        Guid batteryAssetId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
    {
        var connection = _db.Database.GetDbConnection();
        var openedHere = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            openedHere = true;
        }

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = Sql;
            AddParam(cmd, "@assetId", NpgsqlDbType.Uuid, batteryAssetId);
            AddParam(cmd, "@from", NpgsqlDbType.TimestampTz, (object?)ToUtc(from) ?? DBNull.Value);
            AddParam(cmd, "@to", NpgsqlDbType.TimestampTz, (object?)ToUtc(to) ?? DBNull.Value);

            var result = new List<SensorReadingAggregateDto>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new SensorReadingAggregateDto
                {
                    Time = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc),
                    AvgVoltage = Dec(reader, 1) ?? 0m,
                    AvgCurrent = Dec(reader, 2) ?? 0m,
                    AvgTemperature = Dec(reader, 3) ?? 0m,
                    AvgSocPercent = Dec(reader, 4) ?? 0m,
                    AvgSohPercent = Dec(reader, 5),
                    MinVoltage = Dec(reader, 6),
                    MaxVoltage = Dec(reader, 7),
                    MinTemperature = Dec(reader, 8),
                    MaxTemperature = Dec(reader, 9),
                    MaxChargeCurrent = Dec(reader, 10),
                    MinChargeCurrent = Dec(reader, 11),
                    AvgChargeCurrent = Dec(reader, 12),
                    MaxDischargeCurrent = Dec(reader, 13),
                    MinDischargeCurrent = Dec(reader, 14),
                    AvgDischargeCurrent = Dec(reader, 15),
                    ChargeSampleCount = Count(reader, 16),
                    DischargeSampleCount = Count(reader, 17)
                });
            }

            return result;
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Type PHẢI khai báo tường minh: <c>@from</c>/<c>@to</c> có thể là <see cref="DBNull"/>, khi đó Npgsql
    /// không suy được kiểu từ value nên gửi OID 0 → Postgres không resolve nổi <c>$n IS NULL</c> trong
    /// mệnh đề WHERE và trả 42P08 "could not determine data type of parameter".
    /// </summary>
    private static void AddParam(System.Data.Common.DbCommand cmd, string name, NpgsqlDbType type, object value)
    {
        var p = new NpgsqlParameter(name, type) { Value = value };
        cmd.Parameters.Add(p);
    }

    private static decimal? Dec(System.Data.Common.DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal));

    private static int Count(System.Data.Common.DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

    private static DateTime? ToUtc(DateTime? value)
    {
        if (value is null)
            return null;
        var v = value.Value;
        return v.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(v, DateTimeKind.Utc)
            : v.ToUniversalTime();
    }
}
