using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpImGui_Dev.CodeGenerator;

public class CSharpGenerator
{
    private static readonly string ProjectPath = Path.Combine("../../../");
    private static readonly string DearBindingsPath = Path.Combine(ProjectPath, "dcimgui");
    
    private string _outputPath = Path.Combine(ProjectPath, "../SharpImGui/Generated");

    private Context? _context;

    public void Generate()
    {
        if (Directory.Exists(_outputPath))
            Directory.Delete(_outputPath, true);
        
        _context = new Context();
        MethodGenerator.Context = _context;
        
        _outputPath = Path.Combine(ProjectPath, "../SharpImGui/Generated");
        _context.Namespace = "SharpImGui";
        _context.MainMethodsClass = "ImGui";
        _context.NativeClass = "ImGuiNative";
        GenerateBindings("dcimgui.json");
        
        _outputPath = Path.Combine(ProjectPath, "../SharpImGui/Generated/Internal");
        _context.Namespace = "SharpImGui.Internal";
        _context.MainMethodsClass = "ImGuiInternal";
        _context.NativeClass = "ImGuiInternalNative";
        _context.PointerStructs.Clear();
        _context.Delegates.Clear();
        GenerateBindings("dcimgui_internal.json");
    }

    private void GenerateBindings(string metadataFile)
    {
        var path = Path.Combine(DearBindingsPath, metadataFile);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Could not find metadata file: " + path);
        }

        using var reader = new StreamReader(path);
        var jsonFile = reader.ReadToEnd();
        var metadata = JsonSerializer.Deserialize<Definitions>(jsonFile, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        
        GenerateConstants(metadata.Defines);
        GenerateEnums(metadata.Enums);
        ProcessTypedefs(metadata.Typedefs);
        GenerateStructs(metadata);
        GenerateMethods(metadata.Functions);
        GenerateDelegates();
        GenerateMainOverloads(metadata.Functions);
    }

    private void ProcessTypedefs(IReadOnlyList<TypedefItem> typedefs)
    {
        foreach (var typedef in typedefs)
        {
            if (typedef.Type.Description.Kind == "Type")
            {
                var innerType = typedef.Type.Description.InnerType;
                var name = typedef.Type.Description.Name;

                if (innerType.Kind == "Pointer" && innerType.InnerType!.Kind == "Function")
                {
                    innerType = innerType.InnerType;
                    
                    var @delegate = GenerateDelegateFromDescription(innerType, name!);
                    var comment = _context.CleanupComments(typedef.Comments);
                    _context.Delegates.Add((@delegate, comment));
                    continue;
                }
            }

            if (typedef.Name.Contains("flags", StringComparison.OrdinalIgnoreCase))
                continue;
                
            var csharpType = _context.GetCSharpType(typedef.Type.Description);
            _context.FoundedTypes.TryAdd(typedef.Name, csharpType);
        }
    }

    private void GenerateConstants(IReadOnlyList<DefineItem> defines)
    {
        using var writer = new CSharpCodeWriter(_outputPath, "Constants.gen.cs");

        writer.Using("System");
        writer.WriteLine();
        writer.StartNamespace(_context.Namespace);
        writer.WriteLine("public static class Constants");
        writer.PushBlock();
        // dear_bindings writes defines in a strange manner, producing redefines, so when we group them by count, we can produce more accurate result
        var defineGroups = defines.GroupBy(x => x.Conditionals?.Count ?? 0);

        foreach (var key in _context.KnownDefines.Keys)
        {
            writer.WriteLine($"public const string {key} = \"{_context.KnownDefines[key].value.Replace("\"", "\\\"")}\";");
        }

        foreach (var group in defineGroups)
        {
            if (group.Key == 0)
            {
                foreach (var define in group)
                {
                    var constant = GenerateConstant(define);
                    writer.WriteCommentary(_context.CleanupComments(define.Comments));
                    writer.WriteLine($"public const {constant.Type} {constant.Name} = {constant.Value};");
                    _context.KnownDefines[define.Name] = (constant.Type, define.Content?? "");
                }
            }
            else if (group.Key == 1)
            {
                foreach (var define in group)
                {
                    if (!_context.EvalConditionals(define.Conditionals))
                        continue;
                    
                    var constant = GenerateConstant(define);
                    writer.WriteCommentary(_context.CleanupComments(define.Comments));
                    writer.WriteLine($"public const {constant.Type} {constant.Name} = {constant.Value};");
                    _context.KnownDefines[define.Name] = (constant.Type, define.Content?? "");
                }
            }
            else
            {
                foreach (var define in group)
                {
                    if (!_context.EvalConditionals(define.Conditionals))
                        continue;
                    
                    var constant = GenerateConstant(define);
                    writer.WriteCommentary(_context.CleanupComments(define.Comments));
                    writer.WriteLine($"public const {constant.Type} {constant.Name} = {constant.Value};");
                    _context.KnownDefines[define.Name] = (constant.Type, define.Content?? "");
                }
            }
        }
        
        writer.PopBlock();
        writer.EndNamespace();
        
        return;

        (string Name, string Type, string Value) GenerateConstant(DefineItem defineItem)
        {
            if (string.IsNullOrEmpty(defineItem.Content))
            {
                return (defineItem.Name, "bool", "true");
            }

            if (_context.KnownDefines.ContainsKey(defineItem.Content))
            {
                var contentConstant = _context.KnownDefines.Keys.FirstOrDefault(c => c == defineItem.Content, null);
                var type = _context.KnownDefines[contentConstant].type ?? "bool";
                return (defineItem.Name, type, defineItem.Content);
            }

            if (defineItem.Content.StartsWith("0x") &&
                long.TryParse(defineItem.Content.AsSpan(2), NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo, out _) ||
                long.TryParse(defineItem.Content, out _))
            {
                return (defineItem.Name, "long", defineItem.Content);
            }
            
            if (defineItem.Content.StartsWith('\"') && defineItem.Content.EndsWith('\"'))
            {
                return (defineItem.Name, "string", defineItem.Content);
            }
            
            return (defineItem.Name, "bool", "true");
        }
    }
    
    private void GenerateEnums(IReadOnlyList<EnumItem> enums)
    {
        using var writer = new CSharpCodeWriter(_outputPath, "Enums.gen.cs");

        writer.Using("System");
        writer.WriteLine();
        writer.StartNamespace(_context.Namespace);
        
        foreach (var @enum in enums)
        {
            if (!_context.EvalConditionals(@enum.Conditionals))
                continue;
            
            var enumName = @enum.Name;
            var enumNameCleaned = CleanupEnumName(enumName);

            var isExtenssion = false;
            var extendedElements = new List<(string name, string value)>();
            
            if (enumName.EndsWith("Private_"))
            {
                enumName = enumName.Replace("Private", "");
                enumNameCleaned = enumNameCleaned.Replace("Private", "");

                if (_context.Enums.TryGetValue(enumNameCleaned, out var elements))
                {
                    isExtenssion = true;
                    extendedElements = elements;
                }
            }
            _context.FoundedTypes.TryAdd(enumNameCleaned, enumNameCleaned);
            
            if (!isExtenssion)
                _context.Enums.Add(enumNameCleaned, []);
            
            writer.WriteCommentary(_context.CleanupComments(@enum.Comments));
            if (@enum.IsFlagsEnum)
                writer.WriteLine("[Flags]");

            writer.WriteLine($"public enum {enumNameCleaned}");
            writer.PushBlock();

            if (isExtenssion)
            {
                writer.WriteLine($"// {enumNameCleaned} values\n");
                foreach (var element in extendedElements)
                {
                    writer.WriteLine($"{element.name} = {element.value},");
                }
                writer.WriteLine();
                writer.WriteLine($"// Extended {enumNameCleaned} values\n");
            }
            
            foreach (var element in @enum.Elements)
            {
                if (!_context.EvalConditionals(element.Conditionals))
                    continue;
                
                
                if (element.Name.Contains("COUNT", StringComparison.Ordinal))
                {
                    _context.ArraySizes[element.Name] = (int)element.Value;
                }
                
                var value = element.Value.ToString(CultureInfo.InvariantCulture);
                if (element.ValueExpression != null)
                    value = CleanupEnumElement(element.ValueExpression, enumName);
                
                writer.WriteCommentary(_context.CleanupComments(element.Comments));
                var cleanedName = CleanupEnumElement(element.Name, enumName);
                writer.WriteLine($"{cleanedName} = {value},");
                
                _context.Enums[enumNameCleaned].Add((cleanedName, value));
            }
            
            writer.PopBlock();
            writer.WriteLine();
        }
        
        writer.EndNamespace();
        
        return;

        string CleanupEnumName(string name)
        {
            if (name.EndsWith('_'))
                return name[..^1];
            return name;
        }

        string CleanupEnumElement(string name, string enumName)
        {
            name = name.Replace(enumName, "");
            if (name.StartsWith('_') && name.Length > 1 && !char.IsDigit(name[1]))
                name = name[1..];
            
            var minusSplit = name.Split('-');
            for (int i = 0; i < minusSplit.Length; i++)
            {
                if (minusSplit[i].StartsWith('_') && minusSplit[i].Length > 1 && !char.IsDigit(minusSplit[i][1]))
                    minusSplit[i] = minusSplit[i][1..];
            }
            name = string.Join("-", minusSplit);

            return name;
        }
    }

    private void GenerateStructs(Definitions definitions)
    {
        var arraySizeTable = new DataTable("TryGetArraySize");
        
        var structs = definitions.Structs;
        var structPath = Path.Combine(_outputPath, "Structs");
        foreach (var @struct in structs)
        {
            var structName = @struct.Name;
            
            if (TypeInfo.CustomTypes.Contains(structName) || structName.Contains("ImVector"))
                continue;
            
            if (@struct.Fields.Count == 0)
            {
                // _context.FoundedTypes.TryAdd(structName, "IntPtr");
                _context.PointerStructs.Add(structName);
                continue;
            }
            
            if (structName.Contains('_'))
                structName = structName.Split('_')[^1];
            
            using var writer = new CSharpCodeWriter(structPath, $"{structName}.gen.cs");

            writer.Using("System");
            writer.Using("System.Numerics");
            writer.Using("System.Runtime.CompilerServices");
            writer.Using("System.Text");
            writer.WriteLine();
            writer.StartNamespace(_context.Namespace);
            
            _context.FoundedTypes.Add(@struct.Name, structName);
            
            writer.WriteCommentary(_context.CleanupComments(@struct.Comments));
            writer.WriteLine($"public unsafe partial struct {structName}");
            writer.PushBlock();
            
            foreach (var field in @struct.Fields)
            {
                if (!_context.EvalConditionals(field.Conditionals))
                    continue;
                
                if (field.Name.Contains("__anonymous_type"))
                    continue;
                
                var fieldType = _context.GetCSharpType(field.Type.Description);

                
                writer.WriteCommentary(_context.CleanupComments(field.Comments));
                var nativeType = field.Type.Description;
                
                if (field.IsArray)
                {
                    var arraySize = _context.ArraySizes.GetValueOrDefault(field.ArrayBounds!, 0);
                    if (arraySize == 0)
                    {
                        var arrayBounds = field.ArrayBounds!;
                        foreach (var define in _context.KnownDefines)
                        {
                            if (arrayBounds.Contains(define.Key))
                            {
                                var value = define.Value.value;

                                if (value.Contains("0x"))
                                {
                                    value = int.Parse(value[2..], NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo).ToString(CultureInfo.InvariantCulture);
                                }
                                
                                arrayBounds = arrayBounds.Replace(define.Key, value);
                                break;
                            }
                        }
                        try { arraySize = Convert.ToInt32(arraySizeTable.Compute(arrayBounds, "")); }
                        catch (Exception e) { Console.WriteLine($"Cannot compute array size for {field.Name} with bounds {arrayBounds}: {e.Message}"); }
                        
                        _context.ArraySizes[field.ArrayBounds!] = arraySize;
                    }
                    
                    fieldType = _context.GetCSharpType(field.Type.Description.InnerType!);
                    
                    if (fieldType.Contains("ImVector"))
                    {
                        fieldType = "ImVector";
                    }
                    
                    if (TypeInfo.FixedTypes.Contains(fieldType))
                    {
                        writer.WriteLine($"public fixed {fieldType} {field.Name}[{arraySize}];");
                    }
                    else
                    {
                        for (var i = 0; i < arraySize; i++)
                        {
                            writer.WriteLine($"public {fieldType} {field.Name}_{i};");
                        }
                    }
                }
                else if (nativeType.Kind == "Type")
                {
                    // this is most possibly a delegate
                    var innerType = nativeType.InnerType!;

                    var name = nativeType.Name;

                    if (innerType.Kind == "Pointer" && innerType.InnerType!.Kind == "Function")
                    {
                        // in case of a pointer to a function
                        // we have to gen a [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
                        innerType = innerType.InnerType!;

                        var cSharpDelegate = GenerateDelegateFromDescription(innerType, name + "Delegate");
                        _context.Delegates.Add((cSharpDelegate, null));
                    }
                    else
                    {
                        Console.WriteLine($"Unknown Type field {field.Name} of {structName}");
                    }
                    
                    writer.WriteLine($"public {fieldType} {field.Name};");
                }
                else
                {
                    if (fieldType.Contains("ImVector"))
                    {
                        fieldType = "ImVector";
                    }
                    
                    writer.WriteLine($"public {fieldType} {field.Name};");
                }
            }
            
            writer.PopBlock();
            writer.WriteLine();

            writer.WriteCommentary(_context.CleanupComments(@struct.Comments));
            var ptrStructName = $"{structName}Ptr";
            writer.WriteLine($"public unsafe partial struct {ptrStructName}");
            writer.PushBlock();
            writer.WriteLine($"public {structName}* NativePtr {{ get; }}");
            writer.WriteLine($"public {ptrStructName}({structName}* nativePtr) => NativePtr = nativePtr;");
            writer.WriteLine($"public {ptrStructName}(IntPtr nativePtr) => NativePtr = ({structName}*)nativePtr;");
            writer.WriteLine($"public static implicit operator {ptrStructName}({structName}* nativePtr) => new {ptrStructName}(nativePtr);");
            writer.WriteLine($"public static implicit operator {structName}* ({ptrStructName} wrappedPtr) => wrappedPtr.NativePtr;");
            writer.WriteLine($"public static implicit operator {ptrStructName}(IntPtr nativePtr) => new {ptrStructName}(nativePtr);");

            foreach (var field in @struct.Fields)
            {
                if (!_context.EvalConditionals(field.Conditionals))
                    continue;
                
                if (field.Name.Contains("__anonymous_type"))
                    continue;
                
                writer.WriteLine();
                
                var fieldType = _context.GetCSharpType(field.Type.Description);

                writer.WriteCommentary(_context.CleanupComments(field.Comments));
                if (structName == "ImDrawData" && field.Name == "CmdLists")
                {
                        
                }

                if (field.IsArray)
                {
                    fieldType = _context.GetCSharpType(field.Type.Description.InnerType!);
                    var arraySize = _context.ArraySizes.GetValueOrDefault(field.ArrayBounds!, 0).ToString();
                    if (arraySize == "0")
                    {
                        arraySize = field.ArrayBounds!;
                        foreach (var define in _context.KnownDefines)
                        {
                            if (arraySize.Contains(define.Key))
                            {
                                arraySize = arraySize.Replace(define.Key, define.Value.value);
                                break;
                            }
                        }
                    }

                    if (fieldType.Contains("ImVector"))
                    {
                        fieldType = fieldType.Split('_')[^1];

                        if (TypeInfo.ConversionTypes.TryGetValue(fieldType, out var conversionType))
                        {
                            fieldType = conversionType;
                        }

                        if (_context.GetWrappedType($"{fieldType}*", out var wrappedType))
                        {
                            fieldType = $"ImPtrVector<{wrappedType}>";
                        }
                        else
                        {
                            if (_context.GetWrappedType(fieldType, out wrappedType))
                            {
                                fieldType = wrappedType;
                            }

                            if (fieldType.EndsWith('*'))
                            {
                                fieldType = fieldType[..^1];
                                fieldType = $"ImPtrVector<{fieldType}>";
                            }
                            else
                            {
                                fieldType = $"ImVector<{fieldType}>";
                            }
                        }
                    }

                    
                    string addressType = TypeInfo.FixedTypes.Contains(fieldType) ? $"NativePtr->{field.Name}" : $"&NativePtr->{field.Name}_{0}";
                    if (fieldType.EndsWith('*'))
                    {
                        fieldType = fieldType[..^1];
                    }
                    writer.WriteLine($"public RangeAccessor<{fieldType}> {field.Name} => new RangeAccessor<{fieldType}>({addressType}, {arraySize});");
                }
                else if (fieldType.Contains("ImVector"))
                {
                    var elementType = fieldType.Split('_')[^1];

                    if (TypeInfo.ConversionTypes.TryGetValue(elementType, out var conversionType))
                    {
                        elementType = conversionType;
                    }

                    if (_context.GetWrappedType($"{elementType}*", out var wrappedType))
                    {
                        writer.WriteLine($"public ImPtrVector<{wrappedType}> {field.Name} => new ImPtrVector<{wrappedType}>(NativePtr->{field.Name}, Unsafe.SizeOf<{elementType}>());");
                    }
                    else
                    {
                        if (_context.GetWrappedType(elementType, out wrappedType))
                        {
                            elementType = wrappedType;
                        }
                        writer.WriteLine($"public ImVector<{elementType}> {field.Name} => new ImVector<{elementType}>(NativePtr->{field.Name});");
                    }
                }
                else
                {
                    if (fieldType.Contains('*') && !fieldType.Contains("ImVector"))
                    {
                        if (_context.GetWrappedType(fieldType, out var wrappedType))
                        {
                            writer.WriteLine($"public {wrappedType} {field.Name} => new {wrappedType}(NativePtr->{field.Name});");
                        }
                        else if(fieldType == "byte*" && IsStringFieldName(field.Name))
                        {
                            writer.WriteLine($"public NullTerminatedString {field.Name} => new NullTerminatedString(NativePtr->{field.Name});");
                        }
                        else
                        {
                            writer.WriteLine($"public IntPtr {field.Name} {{ get => (IntPtr)NativePtr->{field.Name}; set => NativePtr->{field.Name} = ({fieldType})value; }}");
                        }
                    }
                    else
                    {
                        writer.WriteLine($"public ref {fieldType} {field.Name} => ref Unsafe.AsRef<{fieldType}>(&NativePtr->{field.Name});");
                    }
                }
            }
            
            MethodGenerator.Begin();
            foreach (var function in definitions.Functions)
            {
                var nameSplit = function.Name.Split('_');
                if (nameSplit.Length == 0 ||
                    nameSplit.Length == 1 ||
                    nameSplit.Length == 2 && nameSplit[0] != structName ||
                    nameSplit.Length >= 3 && nameSplit[^2] != structName)
                {
                    continue;
                }
                if (!_context.EvalConditionals(function.Conditionals))
                {
                    continue;
                }
                if (function.Arguments.Count > 0 && function.Arguments[^1].Type?.Declaration == "va_list")
                    continue;
                
                writer.WriteLine();
                
                MethodGenerator.WriteMethodOverload(function, writer);
            }
            
            writer.PopBlock();
            writer.EndNamespace();
            arraySizeTable.Dispose();
        }
        
        return;
        
        bool IsStringFieldName(string name)
        {
            return Regex.IsMatch(name, ".*Filename.*")
                   || Regex.IsMatch(name, ".*Name");
        }
    }

    private void GenerateMethods(IReadOnlyList<FunctionItem> functions)
    {
        using var writer = new CSharpCodeWriter(_outputPath, $"{_context.NativeClass}.gen.cs");

        writer.Using("System");
        writer.Using("System.Numerics");
        writer.Using("System.Runtime.InteropServices");
        writer.StartNamespace(_context.Namespace);
        writer.WriteLine($"public static unsafe partial class {_context.NativeClass}");
        writer.PushBlock();

        foreach (var function in functions)
        {
            if (!_context.EvalConditionals(function.Conditionals))
                continue;
            
            var functionName = function.Name;

            if (functionName.Contains("ImVector_")) continue;
            if (functionName.Contains("ImChunkStream_")) continue;
            if (function.Arguments.Count > 0 && function.Arguments[^1].Type?.Declaration == "va_list")
                continue;
            
            var csharpReturnType = _context.GetCSharpType(function.ReturnType!.Description);
            var parameters = new List<string>();
            
            foreach (var argument in function.Arguments)
            {
                var argumentName = argument.Name;

                if (TypeInfo.CSharpIdentifiers.TryGetValue(argumentName, out var csharpIdentifier))
                    argumentName = csharpIdentifier;

                if (argument.Type is null)
                    continue;
                
                var argumentTypeDesc = argument.Type.Description;

                if (argumentTypeDesc.Kind == "Array")
                {
                    var paramType = _context.GetCSharpType(argumentTypeDesc.InnerType!);
                    parameters.Add($"{paramType}* {argumentName}");
                }
                else if (argumentTypeDesc.Kind == "Type")
                {
                    // this is most possibly a delegate
                    var innerType = argumentTypeDesc.InnerType!;

                    if (innerType.Kind == "Pointer" && innerType.InnerType!.Kind == "Function")
                    {
                        // in case of a pointer to a function
                        // we have to gen a [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
                        innerType = innerType.InnerType!;

                        var delegateName = functionName + argumentName + "Delegate";
                        var csharpDelegate = GenerateDelegateFromDescription(innerType, delegateName);
                        
                        _context.Delegates.Add((csharpDelegate, null));

                        parameters.Add($"{delegateName} {argumentName}");
                    }
                    else
                    {
                        Console.WriteLine($"Unknown Type argument {argumentTypeDesc.Name}");
                    }
                }
                else
                {
                    var csharpType = _context.GetCSharpType(argumentTypeDesc);
                    //if type is ImVector_ImWchar* or ImVector_ImGuiTextFilter_ImGuiTextRange*, etc... will be converted to ImVector*
                    if (csharpType.Contains("ImVector_") && csharpType.EndsWith('*'))
                    {
                        csharpType = "ImVector*";
                    }
                    parameters.Add($"{csharpType} {argumentName}");
                }
            }
            
            writer.WriteCommentary(_context.CleanupComments(function.Comments));
            writer.WriteLine($"[DllImport(\"dcimgui\", CallingConvention = CallingConvention.Cdecl, EntryPoint = \"{functionName}\")]");
            writer.WriteLine($"public static extern {csharpReturnType} {functionName}({string.Join(", ", parameters)});");
        }
        
        writer.PopBlock();
        writer.EndNamespace();
    }
    
    private void GenerateDelegates()
    {
        using var writer = new CSharpCodeWriter(_outputPath, "Delegates.gen.cs");
        
        writer.Using("System");
        writer.Using("System.Runtime.InteropServices");
        writer.WriteLine();
        writer.StartNamespace(_context.Namespace);
        
        foreach (var @delegate in _context.Delegates)
        {
            writer.WriteCommentary(@delegate.comments);
            writer.WriteLine("[UnmanagedFunctionPointer(CallingConvention.Cdecl)]");
            writer.WriteLine(@delegate.content);
        }
        
        writer.EndNamespace();
    }
    
    private string GenerateDelegateFromDescription(TypeDescription description, string delegateName)
    {
        var csharpReturnType = _context.GetCSharpType(description.ReturnType!);
    
        var parameters = new List<string>();
        foreach (var parameter in description.Parameters!)
        {
            var argumentName = parameter.Name!;
            if (string.IsNullOrEmpty(argumentName))
            {
                argumentName = "arg" + description.Parameters.IndexOf(parameter);
            }
            var argumentType = parameter;
            if (parameter.Kind == "Type")
            {
                argumentType = parameter.InnerType;
            }
            else
            {
                Console.WriteLine($"Function parameter {parameter.Name} was not of kind Type. Was {parameter.Kind}");
            }

            var csharpType = _context.GetCSharpType(argumentType!);
        
            parameters.Add($"{csharpType} {argumentName}");
        }

        return $"public unsafe delegate {csharpReturnType} {delegateName}({string.Join(", ", parameters)});";
    }

    private void GenerateMainOverloads(IReadOnlyList<FunctionItem> functions)
    {
        using var writer = new CSharpCodeWriter(_outputPath, $"{_context.MainMethodsClass}.gen.cs");

        writer.Using("System");
        writer.Using("System.Numerics");
        writer.Using("System.Runtime.InteropServices");
        writer.Using("System.Text");
        writer.WriteLine("// ReSharper disable InconsistentNaming");
        writer.WriteLine();
        writer.StartNamespace(_context.Namespace);
        writer.WriteLine($"public static unsafe partial class {_context.MainMethodsClass}");
        writer.PushBlock();

        MethodGenerator.Begin();
        foreach (var function in functions.Where(f => f.Name.StartsWith("ImGui_")))
        {
            if (!_context.EvalConditionals(function.Conditionals))
                continue;
            
            if (function.Arguments.Count > 0 && function.Arguments[^1].Type?.Declaration == "va_list")
                continue;

            writer.WriteLine();
            
            MethodGenerator.WriteMethodOverload(function, writer, true);
        }
        
        writer.PopBlock();
        writer.EndNamespace();
    }
}