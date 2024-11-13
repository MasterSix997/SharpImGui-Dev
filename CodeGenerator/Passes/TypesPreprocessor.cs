using System.Collections.Generic;
using System.Linq;
using SharpImGui_Dev.CodeGenerator.CSharp;

namespace SharpImGui_Dev.CodeGenerator.Passes;

public class TypesPreprocessor : IDefinitionPreprocess
{
    public void Preprocess(CSharpContext context)
    {
        var structsToRemove = new List<CSharpStruct>();
        foreach (var csharpStruct in context.Structs)
        {
            //Empty structs become IntPtr
            if (csharpStruct.Definitions.Count == 0)
            {
                structsToRemove.Add(csharpStruct);
                if (context.TypeMap.TryGetValue(csharpStruct.Name, out var structType))
                {
                    structType.TypeName = "IntPtr";
                    UpdateTypeReferences(context, csharpStruct.Name, structType);
                }
            }
            //Structs with ImVector_T can be merged into a single struct (ImVector)
            else if(csharpStruct.Name.Contains("ImVector"))
            {
                structsToRemove.Add(csharpStruct);
                if (context.TypeMap.TryGetValue(csharpStruct.Name, out var structType))
                {
                    structType.TypeName = "ImVector";
                    UpdateTypeReferences(context, csharpStruct.Name, structType);
                }
            }
        }

        foreach (var csharpStruct in structsToRemove)
        {
            context.Files.Remove(csharpStruct.File!);
            context.RemoveDefinition(csharpStruct);
        }

        foreach (var parameter in context.Methods.SelectMany(csharpMethod => csharpMethod.Parameters.Where(parameter => parameter.Type.TypeName.Contains("ImVector"))))
        {
            parameter.Type.TypeName = "ImVector";
            parameter.Type.IsPointer = true;
        }
    }
    
    private void UpdateTypeReferences(CSharpContext context, string oldTypeName, CSharpType newType)
    {
        foreach (var file in context.Files)
        {
            UpdateTypeReferences(file.Definitions, oldTypeName, newType);
        }
    }

    private void UpdateTypeReferences(IEnumerable<CSharpDefinition> definitions, string oldTypeName, CSharpType newType)
    {
        foreach (var definition in definitions)
        {
            UpdateTypeReferences(definition, oldTypeName, newType);
        }
    }

    private void UpdateTypeReferences(CSharpDefinition definition, string oldTypeName, CSharpType newType)
    {
        switch (definition)
        {
            case CSharpStruct csharpStruct:
                UpdateTypeReferences(csharpStruct.Definitions, oldTypeName, newType);
                break;
            case CSharpClass csharpClass:
                UpdateTypeReferences(csharpClass.Definitions, oldTypeName, newType);
                break;
            case CSharpField csharpField:
                if (csharpField.Type.TypeName == oldTypeName)
                {
                    csharpField.Type = newType;
                }
                break;
            case CSharpMethod csharpMethod:
                UpdateTypeReferences(csharpMethod.Parameters, oldTypeName, newType);
                if (csharpMethod.ReturnType.TypeName == oldTypeName)
                {
                    csharpMethod.ReturnType = newType;
                }
                break;
            case CSharpDelegate csharpDelegate:
                UpdateTypeReferences(csharpDelegate.Parameters, oldTypeName, newType);
                if (csharpDelegate.ReturnType.TypeName == oldTypeName)
                {
                    csharpDelegate.ReturnType = newType;
                }
                break;
            case CSharpParameter csharpParameter:
                if (csharpParameter.Type.TypeName == oldTypeName)
                {
                    csharpParameter.Type = newType;
                }
                break;
            case CSharpCode csharpCode:
                csharpCode.Code = ReplaceType(oldTypeName, newType.TypeName, csharpCode.Code);
                break;
        }

        return;

        string ReplaceType(string oldType, string newType, string code)
        {
            for (int i = 0; i < code.Length; i++)
            {
                if (code[i] == oldType[0])
                {
                    if (code.Length - i >= oldType.Length && code.Substring(i, oldType.Length) == oldType)
                    {
                        var end = i + oldType.Length;
                        while (char.IsLetterOrDigit(code[end]))
                            end++;
                        
                        code = code.Substring(0, i) + newType + code.Substring(end);
                        i += newType.Length - 1;
                    }
                }
            }

            return code;
        }
    }
}