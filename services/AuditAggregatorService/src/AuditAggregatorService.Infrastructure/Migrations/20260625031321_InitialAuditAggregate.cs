using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditAggregatorService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuditAggregate : Migration
    {
        // ⚠️ Migration hand-authored (#AUDIT-14): EF không biểu diễn được PARTITION BY / pg_partman / GIN index.
        // KHÔNG auto-scaffold migration kế tiếp cho bảng này mà chưa reconcile (model snapshot mô tả schema logic;
        // bảng thật là partitioned + GIN index extra). Quản lý partition về sau qua pg_partman maintenance / SQL.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- Partitioned parent table (PARTITION BY RANGE occurred_at). PK bắt buộc chứa partition key.
CREATE TABLE audit_aggregate (
    id               uuid        NOT NULL DEFAULT gen_random_uuid(),
    occurred_at      timestamptz NOT NULL,
    event_id         uuid        NOT NULL,
    service_name     varchar(50) NOT NULL,
    action_code      varchar(100) NOT NULL,
    action_category  varchar(50) NOT NULL,
    severity         varchar(20) NOT NULL,
    target_type      varchar(50),
    target_id        uuid,
    target_display   varchar(255),
    actor_account_id uuid,
    actor_role       varchar(50),
    actor_display    varchar(255),
    actor_ip         varchar(64),
    actor_user_agent varchar(512),
    is_success       boolean     NOT NULL,
    error_code       varchar(50),
    reason           varchar(1024),
    metadata_json    jsonb,
    correlation_id   uuid,
    causation_id     uuid,
    recorded_at      timestamptz NOT NULL,
    ingested_at      timestamptz NOT NULL DEFAULT now(),
    geo_country      varchar(2),
    geo_city         varchar(100),
    CONSTRAINT ""PK_audit_aggregate"" PRIMARY KEY (id, occurred_at)
) PARTITION BY RANGE (occurred_at);

-- Indexes trên parent (propagate xuống mọi partition).
CREATE UNIQUE INDEX ux_agg_event_occurred ON audit_aggregate (event_id, occurred_at);
CREATE INDEX ix_agg_service     ON audit_aggregate (service_name, occurred_at);
CREATE INDEX ix_agg_actor       ON audit_aggregate (actor_account_id, occurred_at);
CREATE INDEX ix_agg_correlation ON audit_aggregate (correlation_id);
CREATE INDEX ix_agg_severity    ON audit_aggregate (severity, occurred_at);
CREATE INDEX ix_agg_metadata_gin ON audit_aggregate USING GIN (metadata_json);

-- Partition management: dùng pg_partman nếu có (auto-create partition tháng, premake 3);
-- fallback tạo partition tháng [now-3 .. now+3] + DEFAULT catch-all (chạy được trên postgres vanilla).
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'pg_partman') THEN
        CREATE SCHEMA IF NOT EXISTS partman;
        CREATE EXTENSION IF NOT EXISTS pg_partman SCHEMA partman;
        PERFORM partman.create_parent(
            p_parent_table := 'public.audit_aggregate',
            p_control      := 'occurred_at',
            p_interval     := '1 month',
            p_premake      := 3
        );
    ELSE
        FOR i IN -3..3 LOOP
            DECLARE
                start_d date := (date_trunc('month', now())::date + (i || ' month')::interval)::date;
                end_d   date := (date_trunc('month', now())::date + ((i + 1) || ' month')::interval)::date;
                pname   text := 'audit_aggregate_' || to_char((date_trunc('month', now())::date + (i || ' month')::interval), 'YYYYMM');
            BEGIN
                EXECUTE format(
                    'CREATE TABLE IF NOT EXISTS %I PARTITION OF audit_aggregate FOR VALUES FROM (%L) TO (%L);',
                    pname, start_d, end_d);
            END;
        END LOOP;
        EXECUTE 'CREATE TABLE IF NOT EXISTS audit_aggregate_default PARTITION OF audit_aggregate DEFAULT;';
    END IF;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_partman') THEN
        DELETE FROM partman.part_config WHERE parent_table = 'public.audit_aggregate';
    END IF;
END $$;
DROP TABLE IF EXISTS audit_aggregate CASCADE;
");
        }
    }
}
