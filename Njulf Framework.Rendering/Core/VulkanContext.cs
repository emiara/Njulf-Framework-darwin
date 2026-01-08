using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Core.Native;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Njulf_Framework.Rendering.Core;

public class VulkanContext : IDisposable
{
    private Vk _vk = null!;
    private Instance _instance;
    private PhysicalDevice _physicalDevice;
    private Device _device;
    private Queue _graphicsQueue;
    private Queue _transferQueue;
    private uint _graphicsQueueFamily;
    private uint _transferQueueFamily;
    private DebugUtilsMessengerEXT _debugMessenger;

    public Instance Instance => _instance;
    public Device Device => _device;
    public PhysicalDevice PhysicalDevice => _physicalDevice;
    public Queue GraphicsQueue => _graphicsQueue;
    public Queue TransferQueue => _transferQueue;
    public uint GraphicsQueueFamily => _graphicsQueueFamily;
    public uint TransferQueueFamily => _transferQueueFamily;
    public Vk VulkanApi => _vk;

    public VulkanContext(bool enableValidationLayers = true)
    {
        _vk = Vk.GetApi();
        CreateInstance(enableValidationLayers);
        SelectPhysicalDevice();
        CreateLogicalDevice(enableValidationLayers);
    }

    private unsafe void CreateInstance(bool enableValidation)
    {
        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)SilkMarshal.StringToPtr("YourFramework"),
            ApplicationVersion = new Silk.NET.Core.Version32(1, 0, 0),
            PEngineName = (byte*)SilkMarshal.StringToPtr("YourFramework"),
            EngineVersion = new Silk.NET.Core.Version32(1, 0, 0),
            ApiVersion = Vk.Version11
        };

        var extensions = GetRequiredExtensions(enableValidation);
        var layers = enableValidation ? GetValidationLayers() : Array.Empty<string>();

        Console.WriteLine($"Requesting {extensions.Length} extensions:");
        foreach (var ext in extensions)
        {
            Console.WriteLine($"  - {ext}");
        }

        var enabledExtensionNames = new List<IntPtr>();
        foreach (var ext in extensions)
        {
            enabledExtensionNames.Add((IntPtr)SilkMarshal.StringToPtr(ext));
        }

        var enabledLayerNames = new List<IntPtr>();
        foreach (var layer in layers)
        {
            enabledLayerNames.Add((IntPtr)SilkMarshal.StringToPtr(layer));
        }

        var extensionArray = enabledExtensionNames.ToArray();
        var layerArray = enabledLayerNames.ToArray();

        fixed (IntPtr* extensionPtr = extensionArray)
        fixed (IntPtr* layerPtr = layerArray)
        {
            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
                EnabledExtensionCount = (uint)enabledExtensionNames.Count,
                PpEnabledExtensionNames = (byte**)extensionPtr,
                EnabledLayerCount = (uint)enabledLayerNames.Count,
                PpEnabledLayerNames = (byte**)layerPtr
            };

            if (_vk.CreateInstance(&createInfo, null, out _instance) != Result.Success)
            {
                throw new Exception("Failed to create Vulkan instance. Check that Vulkan SDK is installed and GPU drivers are up to date.");
            }
        }

        // Reload Vk with the instance - IMPORTANT for extensions to work
        if (!_vk.TryGetInstanceExtension(_instance, out KhrSurface khrSurface))
        {
            throw new Exception("Failed to load KHR_surface extension");
        }

        if (!_vk.TryGetInstanceExtension(_instance, out KhrWin32Surface khrWin32Surface) &&
            !_vk.TryGetInstanceExtension(_instance, out KhrXcbSurface khrXcbSurface) &&
            !_vk.TryGetInstanceExtension(_instance, out KhrWaylandSurface khrWaylandSurface))
        {
            Console.WriteLine("Warning: Could not load platform-specific surface extension");
        }

        // Cleanup temp allocations
        foreach (var ptr in enabledExtensionNames)
            Marshal.FreeHGlobal(ptr);
        foreach (var ptr in enabledLayerNames)
            Marshal.FreeHGlobal(ptr);

        SilkMarshal.Free((nint)appInfo.PApplicationName);
        SilkMarshal.Free((nint)appInfo.PEngineName);
    }

    private unsafe void SelectPhysicalDevice()
    {
        uint deviceCount = 0;
        var result = _vk.EnumeratePhysicalDevices(_instance, &deviceCount, null);
        
        if (result != Result.Success)
        {
            throw new Exception($"Failed to enumerate physical devices: {result}");
        }

        if (deviceCount == 0)
        {
            throw new Exception("No Vulkan-capable GPUs found");
        }

        Console.WriteLine($"Found {deviceCount} physical device(s)");

        var devices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* devicesPtr = devices)
        {
            result = _vk.EnumeratePhysicalDevices(_instance, &deviceCount, devicesPtr);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to get physical device list: {result}");
            }
        }

        _physicalDevice = devices[0]; // For now, select first device

        _vk.GetPhysicalDeviceProperties(_physicalDevice, out var properties);
        var gpuName = SilkMarshal.PtrToString((nint)properties.DeviceName);
        System.Diagnostics.Debug.WriteLine($"Selected GPU: {gpuName}");
        Console.WriteLine($"Selected GPU: {gpuName}");
    }

    private unsafe void CreateLogicalDevice(bool enableValidation)
    {
        FindQueueFamilies();

        var queueCreateInfos = new List<DeviceQueueCreateInfo>();
        var uniqueQueueFamilies = new HashSet<uint> { _graphicsQueueFamily, _transferQueueFamily };

        float queuePriority = 1.0f;
        foreach (var queueFamily in uniqueQueueFamilies)
        {
            var queueCreateInfo = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = queueFamily,
                QueueCount = 1,
                PQueuePriorities = &queuePriority
            };
            queueCreateInfos.Add(queueCreateInfo);
        }

        var deviceFeatures = new PhysicalDeviceFeatures();

        var extensions = new[] { KhrSwapchain.ExtensionName };
        var extensionPtrs = new List<IntPtr>();
        foreach (var ext in extensions)
        {
            extensionPtrs.Add((IntPtr)SilkMarshal.StringToPtr(ext));
        }
        
        var extensionArray = extensionPtrs.ToArray();
        
        fixed (IntPtr* extensionPtr = extensionArray)
        {
            // Get layer names
            var layerNames = enableValidation ? GetValidationLayers() : Array.Empty<string>();

            // Marshal to unmanaged memory
            var layerNamesPointers = stackalloc byte*[layerNames.Length];
            for (int i = 0; i < layerNames.Length; i++)
            {
                layerNamesPointers[i] = (byte*)Marshal.StringToHGlobalAnsi(layerNames[i]);
            }
            var createInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = (uint)queueCreateInfos.Count,
                PQueueCreateInfos =
                    (DeviceQueueCreateInfo*)Marshal.AllocHGlobal(queueCreateInfos.Count * sizeof(DeviceQueueCreateInfo)),
                PEnabledFeatures = &deviceFeatures,
                EnabledExtensionCount = (uint)extensionPtrs.Count,
                PpEnabledExtensionNames = (byte**)extensionPtr,
                EnabledLayerCount = (uint)layerNames.Length,
                PpEnabledLayerNames = layerNamesPointers 
            };

            // Copy queue create infos
            var queueCreateInfoArray = (DeviceQueueCreateInfo*)createInfo.PQueueCreateInfos;
            for (int i = 0; i < queueCreateInfos.Count; i++)
            {
                queueCreateInfoArray[i] = queueCreateInfos[i];
            }

            if (_vk.CreateDevice(_physicalDevice, &createInfo, null, out _device) != Result.Success)
            {
                throw new Exception("Failed to create logical device");
            }

            _vk.GetDeviceQueue(_device, _graphicsQueueFamily, 0, out _graphicsQueue);
            _vk.GetDeviceQueue(_device, _transferQueueFamily, 0, out _transferQueue);

            Marshal.FreeHGlobal((nint)createInfo.PQueueCreateInfos);
        }
        
        foreach (var ptr in extensionPtrs)
                Marshal.FreeHGlobal(ptr);
    }

    private unsafe void FindQueueFamilies()
    {
        uint queueFamilyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, &queueFamilyCount, null);

        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* queueFamiliesPtr = queueFamilies)
        {
            _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, &queueFamilyCount, queueFamiliesPtr);
        }

        _graphicsQueueFamily = uint.MaxValue;
        _transferQueueFamily = uint.MaxValue;

        for (uint i = 0; i < queueFamilies.Length; i++)
        {
            if ((queueFamilies[i].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                _graphicsQueueFamily = i;
            }

            if ((queueFamilies[i].QueueFlags & QueueFlags.TransferBit) != 0 && i != _graphicsQueueFamily)
            {
                _transferQueueFamily = i;
            }
        }

        if (_transferQueueFamily == uint.MaxValue)
        {
            _transferQueueFamily = _graphicsQueueFamily;
        }

        if (_graphicsQueueFamily == uint.MaxValue)
        {
            throw new Exception("No suitable graphics queue family found");
        }
    }

    private string[] GetRequiredExtensions(bool enableValidation)
    {
        var extensions = new List<string>
        {
            KhrSurface.ExtensionName,
        };

        // Add platform-specific surface extension
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            extensions.Add(KhrWin32Surface.ExtensionName);
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
        {
            // Try Xcb first (most common)
            extensions.Add(KhrXcbSurface.ExtensionName);
        }

        if (enableValidation)
        {
            extensions.Add(ExtDebugUtils.ExtensionName);
        }

        return extensions.ToArray();
    }

    private string[] GetValidationLayers()
    {
        return new[] { "VK_LAYER_KHRONOS_validation" };
    }

    public unsafe void Dispose()
    {
        if (_device.Handle != 0)
        {
            _vk.DeviceWaitIdle(_device);
            _vk.DestroyDevice(_device, null);
        }

        if (_instance.Handle != 0)
        {
            _vk.DestroyInstance(_instance, null);
        }
    }
}
    
