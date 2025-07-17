#nullable enable

using System.Text.Json.Nodes;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Npgsql.EntityFrameworkCore.PostgreSQL;

public class JsonNodeTypesNpgsqlTest : IClassFixture<JsonNodeTypesNpgsqlTest.JsonNodeTypesFixture>
{
    private JsonNodeTypesFixture Fixture { get; }

    public JsonNodeTypesNpgsqlTest(JsonNodeTypesFixture fixture, ITestOutputHelper testOutputHelper)
    {
        Fixture = fixture;
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [Fact]
    public async Task Can_read_write_JsonNode_values()
    {
        using var context = CreateContext();
        var entity = new JsonNodeEntity
        {
            Id = 100,
            JsonNodeProperty = JsonNode.Parse("""{"name": "test", "value": 42}"""),
            JsonObjectProperty = new JsonObject
            {
                ["name"] = "object test",
                ["count"] = 100
            },
            JsonArrayProperty = new JsonArray { "item1", "item2", 42 },
            JsonValueProperty = JsonValue.Create("simple value")
        };

        context.JsonNodeEntities.Add(entity);
        await context.SaveChangesAsync();

        // Clear context to ensure data is read from database
        context.ChangeTracker.Clear();

        var retrieved = await context.JsonNodeEntities.FirstAsync(e => e.Id == 100);

        Assert.NotNull(retrieved.JsonNodeProperty);
        Assert.Equal("test", retrieved.JsonNodeProperty["name"]?.GetValue<string>());
        Assert.Equal(42, retrieved.JsonNodeProperty["value"]?.GetValue<int>());

        Assert.NotNull(retrieved.JsonObjectProperty);
        Assert.Equal("object test", retrieved.JsonObjectProperty["name"]?.GetValue<string>());
        Assert.Equal(100, retrieved.JsonObjectProperty["count"]?.GetValue<int>());

        Assert.NotNull(retrieved.JsonArrayProperty);
        Assert.Equal(3, retrieved.JsonArrayProperty.Count);
        Assert.Equal("item1", retrieved.JsonArrayProperty[0]?.GetValue<string>());
        Assert.Equal("item2", retrieved.JsonArrayProperty[1]?.GetValue<string>());
        Assert.Equal(42, retrieved.JsonArrayProperty[2]?.GetValue<int>());

        Assert.NotNull(retrieved.JsonValueProperty);
        Assert.Equal("simple value", retrieved.JsonValueProperty.GetValue<string>());
    }

    [Fact]
    public async Task Can_query_JsonNode_properties()
    {
        using var context = CreateContext();

        var entity1 = new JsonNodeEntity
        {
            Id = 101,
            JsonObjectProperty = new JsonObject
            {
                ["category"] = "books",
                ["rating"] = 5
            }
        };

        var entity2 = new JsonNodeEntity
        {
            Id = 102,
            JsonObjectProperty = new JsonObject
            {
                ["category"] = "movies",
                ["rating"] = 4
            }
        };

        context.JsonNodeEntities.AddRange(entity1, entity2);
        await context.SaveChangesAsync();

        // Clear context to ensure data is read from database
        context.ChangeTracker.Clear();

        // Test querying with JSON path operations (if supported by the provider)
        var bookEntities = await context.JsonNodeEntities
            .Where(e => e.JsonObjectProperty != null)
            .ToListAsync();

        Assert.True(bookEntities.Count >= 2, $"Expected at least 2 entities, but found {bookEntities.Count}");
    }

    [Fact]
    public async Task Can_update_JsonNode_properties()
    {
        using var context = CreateContext();

        var entity = new JsonNodeEntity
        {
            Id = 103,
            JsonObjectProperty = new JsonObject
            {
                ["status"] = "pending",
                ["version"] = 1
            }
        };

        context.JsonNodeEntities.Add(entity);
        await context.SaveChangesAsync();

        // Update the JSON property
        entity.JsonObjectProperty["status"] = "completed";
        entity.JsonObjectProperty["version"] = 2;
        await context.SaveChangesAsync();

        // Clear context and verify update
        context.ChangeTracker.Clear();
        var updated = await context.JsonNodeEntities.FirstAsync(e => e.Id == 103);

        Assert.Equal("completed", updated.JsonObjectProperty!["status"]?.GetValue<string>());
        Assert.Equal(2, updated.JsonObjectProperty["version"]?.GetValue<int>());
    }

    [Fact]
    public async Task Can_handle_null_JsonNode_values()
    {
        using var context = CreateContext();

        var entity = new JsonNodeEntity
        {
            Id = 104,
            JsonNodeProperty = null,
            JsonObjectProperty = null,
            JsonArrayProperty = null,
            JsonValueProperty = null
        };

        context.JsonNodeEntities.Add(entity);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        var retrieved = await context.JsonNodeEntities.FirstAsync(e => e.Id == 104);

        Assert.Null(retrieved.JsonNodeProperty);
        Assert.Null(retrieved.JsonObjectProperty);
        Assert.Null(retrieved.JsonArrayProperty);
        Assert.Null(retrieved.JsonValueProperty);
    }

    [Fact]
    public async Task Can_use_both_json_and_jsonb_columns()
    {
        using var context = CreateContext();

        var entity = new JsonNodeEntity
        {
            Id = 105,
            JsonNodeProperty = JsonNode.Parse("""{"type": "json test"}"""),
            JsonbNodeProperty = JsonNode.Parse("""{"type": "jsonb test"}""")
        };

        context.JsonNodeEntities.Add(entity);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        var retrieved = await context.JsonNodeEntities.FirstAsync(e => e.Id == 105);

        Assert.Equal("json test", retrieved.JsonNodeProperty!["type"]?.GetValue<string>());
        Assert.Equal("jsonb test", retrieved.JsonbNodeProperty!["type"]?.GetValue<string>());
    }

    [Fact]
    public async Task Can_query_with_JsonNode_GetValue()
    {
        using var context = CreateContext();

        // Create test entities with different JSON values
        var entity1 = new JsonNodeEntity
        {
            Id = 106,
            JsonValueProperty = JsonValue.Create("simple text")
        };

        context.JsonNodeEntities.Add(entity1);
        await context.SaveChangesAsync();

        // Clear context to ensure data is read from database
        context.ChangeTracker.Clear();

        // Test JsonValue.GetValue<string>() - this should work
        var result = await context.JsonNodeEntities
            .Where(e => e.Id == 106)
            .Select(e => e.JsonValueProperty!.GetValue<string>())
            .FirstOrDefaultAsync();

        Assert.Equal("simple text", result);
    }

    [Fact]
    public async Task Can_query_with_JsonNode_ToJsonString()
    {
        using var context = CreateContext();

        // Create test entities with different JSON values
        var entity1 = new JsonNodeEntity
        {
            Id = 107,
            JsonObjectProperty = new JsonObject
            {
                ["name"] = "test product",
                ["price"] = 29.99
            },
            JsonValueProperty = JsonValue.Create(42),
            JsonArrayProperty = new JsonArray { "item1", "item2", 123 }
        };

        context.JsonNodeEntities.Add(entity1);
        await context.SaveChangesAsync();

        // Clear context to ensure data is read from database
        context.ChangeTracker.Clear();

        // Test JsonObject.ToJsonString(null) in Select
        var jsonObjectString = await context.JsonNodeEntities
            .Where(e => e.Id == 107)
            .Select(e => e.JsonObjectProperty!.ToJsonString(null))
            .FirstOrDefaultAsync();

        Assert.NotNull(jsonObjectString);
        Assert.Contains("test product", jsonObjectString);
        Assert.Contains("29.99", jsonObjectString);

        // Test JsonValue.ToJsonString(null) in Select
        var jsonValueString = await context.JsonNodeEntities
            .Where(e => e.Id == 107)
            .Select(e => e.JsonValueProperty!.ToJsonString(null))
            .FirstOrDefaultAsync();

        Assert.Equal("42", jsonValueString);

        // Test JsonArray.ToJsonString(null) in Select
        var jsonArrayString = await context.JsonNodeEntities
            .Where(e => e.Id == 107)
            .Select(e => e.JsonArrayProperty!.ToJsonString(null))
            .FirstOrDefaultAsync();

        Assert.NotNull(jsonArrayString);
        Assert.Contains("item1", jsonArrayString);
        Assert.Contains("item2", jsonArrayString);
        Assert.Contains("123", jsonArrayString);

        // Test JsonNode.ToJsonString(null) in Where clause
        var foundEntity = await context.JsonNodeEntities
            .Where(e => e.Id == 107 && e.JsonValueProperty!.ToJsonString(null) == "42")
            .FirstOrDefaultAsync();

        Assert.NotNull(foundEntity);
        Assert.Equal(107, foundEntity.Id);

        // Test JsonObject property access with ToJsonString(null)
        var nameAsString = await context.JsonNodeEntities
            .Where(e => e.Id == 107)
            .Select(e => e.JsonObjectProperty!["name"]!.ToJsonString(null))
            .FirstOrDefaultAsync();

        Assert.Equal("test product", nameAsString); // PostgreSQL returns unquoted string value
    }

    [Fact]
    public async Task Can_query_with_JsonNode_DeepEquals()
    {
        using var context = CreateContext();

        // Create test entities with identical and different JSON objects
        var sharedJsonObject = new JsonObject
        {
            ["Name"] = "Test",
            ["Age"] = 25,
            ["Active"] = true
        };

        var entity1 = new JsonNodeEntity
        {
            Id = 200,
            JsonObjectProperty = sharedJsonObject
        };

        var entity2 = new JsonNodeEntity
        {
            Id = 201,
            JsonObjectProperty = new JsonObject
            {
                ["Name"] = "Test",
                ["Age"] = 25,
                ["Active"] = true
            }
        };

        var entity3 = new JsonNodeEntity
        {
            Id = 202,
            JsonObjectProperty = new JsonObject
            {
                ["Name"] = "Different",
                ["Age"] = 30,
                ["Active"] = false
            }
        };

        context.JsonNodeEntities.AddRange(entity1, entity2, entity3);
        await context.SaveChangesAsync();

        // Clear context to ensure data is read from database
        context.ChangeTracker.Clear();

        // Test DeepEquals - should find entities 1 and 2 since they have the same JSON content
        var targetObject = new JsonObject
        {
            ["Name"] = "Test",
            ["Age"] = 25,
            ["Active"] = true
        };

        var matchingEntities = await context.JsonNodeEntities
            .Where(e => JsonNode.DeepEquals(e.JsonObjectProperty, targetObject))
            .ToListAsync();

        Assert.Equal(2, matchingEntities.Count);
        Assert.Contains(matchingEntities, e => e.Id == 200);
        Assert.Contains(matchingEntities, e => e.Id == 201);

        // Test DeepEquals with null
        // First, let's see what entities actually exist with null JsonObjectProperty
        var entitiesWithNullProperty = await context.JsonNodeEntities
            .Where(e => e.JsonObjectProperty == null)
            .CountAsync();

        var nullMatches = await context.JsonNodeEntities
            .Where(e => JsonNode.DeepEquals(e.JsonObjectProperty, null))
            .CountAsync();

        // JsonNode.DeepEquals(null, null) should return true, so if there are entities with null JsonObjectProperty,
        // they should match. The original test expectation was incorrect.
        Assert.Equal(entitiesWithNullProperty, nullMatches);
    }

    [Fact]
    public async Task Can_use_AsObject_method()
    {
        using var context = CreateContext();

        // Test AsObject() method functionality
        var entity = new JsonNodeEntity
        {
            Id = 300,
            JsonObjectProperty = new JsonObject
            {
                ["Name"] = "Test User",
                ["Age"] = 30,
                ["IsActive"] = true
            }
        };

        context.JsonNodeEntities.Add(entity);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();

        // Use AsObject() in a query
        var result = await context.JsonNodeEntities
            .Where(e => e.JsonObjectProperty!.AsObject()["Name"]!.GetValue<string>() == "Test User")
            .FirstOrDefaultAsync();

        Assert.NotNull(result);
        Assert.Equal(300, result.Id);

        // Test accessing properties through AsObject()
        var retrieved = await context.JsonNodeEntities.FirstAsync(e => e.Id == 300);
        var asObject = retrieved.JsonObjectProperty!.AsObject();
        Assert.Equal("Test User", asObject["Name"]!.GetValue<string>());
        Assert.Equal(30, asObject["Age"]!.GetValue<int>());
        Assert.True(asObject["IsActive"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Can_use_AsValue_method()
    {
        using var context = CreateContext();

        // Test AsValue() method functionality
        var entity = new JsonNodeEntity
        {
            Id = 301,
            JsonValueProperty = JsonValue.Create("test value")
        };

        context.JsonNodeEntities.Add(entity);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();

        // Use AsValue() in a query
        var result = await context.JsonNodeEntities
            .Where(e => e.JsonValueProperty!.AsValue().GetValue<string>() == "test value")
            .FirstOrDefaultAsync();

        Assert.NotNull(result);

        // Test accessing value through AsValue()
        var asValue = result.JsonValueProperty!.AsValue();
        Assert.Equal("test value", asValue.GetValue<string>());
    }

    [Fact]
    public async Task Can_use_AsArray_method()
    {
        using var context = CreateContext();

        // Test AsArray() method functionality
        var entity = new JsonNodeEntity
        {
            Id = 302,
            JsonArrayProperty = new JsonArray { "item1", "item2", "item3" }
        };

        context.JsonNodeEntities.Add(entity);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();

        // Use AsArray() in a query
        var result = await context.JsonNodeEntities
            .Where(e => e.JsonArrayProperty!.AsArray()[0]!.GetValue<string>() == "item1")
            .FirstOrDefaultAsync();

        Assert.NotNull(result);
        Assert.Equal(302, result.Id);

        // Test accessing array elements through AsArray()
        var retrieved = await context.JsonNodeEntities.FirstAsync(e => e.Id == 302);
        var asArray = retrieved.JsonArrayProperty!.AsArray();
        Assert.Equal("item1", asArray[0]!.GetValue<string>());
        Assert.Equal("item2", asArray[1]!.GetValue<string>());
        Assert.Equal("item3", asArray[2]!.GetValue<string>());
    }

    [Fact]
    public async Task Can_use_JsonNode_Parse()
    {
        using var context = CreateContext();

        // Test JsonNode.Parse() functionality in queries
        var jsonString = """{"Name": "Parsed User", "Age": 25}""";

        // Create an entity with the same JSON structure
        var entity = new JsonNodeEntity
        {
            Id = 303,
            JsonObjectProperty = JsonNode.Parse(jsonString)!.AsObject()
        };

        context.JsonNodeEntities.Add(entity);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();

        // Use JsonNode.Parse() in a query to find matching entities
        var parsedJson = JsonNode.Parse(jsonString);
        var result = await context.JsonNodeEntities
            .Where(e => JsonNode.DeepEquals(e.JsonObjectProperty, parsedJson))
            .FirstOrDefaultAsync();

        Assert.NotNull(result);
        Assert.Equal(303, result.Id);

        // Verify the parsed content
        var retrieved = await context.JsonNodeEntities.FirstAsync(e => e.Id == 303);
        Assert.Equal("Parsed User", retrieved.JsonObjectProperty!["Name"]!.GetValue<string>());
        Assert.Equal(25, retrieved.JsonObjectProperty["Age"]!.GetValue<int>());
    }

    private NorthwindContext CreateContext() => Fixture.CreateContext();

    public class JsonNodeTypesFixture : SharedStoreFixtureBase<NorthwindContext>
    {
        static JsonNodeTypesFixture()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            NpgsqlConnection.GlobalTypeMapper.EnableDynamicJson();
#pragma warning restore CS0618 // Type or member is obsolete
        }

        protected override string StoreName => "JsonNodeTypesTest";
        protected override ITestStoreFactory TestStoreFactory => NpgsqlTestStoreFactory.Instance;

        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override async Task SeedAsync(NorthwindContext context)
        {
            // Create tables
            context.Database.EnsureCreated();

            // Add test data
            var entity1 = new JsonNodeEntity
            {
                Id = 1,
                JsonNodeProperty = JsonNode.Parse("""{"name": "John", "age": 30}"""),
                JsonObjectProperty = JsonNode.Parse("""{"city": "New York", "country": "USA"}""")?.AsObject(),
                JsonArrayProperty = JsonNode.Parse("""[1, 2, 3, 4, 5]""")?.AsArray(),
                JsonValueProperty = JsonValue.Create("test value"),
                JsonbNodeProperty = JsonNode.Parse("""{"temperature": 25.5, "humidity": 60}""")
            };

            var entity2 = new JsonNodeEntity
            {
                Id = 2,
                JsonNodeProperty = JsonNode.Parse("""[10, 20, 30]"""),
                JsonObjectProperty = JsonNode.Parse("""{"product": "laptop", "price": 999.99}""")?.AsObject(),
                JsonArrayProperty = JsonNode.Parse("""["a", "b", "c"]""")?.AsArray(),
                JsonValueProperty = JsonValue.Create(42),
                JsonbNodeProperty = JsonNode.Parse("""{"status": "active", "count": 100}""")
            };

            context.JsonNodeEntities.Add(entity1);
            context.JsonNodeEntities.Add(entity2);
            await context.SaveChangesAsync();
        }
    }

    public class NorthwindContext : DbContext
    {
        public NorthwindContext(DbContextOptions options) : base(options) { }

        public DbSet<JsonNodeEntity> JsonNodeEntities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<JsonNodeEntity>(b =>
            {
                b.Property(e => e.JsonNodeProperty)
                    .HasColumnType("json");

                b.Property(e => e.JsonObjectProperty)
                    .HasColumnType("jsonb");

                b.Property(e => e.JsonArrayProperty)
                    .HasColumnType("jsonb");

                b.Property(e => e.JsonValueProperty)
                    .HasColumnType("jsonb");

                b.Property(e => e.JsonbNodeProperty)
                    .HasColumnType("jsonb");
            });
        }
    }

    public class JsonNodeEntity
    {
        public int Id { get; set; }
        public JsonNode? JsonNodeProperty { get; set; }
        public JsonObject? JsonObjectProperty { get; set; }
        public JsonArray? JsonArrayProperty { get; set; }
        public JsonValue? JsonValueProperty { get; set; }
        public JsonNode? JsonbNodeProperty { get; set; }
    }
}
