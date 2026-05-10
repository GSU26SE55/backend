using EmailService.Infrastructure.Templates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EmailService.UnitTests.Templates;

/// <summary>
/// Test EmailTemplateRenderer: file IO, placeholder regex, caching toggle.
/// Dùng tempdir thật cho file I/O — không mock filesystem.
/// </summary>
public class EmailTemplateRendererTests : IDisposable
{
    private readonly string _tempRoot;

    public EmailTemplateRendererTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"email-templates-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private EmailTemplateRenderer NewRenderer(bool cacheDisabled = false)
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(Path.GetTempPath());

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailTemplates:Path"] = _tempRoot,
                ["EmailTemplates:CacheDisabled"] = cacheDisabled ? "true" : "false"
            })
            .Build();

        return new EmailTemplateRenderer(env.Object, config);
    }

    private void WriteTemplate(string name, string content)
        => File.WriteAllText(Path.Combine(_tempRoot, $"{name}.html"), content);

    [Fact]
    public async Task RenderAsync_ReplacesPlaceholders_WithValues()
    {
        WriteTemplate("Welcome", "<h1>Hi {{ UserName }}, OTP={{Otp}} expires {{ExpireMinutes}}m</h1>");
        var sut = NewRenderer();

        var result = await sut.RenderAsync("Welcome", new Dictionary<string, string?>
        {
            ["UserName"] = "Alice",
            ["Otp"] = "123456",
            ["ExpireMinutes"] = "5"
        });

        result.Should().Be("<h1>Hi Alice, OTP=123456 expires 5m</h1>");
    }

    [Fact]
    public async Task RenderAsync_NullValueInDict_ReplacesWithEmptyString()
    {
        WriteTemplate("X", "Hello {{Name}}!");
        var sut = NewRenderer();

        var result = await sut.RenderAsync("X", new Dictionary<string, string?>
        {
            ["Name"] = null
        });

        result.Should().Be("Hello !");
    }

    [Fact]
    public async Task RenderAsync_KeyNotInDict_LeavesPlaceholderIntact()
    {
        WriteTemplate("X", "Known={{Known}}, Missing={{Missing}}");
        var sut = NewRenderer();

        var result = await sut.RenderAsync("X", new Dictionary<string, string?>
        {
            ["Known"] = "yes"
        });

        result.Should().Be("Known=yes, Missing={{Missing}}");
    }

    [Fact]
    public async Task RenderAsync_PlaceholderWithWhitespace_StillMatches()
    {
        WriteTemplate("X", "{{   Key   }}");
        var sut = NewRenderer();

        var result = await sut.RenderAsync("X", new Dictionary<string, string?>
        {
            ["Key"] = "value"
        });

        result.Should().Be("value");
    }

    [Fact]
    public async Task RenderAsync_TemplateNotFound_ThrowsFileNotFound()
    {
        var sut = NewRenderer();

        var act = async () => await sut.RenderAsync("Missing", new Dictionary<string, string?>());

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*Missing*");
    }

    [Fact]
    public async Task RenderAsync_EmptyTemplateName_ThrowsArgumentException()
    {
        var sut = NewRenderer();

        var act = async () => await sut.RenderAsync(" ", new Dictionary<string, string?>());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RenderAsync_CacheEnabled_DoesNotRereadFileOnSecondCall()
    {
        WriteTemplate("Cached", "v1 {{Key}}");
        var sut = NewRenderer(cacheDisabled: false);

        var first = await sut.RenderAsync("Cached", new Dictionary<string, string?> { ["Key"] = "X" });
        first.Should().Be("v1 X");

        // Modify file on disk — cached version should still be served.
        WriteTemplate("Cached", "v2 {{Key}}");
        var second = await sut.RenderAsync("Cached", new Dictionary<string, string?> { ["Key"] = "X" });

        second.Should().Be("v1 X", "cache must serve original content");
    }

    [Fact]
    public async Task RenderAsync_CacheDisabled_RereadsFileEveryCall()
    {
        WriteTemplate("Live", "v1 {{Key}}");
        var sut = NewRenderer(cacheDisabled: true);

        var first = await sut.RenderAsync("Live", new Dictionary<string, string?> { ["Key"] = "X" });
        first.Should().Be("v1 X");

        WriteTemplate("Live", "v2 {{Key}}");
        var second = await sut.RenderAsync("Live", new Dictionary<string, string?> { ["Key"] = "X" });

        second.Should().Be("v2 X", "cache disabled → file re-read");
    }
}
