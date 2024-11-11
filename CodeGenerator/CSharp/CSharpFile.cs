using System.Collections.Generic;

namespace SharpImGui_Dev.CodeGenerator.CSharp;

public class CSharpFile(string fileName, string @namespace)
{
    public string FileName { set; get; } = fileName;
    public string Namespace { set; get; } = @namespace;
    public List<string> Usings { set; get; } = [];
    public List<CSharpDefinition> Definitions { set; get; } = [];

    public CSharpFile(string fileName, string @namespace, IEnumerable<string>? usings = null, IEnumerable<CSharpDefinition>? definitions = null) : this(fileName, @namespace)
    {
        FileName = fileName;
        Namespace = @namespace;
        if (usings is not null)
            Usings = [..usings];
        if (definitions is not null)
            Definitions = [..definitions];
    }
}