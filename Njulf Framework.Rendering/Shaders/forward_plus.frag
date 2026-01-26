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
layout(location = 4) flat in uint inMeshletIndex;

layout(location = 0) out vec4 outColor;

// ============================================================================
// Bindings
// ============================================================================

// Set 0: Bindless buffers
layout(set = 0, binding = 0) readonly buffer BufferHeap 
{ 
    uint data[]; 
} buffers[65536];

// Set 1: Bindless textures
layout(set = 1, binding = 0) uniform sampler2D textures[65536];

// ============================================================================
// Push Constants
// ============================================================================

layout(push_constant) uniform PushConstants
{
    mat4 model;
    mat4 view;
    mat4 projection;
    uint materialIndex;
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
    uint lightCount;
    uint lightBufferIndex;
    uint tiledLightHeaderBufferIndex;
    uint tiledLightIndicesBufferIndex;
    uint padding;
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
    
    uint tilesPerRow = (pc.screenWidth + TILE_SIZE - 1u) / TILE_SIZE;
    return tileY * tilesPerRow + tileX;
}

GPULight getLight(uint lightIdx)
{
    uint baseOffset = lightIdx * 12u;
    vec4 positionRadius = vec4(
        uintBitsToFloat(buffers[pc.lightBufferIndex].data[baseOffset + 0u]),
        uintBitsToFloat(buffers[pc.lightBufferIndex].data[baseOffset + 1u]),
        uintBitsToFloat(buffers[pc.lightBufferIndex].data[baseOffset + 2u]),
        uintBitsToFloat(buffers[pc.lightBufferIndex].data[baseOffset + 3u])
    );
    vec4 colorIntensity = vec4(
        uintBitsToFloat(buffers[pc.lightBufferIndex].data[baseOffset + 4u]),
        uintBitsToFloat(buffers[pc.lightBufferIndex].data[baseOffset + 5u]),
        uintBitsToFloat(buffers[pc.lightBufferIndex].data[baseOffset + 6u]),
        uintBitsToFloat(buffers[pc.lightBufferIndex].data[baseOffset + 7u])
    );
    uvec4 lightTypeData = uvec4(
        buffers[pc.lightBufferIndex].data[baseOffset + 8u],
        buffers[pc.lightBufferIndex].data[baseOffset + 9u],
        buffers[pc.lightBufferIndex].data[baseOffset + 10u],
        buffers[pc.lightBufferIndex].data[baseOffset + 11u]
    );
    return GPULight(positionRadius, colorIntensity, lightTypeData);
}

TiledLightHeader getTiledLightHeader(uint tileIdx)
{
    TiledLightHeader header;
    header.lightListOffset = buffers[pc.tiledLightHeaderBufferIndex].data[tileIdx * 2u];
    header.lightCount = buffers[pc.tiledLightHeaderBufferIndex].data[tileIdx * 2u + 1u];
    return header;
}

uint getLightIndex(uint globalIdx)
{
    return buffers[pc.tiledLightIndicesBufferIndex].data[globalIdx];
}

/// Compute Blinn-Phong lighting contribution from a single light.
vec3 computeLightContribution(vec3 lightPos, vec3 lightColor, float lightIntensity, 
                              float lightRadius, vec3 fragmentPos, vec3 normal, vec3 albedo)
{
    vec3 toLight = lightPos - fragmentPos;
    float distance = length(toLight);
    if (distance > lightRadius)
        return vec3(0.0);
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
    if (pc.debugMeshlets != 0u)
    {
        // Debug: visualize meshlets in distinct colors
        uint id = inMeshletIndex + 1u;
        float r = float((id * 97u) % 255u) / 255.0;
        float g = float((id * 57u) % 255u) / 255.0;
        float b = float((id * 17u) % 255u) / 255.0;
        outColor = vec4(r, g, b, 1.0);
        return;
    }

    // Get material data (simplified - would read from buffer in real implementation)
    vec3 albedo = vec3(0.8, 0.8, 0.8);  // Default white
    vec3 normal = inNormal;
    float nlen = length(normal);
    if (nlen < 1e-5)
        normal = vec3(0.0, 1.0, 0.0);
    else
        normal /= nlen;
    
    // Ambient lighting
    vec3 shading = albedo * 0.2;
    
    if (pc.lightCount > 0u)
    {
        uint tileIdx = getTileIndex(gl_FragCoord.xy);
        TiledLightHeader header = getTiledLightHeader(tileIdx);
        uint count = min(header.lightCount, 256u);

        for (uint i = 0u; i < count; i++)
        {
            uint lightIdx = getLightIndex(header.lightListOffset + i);
            GPULight light = getLight(lightIdx);
            if (light.lightTypeData.x != 0u)
                continue;

            vec3 lightPos = light.positionRadius.xyz;
            float lightRadius = light.positionRadius.w;
            vec3 lightColor = light.colorIntensity.xyz;
            float lightIntensity = light.colorIntensity.w;

            shading += computeLightContribution(lightPos, lightColor, lightIntensity,
                                                lightRadius, inPosition, normal, albedo);
        }
    }
    
    outColor = vec4(shading, 1.0);
}
