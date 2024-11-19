using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SharpImGui_Dev.CodeGenerator;

internal class Context
{
    public string Namespace = "SharpImGui";
    public string MainMethodsClass = "ImGui";
    public string NativeClass = "ImGuiNative";

    public readonly HashSet<string> PointerStructs = [];
    public readonly Dictionary<string, List<(string name, string value)>> Enums = new();
    public readonly Dictionary<string, string> FoundedTypes = new();
    public readonly Dictionary<string, int> ArraySizes = new();
    public readonly Dictionary<string, (string? type, string value)> KnownDefines = new()
    {
        ["IMGUI_DISABLE_OBSOLETE_FUNCTIONS"] = (null, ""),
        ["IMGUI_DISABLE_OBSOLETE_KEYIO"] = (null, "")
    };

    public readonly List<(string content, Comments? comments)> Delegates = [];
    
    public string GetCSharpType(TypeDescription typeDescription)
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
                if (PointerStructs.Contains(type))
                    return "IntPtr";
                
                return FoundedTypes.GetValueOrDefault(type, type);
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
    
    public bool GetWrappedType(string nativeType, out string wrappedType)
    {
        if (nativeType.StartsWith("Im") && nativeType.EndsWith('*') && !nativeType.StartsWith("ImVector") && !Enums.ContainsKey(nativeType[..^1]))
        {
            int pointerLevel = nativeType.Length - nativeType.IndexOf('*');
            if (pointerLevel > 1)
            {
                wrappedType = null;
                return false;
            }
            string nonPtrType = nativeType[..^pointerLevel];

            if (TypeInfo.ConversionTypes.ContainsKey(nonPtrType))
            {
                wrappedType = null;
                return false;
            }

            if (nonPtrType.EndsWith("Ptr"))
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
    
    public bool EvalConditionals(List<ConditionalItem>? conditionals)
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

    public Comments? CleanupComments(Comments? comment)
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
}