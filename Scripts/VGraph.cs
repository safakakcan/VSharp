namespace VSharp.Scripts;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// --- Core Types ---
public enum VType
{
    Void,
    Int,
    Float,
    Bool,
    String,
    Object,
    GenericT
}

public class VSlot
{
    public string Name { get; set; }
    public VType Type { get; set; }
    public string? GenericGroup { get; set; }

    public VSlot(string name, VType type, string? genericGroup = null)
    {
        Name = name;
        Type = type;
        GenericGroup = genericGroup;
    }

    public override string ToString() => GenericGroup == null ? $"{Name}:{Type}" : $"{Name}:{Type}<{GenericGroup}>";
}

// --- Memory Management ---
public class MemorySlot
{
    public int Index { get; }
    public string? Name { get; set; }
    public Type DataType { get; }

    public MemorySlot(int index, Type dataType)
    {
        Index = index;
        DataType = dataType;
    }

    public override string ToString() => Name ?? $"t{Index}";
}

public class MemoryAllocator
{
    private int _counter = 0;
    private readonly List<MemorySlot> _slots = new();

    public MemorySlot Allocate(Type type, string? nameHint = null)
    {
        var slot = new MemorySlot(_counter, type)
        {
            Name = nameHint != null ? $"{nameHint}{_counter}" : null
        };
        _counter++;
        _slots.Add(slot);
        return slot;
    }

    public IEnumerable<MemorySlot> AllSlots => _slots;
}

// --- Code Generation Context ---
public class CodeGenContext
{
    public HashSet<string> Usings = new() { "System" };
    public MemoryAllocator Memory = new();
    public Dictionary<string, MemorySlot> NodeOutputs = new();
    public DefinitionRegistry Registry { get; set; } = new();
}

// --- Node Hierarchy ---
public abstract class VGraphNode
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public List<VSlot> Inputs { get; set; } = new();
    public List<VSlot> Outputs { get; set; } = new();
    public Dictionary<string, string> ConnectedInputs { get; set; } = new();
    public abstract string GenerateCode(CodeGenContext context);
}

public abstract class DefinitionNode : VGraphNode
{
    public string Name { get; set; } = string.Empty;
}

public abstract class RuntimeNode : VGraphNode
{
    // Inherits GenerateCode
}

// --- Registry ---
public class DefinitionRegistry
{
    private readonly Dictionary<string, DefinitionNode> _definitions = new();
    public void Register(DefinitionNode def) => _definitions[def.Name] = def;
    public T? Get<T>(string name) where T : DefinitionNode
        => _definitions.TryGetValue(name, out var node) ? node as T : null;
    public IEnumerable<DefinitionNode> All => _definitions.Values;
}

// --- Node Connect & Sort ---
public static class NodeConnector
{
    public static void Connect(VGraphNode fromNode, string fromOutputName, VGraphNode toNode, string toInputName)
    {
        var outSlot = fromNode.Outputs.FirstOrDefault(s => s.Name == fromOutputName);
        var inSlot = toNode.Inputs.FirstOrDefault(s => s.Name == toInputName);
        if (outSlot == null || inSlot == null)
            throw new Exception("Slot not found.");
        if (outSlot.Type != inSlot.Type)
            throw new Exception($"Type mismatch: {outSlot.Type} -> {inSlot.Type}");
        toNode.ConnectedInputs[toInputName] = $"{fromNode.Id}.{fromOutputName}";
    }
}

public static class TopologicalSorter
{
    public static List<VGraphNode> Sort(List<VGraphNode> nodes)
    {
        var nodeMap = nodes.ToDictionary(n => n.Id);
        var dependencyMap = nodes.ToDictionary(n => n.Id, n => new HashSet<string>());
        var reverseMap = new Dictionary<string, List<string>>();

        foreach (var node in nodes)
            foreach (var input in node.ConnectedInputs.Values)
            {
                var parts = input.Split('.'); var srcId = parts[0];
                dependencyMap[node.Id].Add(srcId);
                if (!reverseMap.ContainsKey(srcId)) reverseMap[srcId] = new();
                reverseMap[srcId].Add(node.Id);
            }

        var queue = new Queue<string>(dependencyMap.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key));
        var sorted = new List<VGraphNode>();
        while (queue.Count > 0)
        {
            var id = queue.Dequeue(); sorted.Add(nodeMap[id]);
            if (!reverseMap.ContainsKey(id)) continue;
            foreach (var dep in reverseMap[id])
            {
                dependencyMap[dep].Remove(id);
                if (dependencyMap[dep].Count == 0) queue.Enqueue(dep);
            }
        }
        if (sorted.Count != nodes.Count) throw new Exception("Cycle detected in graph.");
        return sorted;
    }
}

// --- Generic Resolver ---
public static class GenericTypeResolver
{
    // Implementation omitted for brevity
    public static void InferGenericTypes(VGraphNode node, VGraphNode fromNode, VSlot fromSlot, string inputName) { }
    public static void Resolve(VGraphNode node, Dictionary<string, VType> resolved) { }
}

// --- Reflection Importer ---
public static class ReflectionImporter
{
    public static void ImportType(Type type, DefinitionRegistry registry)
    {
        if (type.IsGenericTypeDefinition) return;
        var def = new ClassDefinitionNode(type.Name);
        def.Fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(f => (f.Name, ReflectionImporter.ToVType(f.FieldType))).ToList();
        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (m.IsSpecialName) continue;
            var md = new MethodDefinitionNode
            {
                MethodName = m.Name,
                IsStatic = m.IsStatic,
                ReturnType = ReflectionImporter.ToVType(m.ReturnType),
                DeclaringClass = type.Name
            };
            foreach (var p in m.GetParameters())
                md.Parameters.Add(new VSlot(p.Name, ToVType(p.ParameterType)));
            def.Methods.Add(md);
        }
        registry.Register(def);
    }
    public static VType ToVType(Type t)
    {
        return t == typeof(int) ? VType.Int :
               t == typeof(float) ? VType.Float :
               t == typeof(bool) ? VType.Bool :
               t == typeof(string) ? VType.String : VType.Object;
    }
}

// --- Code Builder & Compiler ---
public class CodeBuilder
{
    public string Build(List<VGraphNode> nodes, CodeGenContext context)
    {
        var sb = new StringBuilder();
        foreach (var u in context.Usings) sb.AppendLine($"using {u};");
        // Emit class definitions
        foreach (var def in context.Registry.All.OfType<ClassDefinitionNode>())
        {
            sb.AppendLine(def.GenerateDefinitionCode());
        }
        sb.AppendLine("public static class GeneratedClass { public static object Execute() {");
        foreach (var node in TopologicalSorter.Sort(nodes))
            sb.AppendLine("    " + node.GenerateCode(context));
        sb.AppendLine("}}}");
        return sb.ToString();
    }
}

public class RoslynCompiler
{
    public object? CompileAndExecute(string code, out IEnumerable<Diagnostic> diagnostics)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location));
        var comp = CSharpCompilation.Create("GeneratedAssembly", new[] { tree }, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var ms = new MemoryStream(); var result = comp.Emit(ms);
        diagnostics = result.Diagnostics;
        if (!result.Success) return null;
        ms.Seek(0, SeekOrigin.Begin);
        var asm = Assembly.Load(ms.ToArray());
        var type = asm.GetType("GeneratedClass");
        var method = type?.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static);
        return method?.Invoke(null, null);
    }
}

// --- Sample Node Definitions ---
public class ClassDefinitionNode : DefinitionNode
{
    public List<(string Name, VType Type)> Fields { get; set; } = new();
    public List<MethodDefinitionNode> Methods { get; set; } = new();
    public ClassDefinitionNode(string name)
    {
        Name = name;
    }
    public string GenerateDefinitionCode()
    {
        var fields = Fields.Select(f => $"public {ToCSharp(f.Type)} {f.Name} {{ get; set; }};");
        return $"public class {Name} {{\n    {string.Join("\n    ", fields)}\n}}";
    }
    private string ToCSharp(VType t) => t switch
    {
        VType.Int => "int",
        VType.Float => "float",
        VType.Bool => "bool",
        VType.String => "string",
        _ => "object"
    };
    
    public override string GenerateCode(CodeGenContext context)
    {
        throw new NotImplementedException();
    }
}

public class MethodDefinitionNode : DefinitionNode
{
    public string MethodName;
    public bool IsStatic;
    public VType ReturnType;
    public string DeclaringClass;
    public List<VSlot> Parameters { get; set; } = new();
    
    public override string GenerateCode(CodeGenContext context)
    {
        throw new NotImplementedException();
    }
}

public class VariableNode : RuntimeNode
{
    public object Value;
    public VariableNode(VType type, object value)
    {
        Outputs.Add(new VSlot("Value", type));
        Value = value;
    }
    public override string GenerateCode(CodeGenContext context)
    {
        var slot = context.Memory.Allocate(Value.GetType(), "val");
        context.NodeOutputs[Id] = slot;
        return $"var {slot} = {Value};";
    }
}

public class AddNode : RuntimeNode
{
    public AddNode()
    {
        Inputs.Add(new VSlot("A", VType.Int));
        Inputs.Add(new VSlot("B", VType.Int));
        Outputs.Add(new VSlot("Result", VType.Int));
    }
    public override string GenerateCode(CodeGenContext context)
    {
        var left = ConnectedInputs["A"];
        var right = ConnectedInputs["B"];
        var slot = context.Memory.Allocate(typeof(int), "sum");
        context.NodeOutputs[Id] = slot;
        return $"var {slot} = {left} + {right};";
    }
}

public class ReturnNode : RuntimeNode
{
    public ReturnNode(VType type)
    {
        Inputs.Add(new VSlot("Input", type));
    }
    public override string GenerateCode(CodeGenContext context)
        => $"return {ConnectedInputs["Input"]};";
}
