
using System.IO;
using SharpImGui_Dev.CodeGenerator;

namespace SharpImGui_Dev
{
    class Program
    {
        static void Main(string[] args)
        {
            var generator = new Generator();
            generator.Generate();
        }

        // private static void CopyLibsToOutput()
        // {
        //     string outputDir = Path.Combine("../../../../", "SharpImGui")
        // }
    }
}