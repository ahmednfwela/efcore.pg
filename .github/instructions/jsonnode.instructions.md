---
applyTo: '**'
---
# JsonNode Support Instructions for GitHub Copilot

## Overview
This document provides instructions for working with System.Text.Json.Nodes JsonNode support in the EF Core PostgreSQL provider (Npgsql.EntityFrameworkCore.PostgreSQL).

## Current Implementation Status

### Completed Features
The following JsonNode methods are fully implemented and tested:

1. **JsonNode.AsObject()** - Type casting to JsonObject
2. **JsonNode.AsValue()** - Type casting to JsonValue
3. **JsonNode.AsArray()** - Type casting to JsonArray
4. **JsonNode.Parse(string)** - Parse JSON string to JsonNode
5. **JsonNode.DeepEquals(JsonNode, JsonNode)** - Deep equality comparison
6. **JsonNode.ToJsonString()** - Convert to JSON string
7. **JsonNode indexers** - Property and array access
8. **JsonNode.GetValue<T>()** - Extract typed values

### Core Implementation Files

#### 1. NpgsqlJsonDomTranslator.cs
**Location:** `src/EFCore.PG/Query/ExpressionTranslators/Internal/NpgsqlJsonDomTranslator.cs`

**Key Method References:**
```csharp
// JsonNode.AsObject(), AsValue(), AsArray() methods
private static readonly MethodInfo JsonNodeAsObject = typeof(JsonNode).GetMethod(nameof(JsonNode.AsObject), BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)!;
private static readonly MethodInfo JsonNodeAsValue = typeof(JsonNode).GetMethod(nameof(JsonNode.AsValue), BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)!;
private static readonly MethodInfo JsonNodeAsArray = typeof(JsonNode).GetMethod(nameof(JsonNode.AsArray), BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)!;

// JsonNode.Parse() static method
private static readonly MethodInfo JsonNodeParse = typeof(JsonNode).GetMethod(nameof(JsonNode.Parse), BindingFlags.Public | BindingFlags.Static, [typeof(string)])!;
```

**Translation Logic:**
- **As* Methods**: Pass-through operations (return same instance) since they're type casts
- **Parse Method**: Convert to JSONB using `_sqlExpressionFactory.Convert()`
- **Indexers**: Append to traversal path for property/array access
- **GetValue<T>()**: Convert traversal to text and cast to target type

#### 2. Test Files

**Query Translation Tests:** `test/EFCore.PG.FunctionalTests/Query/JsonNodeQueryTest.cs`
- Tests SQL generation for all JsonNode methods
- Validates correct PostgreSQL operators (`->>`, `#>>`, `CAST`)
- Contains 33+ passing tests

**Functional Tests:** `test/EFCore.PG.FunctionalTests/JsonNodeTypesNpgsqlTest.cs`
- End-to-end integration tests
- Database persistence and retrieval
- Real-world usage scenarios

### PostgreSQL SQL Patterns

#### Single Property Access
```csharp
// C# Code
e.CustomerJsonObject["Name"].GetValue<string>() == "Joe"

// Generated SQL
j."CustomerJsonObject" ->> 'Name' = 'Joe'
```

#### Nested Property Access
```csharp
// C# Code
e.CustomerJsonObject["Statistics"]["Visits"].GetValue<int>() == 4

// Generated SQL
CAST(j."CustomerJsonObject" #>> '{Statistics,Visits}' AS integer) = 4
```

#### Array Access
```csharp
// C# Code
e.CustomerJsonArray[0].GetValue<string>() == "first"

// Generated SQL
j."CustomerJsonArray" ->> 0 = 'first'
```

#### Type Casting (As* Methods)
```csharp
// C# Code
e.CustomerJsonObject.AsObject()["Name"].GetValue<string>() == "Joe"

// Generated SQL (same as without AsObject)
j."CustomerJsonObject" ->> 'Name' = 'Joe'
```

#### JsonNode.Parse Usage
```csharp
// C# Code
JsonNode.Parse(jsonString) used in comparisons

// Generated SQL
@__Parse_0='{"Name":"Joe"}' (DbType = Object)
WHERE j."CustomerJsonObject" = @__Parse_0
```

## Implementation Guidelines

### Adding New JsonNode Methods

1. **Add Method Reference**
   ```csharp
   private static readonly MethodInfo NewMethod = typeof(JsonNode).GetMethod(
       nameof(JsonNode.NewMethod),
       BindingFlags.Public | BindingFlags.Instance,
       [/* parameter types */])!;
   ```

2. **Add Translation Logic**
   - In `TranslateJsonNode()` method for instance methods
   - In main `Translate()` method for static methods
   - Return appropriate `SqlExpression` or `null` if not translatable

3. **Add Tests**
   - Query translation test in `JsonNodeQueryTest.cs`
   - Functional test in `JsonNodeTypesNpgsqlTest.cs`
   - Verify SQL generation with `AssertSql()`

### SQL Generation Rules

- **Single level access**: Use `->>` operator
- **Multi-level access**: Use `#>>` operator with path array `{a,b,c}`
- **Type conversion**: Wrap with `CAST(...AS type)` for non-string types
- **Parameter values**: Use appropriate `DbType` (usually `Object` for JSON)

### Testing Patterns

**Query Translation Test Structure:**
```csharp
[Fact]
public void JsonNode_MethodName_translation()
{
    using var ctx = CreateContext();

    var result = ctx.JsonbEntities
        .Where(e => /* JsonNode method usage */)
        .ToList();

    Assert.Single(result);
    Assert.Equal(expectedId, result[0].Id);

    AssertSql("""
        SELECT j."Id", j."CustomerJsonArray", j."CustomerJsonNode", j."CustomerJsonObject", j."CustomerJsonValue"
        FROM "JsonbEntities" AS j
        WHERE /* expected SQL condition */
        """);
}
```

**Functional Test Structure:**
```csharp
[Fact]
public async Task Can_use_MethodName()
{
    using var context = CreateContext();

    // Setup test data
    var entity = new JsonNodeEntity { /* test data */ };
    context.JsonNodeEntities.Add(entity);
    await context.SaveChangesAsync();

    // Test query with JsonNode method
    var result = await context.JsonNodeEntities
        .Where(e => /* method usage */)
        .FirstOrDefaultAsync();

    Assert.NotNull(result);
    // Additional assertions
}
```

## Common Issues and Solutions

### Method Reference Resolution
- Use exact parameter types in `GetMethod()` calls
- For overloaded methods, specify parameter array `[typeof(T1), typeof(T2)]`
- Static methods need `BindingFlags.Static`, instance methods need `BindingFlags.Instance`

### SQL Generation
- As* methods should return the same traversal expression (pass-through)
- Parse methods should use `Convert()` to appropriate JSON type mapping
- Always validate SQL output with tests

### Test Data Setup
- Use consistent test data across test files
- Entity IDs: 1, 2, 3 for standard test entities
- JsonArray data: `["first", "second", 123]`
- JsonValue data: Various string and numeric values

### Error Patterns to Avoid
- Don't use `...existing code...` comments in `oldString` parameters
- Don't repeat large code blocks when editing
- Don't assume method signatures without checking
- Don't use hardcoded entity IDs in functional tests if database state varies

## File Locations Quick Reference

- **Core Translator**: `src/EFCore.PG/Query/ExpressionTranslators/Internal/NpgsqlJsonDomTranslator.cs`
- **Query Tests**: `test/EFCore.PG.FunctionalTests/Query/JsonNodeQueryTest.cs`
- **Functional Tests**: `test/EFCore.PG.FunctionalTests/JsonNodeTypesNpgsqlTest.cs`
- **Entity Models**: Same test files contain entity definitions

## Development Workflow

1. **Analyze Request**: Understand what JsonNode functionality is needed
2. **Check Current State**: Review existing implementation and tests
3. **Implement Translation**: Add method references and translation logic
4. **Add Tests**: Create both query and functional tests
5. **Validate**: Run tests to ensure correct SQL generation
6. **Debug**: Fix any method resolution or SQL generation issues

## Testing Commands

```bash
# Test all JsonNode query translations
dotnet test test/EFCore.PG.FunctionalTests/EFCore.PG.FunctionalTests.csproj --filter "FullyQualifiedName~JsonNodeQueryTest"

# Test all JsonNode functional tests
dotnet test test/EFCore.PG.FunctionalTests/EFCore.PG.FunctionalTests.csproj --filter "FullyQualifiedName~JsonNodeTypes"

# Test specific method
dotnet test test/EFCore.PG.FunctionalTests/EFCore.PG.FunctionalTests.csproj --filter "FullyQualifiedName~AsObject_translation"
```

This document should be consulted for any JsonNode-related requests to ensure consistency with the existing implementation and maintain the high quality of the current codebase.
