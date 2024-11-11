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
            }
        }
    }

    private IEnumerable<CSharpStruct> GetEmptyStructs(CSharpContext context)
    {
        return context.Structs.Where(csharpStruct => csharpStruct.Definitions.Count == 0);
    }
}