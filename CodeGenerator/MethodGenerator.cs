using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpImGui_Dev.CodeGenerator;

internal static class MethodGenerator
{
    private record MethodParameters(
        List<(string type, string name)> ManagedParameters,
        List<string> NativeParameters,
        List<Action> BeforeCall,
        List<Action> AfterCall,
        List<Action> FixedBlocks)
    {
        public MethodParameters Copy() => new(
            [..ManagedParameters], [..NativeParameters], [..BeforeCall], 
            [..AfterCall], [..FixedBlocks]);
    }
    
    public static Context Context;

    private static readonly HashSet<string> _generatedSignatures = [];
    private static readonly Dictionary<string, Comments?> _comments = new();
    
    private static FunctionItem function;
    private static CodeWriter writer;
    private static bool isStatic = false;
    private static string functionName;

    public static void Begin()
    {
        _generatedSignatures.Clear();
        _comments.Clear();
        function = null;
        writer = null;
        isStatic = false;
        functionName = null;
    }
    
    public static void WriteMethodOverload(FunctionItem functionItem, CodeWriter codeWriter, bool isStatic = false)
    {
        function = functionItem;
        writer = codeWriter;
        MethodGenerator.isStatic = isStatic;
        
        functionName = function.Name.Split('_')[^1];

        if (functionName.EndsWith("Ex"))
        {
            functionName = functionName[..^2];
            if (_comments.TryGetValue(functionName, out var comments))
                function = functionItem with { Comments = comments };
        }
        else
        {
            _comments[functionName] = function.Comments;
        }
        
        WriteMethod(GetReturnType(), GetParameters());
    }

    private static MethodParameters GetParameters()
    {
        var paramInfo = new MethodParameters([], [], [], [], []);
            
        foreach (var argument in function.Arguments)
        {
            if (argument.Type is null)
                continue;
                
            PrecessArgument(argument, paramInfo);
        }
            
        return new MethodParameters(paramInfo.ManagedParameters, paramInfo.NativeParameters, paramInfo.BeforeCall, paramInfo.AfterCall, paramInfo.FixedBlocks);
    }

    private static void PrecessArgument(FunctionArgument argument, MethodParameters paramInfo)
    {
        var argumentName = argument.Name;
        
        var argumentTypeDesc = argument.Type!.Description;
        var isOut = argumentName.StartsWith("out_");

        if (argumentName == "self")
        {
            paramInfo.NativeParameters.Add("NativePtr");
            return;
        }

        if (argument.DefaultValue != null)
            GenerateOverload(function.Arguments[new Range(function.Arguments.IndexOf(argument), function.Arguments.Count)], paramInfo.Copy());
        
        if (TypeInfo.CSharpIdentifiers.TryGetValue(argumentName, out var csharpName))
            argument = argument with { Name = csharpName };

        switch (argumentTypeDesc.Kind)
        {
            case "Array":
                HandleArrayArgument(argument, argumentTypeDesc, paramInfo);
                break;
            case "Type":
                HandleDelegateArgument(argument, paramInfo);
                break;
            case "Pointer":
                HandlePointerArgument(argument, argumentTypeDesc, paramInfo);
                break;
            default:
                HandleDefaultArgument(argument, argumentTypeDesc, paramInfo);
                break;
        }
    }

    #region Argument Handlers
    private static void HandleDefaultArgument(FunctionArgument argument, TypeDescription argumentTypeDesc, MethodParameters paramInfo)
    {
        var argumentName = argument.Name;
        var argumentType = Context.GetCSharpType(argumentTypeDesc);
        
        if (argumentTypeDesc.BuiltinType == "bool")
        {
            paramInfo.ManagedParameters.Add(("bool", argumentName));
            paramInfo.NativeParameters.Add($"native_{argumentName}");
            paramInfo.BeforeCall.Add(() =>
            {
                writer.WriteLine($"// Marshaling '{argumentName}' to native bool");
                writer.WriteLine($"var native_{argumentName} = {argumentName} ? (byte)1 : (byte)0;");
            });
            return;
        }
        
        if (argumentType.Contains("ImVector_") && argumentType.EndsWith('*'))
        {
            argumentType = "ImVector*";
        }
        else if (argumentType.Contains('*') && !argumentType.Contains("ImVector"))
        {
            if (Context.GetWrappedType(argumentType, out var wrappedType))
            {
                argumentType = wrappedType;
            }
        }
                    
        paramInfo.ManagedParameters.Add((argumentType, argumentName));
        paramInfo.NativeParameters.Add(argumentName);
    }

    private static void HandleArrayArgument(FunctionArgument argument, TypeDescription argumentTypeDesc, MethodParameters paramInfo)
    {
        var argumentName = argument.Name;
        var innerTypeDesc = argumentTypeDesc.InnerType!;
        var paramType = Context.GetCSharpType(innerTypeDesc);
                
        if (innerTypeDesc.Kind == "Pointer")
        {
            // String
            if (innerTypeDesc.InnerType!.BuiltinType == "char")
            {
                paramType = "string";
                GenerateStringArray(paramInfo, argumentName);
            }
            else
                throw new InvalidDataException($"Unsupported array of pointers: {innerTypeDesc.Name}");
        }
        else
        {
            // Vector2, Vector3, Vector4
            if (innerTypeDesc is { Kind: "Builtin", BuiltinType: "float" } && int.Parse(argumentTypeDesc.Bounds!) is > 1 and <= 4)
            {
                GenerateVectorX(argument, argumentTypeDesc, paramInfo);
                return;
            }
            
            GenerateNativeArray(paramInfo, argumentName, paramType);
        }
        
        paramInfo.ManagedParameters.Add(($"{paramType}[]", argumentName));
        paramInfo.NativeParameters.Add($"native_{argumentName}");
    }

    private static void GenerateStringArray(MethodParameters paramInfo, string argumentName)
    {
        paramInfo.BeforeCall.Add(() =>
        {
            writer.WriteLine($"// Marshaling '{argumentName}' to native string array");
            var nativeName = $"native_{argumentName}";
            writer.WriteLine($"var {argumentName}_byteCounts = stackalloc int[{argumentName}.Length];");
            writer.WriteLine($"var {argumentName}_byteCount = 0;");
            writer.WriteLine($"for (var i = 0; i < {argumentName}.Length; i++)");
            writer.PushBlock();
            writer.WriteLine($"{argumentName}_byteCounts[i] = Encoding.UTF8.GetByteCount({argumentName}[i]);");
            writer.WriteLine($"{argumentName}_byteCount += {argumentName}_byteCounts[i] + 1;");
            writer.PopBlock();
                    
            writer.WriteLine($"var {nativeName}_data = stackalloc byte[{argumentName}_byteCount];");
            writer.WriteLine($"var {argumentName}_offset = 0;");
            writer.WriteLine($"for (var i = 0; i < {argumentName}.Length; i++)");
            writer.PushBlock();
            writer.WriteLine($"var s = {argumentName}[i];");
            writer.WriteLine($"{argumentName}_offset += Util.GetUtf8(s, {nativeName}_data + {argumentName}_offset, {argumentName}_byteCounts[i]);");
            writer.WriteLine($"{nativeName}_data[{argumentName}_offset++] = 0;");
            writer.PopBlock();
                    
            writer.WriteLine($"var {nativeName} = stackalloc byte*[{argumentName}.Length];");
            writer.WriteLine($"{argumentName}_offset = 0;");
            writer.WriteLine($"for (var i = 0; i < {argumentName}.Length; i++)");
            writer.PushBlock();
            writer.WriteLine($"{nativeName}[i] = &{nativeName}_data[{argumentName}_offset];");
            writer.WriteLine($"{argumentName}_offset += {argumentName}_byteCounts[i] + 1;");
            writer.PopBlock();
        });
    }

    private static void GenerateNativeArray(MethodParameters paramInfo, string argumentName, string paramType)
    {
        paramInfo.BeforeCall.Add(() =>
        {
            writer.WriteLine($"// Marshaling '{argumentName}' to native {paramType} array");
            writer.WriteLine($"var native_{argumentName} = stackalloc {paramType}[{argumentName}.Length];");
            writer.WriteLine($"for (var i = 0; i < {argumentName}.Length; i++)");
            writer.PushBlock();
            writer.WriteLine($"native_{argumentName}[i] = {argumentName}[i];");
            writer.PopBlock();
        });
    }

    private static void GenerateVectorX(FunctionArgument argument, TypeDescription argumentTypeDesc, MethodParameters paramInfo)
    {
        var vector = $"Vector{argumentTypeDesc.Bounds}";
        argumentTypeDesc = argumentTypeDesc with
        {
            Kind = "Pointer", 
            InnerType = new TypeDescription("Builtin", vector, vector, null, null, null, null, null)
        };
        HandlePointerArgument(argument, argumentTypeDesc, paramInfo);
    }
    
    private static void HandleDelegateArgument(FunctionArgument argument, MethodParameters paramInfo)
    {
        var argumentName = argument.Name;
        var delegateName = function.Name + argumentName + "Delegate";
        paramInfo.ManagedParameters.Add((delegateName, argumentName));
        paramInfo.NativeParameters.Add(argumentName);
    }

    private static void HandlePointerArgument(FunctionArgument argument, TypeDescription argumentTypeDesc, MethodParameters paramInfo)
    {
        var argumentName = argument.Name;
        var innerTypeDesc = argumentTypeDesc.InnerType!;

        if (innerTypeDesc.Kind == "Pointer")
        {
            
        }
        else if (innerTypeDesc.Kind != "Builtin")
        {
            HandleDefaultArgument(argument, argumentTypeDesc, paramInfo);
            return;
        }
        
        switch (innerTypeDesc.BuiltinType)
        {
            case "void":
                GenerateVoidPointer(paramInfo, argumentName);
                return;
            case "bool":
                GenerateBoolPointer(paramInfo, argumentName);
                return;
            case "char":
                if (innerTypeDesc.StorageClasses is not null && innerTypeDesc.StorageClasses.Contains("const"))
                    GenerateStringPointer(paramInfo, argumentName);
                else
                    GenerateStringBuffer(paramInfo, argumentName);
                return;
            default:
                GenerateFixedPointer(argument, innerTypeDesc, paramInfo);
                return;
        }
    }

    private static void GenerateVoidPointer(MethodParameters paramInfo, string argumentName)
    {
        paramInfo.ManagedParameters.Add(("IntPtr", argumentName));
        paramInfo.NativeParameters.Add($"native_{argumentName}");
        paramInfo.BeforeCall.Add(() =>
        {
            writer.WriteLine($"// Marshaling '{argumentName}' to native void pointer");
            writer.WriteLine($"var native_{argumentName} = {argumentName}.ToPointer();");
        });
    }

    private static void GenerateBoolPointer(MethodParameters paramInfo, string argumentName)
    {
        paramInfo.ManagedParameters.Add(("ref bool", argumentName));
        paramInfo.NativeParameters.Add($"native_{argumentName}");
        paramInfo.BeforeCall.Add(() =>
        {
            writer.WriteLine($"// Marshaling '{argumentName}' to native bool");
            writer.WriteLine($"var native_{argumentName}_val = {argumentName} ? (byte)1 : (byte)0;");
            writer.WriteLine($"var native_{argumentName} = &native_{argumentName}_val;");
        });
        paramInfo.AfterCall.Add(() =>
        {
            writer.WriteLine($"{argumentName} = native_{argumentName}_val != 0;");
        });
    }

    private static void GenerateStringPointer(MethodParameters paramInfo, string argumentName)
    {
        paramInfo.ManagedParameters.Add(("ReadOnlySpan<char>", argumentName));
        paramInfo.NativeParameters.Add($"native_{argumentName}");
        paramInfo.BeforeCall.Add(() =>
        {
            writer.WriteLine($"// Marshaling '{argumentName}' to native string");
            var nativeName = $"native_{argumentName}";
            writer.WriteLine($"byte* {nativeName};");
            writer.WriteLine($"var {argumentName}_byteCount = 0;");
            writer.WriteLine($"if ({argumentName} != null)");
            writer.PushBlock();
            writer.WriteLine(
                $"{argumentName}_byteCount = Encoding.UTF8.GetByteCount({argumentName});");
            writer.WriteLine($"if ({argumentName}_byteCount > Util.StackAllocationSizeLimit)");
            writer.PushBlock();
            writer.WriteLine($"{nativeName} = Util.Allocate({argumentName}_byteCount + 1);");
            writer.PopBlock();
            writer.WriteLine("else");
            writer.PushBlock();
            writer.WriteLine(
                $"var {nativeName}_stackBytes = stackalloc byte[{argumentName}_byteCount + 1];");
            writer.WriteLine($"{nativeName} = {nativeName}_stackBytes;");
            writer.PopBlock();
            writer.WriteLine(
                $"var {argumentName}_offset = Util.GetUtf8({argumentName}, {nativeName}, {argumentName}_byteCount);");
            writer.WriteLine($"{nativeName}[{argumentName}_offset] = 0;");
            writer.PopBlock();
            writer.WriteLine($"else {nativeName} = null;");
        });
        
        paramInfo.AfterCall.Add(() =>
        {
            writer.WriteLine($"if ({argumentName}_byteCount > Util.StackAllocationSizeLimit)");
            writer.PushBlock();
            writer.WriteLine($"Util.Free(native_{argumentName});");
            writer.PopBlock();
        });
    }

    private static void GenerateStringBuffer(MethodParameters paramInfo, string argumentName)
    {
        paramInfo.ManagedParameters.Add(("byte[]", argumentName));
        paramInfo.NativeParameters.Add($"native_{argumentName}");
        paramInfo.FixedBlocks.Add(() =>
        {
            writer.WriteLine($"fixed (byte* native_{argumentName} = {argumentName})");
        });
    }
    
    private static void GenerateFixedPointer(FunctionArgument argument, TypeDescription fixedTypeDesc, MethodParameters paramInfo)
    {
        var argumentName = argument.Name;
        var argumentType = Context.GetCSharpType(fixedTypeDesc);
            
        var modifierType = argumentName.StartsWith("out_") ? "out" : "ref";
        
        paramInfo.ManagedParameters.Add(($"{modifierType} {argumentType}", argumentName));
            
        paramInfo.NativeParameters.Add($"native_{argumentName}");
        paramInfo.FixedBlocks.Add(() =>
        {
            writer.WriteLine($"fixed ({argumentType}* native_{argumentName} = &{argumentName})");
        });
    }
    
    #endregion
    
    private static void GenerateOverload(IEnumerable<FunctionArgument> arguments, MethodParameters parameters)
    {
        foreach (var argument in arguments)
        {
            var argumentName = argument.Name;
            if (TypeInfo.CSharpIdentifiers.TryGetValue(argumentName, out var csharpIdentifier))
                argumentName = csharpIdentifier;
                
            var argumentTypeDesc = argument.Type!.Description;
            var argumentType = Context.GetCSharpType(argumentTypeDesc);
                
            var defaultValue = TypeInfo.DefaultValues!.GetValueOrDefault(argument.DefaultValue, argument.DefaultValue)!;

            if (defaultValue == "false")
                defaultValue = "0";
            else if (defaultValue == "true")
                defaultValue = "1";
                
            if (argumentTypeDesc.Kind == "Pointer" && defaultValue != "null")
            {
                // String
                if (argumentTypeDesc.InnerType!.BuiltinType == "char")
                {
                    parameters.NativeParameters.Add(argumentName);
                    var stringValue = defaultValue;
                    parameters.BeforeCall.Add(() =>
                    {
                        writer.WriteLine($"{argumentType} {argumentName};");
                        writer.WriteLine($"var {argumentName}_byteCount = Encoding.UTF8.GetByteCount({stringValue});");
                        writer.WriteLine($"if ({argumentName}_byteCount > Util.StackAllocationSizeLimit)");
                        writer.WriteLine($"\t{argumentName} = Util.Allocate({argumentName}_byteCount + 1);");
                        writer.WriteLine("else");
                        writer.PushBlock();
                        writer.WriteLine($"var {argumentName}_stackBytes = stackalloc byte[{argumentName}_byteCount + 1];");
                        writer.WriteLine($"{argumentName} = {argumentName}_stackBytes;");
                        writer.PopBlock();
                        writer.WriteLine($"var {argumentName}_offset = Util.GetUtf8({stringValue}, {argumentName}, {argumentName}_byteCount);");
                        writer.WriteLine($"{argumentName}[{argumentName}_offset] = 0;");
                    });
                    parameters.AfterCall.Add(() =>
                    {
                        writer.WriteLine($"if ({argumentName}_byteCount > Util.StackAllocationSizeLimit)");
                        writer.WriteLine($"\tUtil.Free({argumentName});");
                    });
                    continue;
                }
            }
                
            if (argumentType == "IntPtr")
                defaultValue = "IntPtr.Zero";
            else if (Context.Enums.ContainsKey(argumentType))
                defaultValue = $"({argumentType}){defaultValue}";
                
            parameters.NativeParameters.Add(argumentName);
            parameters.BeforeCall.Add(() =>
            {
                writer.WriteLine($"{argumentType} {argumentName} = {defaultValue};");
            });
        }

        WriteMethod(GetReturnType(), parameters);
    }

    private static (string safeReturnType, string returnCode) GetReturnType()
    {
        var returnCode = "ret";
        var csharpReturnType = Context.GetCSharpType(function.ReturnType!.Description);
        var isWrappedType = Context.GetWrappedType(csharpReturnType, out var safeReturnType);
        if (!isWrappedType)
            safeReturnType = csharpReturnType;

        if (csharpReturnType != "void")
        {
            if (function.ReturnType!.Description.BuiltinType == "bool")
            {
                returnCode = $"ret != 0";
                safeReturnType = "bool";
            }
        }

        return (safeReturnType, returnCode);
    }

    private static void WriteMethod((string safeReturnType, string returnCode) methodReturn, MethodParameters parameters, string? functionCall = null)
    {
        var overloadSignature = $"{functionName}({string.Join(", ", parameters.ManagedParameters.Select(p => p.type + " " + p.name))})";
        if (!_generatedSignatures.Add(overloadSignature))
            return;

        writer.WriteCommentary(Context.CleanupComments(function.Comments));
        writer.WriteLine($"public{(isStatic ? " static" : "")} {methodReturn.safeReturnType} {overloadSignature}");
        writer.PushBlock();

        foreach (var action in parameters.BeforeCall)
        {
            action();
            writer.WriteLine();
        }
        
        if (parameters.FixedBlocks.Count > 0)
        {
            foreach (var action in parameters.FixedBlocks)
                action();
            writer.PushBlock();
        }

        functionCall ??= $"{Context.NativeClass}.{function.Name}";
            
        writer.WriteLine($"{(methodReturn.safeReturnType == "void" ? "" : $"var ret = ")}{functionCall}({string.Join(", ", parameters.NativeParameters)});");

        foreach (var action in parameters.AfterCall)
            action();
        
        if (methodReturn.safeReturnType != "void")
            writer.WriteLine($"return {methodReturn.returnCode};");

        if (parameters.FixedBlocks.Count > 0)
            writer.PopBlock();
        
        writer.PopBlock();
    }
}