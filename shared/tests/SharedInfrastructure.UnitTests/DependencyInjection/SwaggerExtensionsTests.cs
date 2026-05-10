using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using SharedInfrastructure.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SharedInfrastructure.UnitTests.DependencyInjection;

public class SwaggerExtensionsTests
{
    private static void RegisterWebHostEnv(IServiceCollection services)
    {
        // Swashbuckle's ConfigureSwaggerGeneratorOptions cần IWebHostEnvironment — stub bằng impl ngắn.
        services.AddSingleton<IWebHostEnvironment>(new StubEnv());
    }

    private class StubEnv : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string WebRootPath { get; set; } = "/tmp";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "/tmp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void AddSharedSwaggerGen_RegistersSwaggerDoc_WithGivenTitleAndDefaultVersion()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        RegisterWebHostEnv(services);

        services.AddSharedSwaggerGen("My API");

        var sp = services.BuildServiceProvider();
        var swaggerOptions = sp.GetRequiredService<IOptions<SwaggerGeneratorOptions>>().Value;

        swaggerOptions.SwaggerDocs.Should().ContainKey("v1");
        swaggerOptions.SwaggerDocs["v1"].Title.Should().Be("My API");
        swaggerOptions.SwaggerDocs["v1"].Version.Should().Be("v1");
    }

    [Fact]
    public void AddSharedSwaggerGen_CustomVersion_UsesIt()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        RegisterWebHostEnv(services);

        services.AddSharedSwaggerGen("My API", apiVersion: "v2");

        var sp = services.BuildServiceProvider();
        var swaggerOptions = sp.GetRequiredService<IOptions<SwaggerGeneratorOptions>>().Value;

        swaggerOptions.SwaggerDocs.Should().ContainKey("v2");
    }

    [Fact]
    public void AddSharedSwaggerGen_DefinesBearerSecurityScheme()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        RegisterWebHostEnv(services);

        services.AddSharedSwaggerGen("API");

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<SwaggerGeneratorOptions>>().Value;

        options.SecuritySchemes.Should().ContainKey("Bearer");
        var scheme = options.SecuritySchemes["Bearer"];
        scheme.Type.Should().Be(SecuritySchemeType.Http);
        scheme.Scheme.Should().Be("Bearer");
        scheme.BearerFormat.Should().Be("JWT");
        scheme.In.Should().Be(ParameterLocation.Header);
        scheme.Name.Should().Be("Authorization");
    }

    [Fact]
    public void AddSharedSwaggerGen_RegistersBearerSecurityRequirement()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        RegisterWebHostEnv(services);

        services.AddSharedSwaggerGen("API");

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<SwaggerGeneratorOptions>>().Value;

        options.SecurityRequirements.Should().HaveCount(1);
        var requirement = options.SecurityRequirements[0];
        requirement.Keys.Should().Contain(s =>
            s.Reference != null && s.Reference.Id == "Bearer" && s.Reference.Type == ReferenceType.SecurityScheme);
    }

    [Fact]
    public void AddSharedSwaggerGen_CustomSchemaIds_UsesFullName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        RegisterWebHostEnv(services);

        services.AddSharedSwaggerGen("API");

        var sp = services.BuildServiceProvider();
        var schemaOptions = sp.GetRequiredService<IOptions<SchemaGeneratorOptions>>().Value;

        // CustomSchemaIds delegate ánh xạ Type → schemaId.
        var idForString = schemaOptions.SchemaIdSelector(typeof(string));
        idForString.Should().Be(typeof(string).FullName);
    }
}
