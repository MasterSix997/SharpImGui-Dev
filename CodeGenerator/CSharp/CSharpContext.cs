using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpImGui_Dev.CodeGenerator.CSharp;

public class CSharpContext
{
    public const string DefaultNamespace = "SharpImGui";
    public const string UndefinedFileName = "Unfiled.gen.cs";
    public const string EnumFileName = "Enums.gen.cs";
    public const string StructFileName = "Structs.gen.cs";
    public const string MethodFileName = "ImGuiNative.gen.cs";
    public const string DelegateFileName = "Delegates.gen.cs";
    public const string ConstantFileName = "Constants.gen.cs";
    
    public const string ImGuiConstClassName = "ImGuiConst";
    public const string ImGuiNativeClassName = "ImGuiNative";
    
    private readonly List<IDefinitionPreprocess> _preprocessors = [];
    
    public List<CSharpFile> Files { get; } = 
    [
        new CSharpFile(EnumFileName, DefaultNamespace, ["System"]),
        new CSharpFile(StructFileName, DefaultNamespace, ["System"]),
        new CSharpFile(MethodFileName, DefaultNamespace, ["System", "System.Runtime.InteropServices"]),
        new CSharpFile(DelegateFileName, DefaultNamespace, ["System", "System.Runtime.InteropServices"]),
        new CSharpFile(ConstantFileName, DefaultNamespace)
    ];
    public List<CSharpEnum> Enums { get; } = [];
    public List<CSharpStruct> Structs { get; } = [];
    public List<CSharpClass> Classes { get; } = 
    [
        new CSharpClass(ImGuiConstClassName) {Modifiers = ["public", "static", "partial"]},
        new CSharpClass(ImGuiNativeClassName) {Modifiers = ["public", "static", "unsafe", "partial"]},
    ];
    public List<CSharpMethod> Methods { get; } = [];
    public List<CSharpDelegate> Delegates { get; } = [];
    public List<CSharpConstant> Constants { get; } = [];
    private List<CSharpType> Types { get; } = [];

    public Dictionary<string, CSharpType> TypeMap { get; } = new()
    {
        ["int"] = new CSharpType("int"),
        ["unsigned int"] = new CSharpType("uint"),
        ["unsigned char"] = new CSharpType("byte"),
        ["unsigned_char"] = new CSharpType("byte"),
        ["unsigned_int"] = new CSharpType("uint"),
        ["unsigned_short"] = new CSharpType("ushort"),
        ["long long"] = new CSharpType("long"),
        ["long_long"] = new CSharpType("long"),
        ["unsigned_long_long"] = new CSharpType("ulong"),
        ["short"] = new CSharpType("short"),
        ["signed char"] = new CSharpType("sbyte"),
        ["signed short"] = new CSharpType("short"),
        ["signed int"] = new CSharpType("int"),
        ["signed long long"] = new CSharpType("long"),
        ["unsigned long long"] = new CSharpType("ulong"),
        ["unsigned short"] = new CSharpType("ushort"),
        ["float"] = new CSharpType("float"),
        ["bool"] = new CSharpType("bool"),
        ["char"] = new CSharpType("char"),
        ["double"] = new CSharpType("double"),
        ["void"] = new CSharpType("void"),
        ["va_list"] = new CSharpType("va_list"),
        ["size_t"] = new CSharpType("ulong"), // assume only x64 for now
    };
    
    private List<CSharpUnresolvedType> _unresolvedTypes = [];
    public List<CSharpPointerType> _pointerTypes = [];
    
    public IEnumerable<CSharpDefinition> Definitions => Enums.Concat<CSharpDefinition>(Structs).Concat(Methods).Concat(Delegates).Concat(Constants);

    public void AddPreprocessor(IDefinitionPreprocess preprocessor)
    {
        _preprocessors.Add(preprocessor);
    }
    
    public CSharpType GetType(string name)
    {
        if (!TryGetType(name, out var type))
        {
            type = new CSharpUnresolvedType(name);
        }
        return type;
    }
    
    public bool TryGetType(string name, out CSharpType type)
    {
        return TypeMap.TryGetValue(name, out type);
    }
    
    public CSharpType GetOrAddType(string name, bool isPointer = false)
    {
        if (TryGetType(name, out var type))
            return type;
        type = new CSharpType(name, isPointer);
        AddType(name, type);
        return type;
    }

    public CSharpPointerType GetPointerType(string name)
    {
        if (_pointerTypes.Any(p => p.TypeName == name))
            return _pointerTypes.First(p => p.TypeName == name);
        
        var pointerType = new CSharpPointerType(GetOrAddType(name));
        _pointerTypes.Add(pointerType);
        return pointerType;
    }
    
    public void AddType(string name, CSharpType type)
    {
        TypeMap.TryAdd(name, type);
    }
    
    public bool HasType(CSharpDefinition definition)
    {
        return HasType(definition.Name);
    }
    
    public bool HasType(string name)
    {
        return TypeMap.ContainsKey(name);
    }

    public void AddDefinitionToFile(CSharpDefinition definition, string filename)
    {
        var file = Files.Find(f => f.FileName == filename);
        if (file == null)
        {
            file = new CSharpFile(filename, DefaultNamespace);
            AddFile(file);
        }
        file.Definitions.Add(definition);
        definition.File = file;
    }

    public void RemoveDefinition(CSharpDefinition definition)
    {
        if (definition.File != null)
            Files.Find(f => f == definition.File)?.Definitions.Remove(definition);
        
        switch (definition)
        {
            case CSharpEnum e:
                Enums.Remove(e);
                break;
            case CSharpStruct s:
                Structs.Remove(s);
                break;
            case CSharpMethod m:
                Methods.Remove(m);
                break;
            case CSharpDelegate d:
                Delegates.Remove(d);
                break;
            case CSharpConstant c:
                Constants.Remove(c);
                break;
        }
    }
    
    public void AddFile(CSharpFile file)
    {
        Files.Add(file);
    }
    
    public void AddEnum(CSharpEnum @enum)
    {
        if (HasType(@enum))
            return;
        
        Enums.Add(@enum);
        AddType(@enum.Name, new CSharpType(@enum.Name));
        AddDefinitionToFile(@enum, EnumFileName);
    }
    
    public void AddStruct(CSharpStruct @struct)
    {
        if (HasType(@struct))
            return;

        Structs.Add(@struct);
        AddType(@struct.Name, new CSharpType(@struct.Name));
        AddDefinitionToFile(@struct, StructFileName);
    }

    public void AddClass(CSharpClass @class)
    {
        if (HasType(@class))
            return;

        Classes.Add(@class);
        AddType(@class.Name, new CSharpType(@class.Name));
    }
    
    public void AddDelegate(CSharpDelegate @delegate)
    {
        if (HasType(@delegate))
            return;

        Delegates.Add(@delegate);
        AddType(@delegate.Name, new CSharpType(@delegate.Name));
    }
    
    public void AddMethod(CSharpMethod csharpMethod)
    {
        Methods.Add(csharpMethod);
        Classes.First(c => c.Name == ImGuiNativeClassName).Definitions.Add(csharpMethod);
    }

    public void AddConstant(CSharpConstant constant)
    {
        if (HasType(constant))
            return;

        Constants.Add(constant);
        AddType(constant.Name, new CSharpType(constant.Name));
        Classes.First(c => c.Name == ImGuiConstClassName).Definitions.Add(constant);
    }
    
    public void WriteAllFiles(string outputDir)
    {
        TryFindUnresolvedTypes();
        
        foreach (var preprocessor in _preprocessors)
        {
            preprocessor.Preprocess(this);
        }
        
        PlaceDefinitionsInFiles();
        foreach (var file in Files.Where(file => file.Definitions.Count > 0))
        {
            using var write = new CodeWriter(file, outputDir);
            write.WriteFile();
        }
    }

    private void TryFindUnresolvedTypes()
    {
        foreach (var unresolvedType in _unresolvedTypes)
        {
            if (TryGetType(unresolvedType.TypeName, out var type))
            {
                unresolvedType.ResolvedType = type;
            }
            else
            {
                Console.WriteLine($"Could not resolve type {unresolvedType.TypeName}");
            }
        }
        _unresolvedTypes.Clear();
    }

    private void PlaceDefinitionsInFiles()
    {
        var constClass = Classes.First(c => c.Name == ImGuiConstClassName);
        var functionClass = Classes.First(c => c.Name == ImGuiNativeClassName);
        
        AddDefinitionToFile(constClass, ConstantFileName);
        AddDefinitionToFile(functionClass, MethodFileName);
        foreach (var @delegate in Delegates)
        {
            AddDefinitionToFile(@delegate, DelegateFileName);
        }
        
        var definitions = new List<CSharpDefinition>();
        definitions.AddRange(Enums.Where(e => e.File == null));
        definitions.AddRange(Structs.Where(s => s.File == null));
        definitions.AddRange(Classes.Where(s => s.File == null));
        
        var defaultFile = new CSharpFile(UndefinedFileName, DefaultNamespace, ["System"], definitions.ToArray());
        AddFile(defaultFile);
    }
}

