namespace SharpImGui_Dev;

public static class FilesManager
{
    private static readonly string ProjectPath = Path.Combine("../../../");
    private static readonly string _nativesDirectory = Path.Combine(ProjectPath, "dcimgui");
    private static readonly string _outputDirectory = Path.Combine(ProjectPath, "../Plugins/dcimgui");
    
    public static void CopyNativesToOutputDirectory()
    {
        if (Directory.Exists(_outputDirectory))
            Directory.Delete(_outputDirectory, true);
        
        Directory.CreateDirectory(_outputDirectory);
        //dcimgui_x64.dll
        //dcimgui_x86.dll
        //dcimgui.so
        //dcimgui.dylib
        //libdcimgui_arm.dll
        //libdcimgui_x86.dll
        var files = Directory.GetFiles(_nativesDirectory);
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (fileName.EndsWith(".json") || fileName.EndsWith(".h") || fileName.EndsWith(".cpp"))
                continue;
            
            var platformPath = Path.Combine(_outputDirectory, fileName switch
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