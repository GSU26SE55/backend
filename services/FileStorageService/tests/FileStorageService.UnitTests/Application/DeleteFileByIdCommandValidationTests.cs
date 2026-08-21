using FileStorageService.Application.CQRS.Command;

namespace FileStorageService.UnitTests.Application;

/// <summary>
/// Luật validate của <see cref="DeleteFileByIdCommand"/> — hàng rào cuối trước một thao tác xoá
/// không hoàn tác được, nên đáng có test riêng dù chỉ có một luật.
/// </summary>
public class DeleteFileByIdCommandValidationTests
{
    [Fact]
    public async Task ValidId_Passes()
    {
        var r = await new DeleteFileByIdCommand { Id = Guid.NewGuid() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
        r.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyId_Fails()
    {
        var r = await new DeleteFileByIdCommand { Id = Guid.Empty }.ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "Id" && e.Detail.Contains("Invalid FileId"));
    }
}
