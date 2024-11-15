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
    
    private string _namespace = "SharpImGui";
    
    private readonly Dictionary<string, string> _foundedTypes = new();
    private readonly Dictionary<string, int> _arraySizes = new();
    private readonly Dictionary<string, (string? type, string value)> _knownDefines = new()
    {
        ["IMGUI_DISABLE_OBSOLETE_FUNCTIONS"] = (null, ""),
        ["IMGUI_DISABLE_OBSOLETE_KEYIO"] = (null, "")
    };

    private readonly List<(string content, Comments? comments)> _delegates = [];

    public void Generate()
    {
        if (Directory.Exists(_outputPath))
            Directory.Delete(_outputPath, true);
        
        GenerateBindings("dcimgui.json");
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
        // using var writer = new CSharpCodeWriter(_outputPath, "Typedefs.gen.cs");
        //
        // writer.Using("System");
        // writer.WriteLine();
        // writer.StartNamespace(_namespace);
        // writer.WriteLine("public static class Typedefs");
        // writer.PushBlock();

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
                    var comment = CleanupComments(typedef.Comments);
                    _delegates.Add((@delegate, comment));
                    continue;
                }
            }

            if (typedef.Name.Contains("flags", StringComparison.OrdinalIgnoreCase))
                continue;
                
            var csharpType = GetCSharpType(typedef.Type.Description);
            _foundedTypes.TryAdd(typedef.Name, csharpType);
        }
        
        // writer.PopBlock();
        // writer.EndNamespace();
    }

    private void GenerateConstants(IReadOnlyList<DefineItem> defines)
    {
        using var writer = new CSharpCodeWriter(_outputPath, "Constants.gen.cs");

        writer.Using("System");
        writer.WriteLine();
        writer.StartNamespace(_namespace);
        writer.WriteLine("public static class Constants");
        writer.PushBlock();
        // dear_bindings writes defines in a strange manner, producing redefines, so when we group them by count, we can produce more accurate result
        var defineGroups = defines.GroupBy(x => x.Conditionals?.Count ?? 0);

        foreach (var key in _knownDefines.Keys)
        {
            writer.WriteLine($"public const string {key} = \"{_knownDefines[key].value.Replace("\"", "\\\"")}\";");
        }

        foreach (var group in defineGroups)
        {
            if (group.Key == 0)
            {
                foreach (var define in group)
                {
                    var constant = GenerateConstant(define);
                    writer.WriteCommentary(CleanupComments(define.Comments));
                    writer.WriteLine($"public const {constant.Type} {constant.Name} = {constant.Value};");
                    _knownDefines[define.Name] = (constant.Type, define.Content?? "");
                }
            }
            else if (group.Key == 1)
            {
                foreach (var define in group)
                {
                    if (!EvalConditionals(define.Conditionals))
                        continue;
                    
                    var constant = GenerateConstant(define);
                    writer.WriteCommentary(CleanupComments(define.Comments));
                    writer.WriteLine($"public const {constant.Type} {constant.Name} = {constant.Value};");
                    _knownDefines[define.Name] = (constant.Type, define.Content?? "");
                }
            }
            else
            {
                foreach (var define in group)
                {
                    if (!EvalConditionals(define.Conditionals))
                        continue;
                    
                    var constant = GenerateConstant(define);
                    writer.WriteCommentary(CleanupComments(define.Comments));
                    writer.WriteLine($"public const {constant.Type} {constant.Name} = {constant.Value};");
                    _knownDefines[define.Name] = (constant.Type, define.Content?? "");
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

            if (_knownDefines.ContainsKey(defineItem.Content))
            {
                var contentConstant = _knownDefines.Keys.FirstOrDefault(c => c == defineItem.Content, null);
                var type = _knownDefines[contentConstant].type ?? "bool";
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
        writer.StartNamespace(_namespace);
        
        foreach (var @enum in enums)
        {
            if (!EvalConditionals(@enum.Conditionals))
                continue;
            
            var enumName = CleanupEnumName(@enum.Name);
            _foundedTypes.Add(enumName, enumName);
            
            writer.WriteCommentary(CleanupComments(@enum.Comments));
            if (@enum.IsFlagsEnum)
                writer.WriteLine("[Flags]");

            writer.WriteLine($"public enum {enumName}");
            writer.PushBlock();
            
            foreach (var element in @enum.Elements)
            {
                if (!EvalConditionals(element.Conditionals))
                    continue;
                
                
                if (element.Name.Contains("COUNT", StringComparison.Ordinal))
                {
                    _arraySizes[element.Name] = (int)element.Value;
                }
                
                var value = element.Value.ToString(CultureInfo.InvariantCulture);
                if (element.ValueExpression != null)
                    value = CleanupEnumElement(element.ValueExpression, @enum.Name);
                
                writer.WriteCommentary(CleanupComments(element.Comments));
                writer.WriteLine($"{CleanupEnumElement(element.Name, @enum.Name)} = {value},");
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
                _foundedTypes.Add(structName, "IntPtr");
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
            writer.StartNamespace(_namespace);
            
            _foundedTypes.Add(@struct.Name, structName);
            
            writer.WriteCommentary(CleanupComments(@struct.Comments));
            writer.WriteLine($"public unsafe partial struct {structName}");
            writer.PushBlock();
            
            foreach (var field in @struct.Fields)
            {
                if (!EvalConditionals(field.Conditionals))
                    continue;
                
                var fieldType = GetCSharpType(field.Type.Description);
                if (fieldType.Contains("ImVector"))
                {
                    if (fieldType.Contains('*'))
                        throw new NotSupportedException($"ImVector pointer not supported {fieldType}");
                    
                    fieldType = "ImVector";
                }

                writer.WriteCommentary(CleanupComments(field.Comments));
                var nativeType = field.Type.Description;
                
                if (field.IsArray)
                {
                    var arraySize = _arraySizes.GetValueOrDefault(field.ArrayBounds!, 0);
                    if (arraySize == 0)
                    {
                        var arrayBounds = field.ArrayBounds!;
                        foreach (var define in _knownDefines)
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
                        
                        _arraySizes[field.ArrayBounds!] = arraySize;
                    }
                    
                    fieldType = GetCSharpType(field.Type.Description.InnerType!);
                    
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
                        _delegates.Add((cSharpDelegate, null));
                    }
                    else
                    {
                        Console.WriteLine($"Unknown Type field {field.Name} of {structName}");
                    }
                    
                    writer.WriteLine($"public {fieldType} {field.Name};");
                }
                else
                {
                    writer.WriteLine($"public {fieldType} {field.Name};");
                }
            }
            
            writer.PopBlock();
            writer.WriteLine();

            writer.WriteCommentary(CleanupComments(@struct.Comments));
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
                if (!EvalConditionals(field.Conditionals))
                    continue;
                
                writer.WriteLine();
                
                var fieldType = GetCSharpType(field.Type.Description);

                writer.WriteCommentary(CleanupComments(field.Comments));

                if (field.IsArray)
                {
                    fieldType = GetCSharpType(field.Type.Description.InnerType!);
                    var arraySize = _arraySizes.GetValueOrDefault(field.ArrayBounds!, 0).ToString();
                    if (arraySize == "0")
                    {
                        arraySize = field.ArrayBounds!;
                        foreach (var define in _knownDefines)
                        {
                            if (arraySize.Contains(define.Key))
                            {
                                arraySize = arraySize.Replace(define.Key, define.Value.value);
                                break;
                            }
                        }
                    }
                    
                    string addressType = TypeInfo.FixedTypes.Contains(fieldType) ? $"NativePtr->{field.Name}" : $"&NativePtr->{field.Name}_{0}";
                    writer.WriteLine($"public RangeAccessor<{fieldType}> {field.Name} => new RangeAccessor<{fieldType}>({addressType}, {arraySize});");
                }
                else if (fieldType.Contains("ImVector"))
                {
                    var elementType = fieldType.Split('_')[^1];

                    if (TypeInfo.ConversionTypes.TryGetValue(elementType, out var conversionType))
                    {
                        elementType = conversionType;
                    }

                    if (GetWrappedType($"elementType*", out var wrappedType))
                    {
                        writer.WriteLine($"public ImPtrVector<{wrappedType}> {field.Name} => new ImPtrVector<{wrappedType}>(NativePtr->{field.Name}, Unsafe.SizeOf<{elementType}>());");
                    }
                    else
                    {
                        if (GetWrappedType(elementType, out wrappedType))
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
                        if (GetWrappedType(fieldType, out var wrappedType))
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

            foreach (var function in definitions.Functions)
            {
                if (function.Name.Split('_')[^2] != structName)
                {
                    continue;
                }
                if (!EvalConditionals(function.Conditionals))
                {
                    continue;
                }
                if (function.Arguments.Count > 0 && function.Arguments[^1].Type?.Declaration == "va_list")
                    continue;
                
                writer.WriteLine();
                
                // var functionName = function.Name.Split('_')[^1];
                //
                // var csharpReturnType = GetCSharpType(function.ReturnType!.Description);
                // var parameters = new List<(string type, string name)>();
                // foreach (var argument in function.Arguments)
                // {
                //     var argumentName = argument.Name;
                //
                //     if (TypeInfo.CSharpIdentifiers.TryGetValue(argumentName, out var csharpIdentifier))
                //         argumentName = csharpIdentifier;
                //
                //     if (argument.Type is null)
                //         continue;
                //     
                //     var argumentTypeDesc = argument.Type.Description;
                //
                //     if (argumentTypeDesc.Kind == "Array")
                //     {
                //         var paramType = GetCSharpType(argumentTypeDesc.InnerType!);
                //         parameters.Add((paramType + "*", argumentName));
                //     }
                //     else
                //     {
                //         var csharpType = GetCSharpType(argumentTypeDesc);
                //         //if type is ImVector_ImWchar* or ImVector_ImGuiTextFilter_ImGuiTextRange*, etc... will be converted to ImVector*
                //         if (csharpType.Contains("ImVector_") && csharpType.EndsWith('*'))
                //         {
                //             csharpType = "ImVector*";
                //         }
                //         else if (csharpType.Contains('*') && !csharpType.Contains("ImVector"))
                //         {
                //             if (GetWrappedType(csharpType, out var wrappedType))
                //             {
                //                 csharpType = wrappedType;
                //             }
                //         }
                //         
                //         parameters.Add((csharpType, argumentName));
                //     }
                // }
                //
                // writer.WriteCommentary(CleanupComments(function.Comments));
                //
                // var typedParameters = "";
                // if (parameters[0].type == $"{ptrStructName}")
                // {
                //     parameters = parameters[1..];
                //     typedParameters += "NativePtr";
                //     if (parameters.Count > 0) typedParameters += ", ";
                // }
                // typedParameters += string.Join(", ", parameters.Select(p => p.name));
                //
                // writer.WriteLine($"public {csharpReturnType} {functionName}({string.Join(", ", parameters.Select(p => p.type + " " + p.name))})");
                // writer.PushBlock();
                // writer.WriteLine($"{(csharpReturnType == "void" ? "" : "return ")}ImGuiNative.{function.Name}({typedParameters});");
                // writer.PopBlock();
                WriteMethodOverload(function, writer);
            }
            
            writer.PopBlock();
            writer.EndNamespace();
            arraySizeTable.Dispose();
        }
    }

    private void GenerateMethods(IReadOnlyList<FunctionItem> functions)
    {
        using var writer = new CSharpCodeWriter(_outputPath, "ImGuiNative.gen.cs");

        writer.Using("System");
        writer.Using("System.Numerics");
        writer.Using("System.Runtime.InteropServices");
        writer.StartNamespace(_namespace);
        writer.WriteLine("public static unsafe partial class ImGuiNative");
        writer.PushBlock();

        foreach (var function in functions)
        {
            if (!EvalConditionals(function.Conditionals))
                continue;
            
            var functionName = function.Name;

            if (functionName.Contains("ImVector_")) continue;
            if (functionName.Contains("ImChunkStream_")) continue;
            if (function.Arguments.Count > 0 && function.Arguments[^1].Type?.Declaration == "va_list")
                continue;
            
            var csharpReturnType = GetCSharpType(function.ReturnType!.Description);
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
                    var paramType = GetCSharpType(argumentTypeDesc.InnerType!);
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
                        
                        _delegates.Add((csharpDelegate, null));

                        parameters.Add($"{delegateName} {argumentName}");
                    }
                    else
                    {
                        Console.WriteLine($"Unknown Type argument {argumentTypeDesc.Name}");
                    }
                }
                else
                {
                    var csharpType = GetCSharpType(argumentTypeDesc);
                    //if type is ImVector_ImWchar* or ImVector_ImGuiTextFilter_ImGuiTextRange*, etc... will be converted to ImVector*
                    if (csharpType.Contains("ImVector_") && csharpType.EndsWith('*'))
                    {
                        csharpType = "ImVector*";
                    }
                    parameters.Add($"{csharpType} {argumentName}");
                }
            }
            
            writer.WriteCommentary(CleanupComments(function.Comments));
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
        writer.StartNamespace(_namespace);
        
        foreach (var @delegate in _delegates)
        {
            writer.WriteCommentary(@delegate.comments);
            writer.WriteLine("[UnmanagedFunctionPointer(CallingConvention.Cdecl)]");
            writer.WriteLine(@delegate.content);
        }
        
        writer.EndNamespace();
    }
    
    private string GenerateDelegateFromDescription(TypeDescription description, string delegateName)
    {
        var csharpReturnType = GetCSharpType(description.ReturnType!);
    
        var parameters = new List<string>();
        foreach (var parameter in description.Parameters!)
        {
            var argumentName = parameter.Name!;
            var argumentType = parameter;
            if (parameter.Kind == "Type")
            {
                argumentType = parameter.InnerType;
            }
            else
            {
                Console.WriteLine($"Function parameter {parameter.Name} was not of kind Type. Was {parameter.Kind}");
            }

            var csharpType = GetCSharpType(argumentType!);
        
            parameters.Add($"{csharpType} {argumentName}");
        }

        return $"public unsafe delegate {csharpReturnType} {delegateName}({string.Join(", ", parameters)});";
    }

    private void GenerateMainOverloads(IReadOnlyList<FunctionItem> functions)
    {
        using var writer = new CSharpCodeWriter(_outputPath, "ImGui.gen.cs");

        writer.Using("System");
        writer.Using("System.Numerics");
        writer.Using("System.Runtime.InteropServices");
        writer.StartNamespace(_namespace);
        writer.WriteLine("public static unsafe partial class ImGui");
        writer.PushBlock();

        foreach (var function in functions.Where(f => f.Name.StartsWith("ImGui_")))
        {
            if (!EvalConditionals(function.Conditionals))
                continue;
            
            if (function.Arguments.Count > 0 && function.Arguments[^1].Type?.Declaration == "va_list")
                continue;

            writer.WriteLine();
                
            WriteMethodOverload(function, writer, true);
        }
        
        writer.PopBlock();
        writer.EndNamespace();
    }

    private void WriteMethodOverload(FunctionItem function, CSharpCodeWriter writer, bool isStatic = false)
    {
        var functionName = function.Name.Split('_')[^1];
            
        var csharpReturnType = GetCSharpType(function.ReturnType!.Description);
        var isWrappedType = GetWrappedType(csharpReturnType, out var safeReturnType);
        if (!isWrappedType)
            safeReturnType = csharpReturnType;
            
        var parameters = new List<(string type, string name)>();
            
        bool hasSelf = false;
        foreach (var argument in function.Arguments)
        {
            var argumentName = argument.Name;
                
            if (argumentName == "self")
            {
                hasSelf = true;
                continue;
            }
            
            if (TypeInfo.CSharpIdentifiers.TryGetValue(argumentName, out var csharpIdentifier))
                argumentName = csharpIdentifier;
            
            if (argument.Type is null)
                continue;
                
            var argumentTypeDesc = argument.Type.Description;
            
            if (argumentTypeDesc.Kind == "Array")
            {
                var paramType = GetCSharpType(argumentTypeDesc.InnerType!);
                parameters.Add((paramType + "*", argumentName));
            }
            else if (argumentTypeDesc.Kind == "Type")
            {
                // this is most possibly a delegate
                var innerType = argumentTypeDesc.InnerType!;
            
                if (innerType.Kind == "Pointer" && innerType.InnerType!.Kind == "Function")
                {
                    var delegateName = function.Name + argumentName + "Delegate";
                    parameters.Add((delegateName, argumentName));
                }
                else
                {
                    Console.WriteLine($"Unknown Type argument {argumentTypeDesc.Name}");
                }
            }
            else
            {
                var csharpType = GetCSharpType(argumentTypeDesc);
                //if type is ImVector_ImWchar* or ImVector_ImGuiTextFilter_ImGuiTextRange*, etc... will be converted to ImVector*
                if (csharpType.Contains("ImVector_") && csharpType.EndsWith('*'))
                {
                    csharpType = "ImVector*";
                }
                else if (csharpType.Contains('*') && !csharpType.Contains("ImVector"))
                {
                    if (GetWrappedType(csharpType, out var wrappedType))
                    {
                        csharpType = wrappedType;
                    }
                }
                    
                parameters.Add((csharpType, argumentName));
            }
        }
            
        writer.WriteCommentary(CleanupComments(function.Comments));

        var typedParameters = "";
        if (hasSelf)
        {
            typedParameters += "NativePtr";
            if (parameters.Count > 0) typedParameters += ", ";
        }
        typedParameters += string.Join(", ", parameters.Select(p => p.name));
            
        writer.WriteLine($"public{(isStatic ? " static" : "")} {safeReturnType} {functionName}({string.Join(", ", parameters.Select(p => p.type + " " + p.name))})");
        writer.PushBlock();
        writer.WriteLine($"{(safeReturnType == "void" ? "" : "return ")}ImGuiNative.{function.Name}({typedParameters});");
        writer.PopBlock();
    }

    private string GetCSharpType(TypeDescription typeDescription)
    {
        const string unknown = "unknown";
        const string notFound = "notFounded_"; 
        
        switch (typeDescription.Kind)
        {
            case "Builtin":
            {
                var type = typeDescription.BuiltinType!;
                return TypeInfo.ConversionTypes.GetValueOrDefault(type, type);
            }
            case "User":
            {
                var type = typeDescription.Name!;

                // try to find the conversion, or fallback to whats actually declared
                if (TypeInfo.ConversionTypes.TryGetValue(type, out var  conversionType))
                    return conversionType;
                
                return _foundedTypes.GetValueOrDefault(type, type);
            }
            case "Pointer":
            {
                var innerType = typeDescription.InnerType!;
                var innerTypeDef = GetCSharpType(innerType);

                return innerTypeDef == "IntPtr" ? "IntPtr" : $"{innerTypeDef}*";
            }
            case "Type":
            {
                var innerType = typeDescription.InnerType!;

                var name = typeDescription.Name;

                if (innerType.Kind == "Pointer" && innerType.InnerType!.Kind == "Function")
                {
                    return $"{name}Delegate";
                    // var delegateName = name + "Delegate";
                    // // _csharpContext.AddDelegate(new CSharpDelegate(delegateName, GetCSharpType(innerType.InnerType!)));
                    // return _csharpContext.GetType(delegateName);
                }

                return $"{unknown}_{name}";
            }
        }

        return unknown;
    }
    
    private static bool GetWrappedType(string nativeType, out string wrappedType)
    {
        if (nativeType.StartsWith("Im") && nativeType.EndsWith("*") && !nativeType.StartsWith("ImVector"))
        {
            int pointerLevel = nativeType.Length - nativeType.IndexOf('*');
            if (pointerLevel > 1)
            {
                wrappedType = null;
                return false; // TODO
            }
            string nonPtrType = nativeType.Substring(0, nativeType.Length - pointerLevel);

            if (TypeInfo.ConversionTypes.ContainsKey(nonPtrType))
            {
                wrappedType = null;
                return false;
            }

            wrappedType = nonPtrType + "Ptr";

            return true;
        }
        else
        {
            wrappedType = null;
            return false;
        }
    }
    
    private static bool IsStringFieldName(string name)
    {
        return Regex.IsMatch(name, ".*Filename.*")
               || Regex.IsMatch(name, ".*Name");
    }
    
    private bool EvalConditionals(List<ConditionalItem>? conditionals)
    {
        if (conditionals is {Count: > 0})
        {
            if (conditionals.Count == 1)
            {
                var condition = conditionals[0];
                return (condition.Condition == "ifdef" && _knownDefines.ContainsKey(condition.Expression)) ||
                       (condition.Condition == "ifndef" && !_knownDefines.ContainsKey(condition.Expression)) ||
                       (condition.Condition == "if" && condition.Expression.StartsWith("defined") && !condition.Expression.StartsWith("&&") && 
                        _knownDefines.ContainsKey(condition.Expression.Substring(8, condition.Expression.Length - 8 - 1)));
            }
            else
            {
                var condition = conditionals[1];
                return (condition.Condition == "ifdef" && _knownDefines.ContainsKey(condition.Expression)) ||
                       (condition.Condition == "ifndef" && !_knownDefines.ContainsKey(condition.Expression));
            }
        }
        else
        {
            return true;
        }
    }

    private Comments? CleanupComments(Comments? comment)
    {
        if (comment == null)
            return null;

        if (comment.Attached != null)
        {
            comment = comment with { Attached = Cleanup(comment.Attached) };
        }

        if (comment.Preceding != null)
        {
            comment = comment with
            {
                Preceding = comment.Preceding.Select(Cleanup).ToArray()
            };
        }
        
        return comment;

        string Cleanup(string text)
        {
            if (text.StartsWith("// "))
                return text[3..];
            return text;
        }
    }
    
    private class MarshalledParameter
    {
        public MarshalledParameter(string marshalledType, bool isPinned, string varName, bool hasDefaultValue)
        {
            MarshalledType = marshalledType;
            IsPinned = isPinned;
            VarName = varName;
            HasDefaultValue = hasDefaultValue;
        }

        public string MarshalledType { get; }
        public bool IsPinned { get; }
        public string VarName { get; }
        public bool HasDefaultValue { get; }
        public string PinTarget { get; internal set; }
    }
}