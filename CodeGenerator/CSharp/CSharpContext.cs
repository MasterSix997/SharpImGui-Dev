using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpImGui_Dev.CodeGenerator.CSharp;

public class CSharpContext
{
    public string Namespace { get; set; }
    private readonly List<IDefinitionPreprocess> _preprocessors = [];

    private Dictionary<Type, CSharpClass> _predefinedClasses = new();
    private Dictionary<Type, CSharpFile> _predefinedFiles = new();
    
    public List<CSharpFile> Files { get; } = [];
    public List<CSharpEnum> Enums { get; } = [];
    public List<CSharpStruct> Structs { get; } = [];
    public List<CSharpClass> Classes { get; } = [];
    public List<CSharpMethod> Methods { get; } = [];
    public List<CSharpDelegate> Delegates { get; } = [];
    public List<CSharpConstant> Constants { get; } = [];

    public Dictionary<string, CSharpType> TypeMap { get; init; } = new();
    
    private readonly List<CSharpUnresolvedType> _unresolvedTypes = [];
    private readonly List<CSharpPointerType> _pointerTypes = [];

    public Dictionary<Type, CSharpClass> PredefinedClasses
    {
        get => _predefinedClasses;
        init
        {
            _predefinedClasses = value;
            foreach (var @class in value.Values)
            {
                AddClass(@class);
            }
        }
    }

    public Dictionary<Type, CSharpFile> PredefinedFiles
    {
        get => _predefinedFiles;
        init
        {
            _predefinedFiles = value;
            foreach (var file in value.Values)
            {
                AddFile(file);
            }
        }
    }
    
    public IEnumerable<CSharpDefinition> Definitions => Enums.Concat<CSharpDefinition>(Structs).Concat(Methods).Concat(Delegates).Concat(Constants);

    public CSharpContext(string @namespace)
    {
        Namespace = @namespace;
    }
    
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
            file = new CSharpFile(filename, Namespace);
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
        // if (HasType(@enum))
        //     return;
        
        Enums.Add(@enum);
        AddType(@enum.Name, new CSharpType(@enum.Name));
        
        if (_predefinedFiles.ContainsKey(@enum.GetType()))
            AddDefinitionToFile(@enum, _predefinedFiles[@enum.GetType()].FileName);
    }
    
    public void AddStruct(CSharpStruct @struct)
    {
        // if (HasType(@struct))
        //     return;

        Structs.Add(@struct);
        AddType(@struct.Name, new CSharpType(@struct.Name));
        
        if (_predefinedFiles.ContainsKey(@struct.GetType()))
            AddDefinitionToFile(@struct, _predefinedFiles[@struct.GetType()].FileName);
    }

    public void AddClass(CSharpClass @class)
    {
        // if (HasType(@class))
        //     return;

        Classes.Add(@class);
        AddType(@class.Name, new CSharpType(@class.Name));
        
        if (_predefinedFiles.ContainsKey(@class.GetType()))
            AddDefinitionToFile(@class, _predefinedFiles[@class.GetType()].FileName);
    }
    
    public void AddDelegate(CSharpDelegate @delegate)
    {
        // if (HasType(@delegate))
        //     return;

        Delegates.Add(@delegate);
        AddType(@delegate.Name, new CSharpType(@delegate.Name));
        
        if (_predefinedFiles.ContainsKey(@delegate.GetType()))
            AddDefinitionToFile(@delegate, _predefinedFiles[@delegate.GetType()].FileName);
    }
    
    public void AddMethod(CSharpMethod csharpMethod)
    {
        Methods.Add(csharpMethod);
        
        if (_predefinedClasses.ContainsKey(csharpMethod.GetType()))
            Classes.First(c => c.Name == _predefinedClasses[csharpMethod.GetType()].Name).Definitions.Add(csharpMethod);
    }

    public void AddConstant(CSharpConstant constant)
    {
        // if (HasType(constant))
        //     return;

        Constants.Add(constant);
        AddType(constant.Name, new CSharpType(constant.Name));

        if (_predefinedClasses.ContainsKey(constant.GetType()))
            Classes.First(c => c.Name == _predefinedClasses[constant.GetType()].Name).Definitions.Add(constant);
    }
    
    public void WriteAllFiles(string outputDir)
    {
        TryFindUnresolvedTypes();
        PlaceDefinitionsInFiles();
        
        foreach (var preprocessor in _preprocessors)
        {
            preprocessor.Preprocess(this);
        }
        
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
        foreach (var (type, @class) in _predefinedClasses)
        {
            if (_predefinedFiles.TryGetValue(type, out var file))
            {
                AddDefinitionToFile(@class, file.FileName);
            }
        }
        
        const string undefinedFileName = "Undefined.cs";
        var definitions = new List<CSharpDefinition>();
        definitions.AddRange(Enums.Where(e => e.File == null));
        definitions.AddRange(Structs.Where(s => s.File == null));
        definitions.AddRange(Classes.Where(s => s.File == null));
        
        var defaultFile = new CSharpFile(undefinedFileName, Namespace, ["System"], definitions.ToArray());
        AddFile(defaultFile);
    }

}

