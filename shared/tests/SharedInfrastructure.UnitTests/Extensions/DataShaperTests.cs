using SharedInfrastructure.Extensions;

namespace SharedInfrastructure.UnitTests.Extensions;

public class DataShaperTests
{
    private class Person
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public string? Email { get; set; }
    }

    [Fact]
    public void ShapeData_NullFields_ReturnsOriginalEntity()
    {
        var p = new Person { Id = Guid.NewGuid(), Name = "Alice", Age = 30 };

        var result = DataShaper.ShapeData(p, null);

        result.Should().BeSameAs(p);
    }

    [Fact]
    public void ShapeData_EmptyFields_ReturnsOriginalEntity()
    {
        var p = new Person { Id = Guid.NewGuid(), Name = "Alice" };

        var result = DataShaper.ShapeData(p, "  ");

        result.Should().BeSameAs(p);
    }

    [Fact]
    public void ShapeData_AllInvalidFields_ReturnsOriginalEntity()
    {
        var p = new Person { Id = Guid.NewGuid(), Name = "Alice" };

        var result = DataShaper.ShapeData(p, "DoesNotExist,AlsoMissing");

        result.Should().BeSameAs(p);
    }

    [Fact]
    public void ShapeData_ValidFields_ReturnsExpandoWithOnlyThoseFields()
    {
        var p = new Person { Id = Guid.NewGuid(), Name = "Alice", Age = 30, Email = "a@e.com" };

        var result = DataShaper.ShapeData(p, "Name,Age");

        var dict = result as IDictionary<string, object>;
        dict.Should().NotBeNull();
        var shaped = dict!;
        shaped.Should().HaveCount(2);
        shaped["Name"].Should().Be("Alice");
        shaped["Age"].Should().Be(30);
        shaped.Should().NotContainKey("Id");
        shaped.Should().NotContainKey("Email");
    }

    [Fact]
    public void ShapeData_FieldsCaseInsensitive_MatchesAndPreservesCanonicalCase()
    {
        var p = new Person { Name = "Bob", Age = 25 };

        var result = DataShaper.ShapeData(p, "name,AGE");
        var dict = result as IDictionary<string, object>;

        var shaped = dict!;
        shaped.Should().ContainKey("Name");
        shaped.Should().ContainKey("Age");
        shaped["Name"].Should().Be("Bob");
    }

    [Fact]
    public void ShapeData_TrimsWhitespaceAroundFieldNames()
    {
        var p = new Person { Name = "Carol", Email = "c@e.com" };

        var result = DataShaper.ShapeData(p, "  Name  ,  Email  ");
        var dict = result as IDictionary<string, object>;

        var shaped = dict!;
        shaped.Should().HaveCount(2);
        shaped.Should().ContainKey("Name");
        shaped.Should().ContainKey("Email");
    }

    [Fact]
    public void ShapeData_MixOfValidAndInvalid_KeepsOnlyValid()
    {
        var p = new Person { Name = "Dan", Age = 40 };

        var result = DataShaper.ShapeData(p, "Name,Foo,Age,Bar");
        var dict = result as IDictionary<string, object>;

        var shaped = dict!;
        shaped.Should().HaveCount(2);
        shaped["Name"].Should().Be("Dan");
        shaped["Age"].Should().Be(40);
    }
}
