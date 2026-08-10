using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <summary>
    /// Sprint IoT-3 (IOT3-79) — nén + xoá dữ liệu cũ cho hypertable <c>sensor_readings</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Vì sao cần:</b> mỗi pin sinh 3 bản ghi mỗi chu kỳ đo (primary BMS · redundant INA226 ·
    /// external-temp DS18B20). Ở chu kỳ 5 giây, một pin là ~1,55 triệu dòng/năm; nhân số pin lên là
    /// bảng phình không giới hạn. Nén giảm khoảng 10–20 lần với dữ liệu chuỗi thời gian, còn xoá
    /// giữ dung lượng ở mức có trần.
    /// </para>
    /// <para>
    /// <b>7 ngày mới nén:</b> chunk đã nén ghi vào được nhưng chậm hơn hẳn, và mọi truy vấn "gần
    /// đây" (dashboard, phát hiện bất thường, xem chi tiết ticket) đều nằm trong vài ngày đổ lại.
    /// Nén sớm hơn là đánh đổi tốc độ đường nóng lấy dung lượng — sai chiều.
    /// </para>
    /// <para>
    /// <b>180 ngày mới xoá:</b> đủ cho báo cáo theo quý và cho việc so sánh cùng kỳ nửa năm. Số
    /// liệu dài hơn thế đã có <c>sensor_readings_agg_1h</c> giữ ở dạng tổng hợp — continuous
    /// aggregate KHÔNG bị retention policy của bảng gốc xoá theo.
    /// </para>
    /// <para>
    /// <b>An toàn với continuous aggregate:</b> <c>sensor_readings_agg_1h</c> chỉ materialize
    /// khoảng <c>[now−3h, now−5m]</c> (xem migration <c>AddSensorReadingsContinuousAggregate1h</c>).
    /// Cả hai policy dưới đây đều đụng dữ liệu cũ hơn 7 ngày, tức là ngoài hẳn cửa sổ đó — refresh
    /// không bao giờ phải đọc chunk đang bị nén hay đã bị xoá.
    /// </para>
    /// <para>
    /// ⚠️ <b>Yêu cầu TimescaleDB ≥ 2.11.</b> Bản cũ hơn KHÔNG cho INSERT vào chunk đã nén; thiết bị
    /// gửi bù dữ liệu cũ sau một đợt mất mạng dài sẽ bị từ chối. Khối <c>DO</c> ở đầu <c>Up()</c>
    /// kiểm và DỪNG migration với thông báo rõ ràng, thay vì để lỗi lộ ra nhiều tháng sau dưới dạng
    /// mất số liệu.
    /// </para>
    /// <para>
    /// ⚠️ Các hàm policy của TimescaleDB không chạy được trong transaction ⇒
    /// <c>suppressTransaction: true</c>. Theo be.md §14, migration này BẮT BUỘC phải test rollback
    /// trên TimescaleDB thật — CI unit test không có extension này.
    /// </para>
    /// </remarks>
    public partial class AddSensorReadingsCompressionAndRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- 0) Chặn trước trên bản TimescaleDB quá cũ ---
            // Dừng ở đây thì DB không đổi gì cả; để lọt xuống dưới thì compression bật lên và
            // mọi lần gửi bù dữ liệu cũ sẽ thất bại âm thầm.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    v_version text;
BEGIN
    SELECT extversion INTO v_version FROM pg_extension WHERE extname = 'timescaledb';
    IF v_version IS NULL THEN
        RAISE EXCEPTION 'IOT3-79: khong tim thay extension timescaledb.';
    END IF;
    IF string_to_array(v_version, '.')::int[] < ARRAY[2, 11] THEN
        RAISE EXCEPTION 'IOT3-79: can TimescaleDB >= 2.11 (dang co %). Ban cu hon KHONG cho INSERT vao chunk da nen, thiet bi gui bu du lieu cu se bi tu choi.', v_version;
    END IF;
END $$;", suppressTransaction: true);

            // --- 1) Bật nén ---
            // segmentby = battery_asset_id: mọi truy vấn đều lọc theo pin, nên gom theo cột này cho
            //   phép TimescaleDB bỏ qua nguyên đoạn không liên quan mà không phải giải nén.
            // orderby = time DESC: dữ liệu đọc ra gần như luôn theo thứ tự mới-trước; sắp đúng chiều
            //   trong đoạn nén giúp nén tốt hơn và quét ít hơn.
            migrationBuilder.Sql(@"
ALTER TABLE sensor_readings SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'battery_asset_id',
    timescaledb.compress_orderby   = 'time DESC'
);", suppressTransaction: true);

            // --- 2) Policy nén sau 7 ngày ---
            migrationBuilder.Sql(
                "SELECT add_compression_policy('sensor_readings', INTERVAL '7 days', if_not_exists => TRUE);",
                suppressTransaction: true);

            // --- 3) Policy xoá sau 180 ngày ---
            migrationBuilder.Sql(
                "SELECT add_retention_policy('sensor_readings', INTERVAL '180 days', if_not_exists => TRUE);",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Thứ tự NGƯỢC LẠI Up() và KHÔNG được đảo:
            //   gỡ policy trước → giải nén → mới tắt được cờ compress.
            // Tắt cờ khi còn chunk đã nén sẽ ra lỗi "cannot disable compression on hypertable with
            // compressed chunks", và migration dừng giữa chừng ở trạng thái nửa vời.

            migrationBuilder.Sql(
                "SELECT remove_retention_policy('sensor_readings', if_exists => TRUE);",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "SELECT remove_compression_policy('sensor_readings', if_exists => TRUE);",
                suppressTransaction: true);

            // Giải nén MỌI chunk đã nén. Không có bước này thì lệnh SET dưới cùng chắc chắn lỗi.
            // ⚠️ Bước này TỐN THỜI GIAN và TỐN CHỖ (dữ liệu nở lại 10–20 lần) — rollback trên
            // production phải kiểm dung lượng đĩa trước.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    v_chunk text;
BEGIN
    FOR v_chunk IN
        SELECT format('%I.%I', chunk_schema, chunk_name)
        FROM timescaledb_information.chunks
        WHERE hypertable_name = 'sensor_readings' AND is_compressed
    LOOP
        EXECUTE format('SELECT decompress_chunk(%L);', v_chunk);
    END LOOP;
END $$;", suppressTransaction: true);

            migrationBuilder.Sql(
                "ALTER TABLE sensor_readings SET (timescaledb.compress = false);",
                suppressTransaction: true);
        }
    }
}
