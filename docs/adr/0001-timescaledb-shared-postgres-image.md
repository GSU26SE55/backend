# ADR 0001: TimescaleDB Shared PostgreSQL Image

Status: Accepted for Sprint 1 validation.

Decision: Dev PostgreSQL image is `timescale/timescaledb:latest-pg16`.

TimescaleDB remains PostgreSQL plus extensions. Auth, Ticket, Notification, FileStorage metadata, and normal OLTP tables stay regular PostgreSQL tables. Only time-series tables such as `sensor_readings`, `iot_device_heartbeats`, and `analytics_events` should become hypertables.
