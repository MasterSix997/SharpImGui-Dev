using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using SharpImGui_Dev.CodeGenerator.CSharp;
using SharpImGui_Dev.CodeGenerator.Passes;

namespace SharpImGui_Dev.CodeGenerator
{
    public record GenerationConfig(string MetadataFile, string OutputPath, string Namespace)
    {
        public string MetadataFile { get; } = MetadataFile;
        public string OutputPath { get; } = OutputPath;
        public string Namespace { get; } = Namespace;

        public Dictionary<Type, CSharpClass> PredefinedClasses = new();
        public Dictionary<Type, CSharpFile> PredefinedFiles = new();
    }
    
    public class Generator
    {
        private static readonly string ProjectPath = Path.Combine("../../../");
        private static readonly string DearBindingsPath = Path.Combine(ProjectPath, "dcimgui");
        // private string _outputPath = Path.Combine(ProjectPath, "../SharpImGui/Generated");

        private List<GenerationConfig> _configs =
        [
            new GenerationConfig("dcimgui.json", Path.Combine(ProjectPath, "../SharpImGui/Generated"), "SharpImGui")
            {
                PredefinedClasses =
                {
                    [typeof(CSharpConstant)] = new CSharpClass("ImGuiConstants") { Modifiers = ["public", "static", "partial"]},
                    [typeof(CSharpMethod)] = new CSharpClass("ImGuiNative") { Modifiers = ["public", "static", "unsafe", "partial"] } 
                },
                PredefinedFiles =
                {
                    [typeof(CSharpEnum)] = new CSharpFile("Enums.gen.cs", "SharpImGui", ["System"]),
                    //[typeof(CSharpStruct)] = new CSharpFile("Structs.gen.cs", "SharpImGui", ["System", "System.Numerics"]),
                    [typeof(CSharpDelegate)] = new CSharpFile("Delegates.gen.cs", "SharpImGui", ["System", "System.Runtime.InteropServices"]),
                    [typeof(CSharpMethod)] = new CSharpFile("ImGuiNative.gen.cs", "SharpImGui", ["System", "System.Runtime.InteropServices", "System.Numerics"]),
                    [typeof(CSharpConstant)] = new CSharpFile("Constants.gen.cs", "SharpImGui", ["System"])
                }
            },
            new GenerationConfig("dcimgui_internal.json", Path.Combine(ProjectPath, "../SharpImGui/Generated/Internal"), "SharpImGui.Internal")
            {
                PredefinedClasses =
                {
                    [typeof(CSharpConstant)] = new CSharpClass("ImGuiConstantsInternal") { Modifiers = ["public", "static", "partial"]},
                    [typeof(CSharpMethod)] = new CSharpClass("ImGuiInternalNative") { Modifiers = ["public", "static", "unsafe", "partial"] } 
                },
                PredefinedFiles =
                {
                    [typeof(CSharpEnum)] = new CSharpFile("Enums.gen.cs", "SharpImGui.Internal", ["System"]),
                    //[typeof(CSharpStruct)] = new CSharpFile("Structs.gen.cs", "SharpImGui.Internal", ["System", "System.Numerics"]),
                    [typeof(CSharpDelegate)] = new CSharpFile("Delegates.gen.cs", "SharpImGui.Internal", ["System", "System.Runtime.InteropServices"]),
                    [typeof(CSharpMethod)] = new CSharpFile("ImGuiNative.gen.cs", "SharpImGui.Internal", ["System", "System.Runtime.InteropServices", "System.Numerics"]),
                    [typeof(CSharpConstant)] = new CSharpFile("Constants.gen.cs", "SharpImGui.Internal", ["System"])
                }
            },
        ];
        
        private CSharpContext _csharpContext = null!;
        private GenerationConfig _currentConfig = null!;
        
        private readonly List<string> _skippedItems = [];
        private static readonly Dictionary<string, int> ArraySizes = new();
        private static readonly Dictionary<string, string> KnownDefines = new()
        {
            ["IMGUI_DISABLE_OBSOLETE_FUNCTIONS"] = "",
            ["IMGUI_DISABLE_OBSOLETE_KEYIO"] = ""
        };
        private readonly Dictionary<CSharpStruct, List<CSharpMethod>> _structMethods = new();
        
        public void Generate()
        {
            foreach (var config in _configs)
            {
                _currentConfig = config;
                CreateContext(config);
                GenerateBindings(config.MetadataFile);

                if (Directory.Exists(config.OutputPath))
                    Directory.Delete(config.OutputPath, true);
                _csharpContext.WriteAllFiles(config.OutputPath);
            }
            _currentConfig = null!;
            
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Generated!");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Skipped items: {_skippedItems.Count}");
            Console.ResetColor();
        }

        private void CreateContext(GenerationConfig config)
        {
            var typeMap = _csharpContext?.TypeMap ?? new Dictionary<string, CSharpType>
            {
                ["int"] = new CSharpType("int"),
                ["unsigned int"] = new CSharpType("uint"),
                ["unsigned char"] = new CSharpType("byte"),
                ["unsigned_char"] = new CSharpType("byte"),
                ["unsigned_int"] = new CSharpType("uint"),
                ["unsigned_short"] = new CSharpType("ushort"),
                ["long long"] = new CSharpType("long"),
                ["long_long"] = new CSharpType("long"),
                ["unsigned_long_long"] = new CSharpType("ulong"),
                ["short"] = new CSharpType("short"),
                ["signed char"] = new CSharpType("sbyte"),
                ["signed short"] = new CSharpType("short"),
                ["signed int"] = new CSharpType("int"),
                ["signed long long"] = new CSharpType("long"),
                ["unsigned long long"] = new CSharpType("ulong"),
                ["unsigned short"] = new CSharpType("ushort"),
                ["float"] = new CSharpType("float"),
                ["bool"] = new CSharpType("bool"),
                ["char"] = new CSharpType("char"),
                ["double"] = new CSharpType("double"),
                ["void"] = new CSharpType("void"),
                ["va_list"] = new CSharpType("va_list"),
                ["size_t"] = new CSharpType("ulong"), // assume only x64 for now
            };
            _csharpContext = new CSharpContext(config.Namespace)
            {
                PredefinedClasses = config.PredefinedClasses,
                PredefinedFiles = config.PredefinedFiles,
                TypeMap = typeMap
            };
            _csharpContext.AddPreprocessor(new CommentPreprocessor());
            _csharpContext.AddPreprocessor(new NamingPreprocessor());
            _csharpContext.AddPreprocessor(new TypesPreprocessor());
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
            GenerateTypedefs(metadata.Typedefs);
            GenerateStructs(metadata.Structs);
            GenerateFunctions(metadata.Functions);
            
            GenerateStructsPtr();
        }

        private void GenerateConstants(IReadOnlyList<DefineItem> defines)
        {
            // dear_bindings writes defines in a strange manner, producing redefines, so when we group them by count, we can produce more accurate result
            var defineGroups = defines.GroupBy(x => x.Conditionals?.Count ?? 0);

            foreach (var key in KnownDefines.Keys)
            {
                _csharpContext.AddConstant(new CSharpConstant(key, new CSharpType("string"), $"\"{KnownDefines[key].Replace("\"", "\\\"")}\""));
            }

            foreach (var group in defineGroups)
            {
                if (group.Key == 0)
                {
                    foreach (var define in group)
                    {
                        var constant = GenerateConstant(define);
                        AttachComments(define.Comments, constant);
                        _csharpContext.AddConstant(constant);
                        KnownDefines[define.Name] = define.Content?? "";
                    }
                }
                else if (group.Key == 1)
                {
                    foreach (var define in group)
                    {
                        if (!EvalConditionals(define.Conditionals))
                        {
                            _skippedItems.Add($"constant {define.Name}");
                            continue;
                        }
                        
                        var constant = GenerateConstant(define);
                        AttachComments(define.Comments, constant);
                        _csharpContext.AddConstant(constant);
                        KnownDefines[define.Name] = define.Content?? "";
                    }
                }
                else
                {
                    // Dictionary<string, string> newDefines = new();
                    foreach (var define in group)
                    {
                        if (!EvalConditionals(define.Conditionals))
                        {
                            _skippedItems.Add($"constant {define.Name}");
                            continue;
                        }
                        
                        var constant = GenerateConstant(define);
                        AttachComments(define.Comments, constant);
                        _csharpContext.AddConstant(constant);
                        // newDefines[define.Name] = define.Content?? "";
                        KnownDefines[define.Name] = define.Content?? "";
                    }

                    // foreach (var key in newDefines.Keys)
                    // {
                    //     KnownDefines[key] = newDefines[key];
                    // }
                }
            }

            return;

            CSharpConstant GenerateConstant(DefineItem defineItem)
            {
                if (string.IsNullOrEmpty(defineItem.Content))
                {
                    return new CSharpConstant(defineItem.Name, new CSharpType("bool"), "true");
                }
                else if (KnownDefines.ContainsKey(defineItem.Content))
                {
                    var contentConstant = _csharpContext.Constants.FirstOrDefault(c => c.Name == defineItem.Content, null);
                    var type = contentConstant?.Type ?? new CSharpType("bool");
                    return new CSharpConstant(defineItem.Name, type, defineItem.Content);
                }
                else if (defineItem.Content.StartsWith("0x") &&
                         long.TryParse(defineItem.Content.AsSpan(2), NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo, out _) ||
                         long.TryParse(defineItem.Content, out _))
                {
                    return new CSharpConstant(defineItem.Name, new CSharpType("long"), defineItem.Content);
                }
                else if (defineItem.Content.StartsWith('\"') && defineItem.Content.EndsWith('\"'))
                {
                    return new CSharpConstant(defineItem.Name, new CSharpType("string"), defineItem.Content);
                }
                else
                {
                    return new CSharpConstant(defineItem.Name, new CSharpType("bool"), "true");
                }
            }
        }

        private void GenerateTypedefs(IReadOnlyList<TypedefItem> typedefs)
        {
            foreach (var nativeTypedef in typedefs)
            {
                if (nativeTypedef.Type.Description.Kind == "Type")
                {
                    var innerType = nativeTypedef.Type.Description.InnerType;
                    var name = nativeTypedef.Type.Description.Name;

                    if (innerType.Kind == "Pointer" && innerType.InnerType!.Kind == "Function")
                    {
                        innerType = innerType.InnerType;
                        var csharpDelegate = GenerateDelegateFromDescription(innerType, name);
                        AttachComments(nativeTypedef.Comments, csharpDelegate);
                        _csharpContext.AddDelegate(csharpDelegate);
                        continue;
                    }
                }

                if (nativeTypedef.Name.Contains("flags", StringComparison.OrdinalIgnoreCase))
                    continue;
                
                var csharpType = GetCSharpType(nativeTypedef.Type.Description);
                _csharpContext.AddType(nativeTypedef.Name, csharpType);
            }
        }

        private void GenerateEnums(IReadOnlyList<EnumItem> enums)
        {
            foreach (var nativeEnum in enums)
            {
                if (!EvalConditionals(nativeEnum.Conditionals))
                {
                    _skippedItems.Add($"enum {nativeEnum.Name}");
                    continue;
                }
                
                // Setup C# enum
                var csharpEnum = new CSharpEnum(nativeEnum.Name);

                if (nativeEnum.IsFlagsEnum)
                    csharpEnum.Attributes.Add("[Flags]");
                
                AttachComments(nativeEnum.Comments, csharpEnum);
                
                // Add enum items
                foreach (var nativeElement in nativeEnum.Elements)
                {
                    if (!EvalConditionals(nativeElement.Conditionals))
                    {
                        _skippedItems.Add($"enum element {nativeElement.Name}");
                        continue;
                    }

                    var elementValue = (csharpEnum.IsFlags
                        ? nativeElement.ValueExpression
                        : nativeElement.Value.ToString(CultureInfo.InvariantCulture))!;
                    var csharpElement = new CSharpEnumElement(nativeElement.Name, elementValue);
                    
                    AttachComments(nativeElement.Comments, csharpElement);

                    if (csharpElement.Name.Contains("COUNT", StringComparison.Ordinal))
                    {
                        ArraySizes[csharpElement.Name] = int.Parse(csharpElement.Value);
                    }
                    
                    csharpEnum.Elements.Add(csharpElement);
                }
                _csharpContext.AddEnum(csharpEnum);
            }
        }

        private void GenerateStructs(IReadOnlyList<StructItem> structs)
        {
            for (int i = 0; i < structs.Count; i++)
            {
                var nativeStruct = structs[i];
                
                var csharpStruct = new CSharpStruct(nativeStruct.Name);
                csharpStruct.Modifiers.Add("public");
                
                AttachComments(nativeStruct.Comments, csharpStruct);

                for (int j = 0; j < nativeStruct.Fields.Count; j++)
                {
                    var nativeField = nativeStruct.Fields[j];
                    
                    if (!EvalConditionals(nativeField.Conditionals))
                    {
                        _skippedItems.Add($"struct field {nativeField.Name}");
                        continue;
                    }
                    
                    var nativeType = nativeField.Type.Description;
                    var csharpType = GetCSharpType(nativeType);

                    if (nativeField.IsArray)
                    {
                        var arrayType = GetCSharpType(nativeType.InnerType!);//field.Type.Description.InnerType!.Name;
                        var arraySize = ArraySizes.GetValueOrDefault(nativeField.ArrayBounds, 0);
                        var elementName = nativeField.Name;
                
                        for (int k = 0; k < arraySize; k++)
                        {
                            csharpStruct.Definitions.Add(new CSharpField($"{elementName}_{k}", arrayType)
                            {
                                Modifiers = arrayType.IsPointer ? ["public", "unsafe"] : ["public"]
                            });
                        }
                        continue;
                    }

                    if (nativeType.Kind == "Type")
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

                            _csharpContext.AddDelegate(cSharpDelegate);
                        }
                        else
                        {
                            Console.WriteLine($"Unknown Type field {nativeField.Name} of {nativeStruct.Name}");
                        }
                    }

                    var csharpField = new CSharpField(nativeField.Name, csharpType);
                    
                    csharpField.Modifiers.Add("public");
                    if (csharpType.IsPointer)
                        csharpField.Modifiers.Add("unsafe");
                    
                    AttachComments(nativeField.Comments, csharpField);
                    csharpStruct.Definitions.Add(csharpField);
                }
                
                var structFile = new CSharpFile($"Structs/{csharpStruct.Name}.gen.cs", _currentConfig.Namespace, ["System", "System.Numerics", "System.Runtime.CompilerServices", "System.Text"]);
                _csharpContext.AddFile(structFile);
                structFile.Definitions.Add(csharpStruct);
                csharpStruct.File = structFile;
                _csharpContext.AddStruct(csharpStruct);
            }
        }

        private void GenerateFunctions(IReadOnlyList<FunctionItem> functions)
        {
            foreach (var nativeFunction in functions)
            {
                if (!EvalConditionals(nativeFunction.Conditionals))
                {
                    _skippedItems.Add($"function {nativeFunction.Name}");
                    continue;
                }
                
                var functionName = nativeFunction.Name;

                if (nativeFunction.Arguments.Count > 0 && nativeFunction.Arguments[^1].Type?.Declaration == "va_list")
                    continue;
                
                var csharpReturnType = GetCSharpType(nativeFunction.ReturnType!.Description);
                var csharpMethod = new CSharpMethod(functionName, csharpReturnType);
                AttachComments(nativeFunction.Comments, csharpMethod);
                
                csharpMethod.Attributes.Add($"[DllImport(\"dcimgui\", EntryPoint = \"{nativeFunction.Name}\", CallingConvention = CallingConvention.Cdecl)]");
                csharpMethod.Modifiers.Add("public");
                csharpMethod.Modifiers.Add("static");
                csharpMethod.Modifiers.Add("extern");

                foreach (var argument in nativeFunction.Arguments)
                {
                    var argumentName = argument.Name;

                    if (IsKnownCSharpKeyword(argumentName))
                        argumentName = "@" + argumentName;

                    if (argument.Type is null)
                        continue;
                    
                    var argumentTypeDesc = argument.Type.Description;

                    if (argumentTypeDesc.Kind == "Array")
                    {
                        var csharpType = GetCSharpType(argumentTypeDesc.InnerType!);
                        var sharpParameter = new CSharpParameter(argumentName, csharpType);
                        csharpMethod.Parameters.Add(sharpParameter);
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
                            
                            _csharpContext.AddDelegate(csharpDelegate);

                            var delegateType = _csharpContext.GetType(delegateName);

                            csharpMethod.Parameters.Add(new CSharpParameter(argumentName, delegateType));
                        }
                        else
                        {
                            Console.WriteLine($"Unknown Type argument {argumentTypeDesc.Name}");
                        }
                    }
                    else
                    {
                        var csharpType = GetCSharpType(argumentTypeDesc);
                        csharpMethod.Parameters.Add(new CSharpParameter(argumentName, csharpType));
                    }
                }

                var csharpStruct = _csharpContext.Structs.FirstOrDefault(s => s.Name == csharpMethod.Name.Split('_')[0], null);
                if (csharpStruct is not null)
                {
                    _structMethods.TryAdd(csharpStruct, []);
                    _structMethods[csharpStruct].Add(csharpMethod);
                }
                
                _csharpContext.AddMethod(csharpMethod);
            }
            
            return;
            
            bool IsKnownCSharpKeyword(string name)
            {
                return name switch
                {
                    "ref" or "out" or "var" or "in" or "base" => true,
                    _ => false
                };
            }
        }

        private CSharpDelegate GenerateDelegateFromDescription(TypeDescription description, string delegateName)
        {
            var csharpReturnType = GetCSharpType(description.ReturnType!);

            var csharpDelegate = new CSharpDelegate(delegateName, csharpReturnType);
    
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
        
                csharpDelegate.Parameters.Add(new CSharpParameter(argumentName, csharpType));
            }
            
            csharpDelegate.Attributes.Add("[UnmanagedFunctionPointer(CallingConvention.Cdecl)]");
            csharpDelegate.Modifiers.Add("public");
            csharpDelegate.Modifiers.Add("unsafe");
            csharpDelegate.Modifiers.Add("delegate");

            return csharpDelegate;
        }

        private void GenerateStructsPtr()
        {
            foreach (var csharpStruct in _csharpContext.Structs)
            {
                if (csharpStruct.Name.StartsWith("ImVector", StringComparison.Ordinal))
                    continue;
                
                var csharpStructPtr = new CSharpStruct(csharpStruct.Name + "Ptr");
                csharpStructPtr.Modifiers.Add("public");
                csharpStructPtr.Modifiers.Add("unsafe");
                csharpStructPtr.Modifiers.Add("partial");

                var structName = csharpStruct.Name;
                var structNamePtr = structName + "Ptr";
                
                var code = CSharpCode.Begin();
                code.WriteLine($"public {structName}* NativePtr {{ get; }}");
                code.WriteLine($"public {structNamePtr}({structName}* nativePtr) => NativePtr = nativePtr;");
                code.WriteLine($"public {structNamePtr}(IntPtr nativePtr) => NativePtr = ({structName}*)nativePtr;");
                code.WriteLine($"public static implicit operator {structNamePtr}({structName}* nativePtr) => new {structNamePtr}(nativePtr);");
                code.WriteLine($"public static implicit operator {structName}* ({structNamePtr} ptr) => ptr.NativePtr;");
                code.WriteLine($"public static implicit operator {structNamePtr}(IntPtr nativePtr) => new {structNamePtr}(nativePtr);");

                for (var i = 0; i < csharpStruct.Definitions.Count; i++)
                {
                    var definition = csharpStruct.Definitions[i];
                    if (definition is not CSharpField field) continue;

                    var typeString = field.Type.TypeName;
                    if (typeString == "void")
                    {
                        code.WriteLine($"public IntPtr {field.Name} {{ get => (IntPtr)NativePtr->{field.Name}; set => NativePtr->{field.Name} = (void*)value; }}");
                        continue;
                    }

                    if (field.Name.EndsWith("_0", StringComparison.Ordinal))
                    {
                        var currentLength = 0;
                        while (csharpStruct.Definitions[i].Name.EndsWith($"_{currentLength}", StringComparison.Ordinal))
                        {
                            i++;
                            currentLength++;
                        }
                        code.WriteLine($"public RangeAccessor<{typeString}> {field.Name[..^2]} => new RangeAccessor<{typeString}>(&NativePtr->{field.Name}, {currentLength});");
                        continue;
                    }

                    if (typeString.StartsWith("ImVector", StringComparison.Ordinal))
                    {
                        var structType = _csharpContext.Structs.FirstOrDefault(s => s?.Name == typeString, null);
                        if (structType is not null)
                        {
                            var dataFieldType =
                                (CSharpField)structType.Definitions.First(d => d is CSharpField f && f.Name == "Data");
                            if (_csharpContext.Structs.FirstOrDefault(s => s?.Name == dataFieldType.Type.TypeName, null)
                                is not null)
                            {
                                typeString = dataFieldType.Type.TypeName + "Ptr";
                                ((CSharpField)csharpStruct.Definitions[i]).Type = new CSharpType("ImVector");
                                code.WriteLine(
                                    $"public ImPtrVector<{typeString}> {field.Name} => new ImPtrVector<{typeString}>(NativePtr->{field.Name}, Unsafe.SizeOf<{dataFieldType.Type.TypeName}>());");
                                continue;
                            }

                            typeString = dataFieldType.Type.TypeName;
                        }
                        else
                        {
                            typeString = typeString.Split('_')[^1];
                        }

                        ((CSharpField)csharpStruct.Definitions[i]).Type = new CSharpType("ImVector");
                        code.WriteLine(
                            $"public ImVector<{typeString}> {field.Name} => new ImVector<{typeString}>(NativePtr->{field.Name});");
                    }
                    else
                    {
                        if (field.Name == "__anonymous_type1")
                        {
                            //TODO handle anonymous types
                            continue;
                        }

                        var typeString1 = typeString;
                        if (field.Type.IsPointer && _csharpContext.Structs.FirstOrDefault(s => s?.Name == typeString1, null) is not null && !typeString.Contains("Ptr"))
                        {
                            code.WriteLine($"public {typeString1}Ptr {field.Name} => new {typeString1}Ptr(NativePtr->{field.Name});");
                            continue;
                        }
                        code.WriteLine($"public ref {typeString} {field.Name} => ref Unsafe.AsRef<{typeString}>(&NativePtr->{field.Name});");
                    }
                }

                csharpStructPtr.Definitions.Add(CSharpCode.End());
                
                if (_structMethods.TryGetValue(csharpStruct, out var methods))
                {
                    foreach (var method in methods)
                    {
                        var methodName = method.Name.Split('_')[^1];
                        if (methodName.EndsWith("Ex"))
                            methodName = methodName[..^2];

                        // pass parameters names to camelCase
                        var parameters = method.Parameters.Select(p => { p.Name = SnakeToCamelCase(p.Name); return p; }).ToList();
                        var callerClass = _currentConfig.Namespace.Contains("Internal") ? "ImGuiInternalNative" : "ImGuiNative";
                        var body = $"{(method.ReturnType is not { TypeName: "void", IsPointer: false } ? "return " : "")}{callerClass}.{method.Name}(";
                        if (parameters[0].Type.TypeName == csharpStruct.Name)
                        {
                            parameters = parameters[1..];
                            body += "NativePtr";
                            if (parameters.Count > 0) body += ", ";
                        }

                        body += string.Join(", ", parameters.Select(p => p.Name));
                        // foreach (var parameter in parameters) body = body + $", {parameter.Name}";
                        body += ");";
                        var csharpMethod = new CSharpMethod(methodName, method.ReturnType)
                        {
                            Modifiers = ["public"],
                            Parameters = parameters,
                            Body = body,
                            Inline = false,
                            TrailingComment = method.TrailingComment,
                            PrecedingComments = method.PrecedingComments
                        };
                        csharpStructPtr.Definitions.Add(csharpMethod);
                    }
                }
                
                csharpStruct.File!.Definitions.Add(csharpStructPtr);
                csharpStructPtr.File = csharpStruct.File;
            }
            
            return;
            
            CSharpMethod CreateMethod(string name, (string name, bool isPointer) returnType, Dictionary<string, (string name, bool isPointer)> parameters, string body, bool inline = false, List<string>? modifiers = null)
            {
                var returnCsharpType = new CSharpType(returnType.name, returnType.isPointer);
                var csharpMethod = new CSharpMethod(name, returnCsharpType);
                csharpMethod.Body = body;
                csharpMethod.Inline = inline;
                if (modifiers is not null)
                    csharpMethod.Modifiers.AddRange(modifiers);
                
                foreach (var (parameterName, (typeName, isPointer)) in parameters)
                {
                    var csharpType = new CSharpType(typeName, isPointer);
                    var csharpParameter = new CSharpParameter(parameterName, csharpType);
                    csharpMethod.Parameters.Add(csharpParameter);
                }
                
                return csharpMethod;
            }
            
            string SnakeToCamelCase(string str)
            {
                // return str.Split(["_"], StringSplitOptions.RemoveEmptyEntries).Select(s => char.ToUpperInvariant(s[0]) + s.Substring(1, s.Length - 1)).Aggregate(string.Empty, (s1, s2) => s1 + s2);
                return str.Split(["_"], StringSplitOptions.RemoveEmptyEntries)
                    .Select((s, i) => i == 0 ? s.ToLowerInvariant() : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant())
                    .Aggregate(string.Empty, (s1, s2) => s1 + s2);

            }
        }

        private void AttachComments(Comments? comments, CSharpDefinition definition)
        {
            if (comments == null) return;
            
            definition.TrailingComment = comments.Attached;
            if (comments.Preceding != null) 
                definition.PrecedingComments.AddRange(comments.Preceding);
        }
        
        private bool EvalConditionals(List<ConditionalItem>? conditionals)
        {
            if (conditionals is {Count: > 0})
            {
                if (conditionals.Count == 1)
                {
                    var condition = conditionals[0];
                    return (condition.Condition == "ifdef" && KnownDefines.ContainsKey(condition.Expression)) ||
                           (condition.Condition == "ifndef" && !KnownDefines.ContainsKey(condition.Expression)) ||
                           (condition.Condition == "if" && condition.Expression.StartsWith("defined") && !condition.Expression.StartsWith("&&") && 
                            KnownDefines.ContainsKey(condition.Expression.Substring(8, condition.Expression.Length - 8 - 1)));
                }
                else
                {
                    var condition = conditionals[1];
                    return (condition.Condition == "ifdef" && KnownDefines.ContainsKey(condition.Expression)) ||
                           (condition.Condition == "ifndef" && !KnownDefines.ContainsKey(condition.Expression));
                }
            }
            else
            {
                return true;
            }
        }
        
        private CSharpType GetCSharpType(TypeDescription typeDescription)
        {
            const string unknownType = "unknown";
            switch (typeDescription.Kind)
            {
                case "Builtin":
                {
                    var type = typeDescription.BuiltinType!;
                    return _csharpContext.GetType(type);
                }
                case "User":
                {
                    var type = typeDescription.Name!;

                    // try to find the conversion, or fallback to whats actually declared
                    return _csharpContext.GetOrAddType(type);
                }
                case "Pointer":
                {
                    var innerType = typeDescription.InnerType!;
                    var innerTypeDef = GetCSharpType(innerType);

                    return _csharpContext.GetPointerType(innerTypeDef.TypeName);
                }
                case "Type":
                {
                    var innerType = typeDescription.InnerType!;

                    var name = typeDescription.Name;

                    if (innerType.Kind == "Pointer" && innerType.InnerType!.Kind == "Function")
                    {
                        var delegateName = name + "Delegate";
                        // _csharpContext.AddDelegate(new CSharpDelegate(delegateName, GetCSharpType(innerType.InnerType!)));
                        return _csharpContext.GetType(delegateName);
                    }

                    return CSharpType.Unknown;
                }
            }
            
            return CSharpType.Unknown;
        }
    }
}