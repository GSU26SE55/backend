namespace SharedInfrastructure.Extensions;

public static class DataShaper
{
    public static object ShapeData<TSource>(TSource entity, string? fields)
    {
        if (string.IsNullOrWhiteSpace(fields))
        {
            return entity!;
        }

        var result = new System.Dynamic.ExpandoObject() as IDictionary<string, object>;
        var fieldList = fields.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var field in fieldList)
        {
            var propertyName = field.Trim();
            var prop = typeof(TSource).GetProperty(propertyName,
                System.Reflection.BindingFlags.IgnoreCase |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance);

            if (prop != null)
            {
                result[prop.Name] = prop.GetValue(entity)!;
            }
        }

        return result.Count == 0 ? entity! : result;
    }
}
