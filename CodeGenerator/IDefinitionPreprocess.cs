using SharpImGui_Dev.CodeGenerator.CSharp;

namespace SharpImGui_Dev.CodeGenerator;

public interface IDefinitionPreprocess
{
    public void Preprocess(CSharpContext context);
}