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
    vec4 positionRadius;// xyz = position, w = radius
    vec4 colorIntensity;// xyz = color, w = intensity
    uvec4 lightTypeData;// x = light type, yzw = padding
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
