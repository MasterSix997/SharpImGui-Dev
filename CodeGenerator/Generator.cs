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
    public class Generator
    {
        private static readonly string ProjectPath = Path.Combine("../../../");
        private static readonly string DearBindingsPath = Path.Combine(ProjectPath, "dcimgui");
        private string _outputPath = Path.Combine(ProjectPath, "../SharpImGui/Generated");
        private string[] _metadataFiles =
        [
            "dcimgui.json",
            // "dcimgui_internal.json"
        ];
        
        private CSharpContext _csharpContext;
        
        private List<string> _skippedItems = [];
        private static Dictionary<string, int> _arraySizes = new();
        private static Dictionary<string, string> knownDefines = new()
        {
            ["IMGUI_DISABLE_OBSOLETE_FUNCTIONS"] = "",
            ["IMGUI_DISABLE_OBSOLETE_KEYIO"] = ""
        };
        
        public void Generate()
        {
            _csharpContext = new CSharpContext();
            _csharpContext.AddPreprocessor(new CommentPreprocessor());
            _csharpContext.AddPreprocessor(new NamingPreprocessor());
            _csharpContext.AddPreprocessor(new TypesPreprocessor());
            
            foreach (string metadataFile in _metadataFiles)
            {
                GenerateBindings(metadataFile);
            }

            if (Directory.Exists(_outputPath))
                Directory.Delete(_outputPath, true);
            _csharpContext.WriteAllFiles(_outputPath);
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
        }

        private void GenerateConstants(IReadOnlyList<DefineItem> defines)
        {
            // dear_bindings writes defines in a strange manner, producing redefines, so when we group them by count, we can produce more accurate result
            var defineGroups = defines.GroupBy(x => x.Conditionals?.Count ?? 0);

            foreach (var key in knownDefines.Keys)
            {
                _csharpContext.AddConstant(new CSharpConstant(key, new CSharpType("string"), $"\"{knownDefines[key].Replace("\"", "\\\"")}\""));
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
                        knownDefines[define.Name] = define.Content?? "";
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
                        knownDefines[define.Name] = define.Content?? "";
                    }
                }
                else
                {
                    Dictionary<string, string> newDefines = new();
                    foreach (var define in group)
                    {
                        if (!EvalConditionals(define.Conditionals))
                        {
                            _skippedItems.Add($"constant {define.Name}");
                            continue;
                        }
                        
                        var constant = GenerateConstant(define);
                        AttachComments(define.Comments, constant);
                        newDefines[define.Name] = define.Content?? "";
                    }

                    foreach (var key in newDefines.Keys)
                    {
                        knownDefines[key] = newDefines[key];
                    }
                }
            }

            return;

            CSharpConstant GenerateConstant(DefineItem defineItem)
            {
                if (string.IsNullOrEmpty(defineItem.Content))
                {
                    return new CSharpConstant(defineItem.Name, new CSharpType("bool"), "true");
                }
                else if (knownDefines.ContainsKey(defineItem.Content))
                {
                    return new CSharpConstant(defineItem.Name, new CSharpType("bool"), defineItem.Content);
                }
                else if (defineItem.Content.StartsWith("0x") &&
                         long.TryParse(
                             defineItem.Content.AsSpan(2),
                             NumberStyles.HexNumber,
                             NumberFormatInfo.InvariantInfo,
                             out _
                         ) ||
                         long.TryParse(defineItem.Content, out _)
                        )
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
                        var csharpDelegate = UnwrapFunctionTypeDescriptionToDelegate(innerType, name);
                        AttachComments(nativeTypedef.Comments, csharpDelegate);
                        _csharpContext.AddDelegate(csharpDelegate);
                        continue;
                    }
                }
                
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
                    csharpEnum.IsFlags = true;
                
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
                        _arraySizes[csharpElement.Name] = int.Parse(csharpElement.Value);
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
                        var arraySize = _arraySizes.GetValueOrDefault(nativeField.ArrayBounds, 0);
                        var elementName = nativeField.Name;
                
                        for (int k = 0; k < arraySize; k++)
                        {
                            csharpStruct.Definitions.Add(new CSharpField($"{elementName}_{k}", arrayType)
                            {
                                Modifiers = {"public"},
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

                            var cSharpDelegate = UnwrapFunctionTypeDescriptionToDelegate(innerType, name + "Delegate");

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
                csharpMethod.Modifiers.Add("unsafe");

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
                            var csharpDelegate = UnwrapFunctionTypeDescriptionToDelegate(innerType, delegateName);
                            
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
                
                _csharpContext.AddMethod(csharpMethod);
            }
            
            return;
            
            bool IsKnownCSharpKeyword(string name)
            {
                return name switch
                {
                    "ref" or "out" or "var" or "in" => true,
                    _ => false
                };
            }
        }

        private CSharpDelegate UnwrapFunctionTypeDescriptionToDelegate(TypeDescription description, string delegateName)
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
                    return (condition.Condition == "ifdef" && knownDefines.ContainsKey(condition.Expression)) ||
                           (condition.Condition == "ifndef" && !knownDefines.ContainsKey(condition.Expression)) ||
                           (condition.Condition == "if" && condition.Expression.StartsWith("defined") && !condition.Expression.StartsWith("&&") && 
                            knownDefines.ContainsKey(condition.Expression.Substring(8, condition.Expression.Length - 8 - 1)));
                }
                else
                {
                    var condition = conditionals[1];
                    return (condition.Condition == "ifdef" && knownDefines.ContainsKey(condition.Expression)) ||
                           (condition.Condition == "ifndef" && !knownDefines.ContainsKey(condition.Expression));
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
                    
                    return new CSharpPointerType(_csharpContext.GetOrAddType(innerTypeDef.TypeName, true));
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