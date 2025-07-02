using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using VSharp.Scripts;

public partial class Visualizer : Node
{
    public override void _Ready()
    {
        // 1) Oluşturma: Context, Node’lar ve Registry (gerekmiyorsa atlayabilirsiniz)
        var context = new CodeGenContext();

        // 2) Değişken düğümleri (sabit değerler)
        var varA = new VariableNode(VType.Int, "a", 7);
        var varB = new VariableNode(VType.Int, "b", 5);

        // 3) Toplama düğümü
        var add = new AddNode();

        // 4) Sonucu döndüren düğüm
        var ret = new ReturnNode(VType.Int);

        // 5) Bağlantılar
        NodeConnector.Connect(varA, "Value", add, "A");
        NodeConnector.Connect(varB, "Value", add, "B");
        NodeConnector.Connect(add, "Result", ret, "Input");

        // 6) Düğümleri listele ve kodu oluştur
        var nodes = new List<VGraphNode> { varA, varB, add, ret };
        var builder = new CodeBuilder();
        string generatedCode = builder.Build(nodes, context);

        GD.Print("=== Üretilen C# Kodu ===");
        GD.Print(generatedCode);

        // 7) Roslyn ile derle ve çalıştır
        var compiler = new RoslynCompiler();
        var result = compiler.CompileAndExecute(generatedCode, out var diagnostics);

        // Hata varsa yazdır
        if (diagnostics != null)
        {
            foreach (var diag in diagnostics)
            {
                if (diag.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                    Console.Error.WriteLine(diag);
            }
        }

        GD.Print($"=== Sonuç: {diagnostics.First()} ===");
    }
}
