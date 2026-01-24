// SPDX-License-Identifier: MPL-2.0
// forward_plus.frag - Forward+ lighting fragment shader

#version 460 core
#extension GL_EXT_nonuniform_qualifier : require

// ============================================================================
// Input/Output
// ============================================================================

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inTexCoord;
layout(location = 3) flat in uint inMaterialIndex;

layout(location = 0) out vec4 outColor;

// ============================================================================
// Bindings
// ============================================================================

// Set 0: Bindless buffers
layout(set = 0, binding = 0) buffer BufferHeap 
{ 
    vec4 data[]; 
} buffers[65536];

// Set 1: Bindless textures
layout(set = 1, binding = 0) uniform sampler2D textures[65536];

// ============================================================================
// Push Constants
// ============================================================================

layout(push_constant) uniform PushConstants
{
    mat4 viewProj;
    uint frameIndex;
} pc;

// ============================================================================
// Structures (must match GPU side)
// ============================================================================

struct GPULight
{
    vec4 positionRadius;    // xyz = position, w = radius
    vec4 colorIntensity;    // xyz = color, w = intensity
    uvec4 lightTypeData;    // x = light type, yzw = padding
};

struct TiledLightHeader
{
    uint lightListOffset;
    uint lightCount;
};

// ============================================================================
// Utility Functions
// ============================================================================

/// Get tile index for current fragment.
uint getTileIndex(vec2 fragCoord)
{
    const uint TILE_SIZE = 16;
    uint tileX = uint(fragCoord.x) / TILE_SIZE;
    uint tileY = uint(fragCoord.y) / TILE_SIZE;
    
    // Assuming 1920x1080 for now (TODO: pass screen size in push constants)
    uint tilesPerRow = (1920 + TILE_SIZE - 1) / TILE_SIZE;
    return tileY * tilesPerRow + tileX;
}

/// Compute Blinn-Phong lighting contribution from a single light.
vec3 computeLightContribution(vec3 lightPos, vec3 lightColor, float lightIntensity, 
                              vec3 fragmentPos, vec3 normal, vec3 albedo)
{
    vec3 toLight = lightPos - fragmentPos;
    float distance = length(toLight);
    vec3 lightDir = normalize(toLight);
    
    // Attenuation (inverse square law)
    float attenuation = 1.0 / (distance * distance * 0.1 + 1.0);
    
    // Lambertian diffuse
    float diffuse = max(dot(normal, lightDir), 0.0);
    
    // View direction (approximate from fragment position)
    vec3 viewDir = normalize(-fragmentPos);
    
    // Blinn-Phong specular
    vec3 halfDir = normalize(lightDir + viewDir);
    float specular = pow(max(dot(normal, halfDir), 0.0), 32.0);
    
    // Combine
    vec3 contribution = (diffuse * albedo + specular * 0.5) * lightColor * lightIntensity * attenuation;
    return contribution;
}

// ============================================================================
// Main Fragment Shader
// ============================================================================

void main()
{
    // Get material data (simplified - would read from buffer in real implementation)
    vec3 albedo = vec3(0.8, 0.8, 0.8);  // Default white
    vec3 normal = normalize(inNormal);
    
    // Ambient lighting
    vec3 shading = albedo * 0.2;
    
    // TODO: Get tiled light list for this tile
    // uint tileIdx = getTileIndex(gl_FragCoord.xy);
    // TiledLightHeader header = getTiledLightHeader(tileIdx);
    // uint lightCount = header.lightCount;
    // uint lightOffset = header.lightListOffset;
    
    // For now, hardcode simple test lighting
    // In full implementation, would iterate through tiled light list
    
    // Test light 1: white light at (5, 5, 2)
    {
        vec3 lightPos = vec3(5.0, 5.0, 2.0);
        vec3 lightColor = vec3(1.0, 1.0, 1.0);
        float lightIntensity = 5.0;
        
        shading += computeLightContribution(lightPos, lightColor, lightIntensity,
                                            inPosition, normal, albedo);
    }
    
    // Test light 2: red light at (-5, 5, 2)
    {
        vec3 lightPos = vec3(-5.0, 5.0, 2.0);
        vec3 lightColor = vec3(1.0, 0.0, 0.0);
        float lightIntensity = 2.0;
        
        shading += computeLightContribution(lightPos, lightColor, lightIntensity,
                                            inPosition, normal, albedo);
    }
    
    outColor = vec4(shading, 1.0);
}
