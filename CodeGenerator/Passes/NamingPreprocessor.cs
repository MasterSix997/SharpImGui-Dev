using System.Collections.Generic;
using System.Text;
using SharpImGui_Dev.CodeGenerator.CSharp;

namespace SharpImGui_Dev.CodeGenerator.Passes;

public class NamingPreprocessor : IDefinitionPreprocess
{
    public void Preprocess(CSharpContext context)
    {
        ProcessDefinitions(context.Definitions);
    }
    
    private static void ProcessDefinitions(IEnumerable<CSharpDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            switch (definition)
            {
                case CSharpEnum csharpEnum:
                    ProcessEnum(csharpEnum);
                    break;
                case CSharpMethod csharpMethod:
                    ProcessMethod(csharpMethod);
                    break;
                default:
                    continue;
            }
        }
    }

    private static void ProcessMethod(CSharpMethod csharpMethod)
    {
        if (csharpMethod.Name.StartsWith("ImGui_"))
            csharpMethod.Name = csharpMethod.Name[6..];
    }

    private static void ProcessEnum(CSharpEnum csharpEnum)
    {
        foreach (var enumElement in csharpEnum.Elements)
        {
            CleanupEnumElement(enumElement, csharpEnum.Name);
            csharpEnum.Name = csharpEnum.Name.Replace("_", "");
        }
    }

    private static readonly StringBuilder _sb = new();
    
    private static void CleanupEnumElement(CSharpEnumElement element, string enumNamePrefix)
    {
        element.Name = CleanupEnumValue(element.Name, enumNamePrefix);
        element.Value = CleanupEnumValue(element.Value, enumNamePrefix);
    }

    private static string CleanupEnumValue(string name, string enumNamePrefix)
    {
        _sb.Clear();
        _sb.Append(name);
        _sb.Replace(enumNamePrefix, "");
        if (_sb[0] == '_' && !char.IsNumber(_sb[1]))
            _sb.Replace("_", "");
        
        return _sb.ToString();
    }
}