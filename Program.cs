﻿
using SharpImGui_Dev.CodeGenerator;

namespace SharpImGui_Dev
{
    class Program
    {
        static void Main(string[] args)
        {
            var generator = new CSharpGenerator();
            generator.Generate();
        }
    }
}