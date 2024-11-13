using System.Collections.Generic;
using System.Text;

namespace SharpImGui_Dev.CodeGenerator.CSharp
{
    public abstract class CSharpDefinition(string name, CSharpKind kind)
    {
        public string Name { get; set; } = name;
        public CSharpKind Kind { get; set; } = kind;

        public List<string> Modifiers { get; set; } = [];
        public List<string> Attributes { get; set; } = [];
        
        public string? TrailingComment { get; set; }
        public List<string> PrecedingComments { get; set; } = [];
        
        public CSharpFile? File { get; set; }
    }
    
    public class CSharpCode(string code) : CSharpDefinition("code", CSharpKind.Code)
    {
        public class CodeBuilder(StringBuilder builder)
        {
            public void Write(string text)
            {
                builder.Append(text);
            }
            
            public void WriteLine(string line)
            {
                builder.AppendLine(line);
            }
            
            public void WriteLine()
            {
                builder.AppendLine();
            }
        }
        
        public string Code { get; set; } = code;
        
        private static readonly StringBuilder Builder = new();

        public static CodeBuilder Begin()
        {
            Builder.Clear();
            return new CodeBuilder(Builder);
        }
        
        public static CSharpCode End()
        {
            return new CSharpCode(Builder.ToString());
        } 
    }

    public class CSharpType(string typeName, bool isPointer = false)
    {
        public string TypeName { get; set; } = typeName;
        public bool IsPointer { get; set; } = isPointer;
        
        public static CSharpType Undefined(string possibleName = "", bool isPointer = false) => new($"undefined_{possibleName}", isPointer);
        
        public static readonly CSharpType Unknown = new("unknown");
    }

    public class CSharpPointerType(CSharpType type) : CSharpType(type.TypeName, true)
    {
        public new string TypeName { get => type.TypeName; set => type.TypeName = value; }
        public new bool IsPointer { get; set; } = true;
    }

    public class CSharpUnresolvedType(string typeName) : CSharpType(typeName)
    {
        public CSharpType? ResolvedType { get; set; }
        public new string TypeName { get => ResolvedType?.TypeName?? base.TypeName; set => base.TypeName = value; }
        public new bool IsPointer { get => ResolvedType?.IsPointer?? base.IsPointer; set => base.IsPointer = value; }
        
        public bool IsResolved => ResolvedType != null;
    }

    public class CSharpDefinitionType(CSharpDefinition definition, bool isPointer = false) : CSharpType(definition.Name, isPointer)
    {
        public new string TypeName { get; set; } = definition.Name;
    }
    
    public class CSharpContainer(string name, CSharpKind kind) : CSharpDefinition(name, kind)
    {
        public List<CSharpDefinition> Definitions { get; set; } = [];
    }
    
    public class CSharpEnum(string name) : CSharpDefinition(name, CSharpKind.Enum)
    {
        public List<CSharpEnumElement> Elements { get; set; } = [];
        public bool IsFlags { get; set; }
    }
    
    public class CSharpEnumElement(string name, string value) : CSharpDefinition(name, CSharpKind.EnumElement)
    {
        public string Value { get; set; } = value;
    }

    public class CSharpField(string name, CSharpType type) : CSharpDefinition(name, CSharpKind.Field)
    {
        public CSharpType Type { get; set; } = type;
        public string? Initializer { get; set; }
    }
    
    public class CSharpConstant(string name, CSharpType type, string value) : CSharpDefinition(name, CSharpKind.Constant)
    {
        public CSharpType Type { get; set; } = type;
        public string Value { get; set; } = value;
    }

    public class CSharpStruct(string name) : CSharpContainer(name, CSharpKind.Struct)
    {
        public bool IsPartial { get; set; } = false;
    }

    public class CSharpClass(string name) : CSharpContainer(name, CSharpKind.Class)
    {
        public bool IsPartial { get; set; } = false;
        public bool IsAbstract { get; set; } = false;
        public bool IsSealed { get; set; } = false;
        public bool IsStatic { get; set; } = false;
    }

    // public class CSharpInterface(string name) : CSharpContainer(name, CSharpKind.Interface);

    public class CSharpMethod(string name, CSharpType returnType) : CSharpDefinition(name, CSharpKind.Method)
    {
        public CSharpType ReturnType { get; set; } = returnType;
        public List<CSharpParameter> Parameters { get; set; } = [];
        public string? Body { get; set; }
        public bool Inline { get; set; } = false;
    }

    public static class CSharpMethodExtensions
    {
        public static CSharpMethod NewMethod() => new("", CSharpType.Unknown);

        public static CSharpMethod WithName(this CSharpMethod method, string name)
        {
            var newMethod = new CSharpMethod(name, method.ReturnType)
            {
                Modifiers = method.Modifiers,
                Attributes = method.Attributes,
                PrecedingComments = method.PrecedingComments,
                TrailingComment = method.TrailingComment
            };
            return newMethod;
        }
        
        public static CSharpMethod WithReturnType(this CSharpMethod method, CSharpType returnType)
        {
            var newMethod = new CSharpMethod(method.Name, returnType)
            {
                Modifiers = method.Modifiers,
                Attributes = method.Attributes,
                PrecedingComments = method.PrecedingComments,
                TrailingComment = method.TrailingComment
            };
            return newMethod;
        }
        
        public static CSharpMethod WithParameter(this CSharpMethod method, CSharpParameter parameter)
        {
            var newMethod = new CSharpMethod(method.Name, method.ReturnType)
            {
                Modifiers = method.Modifiers,
                Attributes = method.Attributes,
                PrecedingComments = method.PrecedingComments,
                TrailingComment = method.TrailingComment,
                Parameters = new List<CSharpParameter>(method.Parameters) { parameter }
            };
            return newMethod;
        }
        
        public static CSharpMethod WithBody(this CSharpMethod method, string body)
        {
            var newMethod = new CSharpMethod(method.Name, method.ReturnType)
            {
                Modifiers = method.Modifiers,
                Attributes = method.Attributes,
                PrecedingComments = method.PrecedingComments,
                TrailingComment = method.TrailingComment,
                Parameters = method.Parameters,
                Body = body
            };
            return newMethod;
        }
    }

    public class CSharpDelegate(string name, CSharpType returnType) : CSharpDefinition(name, CSharpKind.Delegate)
    {
        public CSharpType ReturnType { get; set; } = returnType;
        public List<CSharpParameter> Parameters { get; set; } = [];
    }

    public class CSharpParameter(string name, CSharpType type) : CSharpDefinition(name, CSharpKind.Parameter)
    {
        public CSharpType Type { get; set; } = type;
        public string? DefaultValue { get; set; }
    }
}