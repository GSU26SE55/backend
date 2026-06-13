using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketService.Application.DTOs.Response.Saga;
using TicketService.Infrastructure.Persistence;
using TicketService.Infrastructure.Sagas;
using TicketService.IntergrationTests.Fixtures;

namespace TicketService.IntergrationTests.Sagas;

/// <summary>
/// Sprint 5B #239 — E2E tests cho admin saga endpoints.
/// </summary>
public class AdminAlertTicketSagasApiTests : IClassFixture<TicketApiFactory>
{
    private readonly TicketApiFactory _factory;
    private readonly HttpClient _client;
    private readonly System.Text.Json.JsonSerializerOptions _jsonOptions;

    public AdminAlertTicketSagasApiTests(TicketApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }

    private async Task<Guid> SeedSagaAsync(string currentState = "TicketRequested", string? failureReason = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var alertId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var batteryAssetId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var failedAtStageVal = failureReason is null ? null : currentState;
        DateTime? failedAtVal = failureReason is null ? null : now;

        string sql;
        object[] parameters;

        if (db.Database.IsNpgsql())
        {
            sql = @"
INSERT INTO alert_ticket_saga_states
  (correlation_id, current_state, version, xmin,
   alert_id, battery_asset_id, customer_id, asset_serial_number,
   anomaly_type, severity, threshold_value, actual_value, unit, detected_at,
   ticket_is_reused, failure_reason, failed_at_stage, failed_at, retry_count,
   started_at)
VALUES
  ({0}, {1}, 1, 1,
   {0}, {2}, {3}, {4},
   1, 3, 60.0, 75.0, 'C', {5},
   0, {6}, {7}, {8}, 0,
   {5})";
            parameters = new object[]
            {
                alertId, currentState,
                batteryAssetId, customerId,
                "BMS-E2E", now,
                failureReason!, failedAtStageVal!, failedAtVal!
            };
        }
        else
        {
            sql = @"
INSERT INTO alert_ticket_saga_states
  (correlation_id, current_state, version,
   alert_id, battery_asset_id, customer_id, asset_serial_number,
   anomaly_type, severity, threshold_value, actual_value, unit, detected_at,
   ticket_is_reused, failure_reason, failed_at_stage, failed_at, retry_count,
   started_at)
VALUES
  ({0}, {1}, 1,
   {0}, {2}, {3}, {4},
   1, 3, 60.0, 75.0, 'C', {5},
   0, {6}, {7}, {8}, 0,
   {5})";
            parameters = new object[]
            {
                alertId, currentState,
                batteryAssetId, customerId,
                "BMS-E2E", now,
                failureReason, failedAtStageVal, failedAtVal
            };
        }


        await db.Database.ExecuteSqlRawAsync(sql, parameters);

        return alertId;
    }

    [Fact]
    public async Task GetList_NoFilter_ShouldReturn200WithPaginationStructure()
    {
        await SeedSagaAsync("Completed");
        await SeedSagaAsync("Failed", "TimedOut");

        var response = await _client.GetAsync("/api/v1/admin/sagas/alert-ticket");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<AlertTicketSagaListResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.TotalItems.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetList_FilterByFailed_ShouldReturnOnlyFailedSagas()
    {
        await SeedSagaAsync("Completed");
        await SeedSagaAsync("Failed", "ASSET_NOT_FOUND");
        await SeedSagaAsync("Failed", "TIMEOUT");

        var response = await _client.GetAsync("/api/v1/admin/sagas/alert-ticket?isFailed=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<AlertTicketSagaListResponse>(_jsonOptions);
        result!.Data!.Items.Should().AllSatisfy(i => i.CurrentState.Should().Be("Failed"));
        result.Data.TotalItems.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetById_Existing_ShouldReturn200WithDto()
    {
        var alertId = await SeedSagaAsync("Failed", "ASSET_NOT_FOUND");

        var response = await _client.GetAsync($"/api/v1/admin/sagas/alert-ticket/{alertId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<AlertTicketSagaDetailResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.AlertId.Should().Be(alertId.ToString());
        result.Data.CurrentState.Should().Be("Failed");
        result.Data.FailureReason.Should().Be("ASSET_NOT_FOUND");
    }

    [Fact]
    public async Task GetById_NotFound_ShouldReturn404Body()
    {
        var randomId = Guid.NewGuid();

        var response = await _client.GetAsync($"/api/v1/admin/sagas/alert-ticket/{randomId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var result = await response.Content.ReadFromJsonAsync<AlertTicketSagaDetailResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Reprocess_MissingIdempotencyKey_ShouldReturn400()
    {
        var alertId = await SeedSagaAsync("Failed", "TIMEOUT");

        var response = await _client.PostAsync(
            $"/api/v1/admin/sagas/alert-ticket/{alertId}/reprocess", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reprocess_WithIdempotencyKey_FailedSaga_ShouldReturn202_AndResetState()
    {
        var alertId = await SeedSagaAsync("Failed", "TIMEOUT");

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/admin/sagas/alert-ticket/{alertId}/reprocess");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var result = await response.Content.ReadFromJsonAsync<SagaReprocessResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();

        // Verify state reset to Initial.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var saga = await db.AlertTicketSagaStates.FindAsync(alertId);
        saga!.CurrentState.Should().Be("Initial");
        saga.FailureReason.Should().BeNull();
        saga.FailedAt.Should().BeNull();
        saga.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task Reprocess_NotInFailedState_ShouldReturn409()
    {
        var alertId = await SeedSagaAsync("TicketRequested");

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/admin/sagas/alert-ticket/{alertId}/reprocess");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Reprocess_DuplicateIdempotencyKey_ShouldReturn200Idempotent()
    {
        var alertId = await SeedSagaAsync("Failed", "TIMEOUT");
        var key = Guid.NewGuid().ToString();

        // First call.
        var firstReq = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/admin/sagas/alert-ticket/{alertId}/reprocess");
        firstReq.Headers.Add("Idempotency-Key", key);
        var first = await _client.SendAsync(firstReq);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Replay same key.
        var secondReq = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/admin/sagas/alert-ticket/{alertId}/reprocess");
        secondReq.Headers.Add("Idempotency-Key", key);
        var second = await _client.SendAsync(secondReq);

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await second.Content.ReadFromJsonAsync<SagaReprocessResponse>(_jsonOptions);
        result!.Message.Should().Contain("Idempotent");
    }
}
