namespace SharpImGui_Dev;

public static class FilesManager
{
    private static readonly string ProjectPath = Path.Combine("../../../");
    private static readonly string NativesDirectory = Path.Combine(ProjectPath, "dcimgui");
    private static readonly string OutputDirectory = Path.Combine(ProjectPath, "../SharpImGui/Plugins/dcimgui");
    
    public static void CopyNativesToOutputDirectory()
    {
        if (Directory.Exists(OutputDirectory))
            Directory.Delete(OutputDirectory, true);
        
        Directory.CreateDirectory(OutputDirectory);
        //dcimgui_x64.dll
        //dcimgui_x86.dll
        //dcimgui.so
        //dcimgui.dylib
        //libdcimgui_arm.dll
        //libdcimgui_x86.dll
        var files = Directory.GetFiles(NativesDirectory);
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (fileName.EndsWith(".json") || fileName.EndsWith(".h") || fileName.EndsWith(".cpp"))
                continue;
            
            var platformPath = Path.Combine(OutputDirectory, fileName switch
            {
                "dcimgui_x64.dll" => "win-x64",
                "dcimgui_x86.dll" => "win-x86",
                "dcimgui.so" => "linux",
                "dcimgui.dylib" => "osx",
                "libdcimgui_arm.so" => "android-arm",
                "libdcimgui_x86.so" => "android-x86",
                _ => throw new Exception("Unknown platform for file: " + fileName)
            });
            
            Directory.CreateDirectory(platformPath);
            var fileExtenstion = Path.GetExtension(file);
            
            File.Copy(file, Path.Combine(platformPath, $"dcimgui{fileExtenstion}"), true);
        }
    }
}