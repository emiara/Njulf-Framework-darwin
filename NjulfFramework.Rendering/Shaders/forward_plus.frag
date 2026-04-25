// SPDX-License-Identifier: MPL-2.0
// forward_plus.frag - Forward+ lighting fragment shader with PBR

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
// PBR Material Structure
// ============================================================================

struct PBRMaterial
{
    vec4 baseColorFactor;
    float metallicFactor;
    float roughnessFactor;
    int baseColorTextureIndex;
    int metallicRoughnessTextureIndex;
    int normalTextureIndex;
    int occlusionTextureIndex;
    int emissiveTextureIndex;
    float normalScale;
    float occlusionStrength;
    vec3 emissiveFactor;
};

// ============================================================================
// PBR Functions
// ============================================================================

// GGX/Trowbridge-Reitz normal distribution function
float DistributionGGX(vec3 N, vec3 H, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;

    float nom = a2;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = 3.14159265359 * denom * denom;

    return nom / denom;
}

// Geometry function using Smith's method (GGX)
float GeometrySchlickGGX(float NdotV, float roughness)
{
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;

    float nom = NdotV;
    float denom = NdotV * (1.0 - k) + k;

    return nom / denom;
}

float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    float ggx2 = GeometrySchlickGGX(NdotV, roughness);
    float ggx1 = GeometrySchlickGGX(NdotL, roughness);

    return ggx1 * ggx2;
}

// Fresnel equation using Schlick approximation
vec3 fresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

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
    PBRMaterial material;
    material.baseColorFactor = vec4(0.8, 0.8, 0.8, 1.0); // Default white
    material.metallicFactor = 0.5;
    material.roughnessFactor = 0.5;
    material.baseColorTextureIndex = -1; // No texture
    material.metallicRoughnessTextureIndex = -1;
    material.normalTextureIndex = -1;
    material.occlusionTextureIndex = -1;
    material.emissiveTextureIndex = -1;
    material.normalScale = 1.0;
    material.occlusionStrength = 1.0;
    material.emissiveFactor = vec3(0.0);

    // Sample textures if available
    vec3 albedo = material.baseColorFactor.rgb;
    if (material.baseColorTextureIndex >= 0)
    {
        albedo = pow(texture(textures[material.baseColorTextureIndex], inTexCoord).rgb, vec3(2.2));
    }

    float metallic = material.metallicFactor;
    float roughness = material.roughnessFactor;
    if (material.metallicRoughnessTextureIndex >= 0)
    {
        vec4 mrSample = texture(textures[material.metallicRoughnessTextureIndex], inTexCoord);
        metallic = mrSample.b; // Metallic in blue channel
        roughness = mrSample.g; // Roughness in green channel
    }

    // Normal mapping
    vec3 normal = inNormal;
    float nlen = length(normal);
    if (nlen < 1e-5)
        normal = vec3(0.0, 1.0, 0.0);
    else
        normal /= nlen;

    if (material.normalTextureIndex >= 0)
    {
        vec3 tangentNormal = texture(textures[material.normalTextureIndex], inTexCoord).xyz * 2.0 - 1.0;
        tangentNormal.xy *= material.normalScale;
        tangentNormal = normalize(tangentNormal);
        
        // TBN matrix would be needed here for proper normal mapping
        // For now, just apply as perturbation
        normal = normalize(normal + tangentNormal * 0.1);
    }

    // Ambient occlusion
    float ao = 1.0;
    if (material.occlusionTextureIndex >= 0)
    {
        ao = texture(textures[material.occlusionTextureIndex], inTexCoord).r;
        ao = mix(1.0, ao, material.occlusionStrength);
    }

    // View direction
    vec3 V = normalize(-inPosition);

    // Calculate lighting
    vec3 Lo = vec3(0.0);

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

            vec3 L = normalize(lightPos - inPosition);
            vec3 H = normalize(V + L);
            float distance = length(lightPos - inPosition);
            float attenuation = 1.0 / (distance * distance * 0.1 + 1.0);

            // Cook-Torrance BRDF
            float NDF = DistributionGGX(normal, H, roughness);
            float G = GeometrySmith(normal, V, L, roughness);
            vec3 F = fresnelSchlick(max(dot(H, V), 0.0), mix(vec3(0.04), albedo, metallic));

            vec3 numerator = NDF * G * F;
            float denominator = 4.0 * max(dot(normal, V), 0.0) * max(dot(normal, L), 0.0) + 0.0001;
            vec3 specular = numerator / denominator;

            // kS is Fresnel
            vec3 kS = F;
            vec3 kD = vec3(1.0) - kS;
            kD *= 1.0 - metallic;

            float NdotL = max(dot(normal, L), 0.0);
            Lo += (kD * albedo / 3.14159265359 + specular) * lightColor * lightIntensity * NdotL * attenuation;
        }
    }

    // Ambient lighting (simplified IBL would go here)
    vec3 ambient = vec3(0.03) * albedo * ao;

    vec3 color = ambient + Lo;

    // HDR tonemapping (simple Reinhard)
    color = color / (color + vec3(1.0));

    // Gamma correction
    color = pow(color, vec3(1.0/2.2));

    outColor = vec4(color, 1.0);
}