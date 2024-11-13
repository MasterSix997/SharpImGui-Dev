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
    
    private bool _isFirstDefinition = true;

    public CodeWriter(CSharpFile csharpFile, string outputPath)
    {
        // var dirInfo = new DirectoryInfo(outputPath);
        // if (!dirInfo.Exists)
        //     dirInfo.Create();
        CheckAndCreateDirectory(outputPath, csharpFile.FileName);
        _writer = new StreamWriter(Path.Combine(outputPath, csharpFile.FileName + ".cs"));
        _csharpFile = csharpFile;
    }

    private void CheckAndCreateDirectory(string outputPath, string filePath)
    {
        if (filePath.Contains('/'))
        {
            var lastSlash = filePath.LastIndexOf('/');
            var dirPath = filePath.Substring(0, lastSlash);
            outputPath = Path.Combine(outputPath, dirPath);
        }
        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);
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

    private void WriteLines(string lines)
    {
        WriteLines(lines.Split('\n'));
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
            case CSharpCode csharpCode:
                WriteCode(csharpCode);
                break;
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
        
        _isFirstDefinition = false;
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

    private void WriteCode(CSharpCode code)
    {
        WriteLines(code.Code);
    }

    private void WriteEnum(CSharpEnum csharpEnum)
    {
        if (!_isFirstDefinition)
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

    private void WriteContainer(CSharpContainer csharpContainer, string keyword)
    {
        if (!_isFirstDefinition)
            WriteLine();

        WriteSummaries(csharpContainer);
        WriteAttributes(csharpContainer.Attributes);

        if (csharpContainer.Modifiers.Count > 0)
        {
            StartLine();
            Write(string.Join(" ", csharpContainer.Modifiers) + " ");
        }
        Write($"{keyword} {csharpContainer.Name}");
        EndLine();
        PushBlock();

        _isFirstDefinition = true;
        WriteCSharpDefinitions(csharpContainer.Definitions);
        
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
        if (csharpField.Initializer is not null)
            Write($" {csharpField.Initializer}");
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
        Write(csharpConstant.Type.IsPointer ? "* " : " ");
        Write(csharpConstant.Name);
        Write(" = ");
        Write(csharpConstant.Value);
        Write(";");
        EndLine();
    }
    
    private void WriteMethod(CSharpMethod csharpMethod)
    {
        if (!_isFirstDefinition)
            WriteLine();
        
        WriteSummaries(csharpMethod);
        WriteAttributes(csharpMethod.Attributes);
            
        StartLine();
        if (csharpMethod.Modifiers.Count > 0)
            Write($"{string.Join(" ", csharpMethod.Modifiers)} ");
        Write(csharpMethod.ReturnType.TypeName);
        Write(csharpMethod.ReturnType.IsPointer ? "* " : " ");
        Write(csharpMethod.Name);
        WriteParams(csharpMethod.Parameters);
        if (csharpMethod.Body is not null)
        {
            if (csharpMethod.Inline)
            {
                Write($" => {csharpMethod.Body};");
                EndLine();
            }
            else
            {
                EndLine();
                PushBlock();
                WriteLines(csharpMethod.Body);
                PopBlock();
            }
        }
        else
        {
            Write(";");
            EndLine();
        }
    }

    private void WriteDelegate(CSharpDelegate csharpDelegate)
    {
        WriteSummaries(csharpDelegate);
        WriteAttributes(csharpDelegate.Attributes);

        StartLine();
        if (csharpDelegate.Modifiers.Count > 0)
            Write($"{string.Join(" ", csharpDelegate.Modifiers)} ");
        Write(csharpDelegate.ReturnType.TypeName);
        Write(csharpDelegate.ReturnType.IsPointer ? "* " : " ");
        Write(csharpDelegate.Name);
        WriteParams(csharpDelegate.Parameters);
        Write(";");
        EndLine();
    }

    private void WriteParams(IReadOnlyList<CSharpParameter> parameters)
    {
        Write("(");
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            Write(parameter.Type.TypeName);
            Write(parameter.Type.IsPointer ? "* " : " ");
            if (string.IsNullOrEmpty(parameter.Name))
                Write("arg" + i);
            else
                Write(parameter.Name);
            if (i < parameters.Count - 1)
                Write(", ");
        }
        Write(")");
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