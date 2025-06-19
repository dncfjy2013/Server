using Server.Common;
using ServerUI.PluginSystem.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace ServerUI.PluginSystem.Implementation
{
    /// <summary>
    /// 基于文件系统的插件加载器实现
    /// </summary>
    public sealed class FileSystemPluginLoader : IPluginLoader
    {
        private readonly string _pluginsDirectory;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly Dictionary<Guid, PluginAssemblyLoadContext> _loadContexts = new();

        public FileSystemPluginLoader(string pluginsDirectory)
        {
            _pluginsDirectory = Path.GetFullPath(pluginsDirectory) ?? throw new ArgumentNullException(nameof(pluginsDirectory));
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        /// <summary>
        /// 发现所有可用插件
        /// </summary>
        /// <returns>插件清单集合</returns>
        public async Task<IEnumerable<IPluginManifest>> DiscoverPluginsAsync()
        {
            if (!Directory.Exists(_pluginsDirectory))
                return Enumerable.Empty<IPluginManifest>();

            var manifestFiles = Directory.GetFiles(_pluginsDirectory, "plugin.json", SearchOption.AllDirectories);
            var manifests = new List<IPluginManifest>();

            foreach (var manifestFile in manifestFiles)
            {
                try
                {
                    var manifest = await LoadManifestAsync(manifestFile);
                    if (manifest != null)
                        manifests.Add(manifest);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"加载插件清单失败: {manifestFile}, 错误: {ex.Message}");
                }
            }

            return manifests;
        }

        /// <summary>
        /// 加载插件程序集
        /// </summary>
        /// <param name="manifest">插件清单</param>
        /// <returns>插件类型</returns>
        public async Task<Type> LoadPluginAssemblyAsync(IPluginManifest manifest)
        {
            // 找到插件目录
            var pluginDirectory = FindPluginDirectory(manifest.Id);
            if (string.IsNullOrEmpty(pluginDirectory))
                throw new DirectoryNotFoundException($"找不到插件目录: {manifest.Id}");

            // 找到插件主程序集
            var assemblyFile = Path.Combine(pluginDirectory, $"{manifest.Name}.dll");
            if (!File.Exists(assemblyFile))
                throw new FileNotFoundException($"找不到插件程序集: {assemblyFile}");

            // 创建插件专用的AssemblyLoadContext
            var loadContext = new PluginAssemblyLoadContext(assemblyFile);
            _loadContexts[manifest.Id] = loadContext;

            // 加载插件程序集
            var assembly = loadContext.LoadFromAssemblyPath(assemblyFile);

            // 查找实现了IPlugin接口的类型
            var pluginType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            if (pluginType == null)
            {
                Console.WriteLine($"错误: 在程序集 {assemblyFile} 中未找到实现 IPlugin 接口的类型");
                Console.WriteLine($"IPlugin 接口类型: {typeof(IPlugin).AssemblyQualifiedName}");
                throw new TypeLoadException($"在程序集 {assemblyFile} 中找不到实现 IPlugin 接口的类型");
            }

            return await Task.FromResult(pluginType);
        }

        private async Task<PluginManifest> LoadManifestAsync(string manifestFile)
        {
            using var stream = File.OpenRead(manifestFile);
            return await JsonSerializer.DeserializeAsync<PluginManifest>(stream, _jsonOptions);
        }

        private string FindPluginDirectory(Guid pluginId)
        {
            // 查找包含特定插件ID的目录
            var directories = Directory.GetDirectories(_pluginsDirectory, "*", SearchOption.AllDirectories);
            foreach (var directory in directories)
            {
                var manifestFile = Path.Combine(directory, "plugin.json");
                if (File.Exists(manifestFile))
                {
                    try
                    {
                        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestFile), _jsonOptions);
                        if (manifest != null && manifest.Id == pluginId)
                            return directory;
                    }
                    catch
                    {
                        // 忽略加载失败的清单
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 卸载插件程序集
        /// </summary>
        /// <param name="pluginId">插件ID</param>
        public void UnloadPluginAssembly(Guid pluginId)
        {
            if (_loadContexts.TryGetValue(pluginId, out var loadContext))
            {
                loadContext.Unload();
                _loadContexts.Remove(pluginId);
            }
        }
    }
}    