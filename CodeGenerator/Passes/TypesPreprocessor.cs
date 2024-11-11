using System.Collections.Generic;
using System.Linq;
using SharpImGui_Dev.CodeGenerator.CSharp;

namespace SharpImGui_Dev.CodeGenerator.Passes;

public class TypesPreprocessor : IDefinitionPreprocess
{
    public void Preprocess(CSharpContext context)
    {
        var emptyStructs = GetEmptyStructs(context).ToList();
        foreach (var emptyStruct in emptyStructs)
        {
            context.RemoveDefinition(emptyStruct);
            if (context.TypeMap.TryGetValue(emptyStruct.Name, out var structType))
            {
                structType.TypeName = "IntPtr";
                UpdateTypeReferences(context, emptyStruct.Name, structType);
            }
        }
    }
    
    private void UpdateTypeReferences(CSharpContext context, string oldTypeName, CSharpType newType)
    {
        foreach (var sharpStruct in context.Structs)
        {
            foreach (var field in sharpStruct.Definitions.OfType<CSharpField>())
            {
                if (field.Type.TypeName == oldTypeName)
                {
                    field.Type = newType;
                }
            }
        }

        foreach (var method in context.Definitions.OfType<CSharpMethod>())
        {
            if (method.ReturnType.TypeName == oldTypeName)
            {
                method.ReturnType = newType;
            }
        
            foreach (var param in method.Parameters)
            {
                if (param.Type.TypeName == oldTypeName)
                {
                    param.Type = newType;
                }
            }
        }

        foreach (var @delegate in context.Definitions.OfType<CSharpDelegate>())
        {
            if (@delegate.ReturnType.TypeName == oldTypeName)
            {
                @delegate.ReturnType = newType;
            }
        
            foreach (var param in @delegate.Parameters)
            {
                if (param.Type.TypeName == oldTypeName)
                {
                    param.Type = newType;
                }
            }
        }
    }

    private IEnumerable<CSharpStruct> GetEmptyStructs(CSharpContext context)
    {
        return context.Structs.Where(csharpStruct => csharpStruct.Definitions.Count == 0);
    }
}