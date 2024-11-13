using System.Collections.Generic;
using SharpImGui_Dev.CodeGenerator.CSharp;

namespace SharpImGui_Dev.CodeGenerator.Passes;

public class CommentPreprocessor : IDefinitionPreprocess
{
    public void Preprocess(CSharpContext context)
    {
        // ProcessDefinitions(context.Definitions);
        ProcessFiles(context.Files);
    }
    
    private static void ProcessFiles(List<CSharpFile> files)
    {
        foreach (var file in files)
        {
            ProcessDefinitions(file.Definitions);
        }
    }
    
    private static void ProcessDefinitions(IEnumerable<CSharpDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            switch (definition)
            {
                case CSharpStruct csharpStruct:
                    ProcessContainerDefinition(csharpStruct);
                    break;
                case CSharpClass csharpClass:
                    ProcessContainerDefinition(csharpClass);
                    break;
                case CSharpEnum csharpEnum:
                    ProcessDefinition(csharpEnum);
                    ProcessDefinitions(csharpEnum.Elements);
                    break;
                default:
                    ProcessDefinition(definition);
                    break;
            }
        }
    }
    
    private static void ProcessContainerDefinition(CSharpContainer container)
    {
        ProcessDefinition(container);
        ProcessDefinitions(container.Definitions);
    }

    private static void ProcessDefinition(CSharpDefinition definition)
    {
        if (definition.TrailingComment != null)
        {
            definition.TrailingComment = CleanComment(definition.TrailingComment);
        }

        for (var i = 0; i < definition.PrecedingComments.Count; i++)
        {
            definition.PrecedingComments[i] = CleanComment(definition.PrecedingComments[i]);
        }
    }

    private static string CleanComment(string comment)
    {
        return comment.StartsWith("// ") ? comment[3..] : comment;
    }
}