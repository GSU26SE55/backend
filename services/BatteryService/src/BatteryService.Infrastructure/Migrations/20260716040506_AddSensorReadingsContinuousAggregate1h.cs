using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <summary>
    /// Sprint Bonus NS-06 (#650, PA-4) — TimescaleDB continuous aggregate 1h cho min/max nạp/xả.
    /// Giải bài toán scale range dài (tháng/năm) mà /aggregate in-memory (NS-02) không kham nổi;
    /// query gần O(số bucket). Chỉ tính trên reading source `primary` (khớp NS-02).
    ///
    /// ⚠️ Continuous aggregate + refresh policy KHÔNG chạy được trong transaction → mọi statement
    /// dùng <c>suppressTransaction: true</c>. Xem checklist migration rule 14: rollback test bắt buộc
    /// chạy trên TimescaleDB thật (không có trong CI unit test).
    /// </summary>
    public partial class AddSensorReadingsContinuousAggregate1h : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // WITH NO DATA: tránh materialize toàn bộ lịch sử ngay trong DDL (initial refresh do policy lo).
            migrationBuilder.Sql(@"
CREATE MATERIALIZED VIEW IF NOT EXISTS sensor_readings_agg_1h
WITH (timescaledb.continuous) AS
SELECT time_bucket('1 hour', time)              AS bucket,
       battery_asset_id,
       avg(voltage)                             AS avg_voltage,
       avg(current)                             AS avg_current,
       avg(temperature)                         AS avg_temperature,
       avg(soc_percent)                         AS avg_soc_percent,
       avg(soh_percent)                         AS avg_soh_percent,
       min(voltage)                             AS min_voltage,
       max(voltage)                             AS max_voltage,
       min(temperature)                         AS min_temperature,
       max(temperature)                         AS max_temperature,
       max(current) FILTER (WHERE current > 0)      AS max_charge_current,
       min(current) FILTER (WHERE current > 0)      AS min_charge_current,
       avg(current) FILTER (WHERE current > 0)      AS avg_charge_current,
       max(abs(current)) FILTER (WHERE current < 0) AS max_discharge_current,
       min(abs(current)) FILTER (WHERE current < 0) AS min_discharge_current,
       avg(abs(current)) FILTER (WHERE current < 0) AS avg_discharge_current,
       count(*) FILTER (WHERE current > 0)          AS charge_sample_count,
       count(*) FILTER (WHERE current < 0)          AS discharge_sample_count
FROM sensor_readings
WHERE sensor_source_code = 'primary' OR sensor_source_code IS NULL
GROUP BY bucket, battery_asset_id
WITH NO DATA;", suppressTransaction: true);

            // Real-time aggregation: query trả cả phần chưa materialize (dữ liệu gần đây) — không lệch NS-02.
            migrationBuilder.Sql(
                "ALTER MATERIALIZED VIEW sensor_readings_agg_1h SET (timescaledb.materialized_only = false);",
                suppressTransaction: true);

            // Policy tự refresh: materialize [now-3h, now-5m], chạy mỗi 1 phút.
            migrationBuilder.Sql(@"
SELECT add_continuous_aggregate_policy('sensor_readings_agg_1h',
    start_offset      => INTERVAL '3 hours',
    end_offset        => INTERVAL '5 minutes',
    schedule_interval => INTERVAL '1 minute',
    if_not_exists     => TRUE);", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CASCADE gỡ luôn refresh policy phụ thuộc. Continuous aggregate không drop được trong transaction.
            migrationBuilder.Sql(
                "DROP MATERIALIZED VIEW IF EXISTS sensor_readings_agg_1h CASCADE;",
                suppressTransaction: true);
        }
    }
}
