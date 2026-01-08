using System.Diagnostics;

namespace Njulf_Framework.Rendering.Shaders;

public static class ShaderCompiler 
{
    /// <summary>
    /// Compile a GLSL shader file to SPIR-V.
    /// </summary>
    public static byte[] CompileGlslToSpirv(string glslSourcePath, ShaderStage stage)
    {
        if (!File.Exists(glslSourcePath))
        {
            throw new FileNotFoundException($"Shader file not found: {glslSourcePath}");
        }

        string spirvPath = Path.ChangeExtension(glslSourcePath, $".{GetStageExtension(stage)}.spv");

        // Use glslc to compile
        var psi = new ProcessStartInfo
        {
            FileName = "glslc",
            Arguments = $"-fshader-stage={GetGlslcStage(stage)} -o \"{spirvPath}\" \"{glslSourcePath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using (var process = Process.Start(psi))
        {
            if (process == null)
            {
                throw new Exception("Failed to start glslc compiler. Ensure glslc is in PATH.");
            }

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();
                throw new Exception($"Shader compilation failed:\n{error}");
            }
        }

        if (!File.Exists(spirvPath))
        {
            throw new Exception("Shader compilation produced no output file");
        }

        byte[] spirvBytecode = File.ReadAllBytes(spirvPath);

        // Optionally keep the spirv file for debugging
        // File.Delete(spirvPath);

        return spirvBytecode;
    }

    /// <summary>
    /// Load pre-compiled SPIR-V bytecode from file.
    /// </summary>
    public static byte[] LoadSpirv(string spirvPath)
    {
        if (!File.Exists(spirvPath))
        {
            throw new FileNotFoundException($"SPIR-V file not found: {spirvPath}");
        }

        return File.ReadAllBytes(spirvPath);
    }

    private static string GetStageExtension(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => "vert",
        ShaderStage.Fragment => "frag",
        ShaderStage.Compute => "comp",
        ShaderStage.Geometry => "geom",
        ShaderStage.TessellationControl => "tesc",
        ShaderStage.TessellationEvaluation => "tese",
        _ => throw new ArgumentException($"Unknown shader stage: {stage}")
    };

    private static string GetGlslcStage(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => "vertex",
        ShaderStage.Fragment => "fragment",
        ShaderStage.Compute => "compute",
        ShaderStage.Geometry => "geometry",
        ShaderStage.TessellationControl => "tess_control",
        ShaderStage.TessellationEvaluation => "tess_eval",
        _ => throw new ArgumentException($"Unknown shader stage: {stage}")
    };
}

public enum ShaderStage
{
    Vertex,
    Fragment,
    Compute,
    Geometry,
    TessellationControl,
    TessellationEvaluation
}