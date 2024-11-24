namespace SharpImGui.Tests;

public class ImGuiTests
{
    [SetUp]
    public void Setup()
    {
        var ctx = ImGui.CreateContext();
        ImGui.SetCurrentContext(ctx);
        ImGui.StyleColorsDark();
        var io = ImGui.GetIO();
        io.DisplaySize = new System.Numerics.Vector2(1280, 720);
        io.Fonts.AddFontDefault();
        io.Fonts.Build();
    }
    
    [TearDown]
    public void TearDown()
    {
        ImGui.DestroyContext();
    }

    [Test]
    public void Try_NewFrame_And_EndFrame()
    {
        ImGui.NewFrame();
        ImGui.EndFrame();
        Assert.Pass();
    }
}