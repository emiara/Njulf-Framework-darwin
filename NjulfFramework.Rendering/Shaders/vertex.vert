#version 460 core

// Inputs
layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inTexCoord;

// Push constants (must match C# struct)
layout(push_constant) uniform PushConstants {
    mat4 model;
    mat4 view;
    mat4 projection;
    uint materialIndex;
    uint meshIndex;
    uint instanceIndex;
    uint vertexOffset;
    uint indexOffset;
    uint indexCount;
    uint vertexCount;
    uint meshletOffset;
    uint meshletCount;
    float meshBoundsRadius;
    uint screenWidth;
    uint screenHeight;
    uint debugMeshlets;
    uint padding;
} pc;

// Outputs (to fragment shader)
layout(location = 0) out vec3 outPosition;      // ← NEW: World position
layout(location = 1) out vec3 outNormal;        // ← NEW: World normal
layout(location = 2) out vec2 outTexCoord;      // ← Keep texture coords
layout(location = 3) out flat uint outMaterialIndex;  // ← Material ID

void main() {
    gl_Position = pc.projection * pc.view * pc.model * vec4(inPosition, 1.0);
    outPosition = vec3(pc.model * vec4(inPosition, 1.0));
    outNormal = normalize(mat3(pc.model) * inNormal);
    outTexCoord = inTexCoord;
    outMaterialIndex = pc.materialIndex;
}
