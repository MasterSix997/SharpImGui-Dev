using System.Collections.Generic;

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

    public class CSharpType(string typeName, bool isPointer = false)
    {
        public string TypeName { get; set; } = typeName;
        public bool IsPointer { get; set; } = isPointer;
        
        public static CSharpType Undefined(string possibleName = "", bool isPointer = false) => new($"undefined_{possibleName}", isPointer);
        
        public static readonly CSharpType Unknown = new("unknown");
    }

    public class CSharpPointerType(CSharpType type) : CSharpType(type.TypeName, true)
    {
        private CSharpType _type = type;
        public new string TypeName { get => _type.TypeName; set => _type.TypeName = value; }
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
        public CSharpDefinition? Body { get; set; }
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