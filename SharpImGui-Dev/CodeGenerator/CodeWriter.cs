using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpImGui_Dev.CodeGenerator
{
    internal class CodeWriter : IDisposable
    {
        private readonly StreamWriter _writer;
        
        private int _currentIndentLevel;
        private string _currentIndent = "";

        public CodeWriter(string outputPath, string fileName)
        {
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);
            
            _writer = new StreamWriter(Path.Combine(outputPath, fileName));
        }

        public void Write(string text)
        {
            _writer.Write(text);
        }
    
        public void WriteLine(string line = "")
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
        
        public void WriteLines(IEnumerable<string> lines)
        {
            foreach (var line in lines)
            {
                WriteLine(line);
            }
        }
    
        public void WriteLines(string lines)
        {
            WriteLines(lines.Split('\n'));
        }
        
        public void StartLine()
        {
            _writer.Write(_currentIndent);
        }
        
        public void EndLine()
        {
            _writer.WriteLine();
        }
    
        public void PushBlock()
        {
            WriteLine("{");
            _currentIndentLevel++;
            _currentIndent = new string('\t', _currentIndentLevel);
        }
    
        public void PopBlock()
        {
            _currentIndentLevel--;
            _currentIndent = new string('\t', _currentIndentLevel);
            WriteLine("}");
        }

        public void Using(string @namespace)
        {
            WriteLine($"using {@namespace};");
        }
        
        public void StartNamespace(string @namespace)
        {
            WriteLine($"namespace {@namespace}");
            PushBlock();
        }
    
        public void EndNamespace()
        {
            PopBlock();
        }
        
        private IEnumerable<string> GenerateSummary(string comment)
        {
            yield return "<summary>";
            yield return new System.Xml.Linq.XText(comment).ToString();
            yield return "</summary>";
        }

        private IEnumerable<string> GenerateSummary(IEnumerable<string> comments)
        {
            yield return "<summary>";
            foreach (var comment in comments)
            {
                yield return $"<para>{new System.Xml.Linq.XText(comment)}</para>";
            }
            yield return "</summary>";
        }

        public void WriteCommentary(Comments? comment)
        {
            if (comment is null)
                return;
            
            if (comment.Preceding is not null)
            {
                WriteLines(GenerateSummary(comment.Preceding!).Select(x => $"/// {x}"));
            }

            if (comment.Attached is not null)
            {
                WriteLines(GenerateSummary(comment.Attached).Select(x => $"/// {x}"));
            }
        }

        public void Dispose()
        {
            _writer.Dispose();
        }
    }
}
