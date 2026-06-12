using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <summary>
    /// Sprint 5B #235 — MassTransit EF Consumer Outbox/Inbox tables cho TicketService.
    ///
    /// 3 bảng:
    /// - <c>inbox_state</c> — message deduplication (consumer Inbox pattern).
    /// - <c>outbox_state</c> — bus outbox per saga/consumer scope.
    /// - <c>outbox_message</c> — pending publish queue (durable post-commit publish).
    ///
    /// Tham chiếu: MassTransit.EntityFrameworkCore docs +
    /// official PostgreSQL DDL https://masstransit.io/documentation/configuration/middleware/outbox.
    /// </summary>
    public partial class AddDurableMessagingFoundation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE inbox_state
(
    id BIGSERIAL PRIMARY KEY,
    message_id UUID NOT NULL,
    consumer_id UUID NOT NULL,
    lock_id UUID NOT NULL,
    row_version BYTEA NULL,
    received TIMESTAMPTZ NOT NULL,
    receive_count INTEGER NOT NULL,
    expiration_time TIMESTAMPTZ NULL,
    consumed TIMESTAMPTZ NULL,
    delivered TIMESTAMPTZ NULL,
    last_sequence_number BIGINT NULL,
    CONSTRAINT ux_inbox_state_message_consumer UNIQUE (message_id, consumer_id)
);

CREATE INDEX ix_inbox_state_delivered ON inbox_state (delivered);
CREATE INDEX ix_inbox_state_expiration ON inbox_state (expiration_time);

CREATE TABLE outbox_state
(
    outbox_id UUID PRIMARY KEY,
    lock_id UUID NOT NULL,
    row_version BYTEA NULL,
    created TIMESTAMPTZ NOT NULL,
    delivered TIMESTAMPTZ NULL,
    last_sequence_number BIGINT NULL
);

CREATE INDEX ix_outbox_state_created ON outbox_state (created);

CREATE TABLE outbox_message
(
    sequence_number BIGSERIAL PRIMARY KEY,
    enqueue_time TIMESTAMPTZ NULL,
    sent_time TIMESTAMPTZ NOT NULL,
    headers TEXT NULL,
    properties TEXT NULL,
    inbox_message_id UUID NULL,
    inbox_consumer_id UUID NULL,
    outbox_id UUID NULL,
    message_id UUID NOT NULL,
    content_type VARCHAR(256) NOT NULL,
    message_type TEXT NOT NULL,
    body TEXT NOT NULL,
    conversation_id UUID NULL,
    correlation_id UUID NULL,
    initiator_id UUID NULL,
    request_id UUID NULL,
    source_address VARCHAR(256) NULL,
    destination_address VARCHAR(256) NULL,
    response_address VARCHAR(256) NULL,
    fault_address VARCHAR(256) NULL,
    expiration_time TIMESTAMPTZ NULL
);

CREATE INDEX ix_outbox_message_inbox ON outbox_message (inbox_message_id, inbox_consumer_id, sequence_number);
CREATE INDEX ix_outbox_message_outbox ON outbox_message (outbox_id, sequence_number);
CREATE INDEX ix_outbox_message_enqueue ON outbox_message (enqueue_time);
CREATE INDEX ix_outbox_message_expiration ON outbox_message (expiration_time);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS outbox_message CASCADE;
DROP TABLE IF EXISTS outbox_state CASCADE;
DROP TABLE IF EXISTS inbox_state CASCADE;
");
        }
    }
}
