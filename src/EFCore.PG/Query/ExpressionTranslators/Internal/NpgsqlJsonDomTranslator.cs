using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Expressions.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.Mapping;
using static Npgsql.EntityFrameworkCore.PostgreSQL.Utilities.Statics;

namespace Npgsql.EntityFrameworkCore.PostgreSQL.Query.ExpressionTranslators.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class NpgsqlJsonDomTranslator : IMemberTranslator, IMethodCallTranslator
{
    private static readonly MemberInfo RootElement = typeof(JsonDocument).GetProperty(nameof(JsonDocument.RootElement))!;

    private static readonly MethodInfo GetProperty = typeof(JsonElement).GetRuntimeMethod(
        nameof(JsonElement.GetProperty), [typeof(string)])!;

    private static readonly MethodInfo GetArrayLength = typeof(JsonElement).GetRuntimeMethod(
        nameof(JsonElement.GetArrayLength), Type.EmptyTypes)!;

    private static readonly MethodInfo ArrayIndexer = typeof(JsonElement).GetProperties()
        .Single(p => p.GetIndexParameters().Length == 1 && p.GetIndexParameters()[0].ParameterType == typeof(int))
        .GetMethod!;

    // JsonArray.Count property
    private static readonly MemberInfo JsonArrayCount = typeof(JsonArray).GetProperty(nameof(JsonArray.Count))!;

    // JsonNode indexers
    private static readonly MethodInfo JsonNodeStringIndexer = typeof(JsonNode).GetProperties()
        .Single(p => p.GetIndexParameters().Length == 1 && p.GetIndexParameters()[0].ParameterType == typeof(string))
        .GetMethod!;

    private static readonly MethodInfo JsonNodeIntIndexer = typeof(JsonNode).GetProperties()
        .Single(p => p.GetIndexParameters().Length == 1 && p.GetIndexParameters()[0].ParameterType == typeof(int))
        .GetMethod!;

    // JsonNode.ToJsonString() method
    private static readonly MethodInfo JsonNodeToJsonString = typeof(JsonNode).GetMethod(nameof(JsonNode.ToJsonString), BindingFlags.Public | BindingFlags.Instance, [typeof(JsonSerializerOptions)])!;

    // JsonNode.DeepEquals() static method
    private static readonly MethodInfo JsonNodeDeepEquals = typeof(JsonNode).GetMethod(nameof(JsonNode.DeepEquals), BindingFlags.Public | BindingFlags.Static, [typeof(JsonNode), typeof(JsonNode)])!;

    // JsonNode.AsObject() method
    private static readonly MethodInfo JsonNodeAsObject = typeof(JsonNode).GetMethod(nameof(JsonNode.AsObject), BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)!;

    // JsonNode.AsValue() method
    private static readonly MethodInfo JsonNodeAsValue = typeof(JsonNode).GetMethod(nameof(JsonNode.AsValue), BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)!;

    // JsonNode.AsArray() method
    private static readonly MethodInfo JsonNodeAsArray = typeof(JsonNode).GetMethod(nameof(JsonNode.AsArray), BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)!;

    // JsonNode.Parse() static method
    private static readonly MethodInfo JsonNodeParse = typeof(JsonNode).GetMethod(
        nameof(JsonNode.Parse),
        BindingFlags.Public | BindingFlags.Static,
        [typeof(string), typeof(JsonNodeOptions), typeof(JsonDocumentOptions)]
    )!;

    private static readonly string[] GetMethods =
    [
        nameof(JsonElement.GetBoolean),
        nameof(JsonElement.GetDateTime),
        nameof(JsonElement.GetDateTimeOffset),
        nameof(JsonElement.GetDecimal),
        nameof(JsonElement.GetDouble),
        nameof(JsonElement.GetGuid),
        nameof(JsonElement.GetInt16),
        nameof(JsonElement.GetInt32),
        nameof(JsonElement.GetInt64),
        nameof(JsonElement.GetSingle),
        nameof(JsonElement.GetString)
    ];

    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly NpgsqlSqlExpressionFactory _sqlExpressionFactory;
    private readonly RelationalTypeMapping _stringTypeMapping;
    private readonly IModel _model;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public NpgsqlJsonDomTranslator(
        IRelationalTypeMappingSource typeMappingSource,
        NpgsqlSqlExpressionFactory sqlExpressionFactory,
        IModel model)
    {
        _typeMappingSource = typeMappingSource;
        _sqlExpressionFactory = sqlExpressionFactory;
        _model = model;
        _stringTypeMapping = typeMappingSource.FindMapping(typeof(string), model)!;
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (member.DeclaringType == typeof(JsonDocument))
        {
            if (member == RootElement && instance is ColumnExpression { TypeMapping: NpgsqlJsonTypeMapping } column)
            {
                // Simply get rid of the RootElement member access
                return column;
            }
        }
        else if (member.DeclaringType == typeof(JsonArray))
        {
            if (member == JsonArrayCount && instance?.TypeMapping is NpgsqlJsonTypeMapping mapping)
            {
                // For JsonArray.Count, use the same function as JsonElement.GetArrayLength
                return _sqlExpressionFactory.Function(
                    mapping.IsJsonb ? "jsonb_array_length" : "json_array_length",
                    [instance],
                    nullable: true,
                    argumentsPropagateNullability: TrueArrays[1],
                    typeof(int));
            }
        }

        return null;
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        // Handle static JsonNode.DeepEquals method
        if (method == JsonNodeDeepEquals && arguments.Count == 2)
        {
            return TranslateJsonNodeDeepEquals(arguments[0], arguments[1]);
        }

        // Handle static JsonNode.Parse method
        if (method == JsonNodeParse && arguments.Count == 1)
        {
            return TranslateJsonNodeParse(arguments[0]);
        }

        return instance?.TypeMapping switch
        {
            NpgsqlJsonTypeMapping jsonMapping when method.DeclaringType?.IsAssignableTo(typeof(JsonNode)) == true
                => TranslateJsonNode(instance, method, arguments, jsonMapping),
            NpgsqlJsonTypeMapping jsonMapping when method.DeclaringType == typeof(JsonElement)
                => TranslateJsonElement(instance, method, arguments, jsonMapping),
            _ => null,
        };
    }

    private SqlExpression? TranslateJsonElement(SqlExpression instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, NpgsqlJsonTypeMapping mapping)
    {
        // The root of the JSON expression is a ColumnExpression. We wrap that with an empty traversal
        // expression (col #>> '{}'); subsequent traversals will gradually append the path into that.
        // Note that it's possible to call methods such as GetString() directly on the root, and the
        // empty traversal is necessary to properly convert it to a text.
        instance = instance is ColumnExpression columnExpression
            ? _sqlExpressionFactory.JsonTraversal(
                columnExpression, returnsText: false, typeof(string), mapping)
            : instance;

        if (method == GetProperty || method == ArrayIndexer)
        {
            return instance is PgJsonTraversalExpression prevPathTraversal
                ? prevPathTraversal.Append(_sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0]))
                : null;
        }

        if (GetMethods.Contains(method.Name) && arguments.Count == 0 && instance is PgJsonTraversalExpression traversal)
        {
            var traversalToText = new PgJsonTraversalExpression(
                traversal.Expression,
                traversal.Path,
                returnsText: true,
                typeof(string),
                _stringTypeMapping);

            // The PostgreSQL traversal operator always returns text - for these scalar-returning methods, apply a conversion from string.
            return method.Name == nameof(JsonElement.GetString)
                ? traversalToText
                : _sqlExpressionFactory.Convert(
                    traversalToText, method.ReturnType, _typeMappingSource.FindMapping(method.ReturnType, _model));
        }

        if (method == GetArrayLength)
        {
            return _sqlExpressionFactory.Function(
                mapping.IsJsonb ? "jsonb_array_length" : "json_array_length",
                [instance],
                nullable: true,
                argumentsPropagateNullability: TrueArrays[1],
                typeof(int));
        }

        if (method.Name.StartsWith("TryGet", StringComparison.Ordinal) && arguments.Count == 0)
        {
            throw new InvalidOperationException($"The TryGet* methods on {nameof(JsonElement)} aren't translated yet, use Get* instead.'");
        }

        return null;
    }

    private SqlExpression? TranslateJsonNode(SqlExpression instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, NpgsqlJsonTypeMapping mapping)
    {
        // Ensure we have a traversal expression
        instance = instance is ColumnExpression columnExpression
            ? _sqlExpressionFactory.JsonTraversal(
                columnExpression, returnsText: false, typeof(string), mapping)
            : instance;

        // Handle JsonNode indexers (both string and int indexers)
        if ((method == JsonNodeStringIndexer || method == JsonNodeIntIndexer) && arguments.Count == 1)
        {
            return instance is PgJsonTraversalExpression prevPathTraversal
                ? prevPathTraversal.Append(_sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0]))
                : null;
        }

        // Handle JsonNode.GetValue<T>() method
        if (arguments.Count == 0 && method.Name == nameof(JsonNode.GetValue) && method.IsGenericMethod &&
            method.DeclaringType?.IsAssignableTo(typeof(JsonNode)) == true)
        {
            if (instance is PgJsonTraversalExpression traversal)
            {
                // Get the target type from the generic method
                var targetType = method.GetGenericArguments()[0];

                var traversalToText = new PgJsonTraversalExpression(
                    traversal.Expression,
                    traversal.Path,
                    returnsText: true,
                    typeof(string),
                    _stringTypeMapping);

                // For string, return the text traversal directly
                if (targetType == typeof(string))
                {
                    return traversalToText;
                }

                // For other types, apply a conversion from string
                return _sqlExpressionFactory.Convert(
                    traversalToText, targetType, _typeMappingSource.FindMapping(targetType, _model));
            }
        }

        // Handle JsonNode.ToJsonString() method
        if (method.Name == nameof(JsonNode.ToJsonString) && method.DeclaringType?.IsAssignableTo(typeof(JsonNode)) == true)
        {
            // For ToJsonString(), we want to return the JSON value as text
            // If we already have a traversal expression, convert it to text
            if (instance is PgJsonTraversalExpression traversal)
            {
                return new PgJsonTraversalExpression(
                    traversal.Expression,
                    traversal.Path,
                    returnsText: true,
                    typeof(string),
                    _stringTypeMapping
                );
            }

            // If it's a direct column reference, create a text traversal
            return new PgJsonTraversalExpression(instance, [], returnsText: true, typeof(string), _stringTypeMapping);
        }

        // Handle JsonNode.AsObject(), JsonNode.AsValue(), JsonNode.AsArray() methods
        if (method == JsonNodeAsObject || method == JsonNodeAsValue || method == JsonNodeAsArray)
        {
            // These methods are type casts that don't change the underlying JSON structure
            // They just provide a different view of the same data, so we return the same traversal
            return instance;
        }

        return null;
    }

    private SqlExpression? TranslateJsonNodeParse(SqlExpression jsonStringExpression)
    {
        // JsonNode.Parse() converts a string to a JsonNode
        // In PostgreSQL, this is essentially a cast from text to JSON/JSONB
        // We use JSONB as the default since it's generally preferred
        if (_typeMappingSource.FindMapping(typeof(JsonNode), _model) is not NpgsqlJsonTypeMapping jsonbMapping)
        {
            return null;
        }

        // Cast the string expression to JSONB
        return _sqlExpressionFactory.Convert(jsonStringExpression, typeof(JsonNode), jsonbMapping);
    }

    private SqlExpression? TranslateJsonNodeDeepEquals(SqlExpression left, SqlExpression right)
    {
        // For JsonNode.DeepEquals, we need at least one argument to have a JSON type mapping
        // The other could be a parameter or constant that will be coerced to JSON
        var hasJsonMapping = left.TypeMapping is NpgsqlJsonTypeMapping || right.TypeMapping is NpgsqlJsonTypeMapping;

        if (!hasJsonMapping)
        {
            return null;
        }

        // For JSON equality comparison, we need to handle null values properly
        // JsonNode.DeepEquals(null, null) should return true, but PostgreSQL's NULL = NULL returns NULL
        // We implement null-safe equality: (left = right) OR (left IS NULL AND right IS NULL)
        var equalExpression = _sqlExpressionFactory.Equal(left, right);
        var leftIsNull = _sqlExpressionFactory.IsNull(left);
        var rightIsNull = _sqlExpressionFactory.IsNull(right);
        var bothNull = _sqlExpressionFactory.AndAlso(leftIsNull, rightIsNull);

        return _sqlExpressionFactory.OrElse(equalExpression, bothNull);
    }
}
