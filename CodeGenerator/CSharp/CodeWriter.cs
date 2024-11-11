using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SharpImGui_Dev.CodeGenerator.CSharp;

public class CodeWriter : IDisposable, IAsyncDisposable
{
    private readonly StreamWriter _writer;
    private readonly CSharpFile _csharpFile;
    
    private int _currentIndentLevel;
    private string _currentIndent = "";

    public CodeWriter(CSharpFile csharpFile, string outputPath)
    {
        var dirInfo = new DirectoryInfo(outputPath);
        if (!dirInfo.Exists)
            dirInfo.Create();

        _writer = new StreamWriter(Path.Combine(outputPath, csharpFile.FileName + ".cs"));
        _csharpFile = csharpFile;
    }
    
    public void WriteFile()
    {
        WriteUsings(_csharpFile.Usings);
        WriteLine();
        StartNamespace(_csharpFile.Namespace);
        WriteCSharpDefinitions(_csharpFile.Definitions);
        EndNamespace();
        
        _writer.Flush();
    }
    
    private void Write(string text)
    {
        _writer.Write(text);
    }

    private void WriteLine(string line = "")
    {
        if (line.Length == 0)
        {
            _writer.WriteLine();
        }
        else
        {
            _writer.WriteLine(_currentIndent + line);
        }
    }
    
    private void WriteLines(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            WriteLine(line);
        }
    }
    
    private void StartLine()
    {
        _writer.Write(_currentIndent);
    }
    
    private void EndLine()
    {
        _writer.WriteLine();
    }

    private void PushBlock()
    {
        WriteLine("{");
        _currentIndentLevel++;
        _currentIndent = new string('\t', _currentIndentLevel);
    }

    private void PopBlock()
    {
        _currentIndentLevel--;
        _currentIndent = new string('\t', _currentIndentLevel);
        WriteLine("}");
    }

    private void WriteUsings(IEnumerable<string> usings)
    {
        foreach (var u in usings)
        {
            WriteLine($"using {u};");
        }
    }

    private void StartNamespace(string ns)
    {
        WriteLine($"namespace {ns}");
        PushBlock();
    }
    
    private void EndNamespace()
    {
        PopBlock();
    }

    private void WriteCSharpDefinitions(IEnumerable<CSharpDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            WriteDefinition(definition);
        }
    }
    
    private void WriteDefinition(CSharpDefinition definition)
    {
        switch (definition)
        {
            case CSharpEnum csharpEnum:
                WriteEnum(csharpEnum);
                break;
            case CSharpContainer csharpContainer:
                WriteContainer(csharpContainer, csharpContainer is CSharpStruct ? "struct" : "class");
                break;
            case CSharpField csharpField:
                WriteField(csharpField);
                break;
            case CSharpConstant csharpConstant:
                WriteConstant(csharpConstant);
                break;
            case CSharpMethod csharpMethod:
                WriteMethod(csharpMethod);
                break;
            case CSharpDelegate csharpDelegate:
                WriteDelegate(csharpDelegate);
                break;
            default:
                throw new NotImplementedException();
        }
    }

    private void WriteAttributes(IReadOnlyCollection<string> attributes)
    {
        if (attributes.Count <= 0)
            return;

        foreach (var attribute in attributes)
        {
            WriteLine(attribute);
        }
    }
    
    private IEnumerable<string> GenSummary(string comment)
    {
        yield return "<summary>";
        yield return new System.Xml.Linq.XText(comment).ToString();
        yield return "</summary>";
    }

    private IEnumerable<string> GenSummary(IEnumerable<string> comments)
    {
        yield return "<summary>";
        foreach (var comment in comments)
        {
            yield return $"<para>{new System.Xml.Linq.XText(comment)}</para>";
        }
        yield return "</summary>";
    }

    private void WriteSummaries(CSharpDefinition definition)
    {
        var hasPreceding = definition.PrecedingComments.Count > 0;
        if (hasPreceding)
        {
            WriteLines(GenSummary(definition.PrecedingComments).Select(x => $"/// {x}"));
        }

        if (definition.TrailingComment is not null)
        {
            WriteLines(GenSummary(definition.TrailingComment).Select(x => $"/// {x}"));
        }
    }

    private void WriteEnum(CSharpEnum csharpEnum)
    {
        WriteLine();
        WriteSummaries(csharpEnum);
        WriteAttributes(csharpEnum.Attributes);
        
        WriteLine($"public enum {csharpEnum.Name}");
        PushBlock();

        foreach (var value in csharpEnum.Elements)
        {
            WriteSummaries(value);
            WriteLine($"{value.Name} = {value.Value},");
        }
        
        PopBlock();
    }

    private void WriteContainer(CSharpContainer csharpStruct, string keyword)
    {
        WriteLine();
        WriteSummaries(csharpStruct);
        WriteAttributes(csharpStruct.Attributes);
        
        WriteLine($"{string.Join(" ", csharpStruct.Modifiers)} {keyword} {csharpStruct.Name}");
        PushBlock();

        WriteCSharpDefinitions(csharpStruct.Definitions);
        
        PopBlock();
    }

    private void WriteField(CSharpField csharpField)
    {
        WriteSummaries(csharpField);
        WriteAttributes(csharpField.Attributes);
        
        StartLine();
        if (csharpField.Modifiers.Count > 0)
            Write($"{string.Join(" ", csharpField.Modifiers)} ");
        Write(csharpField.Type.TypeName);
        Write(csharpField.Type.IsPointer ? "* " : " ");
        Write(csharpField.Name);
        Write(";");
        EndLine();
    }
    
    private void WriteConstant(CSharpConstant csharpConstant)
    {
        WriteSummaries(csharpConstant);
        WriteAttributes(csharpConstant.Attributes);

        StartLine();
        Write("public ");
        if (csharpConstant.Modifiers.Count > 0)
            Write($"{string.Join(" ", csharpConstant.Modifiers)} ");
        Write("const ");
        Write(csharpConstant.Type.TypeName);
        if (csharpConstant.Type.IsPointer)
            Write($"* ");
        else
            Write(" ");
        Write(csharpConstant.Name);
        Write(" = ");
        Write(csharpConstant.Value);
        Write(";");
        EndLine();
    }
    
    private void WriteMethod(CSharpMethod csharpMethod)
    {
        WriteSummaries(csharpMethod);
        WriteAttributes(csharpMethod.Attributes);
            
        StartLine();
        if (csharpMethod.Modifiers.Count > 0)
            Write($"{string.Join(" ", csharpMethod.Modifiers)} ");
        Write(csharpMethod.ReturnType.TypeName);
        if (csharpMethod.ReturnType.IsPointer)
            Write($"* ");
        else
            Write(" ");
        Write(csharpMethod.Name);
        Write("(");
        foreach (var parameter in csharpMethod.Parameters)
        {
            Write(parameter.Type.TypeName);
            if (parameter.Type.IsPointer)
                Write($"* ");
            else
                Write(" ");
            Write(parameter.Name);
            if (parameter!= csharpMethod.Parameters.Last())
                Write(", ");
        }
        Write(")");
        Write(";");
        EndLine();
    }

    private void WriteDelegate(CSharpDelegate csharpDelegate)
    {
        WriteSummaries(csharpDelegate);
        WriteAttributes(csharpDelegate.Attributes);

        StartLine();
        if (csharpDelegate.Modifiers.Count > 0)
            Write($"{string.Join(" ", csharpDelegate.Modifiers)} ");
        Write(csharpDelegate.ReturnType.TypeName);
        if (csharpDelegate.ReturnType.IsPointer)
            Write($"* ");
        else
            Write(" ");
        Write(csharpDelegate.Name);
        Write("(");
        foreach (var parameter in csharpDelegate.Parameters)
        {
            Write(parameter.Type.TypeName);
            if (parameter.Type.IsPointer)
                Write($"* ");
            else
                Write(" ");
            Write(parameter.Name);
            if (parameter != csharpDelegate.Parameters.Last())
                Write(", ");
        }

        Write(")");
        Write(";");
        EndLine();
    }

    public void Dispose()
    {
        _writer.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
    }
}