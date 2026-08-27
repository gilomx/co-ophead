using Mono.Cecil;

if (args.Length == 0)
{
    Console.Error.WriteLine("Uso: AssemblyInspector <ensamblado> [filtro ...]");
    return 1;
}

var assemblyPath = Path.GetFullPath(args[0]);
var fullMembers = args.Skip(1).Any(argument => argument == "--full");
var showIl = args.Skip(1).Any(argument => argument == "--il");
var filters = args.Skip(1).Where(argument => argument is not "--full" and not "--il").ToArray();
if (filters.Length == 0)
    filters = new[] { "Player", "Input", "Rewired", "Motor" };

using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
var types = Flatten(assembly.MainModule.Types)
    .Where(type => MatchesType(type.FullName, filters))
    .OrderBy(type => type.FullName, StringComparer.OrdinalIgnoreCase)
    .ToArray();

Console.WriteLine($"# {assembly.Name.Name} {assembly.Name.Version}");
Console.WriteLine($"# Filtros: {string.Join(", ", filters)}");
Console.WriteLine($"# Tipos encontrados: {types.Length}");

foreach (var type in types)
{
    Console.WriteLine();
    Console.WriteLine("TYPE " + type.FullName);

    foreach (var field in type.Fields.Where(field => fullMembers || Matches(field.Name, filters)))
        Console.WriteLine($"  FIELD {field.FieldType.FullName} {field.Name}" +
            (field.HasConstant ? " = " + field.Constant : string.Empty));

    foreach (var property in type.Properties.Where(property => fullMembers || Matches(property.Name, filters)))
        Console.WriteLine($"  PROPERTY {property.PropertyType.FullName} {property.Name}");

    foreach (var method in type.Methods.Where(method => fullMembers || IsRelevantMethod(method, filters)))
    {
        var parameters = string.Join(", ", method.Parameters.Select(
            parameter => parameter.ParameterType.FullName + " " + parameter.Name));
        Console.WriteLine($"  METHOD {method.ReturnType.FullName} {method.Name}({parameters})");
        if (showIl && method.HasBody)
            foreach (var instruction in method.Body.Instructions)
                Console.WriteLine($"    IL {instruction}");
    }
}

return 0;

static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> roots)
{
    foreach (var type in roots)
    {
        yield return type;
        foreach (var nested in Flatten(type.NestedTypes))
            yield return nested;
    }
}

static bool IsRelevantMethod(MethodDefinition method, string[] filters)
{
    if (Matches(method.Name, filters))
        return true;

    return method.Name is "Awake" or "Start" or "Update" or "FixedUpdate" or "Init";
}

static bool Matches(string value, IEnumerable<string> filters) =>
    filters.Any(filter => value.Contains(filter.TrimStart('='), StringComparison.OrdinalIgnoreCase));

static bool MatchesType(string value, IEnumerable<string> filters) =>
    filters.Any(filter => filter.StartsWith("=", StringComparison.Ordinal)
        ? value.Equals(filter[1..], StringComparison.OrdinalIgnoreCase)
        : value.Contains(filter, StringComparison.OrdinalIgnoreCase));
